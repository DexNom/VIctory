//! Full screen: Alt+Enter (and F11) stretches the title's window over the
//! monitor it is on; the same keys put it back where it was.
//!
//! A borderless window, not a display mode change: the swap chain keeps the
//! size the title made it, and a flip-model chain stretches it to the window.
//! Nothing is re-made, no mode is set, and Alt+Tab is the desktop's business
//! as always. That is what the console's own output does with a title that
//! draws 1280x720 on a 4K panel.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;

use windows::Win32::Foundation::HWND;
use windows::Win32::Graphics::Gdi::{
    GetMonitorInfoW, MONITOR_DEFAULTTONEAREST, MONITORINFO, MonitorFromWindow,
};
use windows::Win32::UI::WindowsAndMessaging::*;

/// Whether the window is showing full screen now.
static FULL: AtomicBool = AtomicBool::new(false);

/// The style and place the window had before it went full screen.
static BEFORE: Mutex<Option<(isize, WINDOWPLACEMENT)>> = Mutex::new(None);

/// Whether the window is full screen now.
pub(crate) fn is_full() -> bool {
    FULL.load(Ordering::Relaxed)
}

/// Switches the window between full screen and the size and place it had.
pub(crate) fn toggle(hwnd: HWND) {
    if is_full() {
        leave(hwnd);
    } else {
        enter(hwnd);
    }
}

/// Puts the window over the monitor, remembering where it was. Asked twice, it
/// does nothing the second time: the startup path and a key press can both ask.
pub(crate) fn enter(hwnd: HWND) {
    if hwnd.0.is_null() {
        return;
    }
    let mut before = BEFORE.lock().unwrap_or_else(|e| e.into_inner());
    // SAFETY: `hwnd` is a live window: the caller has it from a message it
    // is being sent, or from the swap chain it was made for.
    unsafe {
        if FULL.load(Ordering::Relaxed) {
            return;
        }
        let style = GetWindowLongPtrW(hwnd, GWL_STYLE);
        let mut place = WINDOWPLACEMENT {
            length: size_of::<WINDOWPLACEMENT>() as u32,
            ..Default::default()
        };
        let _ = GetWindowPlacement(hwnd, &mut place);
        *before = Some((style, place));
        let monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        let mut info = MONITORINFO {
            cbSize: size_of::<MONITORINFO>() as u32,
            ..Default::default()
        };
        let area = if GetMonitorInfoW(monitor, &mut info).as_bool() {
            info.rcMonitor
        } else {
            // No monitor answered: the primary's size, at its corner.
            windows::Win32::Foundation::RECT {
                left: 0,
                top: 0,
                right: GetSystemMetrics(SM_CXSCREEN),
                bottom: GetSystemMetrics(SM_CYSCREEN),
            }
        };
        // A popup has no frame, no caption and no thick edges: the client is
        // the monitor, so the swap chain stretches over every pixel of it.
        let popup = (style & !(WS_OVERLAPPEDWINDOW.0 as isize)) | WS_POPUP.0 as isize | WS_VISIBLE.0 as isize;
        SetWindowLongPtrW(hwnd, GWL_STYLE, popup);
        let _ = SetWindowPos(
            hwnd,
            Some(HWND_TOP),
            area.left,
            area.top,
            area.right - area.left,
            area.bottom - area.top,
            SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOOWNERZORDER,
        );
        FULL.store(true, Ordering::Relaxed);
        tracing::info!(
            width = area.right - area.left,
            height = area.bottom - area.top,
            "full screen on (Alt+Enter or F11 puts it back)"
        );
    }
}

/// Puts the window back where it was before [`enter`].
pub(crate) fn leave(hwnd: HWND) {
    if hwnd.0.is_null() {
        return;
    }
    let mut before = BEFORE.lock().unwrap_or_else(|e| e.into_inner());
    // SAFETY: as in `enter`.
    unsafe {
        if !FULL.load(Ordering::Relaxed) {
            return;
        }
        if let Some((style, place)) = before.take() {
            SetWindowLongPtrW(hwnd, GWL_STYLE, style);
            let _ = SetWindowPlacement(hwnd, &place);
            let _ = SetWindowPos(
                hwnd,
                None,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_FRAMECHANGED,
            );
        }
        FULL.store(false, Ordering::Relaxed);
        tracing::info!("full screen off");
    }
}

/// Handles the keys that switch full screen; `true` when the message was one
/// of them and is spent.
///
/// Alt+Enter arrives as `WM_SYSKEYDOWN` (Alt is the system key); F11 as a
/// plain `WM_KEYDOWN`. A plain Enter is the title's own A button and is left
/// alone — eating it would turn every menu confirmation into a mode switch.
/// The matching key-up is swallowed too, so the title never sees half a
/// keystroke, and `WM_SYSCHAR` never runs (the system beeps on it when a
/// window has no menu).
pub(crate) fn key(hwnd: HWND, message: u32, wparam: usize) -> bool {
    use windows::Win32::UI::Input::KeyboardAndMouse::{VK_F11, VK_RETURN};
    let alt_enter = wparam == VK_RETURN.0 as usize;
    let f11 = wparam == VK_F11.0 as usize;
    match message {
        WM_SYSKEYDOWN if alt_enter => {
            enter(hwnd);
            true
        }
        WM_SYSKEYUP if alt_enter => true,
        // Alt+Enter also arrives as WM_SYSCHAR, where the system beeps at a
        // window with no menu bar.
        WM_SYSCHAR if alt_enter => true,
        WM_KEYDOWN | WM_KEYUP if f11 => {
            if message == WM_KEYDOWN {
                toggle(hwnd);
            }
            true
        }
        _ => false,
    }
}

/// Puts the window in full screen if `VICTORY_FULLSCREEN` asks for it, a few
/// seconds in: the title sets its own client size right after the swap chain
/// exists, and a window stretched before that would be put back.
pub(crate) fn start(hwnd: HWND) {
    static ON: std::sync::OnceLock<bool> = std::sync::OnceLock::new();
    if !*ON.get_or_init(|| crate::switch::on("VICTORY_FULLSCREEN")) {
        return;
    }
    let hwnd = hwnd.0 as usize;
    let _ = std::thread::Builder::new()
        .name("victory-full-screen".into())
        .spawn(move || {
            std::thread::sleep(std::time::Duration::from_secs(5));
            enter(HWND(hwnd as *mut _));
        });
}
