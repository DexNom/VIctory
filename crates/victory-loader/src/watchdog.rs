//! The title's hang watchdog, kept from ending the title.
//!
//! The title times its waits on another GPU queue. When one runs past its
//! limit, a handler logs the title's error tag and then faults on purpose
//! (a store to address 0), and the title's watchdog thread does the same
//! after the handler returns. On this layer such a wait can run past the
//! limit when a frame is merely slow (a Steam Deck, a long load, a debug
//! log), and the title ended there. With both stores replaced the handler
//! logs and returns, the watchdog thread checks again a second later, and
//! the wait goes on. `VICTORY_TITLE_WATCHDOG=1` keeps the title's stores.
//!
//! The handler's offset and replacement come from victory-linux-nvidia
//! (dfmbrollins); the watchdog thread's is new here. Both check the bytes
//! they replace first and leave other builds alone.

/// The handler's store, in the title's image (the build this was found in).
const AT: usize = 0x23cf683;
/// Signature of the store at [`AT`].
const EXPECTED: [u8; 11] = [
    0xc7, 0x04, 0x25, 0x00, 0x00, 0x00, 0x00, 0x21, 0xd4, 0x8b, 0x4d,
];
/// Eleven bytes of no-op (a 9-byte and a 2-byte `nop`).
const NOPS: [u8; 11] = [
    0x66, 0x0f, 0x1f, 0x84, 0x00, 0x00, 0x00, 0x00, 0x00, 0x66, 0x90,
];

/// Takes the store out of the title's hang handler, unless asked not to.
///
/// # Safety
/// The image of `size` bytes at `base` is mapped, and none of its code runs yet.
#[cfg(windows)]
pub unsafe fn defuse(base: usize, size: usize) {
    use windows_sys::Win32::System::Diagnostics::Debug::FlushInstructionCache;
    use windows_sys::Win32::System::Memory::{PAGE_EXECUTE_READWRITE, VirtualProtect};
    use windows_sys::Win32::System::Threading::GetCurrentProcess;
    if std::env::var_os("VICTORY_TITLE_WATCHDOG").is_some_and(|v| v == "1") {
        tracing::info!("title watchdog: kept (VICTORY_TITLE_WATCHDOG=1)");
        return;
    }
    if AT + EXPECTED.len() > size {
        tracing::warn!("title watchdog: not patched, outside the image");
        return;
    }
    let at = base + AT;
    // SAFETY: inside the mapped image (checked above).
    let there = unsafe { std::slice::from_raw_parts(at as *const u8, EXPECTED.len()) };
    if there != EXPECTED {
        tracing::warn!(
            "title watchdog: not patched, the title's bytes there are not the ones expected (another build?)"
        );
        return;
    }
    let mut old = 0;
    // SAFETY: our own process, a range of the mapped image; nothing runs it yet.
    unsafe {
        if VirtualProtect(at as _, NOPS.len(), PAGE_EXECUTE_READWRITE, &mut old) == 0 {
            tracing::warn!("title watchdog: not patched, the page could not be made writable");
            return;
        }
        std::ptr::copy_nonoverlapping(NOPS.as_ptr(), at as *mut u8, NOPS.len());
        let mut was = 0;
        VirtualProtect(at as _, NOPS.len(), old, &mut was);
        FlushInstructionCache(GetCurrentProcess(), at as _, NOPS.len());
    }
    tracing::info!(
        site = format_args!("{AT:#x}"),
        "title watchdog: the hang handler logs and returns"
    );
    // SAFETY: the caller's promise.
    unsafe { defuse_caller(base, size) };
}

/// The watchdog thread's own store, after it called the handler.
const CALLER_AT: usize = 0x2215c35;
/// Signature of the store at [`CALLER_AT`].
const CALLER_EXPECTED: [u8; 12] = [0xc7, 0x04, 0x25, 0, 0, 0, 0, 0, 0, 0, 0, 0xcc];
/// Ours: reload the thread's last-check time from its global (+0x71b6338,
/// RIP-relative from +0x2215c3c) and jump to +0x2215bfd, where the thread
/// goes round its loop again as when no wait ran out; then a 3-byte no-op.
/// (Jumping earlier, to +0x2215bf3, overwrote a register the loop calls
/// through and crashed.)
const CALLER_CODE: [u8; 12] = [
    0x4c, 0x8b, 0x3d, 0xfc, 0x06, 0xfa, 0x04, 0xeb, 0xbf, 0x0f, 0x1f, 0x00,
];

#[cfg(windows)]
unsafe fn defuse_caller(base: usize, size: usize) {
    use windows_sys::Win32::System::Diagnostics::Debug::FlushInstructionCache;
    use windows_sys::Win32::System::Memory::{PAGE_EXECUTE_READWRITE, VirtualProtect};
    use windows_sys::Win32::System::Threading::GetCurrentProcess;
    if CALLER_AT + CALLER_EXPECTED.len() > size {
        return;
    }
    let at = base + CALLER_AT;
    // SAFETY: inside the mapped image (checked above).
    let there = unsafe { std::slice::from_raw_parts(at as *const u8, CALLER_EXPECTED.len()) };
    if there != CALLER_EXPECTED {
        tracing::warn!("title watchdog: its thread's store not patched, other bytes there");
        return;
    }
    let mut old = 0;
    // SAFETY: our own process, a range of the mapped image; nothing runs it yet.
    unsafe {
        if VirtualProtect(at as _, CALLER_CODE.len(), PAGE_EXECUTE_READWRITE, &mut old) == 0 {
            tracing::warn!(
                "title watchdog: its thread's store not patched, the page could not be made writable"
            );
            return;
        }
        std::ptr::copy_nonoverlapping(CALLER_CODE.as_ptr(), at as *mut u8, CALLER_CODE.len());
        let mut was = 0;
        VirtualProtect(at as _, CALLER_CODE.len(), old, &mut was);
        FlushInstructionCache(GetCurrentProcess(), at as _, CALLER_CODE.len());
    }
    tracing::info!(
        site = format_args!("{CALLER_AT:#x}"),
        "title watchdog: its thread goes round again instead of ending the title"
    );
}
