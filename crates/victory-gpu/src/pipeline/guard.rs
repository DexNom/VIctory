//! Surviving the host driver's shader compiler.
//!
//! AMD's RDNA 4 driver (RX 9060 XT and RX 9070 XT, 32.0.32015 and
//! 32.0.32029) reads through a null pointer inside its shader compiler
//! (`amdxc64.dll`, a read of 0x8) while it creates some of our pipelines,
//! and the process ends there. The programs it fails on are valid DXIL
//! (`dxil.dll` accepts them) and differ from run to run as the title binds
//! them, so no rule in the translator avoids them all.
//!
//! Each host pipeline is therefore created on a thread of its own. If the
//! driver faults with a null read on that thread, a vectored exception
//! handler moves the thread off the driver's frames into [`abandon`], which
//! reports the pipeline as faulted to the thread that asked for it and then
//! parks for good: the driver's frames are left as they are, never resumed
//! or unwound. The pipeline is left out like an untranslated one (its draws
//! are dropped), and its key goes into `driver-faults.txt` beside the
//! pipeline library so later runs (and `victory-warm`) do not compile it
//! again. `VICTORY_DRIVER_GUARD=0` creates pipelines on the asking thread,
//! as before.

use std::cell::Cell;
use std::collections::HashSet;
use std::io::Write as _;
use std::path::PathBuf;
use std::sync::mpsc::{RecvTimeoutError, Sender, channel};
use std::time::Duration;
use std::sync::{Mutex, OnceLock};

use windows_sys::Win32::Foundation::EXCEPTION_ACCESS_VIOLATION;
use windows_sys::Win32::System::Diagnostics::Debug::{
    AddVectoredExceptionHandler, EXCEPTION_POINTERS,
};
use windows_sys::Win32::System::LibraryLoader::{
    GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
    GetModuleFileNameW, GetModuleHandleExW,
};

const EXCEPTION_CONTINUE_EXECUTION: i32 = -1;
const EXCEPTION_CONTINUE_SEARCH: i32 = 0;

/// Whether pipelines are made under the guard (`VICTORY_DRIVER_GUARD`, on
/// unless `0`).
pub(crate) fn enabled() -> bool {
    static ON: OnceLock<bool> = OnceLock::new();
    *ON.get_or_init(|| std::env::var_os("VICTORY_DRIVER_GUARD").is_none_or(|v| v != "0"))
}

/// What a guarded call came to.
enum Outcome<T> {
    Done(T),
    /// The driver faulted at this address (module and offset).
    Faulted(String),
}

/// An outcome sent back across threads.
struct Carried<T>(Outcome<T>);
// SAFETY: the host pipeline's D3D12 objects are free-threaded.
unsafe impl<T> Send for Carried<T> {}

/// The guarded call running on this thread: where a fault is reported.
struct Guarded {
    faulted: Box<dyn Fn(String)>,
    /// Filled by the handler before it moves the thread to [`abandon`].
    at: Cell<usize>,
}

thread_local! {
    static CURRENT: Cell<*const Guarded> = const { Cell::new(std::ptr::null()) };
}

/// How long a pipeline may take on its guard thread before it is given up
/// as hung (`VICTORY_DRIVER_GUARD_S`, 120 s). The slowest the title has
/// compile in 5-9 s, and the slowest known (`too_dear`) in 85 s.
fn patience() -> Duration {
    static S: OnceLock<u64> = OnceLock::new();
    Duration::from_secs(*S.get_or_init(|| {
        std::env::var("VICTORY_DRIVER_GUARD_S").ok().and_then(|v| v.trim().parse().ok()).unwrap_or(120)
    }))
}

/// Runs `f` on a thread of its own under the guard: `None` if the host
/// driver faulted in it or held it past [`patience`] (logged, and `key`
/// recorded), else what `f` returned. `f` owns what it uses: a thread
/// given up on keeps it.
pub(crate) fn run<T, F>(key: &str, what: &str, f: F) -> Option<T>
where
    T: 'static,
    F: FnOnce() -> T + Send + 'static,
{
    install();
    let (tx, rx) = channel::<Carried<T>>();
    let fault_tx: Sender<Carried<T>> = tx.clone();
    let spawned = std::thread::Builder::new()
        .name("victory-pipeline-guard".into())
        // The driver's compiler recurses deeply.
        .stack_size(16 << 20)
        .spawn(move || {
            let guarded = Guarded {
                faulted: Box::new(move |at| {
                    let _ = fault_tx.send(Carried(Outcome::Faulted(at)));
                }),
                at: Cell::new(0),
            };
            CURRENT.with(|c| c.set(&guarded));
            let out = f();
            CURRENT.with(|c| c.set(std::ptr::null()));
            let _ = tx.send(Carried(Outcome::Done(out)));
        });
    if let Err(e) = spawned {
        tracing::warn!(error = %e, "no pipeline guard thread; no pipeline");
        return None;
    }
    let why = match rx.recv_timeout(patience()) {
        Ok(Carried(Outcome::Done(out))) => return Some(out),
        Ok(Carried(Outcome::Faulted(at))) => format!("the host driver's shader compiler faulted at {at}"),
        Err(RecvTimeoutError::Timeout) => format!("the host driver held it past {} s", patience().as_secs()),
        Err(RecvTimeoutError::Disconnected) => return None,
    };
    tracing::warn!(key, pipeline = what, "{why} making this pipeline; it is left out (driver-faults.txt)");
    record(key);
    None
}

/// Where the faulting keys are kept: beside the pipeline library.
fn file() -> Option<PathBuf> {
    super::library::dir().map(|d| d.join("driver-faults.txt"))
}

fn faults() -> &'static Mutex<HashSet<String>> {
    static FAULTS: OnceLock<Mutex<HashSet<String>>> = OnceLock::new();
    FAULTS.get_or_init(|| {
        let keys = file()
            .and_then(|f| std::fs::read_to_string(f).ok())
            .map(|text| {
                text.lines()
                    .map(|l| l.split_whitespace().next().unwrap_or("").to_owned())
                    .filter(|k| !k.is_empty())
                    .collect()
            })
            .unwrap_or_default();
        Mutex::new(keys)
    })
}

/// Whether an earlier run's driver faulted making the pipeline `key`.
pub(crate) fn faulted_before(key: &str) -> bool {
    enabled() && faults().lock().unwrap_or_else(|e| e.into_inner()).contains(key)
}

fn record(key: &str) {
    if !faults().lock().unwrap_or_else(|e| e.into_inner()).insert(key.to_owned()) {
        return;
    }
    let Some(path) = file() else { return };
    if let Ok(mut f) = std::fs::OpenOptions::new().create(true).append(true).open(&path) {
        let _ = writeln!(f, "{key}");
    }
}

fn install() {
    static ONCE: std::sync::Once = std::sync::Once::new();
    ONCE.call_once(|| {
        // SAFETY: registers a handler that only acts on our guard threads.
        unsafe { AddVectoredExceptionHandler(1, Some(handler)) };
    });
}

/// The module holding `address`, as `name+offset`, and whether it is a
/// host GPU driver's shader compiler.
fn module_of(address: usize) -> (String, bool) {
    let mut module = std::ptr::null_mut();
    // SAFETY: looks up the module containing `address`; no reference taken.
    let found = unsafe {
        GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            address as *const u16,
            &mut module,
        )
    };
    if found == 0 {
        return (format!("{address:#x}"), false);
    }
    let mut path = [0u16; 260];
    // SAFETY: `module` is loaded; the buffer's length is passed.
    let len = unsafe { GetModuleFileNameW(module, path.as_mut_ptr(), path.len() as u32) };
    let path = String::from_utf16_lossy(&path[..len as usize]);
    let name = path.rsplit('\\').next().unwrap_or(&path).to_owned();
    let lower = name.to_ascii_lowercase();
    let driver = lower.starts_with("amdxc") || lower.starts_with("d3d12core");
    (format!("{name}+{:#x}", address - module as usize), driver)
}

unsafe extern "system" fn handler(info: *mut EXCEPTION_POINTERS) -> i32 {
    let current = CURRENT.with(Cell::get);
    if current.is_null() {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    // SAFETY: the system passes valid exception pointers.
    let (record, context) = unsafe { (&*(*info).ExceptionRecord, &mut *(*info).ContextRecord) };
    if record.ExceptionCode != EXCEPTION_ACCESS_VIOLATION {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    // Only a read or write near null: not a guard page or the like the
    // driver may handle itself.
    let target = record.ExceptionInformation[1];
    if target >= 0x10000 {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    let at = record.ExceptionAddress as usize;
    let (_, driver) = module_of(at);
    if !driver {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    // SAFETY: `current` is this thread's live guard.
    unsafe { (*current).at.set(at) };
    // Leave the driver's frames where they are, below them a fresh frame
    // for `abandon`, aligned as after a call.
    let rsp = (context.Rsp - 0x4000) & !0xf;
    context.Rsp = rsp - 8;
    context.Rip = abandon as *const () as u64;
    EXCEPTION_CONTINUE_EXECUTION
}

/// Where a guard thread goes from a fault: reports it and parks for good.
extern "system" fn abandon() -> ! {
    let current = CURRENT.with(|c| c.replace(std::ptr::null()));
    if !current.is_null() {
        // SAFETY: the guard lives in this thread's (never resumed) frame.
        let guarded = unsafe { &*current };
        let (at, _) = module_of(guarded.at.get());
        (guarded.faulted)(at);
    }
    loop {
        std::thread::park();
    }
}
