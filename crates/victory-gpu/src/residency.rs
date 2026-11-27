//! Host-memory admission for speculative texture work and reusable upload storage.
use std::sync::OnceLock;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use windows_sys::Win32::System::SystemInformation::{GlobalMemoryStatusEx, MEMORYSTATUSEX};

const MIB: u64 = 1 << 20;
const GIB: u64 = 1 << 30;
static PRESSURED: AtomicBool = AtomicBool::new(false);
static HEADROOM: AtomicU64 = AtomicU64::new(u64::MAX);

fn memory_status() -> Option<MEMORYSTATUSEX> {
    // SAFETY: the API initializes the fields of a correctly sized structure.
    let mut status: MEMORYSTATUSEX = unsafe { std::mem::zeroed() };
    status.dwLength = std::mem::size_of::<MEMORYSTATUSEX>() as u32;
    (unsafe { GlobalMemoryStatusEx(&mut status) } != 0).then_some(status)
}

// The texture sweep may run for seconds inside a single submitted frame.
static NEXT_COMMIT_SAMPLE: AtomicU64 = AtomicU64::new(0);
static AVAILABLE_COMMIT: AtomicU64 = AtomicU64::new(u64::MAX);
static RETRY_UPLOAD_AT: AtomicU64 = AtomicU64::new(0);

pub(crate) fn allocation_error(error: &windows::core::Error) {
    if error.code() == windows::Win32::Foundation::E_OUTOFMEMORY {
        RETRY_UPLOAD_AT.fetch_max(crate::host::coarse_ms().saturating_add(500), Ordering::Relaxed);
    }
}

thread_local! {
    static STAGING_DEFERRED: std::cell::Cell<bool> = const { std::cell::Cell::new(false) };
}

pub(crate) fn note_staging_deferred() {
    STAGING_DEFERRED.set(true);
}

pub(crate) fn take_staging_deferred() -> bool {
    STAGING_DEFERRED.replace(false)
}

pub(crate) fn upload_refused() -> bool {
    crate::host::coarse_ms() < RETRY_UPLOAD_AT.load(Ordering::Relaxed)
}

fn sample_commit(now: u64) -> u64 {
    let next = NEXT_COMMIT_SAMPLE.load(Ordering::Relaxed);
    if now >= next
        && NEXT_COMMIT_SAMPLE
            .compare_exchange(next, now.saturating_add(32), Ordering::Relaxed, Ordering::Relaxed)
            .is_ok()
        && let Some(status) = memory_status()
    {
        // What an allocation needs is both: commit to charge it and a page
        // to back it. A 40 GB pagefile left 25 GB of commit while the PC
        // had 376 MB of RAM free, and the driver's own allocation failed
        // there (run cf-18: a host command list did not close, 0x8007000E).
        // Every reserve below is a reserve of the PC's memory, so it reads
        // the smaller of the two accounts.
        AVAILABLE_COMMIT.store(status.ullAvailPageFile.min(status.ullAvailPhys), Ordering::Relaxed);
    }
    AVAILABLE_COMMIT.load(Ordering::Relaxed)
}

fn has_upload_reserve(commit: u64, bake: bool) -> bool {
    // Keep a protective floor against Rust/game OOM aborts without starving
    // bindless world textures or one-shot ped decoration bakes when steady-state
    // commit headroom sits in the 1-4 GiB range on 32 GiB hosts.
    //
    // Measured (run cf-19c and play2): a 23 GB process on 16 GB settles with
    // 0.3-2.0 GB of RAM free. Any floor near a gigabyte is therefore always
    // crossed, and the textures it holds back are what the screen flickers
    // with. 192 MB is under every state this PC reaches while it is alive,
    // and still leaves the allocator a page to land on: the driver failed at
    // 325 MB only when nothing was held back at all (run cf-18).
    commit >= if bake { 128 * MIB } else { 192 * MIB }
}

fn has_growth_reserve(commit: u64, bake: bool) -> bool {
    // Growing a fresh 64 MiB upload block for speculative bindless prefetch
    // requires more headroom than reusing slices already committed inside the
    // staging pool. 512 MB: at 900 MB free a 1 GB line kept the pool from
    // ever growing, every texture took an upload buffer of its own and
    // 14,256 MB of them were made in one run.
    commit >= if bake { 192 * MIB } else { 512 * MIB }
}

/// Leave commit for game allocations and submissions already in flight while
/// still letting bindless textures and one-shot material bakes upload.
pub(crate) fn admit_texture_pass(bake: bool) -> bool {
    let now = crate::host::coarse_ms();
    if now < RETRY_UPLOAD_AT.load(Ordering::Relaxed) {
        return false;
    }
    has_upload_reserve(sample_commit(now), bake)
}

/// Whether the staging allocator may commit a brand-new OS upload buffer
/// (reusing existing free ranges in already-committed pool blocks stays open
/// whenever `admit_texture_pass` is true).
pub(crate) fn allow_staging_growth(bake: bool) -> bool {
    let now = crate::host::coarse_ms();
    if now < RETRY_UPLOAD_AT.load(Ordering::Relaxed) {
        return false;
    }
    has_growth_reserve(sample_commit(now), bake)
}

pub(crate) fn limited_host_ram() -> bool {
    static LIMITED: OnceLock<bool> = OnceLock::new();
    *LIMITED.get_or_init(|| memory_status().is_some_and(|s| s.ullTotalPhys <= 32 * GIB))
}

/// The two free-RAM lines that bracket host pressure.
///
/// The defaults are the lines every measured run on this build used: enter at
/// 3 GiB of free RAM, leave at 5 GiB. `VICTORY_PRESSURE_FREE_MB` and
/// `VICTORY_PRESSURE_CLEAR_MB` move them, in MiB, for hosts whose steady state
/// sits somewhere else. The commit lines below are not knobs: they exist
/// because a 40 GB pagefile leaves commit to spare while the RAM is gone, and
/// that is the state the card was removed in.
#[derive(Clone, Copy, Debug)]
pub(crate) struct Lines {
    pub free: u64,
    pub clear: u64,
}

fn mib_from_env(name: &str) -> Option<u64> {
    std::env::var(name)
        .ok()
        .and_then(|v| v.trim().parse::<u64>().ok())
        .filter(|mib| *mib > 0)
        .map(|mib| mib << 20)
}

fn clamp_lines(free: u64, clear: u64) -> Lines {
    // A clear line at or under the free line flaps the state on every sample,
    // and each flip rewrites the prefetch budget: 256 MiB is the smallest gap a
    // 16 GB host resolves between two one-second samples.
    Lines { free, clear: clear.max(free.saturating_add(256 * MIB)) }
}

pub(crate) fn lines() -> Lines {
    static LINES: OnceLock<Lines> = OnceLock::new();
    *LINES.get_or_init(|| {
        let free = mib_from_env("VICTORY_PRESSURE_FREE_MB").unwrap_or(3 * GIB);
        let clear = mib_from_env("VICTORY_PRESSURE_CLEAR_MB").unwrap_or(5 * GIB);
        clamp_lines(free, clear)
    })
}

fn next_pressure(was: bool, physical: u64, commit: u64, lines: Lines) -> bool {
    if was {
        physical < lines.clear || commit < 6 * GIB
    } else {
        physical <= lines.free || commit <= 4 * GIB
    }
}

/// Called by the once-per-second submission sweep. A failed sample keeps the last good state.
pub(crate) fn refresh() {
    let Some(status) = memory_status() else { return };
    let (physical, commit) = (status.ullAvailPhys, status.ullAvailPageFile);
    HEADROOM.store(physical.min(commit), Ordering::Relaxed);
    let was = pressured();
    let lines = lines();
    let pressure = next_pressure(was, physical, commit, lines);
    if pressure != was {
        PRESSURED.store(pressure, Ordering::Relaxed);
        if pressure {
            tracing::warn!(
                pid = std::process::id(),
                frame = crate::frame::current(),
                physical_free_mib = physical >> 20,
                available_commit_mib = commit >> 20,
                enter_free_mib = lines.free >> 20,
                clear_free_mib = lines.clear >> 20,
                buffer_mirror_span_mib = crate::shadow::live_span_bytes() >> 20,
                texture_staging_mib = crate::texture::live_staging_bytes() >> 20,
                spare_scratch_mib = crate::host::ALIVE[4].load(Ordering::Relaxed) >> 20,
                game_filled_gpu_texture_mib = crate::resource::KINDS[1].load(Ordering::Relaxed) >> 20,
                "host memory pressure: limiting texture prefetch and idle upload storage"
            );
        } else {
            tracing::info!(physical_free_mib = physical >> 20, available_commit_mib = commit >> 20, "host memory pressure cleared");
        }
    }
}

pub(crate) fn headroom() -> Option<u64> {
    let bytes = HEADROOM.load(Ordering::Relaxed);
    (bytes != u64::MAX).then_some(bytes)
}

pub(crate) fn pressured() -> bool {
    PRESSURED.load(Ordering::Relaxed)
}

/// True when OS commit headroom is critically low (< 1.5 GiB) or an allocation
/// just failed with `E_OUTOFMEMORY`, requiring emergency throttling.
///
/// Commit headroom here is [`sample_commit`]'s, which is the smaller of the
/// pagefile's and the PC's free memory: a host with a 40 GB pagefile has
/// commit to spare while its RAM is gone, and that is the state the card
/// was removed in (run cf-15) and the driver refused an allocation in (run
/// cf-18).
pub(crate) fn critical_commit() -> bool {
    // 384 MB, not 1,536: this PC's steady state is 0.3-2.0 GB free with the
    // world loaded, so a 1.5 GB line was always crossed and the per-frame
    // prefetch was cut to 16 MB for the whole run. The driver's own failure
    // was at 325 MB, with no floor at all.
    let now = crate::host::coarse_ms();
    now < RETRY_UPLOAD_AT.load(Ordering::Relaxed) || sample_commit(now) < 384 * MIB
}

/// A soft per-frame admission limit. A single texture can cross it. The
/// queue retains unserved slots so they can be retried on later frames.
pub(crate) fn prefetch_limit(bytes: u64, pressure: bool, critical: bool, limited: bool, staging: u64) -> u64 {
    if critical {
        bytes.min(64 << 20)
    } else if pressure || (limited && staging >= GIB) {
        bytes.min(64 << 20)
    } else {
        bytes
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn bake_and_lookahead_leave_room_for_live_game_allocations() {
        // The floors are inclusive: a host sitting exactly on one admits.
        // Upload: 192 MiB for lookahead, 128 MiB for a one-shot bake.
        assert!(!has_upload_reserve(191 * MIB, false));
        assert!(has_upload_reserve(192 * MIB, false));
        assert!(!has_upload_reserve(127 * MIB, true));
        assert!(has_upload_reserve(128 * MIB, true));
        // Growth: 512 MiB for a new pool block, 192 MiB for a bake.
        assert!(!has_growth_reserve(511 * MIB, false));
        assert!(has_growth_reserve(512 * MIB, false));
        assert!(!has_growth_reserve(191 * MIB, true));
        assert!(has_growth_reserve(192 * MIB, true));
    }

    #[test]
    fn either_constraint_enters_pressure_and_both_must_recover() {
        let l = Lines { free: 3 * GIB, clear: 5 * GIB };
        assert!(next_pressure(false, 12 * GIB, 4 * GIB, l));
        assert!(next_pressure(false, 3 * GIB, 16 * GIB, l));
        assert!(next_pressure(true, 4 * GIB, 7 * GIB, l));
        assert!(next_pressure(true, 6 * GIB, 5 * GIB, l));
        assert!(!next_pressure(true, 5 * GIB, 6 * GIB, l));
        assert!(!next_pressure(false, 4 * GIB, 5 * GIB, l));
    }

    #[test]
    fn the_configured_lines_move_the_state_without_flapping() {
        // The launcher's default: enter at 1 GiB, leave at 2 GiB.
        let tight = clamp_lines(1024 * MIB, 2048 * MIB);
        assert_eq!(tight.free, 1024 * MIB);
        assert_eq!(tight.clear, 2048 * MIB);
        // 2 GiB free is idle at the defaults and still idle here.
        assert!(!next_pressure(false, 2 * GIB, 32 * GIB, tight));
        assert!(!next_pressure(false, 1100 * MIB, 32 * GIB, tight));
        assert!(next_pressure(false, 1024 * MIB, 32 * GIB, tight));
        // Once in, it stays in until the clear line, not the free one.
        assert!(next_pressure(true, 1500 * MIB, 32 * GIB, tight));
        assert!(!next_pressure(true, 2048 * MIB, 32 * GIB, tight));
        // A clear line under the free line is pushed to free + 256 MiB.
        let clamped = clamp_lines(4 * GIB, 1 * GIB);
        assert_eq!(clamped.clear, 4 * GIB + 256 * MIB);
    }

    #[test]
    fn prefetch_respects_existing_budgets_and_resumes_after_recovery() {
        assert_eq!(prefetch_limit(512 << 20, false, false, true, GIB), 64 << 20);
        assert_eq!(prefetch_limit(512 << 20, false, false, true, GIB - 1), 512 << 20);
        assert_eq!(prefetch_limit(512 << 20, false, false, false, 2 * GIB), 512 << 20);
        assert_eq!(prefetch_limit(0, true, true, false, 0), 0);
        assert_eq!(prefetch_limit(8 << 20, true, true, false, 0), 8 << 20);
        assert_eq!(prefetch_limit(512 << 20, true, false, false, 0), 64 << 20);
        assert_eq!(prefetch_limit(512 << 20, true, true, false, 0), 64 << 20);
        assert_eq!(prefetch_limit(192 << 20, false, false, false, 0), 192 << 20);
    }
}
