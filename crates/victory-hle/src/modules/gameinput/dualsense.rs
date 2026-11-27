//! Wired DualSense HID input through the Windows HID class driver.

use std::time::{Duration, Instant};

use hidapi::{BusType, HidApi, HidDevice, HidResult};

use super::{
    A, B, DOWN, LEFT, LEFT_SHOULDER, LEFT_STICK, MENU, PadState, RIGHT, RIGHT_SHOULDER,
    RIGHT_STICK, UP, VIEW, X, Y,
};

const SONY: u16 = 0x054c;
const DUALSENSE: u16 = 0x0ce6;
const REPORT_LEN: usize = 64;
const RECHECK: Duration = Duration::from_secs(1);
/// USB output report 0x02: the ID and 47 bytes of settings.
const OUTPUT_LEN: usize = 48;
/// The first firmware that takes "vibration v2" rumble (flag 2 bit 2).
const VIBRATION_V2_FIRMWARE: u16 = 0x0215;

#[derive(Default)]
pub(super) struct DualSense {
    device: Option<HidDevice>,
    state: Option<PadState>,
    retry_at: Option<Instant>,
    /// Whether the pad's firmware takes vibration v2 (None: unknown).
    vibration_v2: Option<bool>,
    /// The motor levels last written, to write only changes.
    sent: Option<Motors>,
}

/// Rumble levels, 0-255: the two body motors (played on the haptics) and
/// the Xbox trigger motors (played as adaptive-trigger vibration).
#[derive(Clone, Copy, Default, PartialEq, Eq, Debug)]
pub(super) struct Motors {
    pub low: u8,
    pub high: u8,
    pub left_trigger: u8,
    pub right_trigger: u8,
}

impl DualSense {
    pub(super) fn poll(&mut self) -> Option<PadState> {
        if self.device.is_none() {
            let now = Instant::now();
            if self.retry_at.is_some_and(|retry| now < retry) {
                return None;
            }
            self.retry_at = Some(now + RECHECK);
            match open() {
                Ok(device) => {
                    self.vibration_v2 = device.as_ref().and_then(firmware).map(|v| v >= VIBRATION_V2_FIRMWARE);
                    self.sent = None;
                    self.device = device;
                }
                Err(error) => tracing::debug!(%error, "could not open DualSense"),
            }
        }

        let device = self.device.as_ref()?;
        let mut report = [0; REPORT_LEN];
        // Drain queued reports without waiting. Keep the bound even when a
        // device submits reports faster than the host can consume them.
        for _ in 0..32 {
            match device.read_timeout(&mut report, 0) {
                Ok(0) => break,
                Ok(len) => {
                    if let Some(state) = report.get(..len).and_then(decode) {
                        if self.state.is_none() {
                            tracing::info!("wired DualSense input connected");
                        }
                        if self
                            .state
                            .is_none_or(|previous| previous.buttons != state.buttons)
                        {
                            tracing::debug!(buttons = state.buttons, "DualSense buttons");
                        }
                        self.state = Some(state);
                    }
                }
                Err(error) => {
                    tracing::debug!(%error, "DualSense input disconnected");
                    self.device = None;
                    self.state = None;
                    self.retry_at = Some(Instant::now() + RECHECK);
                    break;
                }
            }
        }
        self.state
    }

    /// Sets the rumble of a connected pad (`VICTORY_DS_RUMBLE=0` turns it
    /// off, `VICTORY_DS_TRIGGERS=0` keeps the triggers quiet).
    pub(super) fn rumble(&mut self, motors: Motors) {
        static SETTINGS: std::sync::LazyLock<(bool, bool, u8)> = std::sync::LazyLock::new(|| {
            let off = |key| std::env::var(key).is_ok_and(|v| v == "0");
            let hz = std::env::var("VICTORY_DS_TRIGGER_HZ")
                .ok()
                .and_then(|v| v.parse().ok())
                .unwrap_or(40);
            (!off("VICTORY_DS_RUMBLE"), !off("VICTORY_DS_TRIGGERS"), hz)
        });
        let (on, triggers, hz) = *SETTINGS;
        let Some(device) = self.device.as_ref() else {
            return;
        };
        let motors = if on { motors } else { Motors::default() };
        if self.sent == Some(motors) {
            return;
        }
        let report = output_report(self.vibration_v2, triggers.then_some(hz), motors);
        match device.write(&report) {
            Ok(_) => self.sent = Some(motors),
            Err(error) => tracing::debug!(%error, "DualSense rumble not written"),
        }
    }
}

/// The firmware version from feature report 0x20, bytes 44-45.
fn firmware(device: &HidDevice) -> Option<u16> {
    let mut report = [0u8; 64];
    report[0] = 0x20;
    let len = device.get_feature_report(&mut report).ok()?;
    (len >= 46).then(|| u16::from_le_bytes([report[44], report[45]]))
}

/// USB output report 0x02 with the motors on the haptics and, with
/// `trigger_hz`, both adaptive triggers vibrating (or off when quiet).
fn output_report(vibration_v2: Option<bool>, trigger_hz: Option<u8>, motors: Motors) -> [u8; OUTPUT_LEN] {
    let mut report = [0u8; OUTPUT_LEN];
    report[0] = 0x02;
    let c = &mut report[1..];
    // The haptics play the rumble, not audio.
    c[0] = 0x02;
    match vibration_v2 {
        Some(true) => c[38] |= 0x04,
        Some(false) => c[0] |= 0x01,
        None => {
            c[0] |= 0x01;
            c[38] |= 0x04;
        }
    }
    c[2] = motors.high;
    c[3] = motors.low;
    if let Some(hz) = trigger_hz {
        c[0] |= 0x04 | 0x08;
        trigger_effect(&mut c[10..21], motors.right_trigger, hz);
        trigger_effect(&mut c[21..32], motors.left_trigger, hz);
    }
    report
}

/// The adaptive trigger's vibration effect (0x26) over its whole travel, at
/// strength 1 to 8; off (0x05) at level 0.
fn trigger_effect(effect: &mut [u8], level: u8, hz: u8) {
    effect.fill(0);
    let strength = u32::from(level).div_ceil(32); // 0..=8
    if strength == 0 || hz == 0 {
        effect[0] = 0x05;
        return;
    }
    let per_zone = (strength - 1) & 0x7;
    let zones: u32 = (0..10).fold(0, |all, zone| all | per_zone << (3 * zone));
    effect[0] = 0x26;
    effect[1..3].copy_from_slice(&0x3ffu16.to_le_bytes());
    effect[3..7].copy_from_slice(&zones.to_le_bytes());
    effect[9] = hz;
}

fn open() -> HidResult<Option<HidDevice>> {
    let api = HidApi::new()?;
    api.device_list()
        .find(|device| {
            device.vendor_id() == SONY
                && device.product_id() == DUALSENSE
                && matches!(device.bus_type(), BusType::Usb)
                && device.usage_page() == 0x01
                && device.usage() == 0x05
        })
        .map(|device| device.open_device(&api))
        .transpose()
}

fn axis(value: u8) -> f32 {
    let offset = f32::from(value) - 128.0;
    (offset / 127.0).max(-1.0)
}

/// The USB report begins with its ID, four stick axes, two trigger axes,
/// a sequence byte, and the hat/face and shoulder/system button fields.
fn decode(report: &[u8]) -> Option<PadState> {
    let &[0x01, lx, ly, rx, ry, lt, rt, _, face, shoulders, ..] = report else {
        return None;
    };
    if report.len() != REPORT_LEN {
        return None;
    }
    let hat = [
        UP,
        UP | RIGHT,
        RIGHT,
        DOWN | RIGHT,
        DOWN,
        DOWN | LEFT,
        LEFT,
        UP | LEFT,
    ];
    let buttons = hat.get(usize::from(face & 0xf)).copied().unwrap_or(0);
    let bits = u16::from(face) | u16::from(shoulders) << 8;
    let mapping = [
        (4, X),
        (5, A),
        (6, B),
        (7, Y),
        (8, LEFT_SHOULDER),
        (9, RIGHT_SHOULDER),
        (12, VIEW),
        (13, MENU),
        (14, LEFT_STICK),
        (15, RIGHT_STICK),
    ];
    Some(PadState {
        buttons: mapping.iter().fold(buttons, |all, (host, title)| {
            if bits & (1 << host) != 0 {
                all | title
            } else {
                all
            }
        }),
        left_trigger: f32::from(lt) / 255.0,
        right_trigger: f32::from(rt) / 255.0,
        left_x: axis(lx),
        left_y: -axis(ly),
        right_x: axis(rx),
        right_y: -axis(ry),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn maps_usb_buttons_hat_and_axes() {
        let mut report = [0; REPORT_LEN];
        report[..10].copy_from_slice(&[1, 255, 0, 128, 255, 0, 255, 0, 0x21, 0x51]);
        assert_eq!(
            decode(&report),
            Some(PadState {
                buttons: A | UP | RIGHT | LEFT_SHOULDER | VIEW | LEFT_STICK,
                left_x: 1.0,
                left_y: 1.0,
                right_y: -1.0,
                right_trigger: 1.0,
                ..PadState::default()
            })
        );
        report[..10].copy_from_slice(&[1, 128, 128, 128, 128, 0, 0, 0, 8, 0]);
        assert_eq!(decode(&report), Some(PadState::default()));
        assert_eq!(decode(&report[..10]), None);
        report[0] = 0x31;
        assert_eq!(decode(&report), None);
    }

    #[test]
    fn rumble_report_places_motors_and_flags() {
        let m = Motors { low: 200, high: 100, ..Motors::default() };
        let r = output_report(Some(true), Some(40), m);
        assert_eq!(r[0], 0x02);
        assert_eq!(r[1], 0x02 | 0x04 | 0x08, "haptics select and both triggers");
        assert_eq!((r[3], r[4]), (100, 200), "right is high frequency, left low");
        assert_eq!(r[39], 0x04, "vibration v2 flag");
        assert_eq!((r[11], r[22]), (0x05, 0x05), "quiet triggers are off");
        let r = output_report(Some(false), None, m);
        assert_eq!((r[1], r[39]), (0x03, 0));
    }

    /// Not part of the suite: shakes a connected pad, then buzzes its
    /// triggers. `cargo test -p victory-hle --lib rumbles_the_pad -- --ignored`
    #[test]
    #[ignore]
    fn rumbles_the_pad() {
        let mut pad = DualSense::default();
        pad.poll();
        assert!(pad.device.is_some(), "a wired DualSense");
        println!("vibration v2: {:?}", pad.vibration_v2);
        pad.rumble(Motors { low: 180, high: 180, ..Motors::default() });
        std::thread::sleep(Duration::from_secs(1));
        pad.rumble(Motors { left_trigger: 255, right_trigger: 255, ..Motors::default() });
        std::thread::sleep(Duration::from_secs(1));
        pad.rumble(Motors::default());
        assert_eq!(pad.sent, Some(Motors::default()));
    }

    #[test]
    fn trigger_levels_become_a_vibration_effect() {
        let mut e = [0u8; 11];
        trigger_effect(&mut e, 255, 40);
        assert_eq!(e[0], 0x26);
        assert_eq!(u16::from_le_bytes([e[1], e[2]]), 0x3ff);
        assert_eq!(u32::from_le_bytes([e[3], e[4], e[5], e[6]]), 0x3fff_ffff);
        assert_eq!(e[9], 40);
        trigger_effect(&mut e, 1, 40);
        assert_eq!((e[0], e[3]), (0x26, 0), "the weakest level is strength 1");
        trigger_effect(&mut e, 0, 40);
        assert_eq!(e, [0x05, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
    }
}
