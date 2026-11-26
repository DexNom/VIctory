//! Opt-in CPU-side D3D12 validation diagnostics. Callback attribution is
//! temporal and thread-local, never proof of causality.
use std::cell::Cell;
use std::sync::OnceLock;
use std::sync::atomic::{AtomicU64, Ordering};
use windows::Win32::Graphics::Direct3D12::*;
use windows::core::{Interface, PCSTR};

static NEXT: AtomicU64 = AtomicU64::new(1);
static CACHE_MESSAGES: AtomicU64 = AtomicU64::new(0);
static CACHE_RESULTS: AtomicU64 = AtomicU64::new(0);
static SUCCESSES: AtomicU64 = AtomicU64::new(0);
static SCOPED: AtomicU64 = AtomicU64::new(0);
static UNSCOPED: AtomicU64 = AtomicU64::new(0);
static FAILURES: AtomicU64 = AtomicU64::new(0);
static QUEUED: AtomicU64 = AtomicU64::new(0);
static REGISTERED: OnceLock<bool> = OnceLock::new();

pub(crate) fn enabled() -> bool {
    static ON: OnceLock<bool> = OnceLock::new();
    cfg!(feature = "diagnostics")
        && *ON.get_or_init(|| std::env::var_os("VICTORY_D3D12_PSO_TRACE").is_some_and(|v| v == "1"))
}

#[derive(Clone, Copy)]
struct Attempt {
    id: u64,
    hash: u64,
    phase: &'static str,
    selected: bool,
}
thread_local! { static CURRENT: Cell<Option<Attempt>> = const { Cell::new(None) }; }
struct Restore(Option<Attempt>);
impl Drop for Restore {
    fn drop(&mut self) {
        let _ = CURRENT.try_with(|c| c.set(self.0));
    }
}

fn selected(hash: &str) -> bool {
    static FILTER: OnceLock<Option<String>> = OnceLock::new();
    FILTER
        .get_or_init(|| {
            std::env::var_os("VICTORY_D3D12_PSO_ONLY").map(|v| v.into_string().unwrap_or_default())
        })
        .as_ref()
        .is_none_or(|f| f.split(',').any(|h| h.trim().eq_ignore_ascii_case(hash)))
}

fn permit(counter: &AtomicU64, limit: u64, group: &str) -> bool {
    let n = counter.fetch_add(1, Ordering::Relaxed);
    if n == limit {
        tracing::warn!(group, limit, "PSOTRACE1 further messages suppressed");
    }
    n < limit
}

pub(crate) fn call<T>(
    hash: &str,
    phase: &'static str,
    f: impl FnOnce() -> windows::core::Result<T>,
) -> windows::core::Result<T> {
    if !enabled() {
        return f();
    }
    let Some(hash_value) = u64::from_str_radix(hash, 16).ok() else {
        return f();
    };
    let attempt = Attempt {
        id: NEXT.fetch_add(1, Ordering::Relaxed),
        hash: hash_value,
        phase,
        selected: selected(hash),
    };
    let _restore = Restore(CURRENT.with(|c| c.replace(Some(attempt))));
    let result = f();
    if attempt.selected {
        match &result {
            Err(e) if phase == "load" && permit(&CACHE_RESULTS, 128, "cache misses") => {
                tracing::debug!(attempt = attempt.id, shader = %format_args!("{:016x}", attempt.hash), phase, hresult = %format_args!("0x{:08x}", e.code().0 as u32), "PSOTRACE1 cache load failed");
            }
            Err(e) if phase != "load" && permit(&FAILURES, 128, "root/create failures") => {
                tracing::warn!(attempt = attempt.id, shader = %format_args!("{:016x}", attempt.hash), phase, hresult = %format_args!("0x{:08x}", e.code().0 as u32), "PSOTRACE1 call failed");
            }
            Ok(_) if permit(&SUCCESSES, 256, "successful calls") => {
                tracing::info!(attempt = attempt.id, shader = %format_args!("{:016x}", attempt.hash), phase, "PSOTRACE1 call succeeded");
            }
            _ => {}
        }
    }
    result
}

unsafe extern "system" fn callback(
    category: D3D12_MESSAGE_CATEGORY,
    severity: D3D12_MESSAGE_SEVERITY,
    id: D3D12_MESSAGE_ID,
    description: PCSTR,
    _: *mut core::ffi::c_void,
) {
    // Never call D3D12 from a callback or unwind through the ABI.
    let _ = std::panic::catch_unwind(|| {
        if severity.0 > D3D12_MESSAGE_SEVERITY_WARNING.0 {
            return;
        }
        let attempt = CURRENT.try_with(Cell::get).ok().flatten();
        if attempt.is_some_and(|a| !a.selected) {
            return;
        }
        let cache_miss = attempt.is_some_and(|a| a.phase == "load") && id.0 == 971;
        let allowed = if cache_miss {
            permit(&CACHE_MESSAGES, 128, "cache validation")
        } else if attempt.is_some() {
            permit(&SCOPED, 256, "scoped validation")
        } else {
            permit(&UNSCOPED, 64, "unscoped validation")
        };
        if !allowed {
            return;
        }
        let bytes = if description.is_null() {
            &[][..]
        } else {
            // SAFETY: D3D12 supplies a NUL-terminated string valid in the callback.
            unsafe { std::ffi::CStr::from_ptr(description.0.cast()).to_bytes() }
        };
        let text = String::from_utf8_lossy(&bytes[..bytes.len().min(4096)]);
        if let Some(a) = attempt {
            if cache_miss {
                tracing::debug!(attempt = a.id, shader = %format_args!("{:016x}", a.hash), phase = a.phase, category = category.0, severity = severity.0, id = id.0, truncated = bytes.len() > 4096, "PSOTRACE1 same-thread cache: {text}");
                return;
            }
            tracing::warn!(attempt = a.id, shader = %format_args!("{:016x}", a.hash), phase = a.phase, category = category.0, severity = severity.0, id = id.0, truncated = bytes.len() > 4096, "PSOTRACE1 same-thread: {text}");
        } else {
            tracing::warn!(
                category = category.0,
                severity = severity.0,
                id = id.0,
                truncated = bytes.len() > 4096,
                "PSOTRACE1 unassigned: {text}"
            );
        }
    });
}

pub(crate) fn register(device: &ID3D12Device, queue: Option<&ID3D12InfoQueue>) {
    if !enabled() {
        return;
    }
    // SAFETY: callback uses process-lifetime statics, no borrowed context.
    // Host is a process-lifetime singleton; registration lives with its device.
    let registered = unsafe {
        if let Some(q) = queue {
            tracing::info!(
                stored = q.GetNumStoredMessages(),
                retrievable = q.GetNumStoredMessagesAllowedByRetrievalFilter(),
                denied = q.GetNumMessagesDeniedByStorageFilter(),
                discarded = q.GetNumMessagesDiscardedByMessageCountLimit(),
                "PSOTRACE1 InfoQueue available"
            );
        } else {
            tracing::warn!("PSOTRACE1 InfoQueue unavailable");
        }
        match device.cast::<ID3D12InfoQueue1>() {
            Ok(q) => {
                let mut cookie = 0;
                match q.RegisterMessageCallback(
                    Some(callback),
                    D3D12_MESSAGE_CALLBACK_IGNORE_FILTERS,
                    std::ptr::null_mut(),
                    &mut cookie,
                ) {
                    Ok(()) => {
                        tracing::info!(
                            cookie,
                            "PSOTRACE1 InfoQueue1 callback registered (ignore filters)"
                        );
                        true
                    }
                    Err(e) => {
                        tracing::warn!(error = %e, "PSOTRACE1 callback registration failed");
                        false
                    }
                }
            }
            Err(e) => {
                tracing::warn!(error = %e, "PSOTRACE1 InfoQueue1 unavailable");
                false
            }
        }
    };
    let _ = REGISTERED.set(registered);
}

pub(crate) fn queued(m: &D3D12_MESSAGE, text: &str) {
    // Legacy queue remains drained for device-removed reporting. Callback
    // and queue entries may duplicate each other; queue attribution is unknown.
    if !REGISTERED.get().copied().unwrap_or(false) && permit(&QUEUED, 256, "shared queue") {
        let end = text.floor_char_boundary(text.len().min(4096));
        tracing::warn!(
            id = m.ID.0,
            severity = m.Severity.0,
            category = m.Category.0,
            truncated = end < text.len(),
            "PSOTRACE1 shared queue (ambiguous): {}",
            &text[..end]
        );
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn bounded_counter_and_nested_scope() {
        let n = AtomicU64::new(0);
        assert!(permit(&n, 1, "test"));
        assert!(!permit(&n, 1, "test"));
        let outer = Attempt {
            id: 1,
            hash: 2,
            phase: "outer",
            selected: true,
        };
        let inner = Attempt { id: 2, ..outer };
        let _outer = Restore(CURRENT.with(|c| c.replace(Some(outer))));
        {
            let _inner = Restore(CURRENT.with(|c| c.replace(Some(inner))));
            assert_eq!(CURRENT.with(Cell::get).unwrap().id, 2);
        }
        assert_eq!(CURRENT.with(Cell::get).unwrap().id, 1);
        assert!(
            std::thread::spawn(|| CURRENT.with(Cell::get).is_none())
                .join()
                .unwrap()
        );
    }
}
