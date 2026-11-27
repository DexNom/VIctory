//! Optional desktop HUD that tracks the game's client area without blocking presents.

use std::cell::RefCell;
use std::collections::VecDeque;
use std::fmt::Write as _;
use std::sync::{Mutex, Once, OnceLock};
use std::time::{Duration, Instant};

use windows::Win32::Foundation::{COLORREF, HWND, LPARAM, LRESULT, POINT, RECT, WPARAM};
use windows::Win32::Graphics::Gdi::*;
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::WindowsAndMessaging::*;
use windows::core::{PCWSTR, w};

struct Sample {
    game: usize,
    text: String,
    received: Instant,
    graph: Option<FrameGraph>,
}

pub(crate) const FRAME_HISTORY: usize = 120;
const GRAPH_HEIGHT: i32 = 62;

struct FrameGraph {
    milliseconds: Vec<f32>,
    scale: f32,
}

thread_local! {
    // Only the HUD thread updates and paints this graph.
    static GRAPH: RefCell<Option<FrameGraph>> = const { RefCell::new(None) };
}

static PENDING: Mutex<Option<Sample>> = Mutex::new(None);

pub(crate) fn update(game: HWND, frame_times: &VecDeque<f32>, text: impl FnOnce() -> String) {
    static ENABLED: OnceLock<bool> = OnceLock::new();
    if !*ENABLED.get_or_init(|| std::env::var("VICTORY_DEBUG_HUD").as_deref() == Ok("1")) {
        return;
    }
    let mut text = text();
    let graph = frame_times.back().map(|latest| {
        let scale = frame_times.iter().copied().fold(33.3, f32::max).ceil();
        let _ = write!(
            text,
            "\nFrame time   {latest:.1} ms   scale 0–{scale:.0} ms"
        );
        FrameGraph {
            milliseconds: frame_times.iter().copied().collect(),
            scale,
        }
    });
    *PENDING.lock().unwrap_or_else(|e| e.into_inner()) = Some(Sample {
        game: game.0 as usize,
        text,
        received: Instant::now(),
        graph,
    });
    static START: Once = Once::new();
    START.call_once(|| {
        if let Err(error) = std::thread::Builder::new()
            .name("victory-hud".into())
            .spawn(|| {
                if let Err(error) = run() {
                    tracing::warn!(%error, "debug overlay could not start");
                }
            })
        {
            tracing::warn!(%error, "no debug overlay thread");
        }
    });
}

fn run() -> windows::core::Result<()> {
    const CLASS: PCWSTR = w!("VIctoryDebugHud");
    // SAFETY: the dedicated HUD thread owns this window and its message queue.
    unsafe {
        let instance = GetModuleHandleW(None)?;
        let class = WNDCLASSW {
            lpfnWndProc: Some(window_proc),
            hInstance: instance.into(),
            lpszClassName: CLASS,
            ..Default::default()
        };
        if RegisterClassW(&class) == 0 {
            return Err(windows::core::Error::from_thread());
        }
        let hwnd = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED,
            CLASS,
            w!("DexNom RayTime diagnostics"),
            WS_POPUP,
            0,
            0,
            400,
            102,
            None,
            None,
            Some(instance.into()),
            None,
        )?;
        SetLayeredWindowAttributes(hwnd, COLORREF(0), 235, LWA_ALPHA)?;
        let mut sample = None::<Sample>;
        let mut shown_at = None;
        let mut size = (0, 0);
        let mut previous = String::new();
        loop {
            let mut msg = MSG::default();
            while PeekMessageW(&mut msg, None, 0, 0, PM_REMOVE).as_bool() {
                if msg.message == WM_QUIT {
                    return Ok(());
                }
                let _ = TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }
            let newest = PENDING.lock().unwrap_or_else(|e| e.into_inner()).take();
            let graph_changed = newest.is_some();
            if let Some(mut newest) = newest {
                GRAPH.with(|graph| graph.replace(newest.graph.take()));
                sample = Some(newest);
            }
            let position = sample.as_ref().and_then(position);
            if let (Some(sample), Some((x, y))) = (&sample, position) {
                let age = sample.received.elapsed().as_secs();
                let text = if age >= 3 {
                    format!("{}\nNo new frame sample for {age} s", sample.text)
                } else {
                    sample.text.clone()
                };
                if text != previous || graph_changed {
                    let mut words: Vec<u16> = text.encode_utf16().chain([0]).collect();
                    let dc = GetDC(Some(hwnd));
                    let prior_font = SelectObject(dc, GetStockObject(ANSI_FIXED_FONT));
                    let mut bounds = RECT::default();
                    let len = words.len() - 1;
                    DrawTextW(
                        dc,
                        &mut words[..len],
                        &mut bounds,
                        DT_NOPREFIX | DT_LEFT | DT_CALCRECT,
                    );
                    SelectObject(dc, prior_font);
                    ReleaseDC(Some(hwnd), dc);
                    let plot = GRAPH.with(|graph| graph.borrow().is_some());
                    let plot_height = if plot { GRAPH_HEIGHT + 6 } else { 0 };
                    size = (
                        size.0.max(bounds.right + 24),
                        bounds.bottom + 16 + plot_height,
                    );
                    SetWindowTextW(hwnd, PCWSTR(words.as_ptr()))?;
                    previous = text;
                }
                let placement = Some((x, y, size));
                if shown_at != placement {
                    SetWindowPos(
                        hwnd,
                        Some(HWND_TOPMOST),
                        x,
                        y,
                        size.0,
                        size.1,
                        SWP_NOACTIVATE | SWP_SHOWWINDOW,
                    )?;
                    shown_at = placement;
                }
                if graph_changed {
                    let _ = InvalidateRect(Some(hwnd), None, false);
                }
            } else if shown_at.take().is_some() {
                let _ = ShowWindow(hwnd, SW_HIDE);
            }
            std::thread::sleep(Duration::from_millis(100));
        }
    }
}

fn position(sample: &Sample) -> Option<(i32, i32)> {
    let game = HWND(sample.game as *mut _);
    // SAFETY: the window functions handle a destroyed or hidden game window.
    unsafe {
        if GetForegroundWindow() != game || IsIconic(game).as_bool() {
            return None;
        }
        let mut origin = POINT { x: 12, y: 12 };
        ClientToScreen(game, &mut origin)
            .as_bool()
            .then_some((origin.x, origin.y))
    }
}

impl FrameGraph {
    fn points(&self, rect: RECT) -> Vec<POINT> {
        let last = self.milliseconds.len() - 1;
        let width = rect.right - rect.left - 1;
        let height = rect.bottom - rect.top - 1;
        self.milliseconds
            .iter()
            .enumerate()
            .map(|(i, &ms)| POINT {
                x: if last == 0 {
                    rect.right - 1
                } else {
                    rect.left + width * i as i32 / last as i32
                },
                y: rect.bottom
                    - 1
                    - (height as f32 * (ms / self.scale).clamp(0.0, 1.0)).round() as i32,
            })
            .collect()
    }

    fn paint(&self, dc: HDC, client: RECT) {
        let rect = RECT {
            left: 12,
            top: client.bottom - 8 - GRAPH_HEIGHT,
            right: client.right - 12,
            bottom: client.bottom - 8,
        };
        if rect.right <= rect.left || rect.top < client.top {
            return;
        }
        let points = self.points(rect);
        // SAFETY: called during WM_PAINT with the HUD's live device context.
        unsafe {
            let background = CreateSolidBrush(COLORREF(0x302518));
            FillRect(dc, &rect, background);
            let _ = DeleteObject(background.into());
            let trace = COLORREF(0xedda78);
            if let [point] = points.as_slice() {
                let _ = SetPixelV(dc, point.x, point.y, trace);
            } else {
                let pen = CreatePen(PS_SOLID, 1, trace);
                let prior = SelectObject(dc, pen.into());
                let _ = Polyline(dc, &points);
                SelectObject(dc, prior);
                let _ = DeleteObject(pen.into());
            }
        }
    }
}

unsafe extern "system" fn window_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    // SAFETY: handles are supplied by Windows for this HUD window.
    unsafe {
        match msg {
            WM_NCHITTEST => LRESULT(HTTRANSPARENT as isize),
            WM_MOUSEACTIVATE => LRESULT(MA_NOACTIVATE as isize),
            WM_SETTEXT => {
                let result = DefWindowProcW(hwnd, msg, wparam, lparam);
                let _ = InvalidateRect(Some(hwnd), None, false);
                result
            }
            WM_PAINT => {
                let mut paint = PAINTSTRUCT::default();
                let dc = BeginPaint(hwnd, &mut paint);
                let mut rect = RECT::default();
                let _ = GetClientRect(hwnd, &mut rect);
                let bg = CreateSolidBrush(COLORREF(0x241b13));
                FillRect(dc, &rect, bg);
                let _ = DeleteObject(bg.into());
                let mut words = vec![0u16; GetWindowTextLengthW(hwnd).max(0) as usize + 1];
                let len = GetWindowTextW(hwnd, &mut words).max(0) as usize;
                let font = SelectObject(dc, GetStockObject(ANSI_FIXED_FONT));
                SetTextColor(dc, COLORREF(0xf4efe5));
                SetBkMode(dc, TRANSPARENT);
                let mut text_rect = RECT {
                    left: rect.left + 12,
                    top: rect.top + 8,
                    ..rect
                };
                DrawTextW(dc, &mut words[..len], &mut text_rect, DT_NOPREFIX | DT_LEFT);
                SelectObject(dc, font);
                GRAPH.with(|graph| {
                    if let Some(graph) = &*graph.borrow() {
                        graph.paint(dc, rect);
                    }
                });
                let _ = EndPaint(hwnd, &paint);
                LRESULT(0)
            }
            WM_DESTROY => {
                PostQuitMessage(0);
                LRESULT(0)
            }
            _ => DefWindowProcW(hwnd, msg, wparam, lparam),
        }
    }
}
