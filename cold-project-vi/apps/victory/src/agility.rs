//! The D3D12 Agility SDK: the runtime ships `D3D12\D3D12Core.dll` (Microsoft's
//! redistributable, from the `Microsoft.Direct3D.D3D12` package) beside
//! `victory.exe`, and these two exports tell Windows' D3D12 loader to use it.
//!
//! Windows 10's own D3D12 is older than what the translated shaders need: its
//! runtime refused the root signatures' flags (0x470, heaps indexed directly
//! from shaders) and the engine never got past making its device (players on
//! Windows 10 19045, 27/11/2026). With the SDK beside the program Windows 10
//! gets the newer runtime. Where Windows' own is newer (recent Windows 11), or
//! the folder is missing, the loader keeps Windows' own: nothing changes there.
//!
//! The exports are made by `build.rs` (`/EXPORT`): a program exports nothing
//! by default.

/// The SDK version of the `D3D12Core.dll` the release carries
/// (`.github/workflows/release.yml`, `AGILITY`).
#[cfg(windows)]
#[allow(non_upper_case_globals)]
#[unsafe(no_mangle)]
#[used]
pub static D3D12SDKVersion: u32 = 620;

/// A pointer to a string the loader reads; never written.
#[cfg(windows)]
#[repr(transparent)]
pub struct SdkPath(*const u8);

// SAFETY: it points at a static string that nothing writes.
#[cfg(windows)]
unsafe impl Sync for SdkPath {}

/// Where the SDK is, from the program's folder.
#[cfg(windows)]
#[allow(non_upper_case_globals)]
#[unsafe(no_mangle)]
#[used]
pub static D3D12SDKPath: SdkPath = SdkPath(c".\\D3D12\\".as_ptr().cast());
