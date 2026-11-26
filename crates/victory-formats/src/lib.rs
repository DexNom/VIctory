//! Platform-level data formats, shared by every Xbox Series title.
//!
//! Each module is pure (bytes in, bytes out, no I/O): [`texture`] (the
//! GPU's tiled texture layouts and copies to and from linear rows),
//! [`bcpack`] (BCPack-packed BC block streams) and [`xma`] (XMA2 audio).
//! Opus packets are decoded by the audio module in `victory-hle`, and zlib
//! streams are inflated by its DirectStorage module.

pub mod bcpack;
pub mod texture;
pub mod xma;
