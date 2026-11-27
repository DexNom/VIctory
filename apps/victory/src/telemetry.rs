//! Host and child-process samples taken outside the rendering process.

use std::fs::{File, OpenOptions};
use std::io::{self, BufRead, BufReader, BufWriter, Write};
use std::os::windows::io::AsRawHandle;
use std::path::Path;
use std::process::{Child, Command, ExitStatus, Stdio};
use std::time::{Instant, SystemTime, UNIX_EPOCH};

use anyhow::Context;
use windows_sys::Win32::Foundation::{FILETIME, HANDLE, WAIT_OBJECT_0, WAIT_TIMEOUT};
use windows_sys::Win32::System::ProcessStatus::{GetProcessMemoryInfo, PROCESS_MEMORY_COUNTERS_EX};
use windows_sys::Win32::System::SystemInformation::{GlobalMemoryStatusEx, MEMORYSTATUSEX};
use windows_sys::Win32::System::Threading::{
    ALL_PROCESSOR_GROUPS, GetActiveProcessorCount, GetProcessIoCounters, GetProcessTimes,
    GetSystemTimes, IO_COUNTERS, WaitForSingleObject,
};

pub(super) fn run(
    command: &mut Command,
    log: &mut File,
    log_path: &Path,
    session_logs: &super::SessionLogs<'_>,
    errors: &mut File,
    full_log: bool,
) -> anyhow::Result<ExitStatus> {
    let csv_path = log_path.with_extension("csv");
    let performance_path = session_logs.file("performance.txt");
    let full_path = session_logs.file("flog.txt");
    victory_core::policy::ensure_log_path(&performance_path)?;
    let mut performance = Performance::default();
    performance.write(&performance_path, log_path)?;
    let mut csv = open(&csv_path)
        .with_context(|| format!("Could not create telemetry CSV {}", csv_path.display()))?;
    writeln!(log, "Telemetry CSV: {}", csv_path.display())?;
    let mut full = if full_log {
        victory_core::policy::ensure_log_path(&full_path)?;
        let mut file = BufWriter::new(File::create(&full_path)?);
        file.write_all(&std::fs::read(log_path)?)?;
        Some(file)
    } else {
        None
    };

    // Merge the hidden process's two streams into one pipe, in observed order.
    let (output, input) = io::pipe()?;
    let child = command
        .stdout(Stdio::from(input.try_clone()?))
        .stderr(Stdio::from(input))
        .spawn()
        .context("Could not start the VIctory runtime");
    // Command keeps its own pipe writers until the settings are replaced.
    command.stdout(Stdio::null()).stderr(Stdio::null());
    let mut child = child?;

    let mut output = BufReader::new(output);
    let (captured, sampled) = std::thread::scope(|scope| {
        let child = &child;
        let samples = std::thread::Builder::new()
            .name("victory-telemetry".into())
            .spawn_scoped(scope, move || record(child, &mut csv));
        let captured: io::Result<()> = (|| {
            let mut line = Vec::new();
            let mut last_report = Instant::now();
            while output.read_until(b'\n', &mut line)? != 0 {
                if let Some(full) = &mut full {
                    full.write_all(&line)?;
                }
                if let Ok(text) = std::str::from_utf8(&line) {
                    if text.contains("victory_telemetry::") {
                        log.write_all(&line)?;
                        if performance.observe(text) && last_report.elapsed().as_secs() >= 5 {
                            performance.write(&performance_path, log_path)?;
                            last_report = Instant::now();
                        }
                    } else if text.starts_with("Error: ") || text.starts_with("error: ") {
                        // Rust's main Result and CLI parsing can write directly to stderr.
                        errors.write_all(&line)?;
                    }
                }
                line.clear();
            }
            Ok(())
        })();
        if captured.is_err() {
            // Keep reading after an output-file error so the game can still finish.
            let _ = io::copy(&mut output, &mut io::sink());
        }
        let sampled = samples.and_then(|thread| {
            thread
                .join()
                .map_err(|_| io::Error::other("telemetry sampling thread panicked"))?
        });
        (captured, sampled)
    });
    let status = child.wait()?;
    writeln!(log, "VIctory exited with {status}")?;
    writeln!(errors, "VIctory exited with {status}")?;
    if let Some(mut full) = full {
        writeln!(full, "\nVIctory exited with {status}")?;
        full.flush()?;
        writeln!(
            log,
            "Full output {} bytes in {}",
            std::fs::metadata(&full_path)?.len(),
            full_path.display()
        )?;
    }
    performance.write(&performance_path, log_path)?;
    captured.context("Could not capture runtime output")?;
    sampled.context("Could not sample runtime CPU, RAM and process I/O")?;
    Ok(status)
}

#[derive(Default)]
struct Performance {
    samples: u64,
    seconds: f64,
    bytes: [u64; 4],
    peaks: [(f64, String); 4],
}

const TRANSFERS: [&str; 4] = ["ram_read", "ram_write", "vram_read", "vram_write"];

impl Performance {
    fn observe(&mut self, line: &str) -> bool {
        let Some((prefix, fields)) = line.split_once("victory_telemetry::gpu: frame sample ")
        else {
            return false;
        };
        let mut bytes = [None; 4];
        let mut window = None;
        let mut pid = "";
        for (key, value) in fields
            .split_ascii_whitespace()
            .filter_map(|field| field.split_once('='))
        {
            match key {
                "pid" => pid = value,
                "window_us" => window = value.parse::<u64>().ok().filter(|&us| us != 0),
                _ => {
                    if let Some(direction) = key.strip_suffix("_bytes")
                        && let Some(i) = TRANSFERS.iter().position(|&name| name == direction)
                    {
                        bytes[i] = value.parse::<u64>().ok();
                    }
                }
            }
        }
        let (
            Some(window),
            [
                Some(ram_read),
                Some(ram_write),
                Some(vram_read),
                Some(vram_write),
            ],
        ) = (window, bytes)
        else {
            return false;
        };
        let seconds = window as f64 / 1_000_000.0;
        self.samples += 1;
        self.seconds += seconds;
        for (i, bytes) in [ram_read, ram_write, vram_read, vram_write]
            .into_iter()
            .enumerate()
        {
            self.bytes[i] += bytes;
            let rate = mib_per_second(bytes, seconds);
            if self.samples == 1 || rate > self.peaks[i].0 {
                let utc = prefix
                    .split_ascii_whitespace()
                    .next()
                    .unwrap_or("unknown time");
                self.peaks[i] = (rate, format!("{utc} PID {pid}"));
            }
        }
        true
    }

    fn write(&self, path: &Path, log_path: &Path) -> io::Result<()> {
        use std::fmt::Write as _;
        let mut report = format!(
            "Session {}\nTracked renderer transfers (MiB/s)\nSamples {} over {:.3} measured seconds\n",
            log_path.display(),
            self.samples,
            self.seconds
        );
        if self.samples == 0 {
            report.push_str("No rendered transfer samples.\n");
        } else {
            for (i, name) in TRANSFERS.iter().enumerate() {
                let (peak, at) = &self.peaks[i];
                let average = mib_per_second(self.bytes[i], self.seconds);
                let _ = writeln!(
                    report,
                    "{name:<10} average {average:10.2}  peak {peak:10.2}  at {at}"
                );
            }
        }
        std::fs::write(path, report)
    }
}

fn mib_per_second(bytes: u64, seconds: f64) -> f64 {
    bytes as f64 / (1u64 << 20) as f64 / seconds
}

#[cfg(test)]
mod tests {
    use super::{Performance, mib_per_second};

    #[test]
    fn averages_use_measured_time_and_peaks_keep_the_sample_timestamp() {
        let mut report = Performance::default();
        assert!(report.observe(
            "2026-01-01T00:00:00Z  INFO victory_telemetry::gpu: frame sample pid=12 window_us=1000000 ram_read_bytes=4194304 ram_write_bytes=0 vram_read_bytes=0 vram_write_bytes=1048576"
        ));
        assert!(report.observe(
            "2026-01-01T00:00:03Z  INFO victory_telemetry::gpu: frame sample pid=12 window_us=3000000 ram_read_bytes=0 ram_write_bytes=6291456 vram_read_bytes=3145728 vram_write_bytes=0"
        ));
        assert!(!report.observe("INFO victory_telemetry::gpu: frame sample window_us=0"));
        assert_eq!(report.samples, 2);
        assert_eq!(
            report
                .bytes
                .map(|bytes| mib_per_second(bytes, report.seconds)),
            [1.0, 1.5, 0.75, 0.25]
        );
        assert_eq!(
            report.peaks.map(|(rate, at)| (rate, at[11..19].to_owned())),
            [
                (4.0, "00:00:00".into()),
                (2.0, "00:00:03".into()),
                (1.0, "00:00:03".into()),
                (1.0, "00:00:00".into())
            ]
        );
    }
}

pub(super) fn open(path: &Path) -> io::Result<File> {
    let mut csv = OpenOptions::new().write(true).create_new(true).open(path)?;
    writeln!(
        csv,
        "utc_unix_ms,elapsed_ms,pid,event,system_cpu_pct,process_cpu_pct,\
         system_ram_total_bytes,system_ram_available_bytes,process_working_set_bytes,\
         process_private_commit_bytes,process_io_read_bytes,process_io_write_bytes"
    )?;
    Ok(csv)
}

pub(super) fn record(child: &Child, csv: &mut File) -> io::Result<()> {
    const INTERVAL_MS: u32 = 1_000;
    let started = Instant::now();
    let handle = child.as_raw_handle();
    // SAFETY: ALL_PROCESSOR_GROUPS is a documented selector.
    let processors = f64::from(unsafe { GetActiveProcessorCount(ALL_PROCESSOR_GROUPS) }.max(1));
    let mut previous: Option<CpuSample> = None;
    let mut event = "start";
    loop {
        let elapsed = started.elapsed();
        let utc_ms = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map_err(io::Error::other)?
            .as_millis();
        let now = CpuSample {
            elapsed,
            system: system_cpu_ticks(),
            process: process_cpu_ticks(handle),
        };
        let system_cpu = previous.as_ref().and_then(|before| {
            let [idle, total] = now.system?;
            let [old_idle, old_total] = before.system?;
            let idle = idle.checked_sub(old_idle)?;
            let total = total.checked_sub(old_total)?;
            let busy = total.checked_sub(idle)?;
            (total > 0).then(|| format!("{:.2}", busy as f64 * 100.0 / total as f64))
        });
        let process_cpu = previous.as_ref().and_then(|before| {
            let ticks = now.process?.checked_sub(before.process?)?;
            let seconds = (now.elapsed - before.elapsed).as_secs_f64();
            (seconds > 0.0).then(|| format!("{:.2}", ticks as f64 / (1e5 * seconds * processors)))
        });
        previous = Some(now);

        let [total_ram, available_ram] = csv_pair(system_ram());
        let [working_set, private_commit] = if event == "exit" {
            csv_pair(None)
        } else {
            csv_pair(process_ram(handle))
        };
        let [read, written] = csv_pair(process_io(handle));
        let elapsed_ms = elapsed.as_millis();
        let pid = child.id();
        let system_cpu = system_cpu.unwrap_or_default();
        let process_cpu = process_cpu.unwrap_or_default();
        let row = format!(
            "{utc_ms},{elapsed_ms},{pid},{event},{system_cpu},{process_cpu},\
             {total_ram},{available_ram},{working_set},{private_commit},{read},{written}\n"
        );
        csv.write_all(row.as_bytes())?;
        if event == "exit" {
            return Ok(());
        }

        // SAFETY: Child owns the process handle for the entire wait.
        event = match unsafe { WaitForSingleObject(handle, INTERVAL_MS) } {
            WAIT_TIMEOUT => "sample",
            WAIT_OBJECT_0 => "exit",
            _ => return Err(io::Error::last_os_error()),
        };
    }
}

struct CpuSample {
    elapsed: std::time::Duration,
    // Idle and total CPU time. The kernel time from Windows includes idle time.
    system: Option<[u64; 2]>,
    process: Option<u64>,
}

fn csv_pair(values: Option<[u64; 2]>) -> [String; 2] {
    values
        .map(|pair| pair.map(|value| value.to_string()))
        .unwrap_or_default()
}

fn ticks(time: FILETIME) -> u64 {
    (u64::from(time.dwHighDateTime) << 32) | u64::from(time.dwLowDateTime)
}

fn system_cpu_ticks() -> Option<[u64; 2]> {
    let [mut idle, mut kernel, mut user] = [FILETIME::default(); 3];
    // SAFETY: each argument points to a writable FILETIME.
    let ok = unsafe { GetSystemTimes(&mut idle, &mut kernel, &mut user) };
    (ok != 0).then(|| [ticks(idle), ticks(kernel) + ticks(user)])
}

fn process_cpu_ticks(handle: HANDLE) -> Option<u64> {
    let [mut created, mut exited, mut kernel, mut user] = [FILETIME::default(); 4];
    // SAFETY: borrowed process handle and four writable FILETIMEs.
    let ok = unsafe { GetProcessTimes(handle, &mut created, &mut exited, &mut kernel, &mut user) };
    (ok != 0).then(|| ticks(kernel) + ticks(user))
}

fn system_ram() -> Option<[u64; 2]> {
    let mut counters = MEMORYSTATUSEX {
        dwLength: std::mem::size_of::<MEMORYSTATUSEX>() as u32,
        ..Default::default()
    };
    // SAFETY: counters is a writable structure with the correct size set.
    let ok = unsafe { GlobalMemoryStatusEx(&mut counters) };
    (ok != 0).then_some([counters.ullTotalPhys, counters.ullAvailPhys])
}

fn process_ram(handle: HANDLE) -> Option<[u64; 2]> {
    let size = std::mem::size_of::<PROCESS_MEMORY_COUNTERS_EX>() as u32;
    let mut counters = PROCESS_MEMORY_COUNTERS_EX {
        cb: size,
        ..Default::default()
    };
    // SAFETY: borrowed process handle and an extended struct of the declared size.
    let ok = unsafe { GetProcessMemoryInfo(handle, (&raw mut counters).cast(), size) };
    (ok != 0).then_some([counters.WorkingSetSize as u64, counters.PrivateUsage as u64])
}

fn process_io(handle: HANDLE) -> Option<[u64; 2]> {
    let mut counters = IO_COUNTERS::default();
    // SAFETY: borrowed process handle and a writable IO_COUNTERS structure.
    let ok = unsafe { GetProcessIoCounters(handle, &mut counters) };
    (ok != 0).then_some([counters.ReadTransferCount, counters.WriteTransferCount])
}
