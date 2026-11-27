//! `victory gpu`: the host card as a run sees it, one `key=value` per line for
//! the launcher. A run makes its device on the default adapter (the one
//! Windows' graphics preference for victory.exe picks), and puts buffers in
//! card memory the CPU maps only when D3D12 offers GPU upload heaps: a
//! resizable BAR that the card, the PC's firmware, Windows and the driver all
//! allow. The BIOS switch alone is not enough, which is why this asks D3D12.

use windows::Win32::Graphics::Direct3D::D3D_FEATURE_LEVEL_12_0;
use windows::Win32::Graphics::Direct3D12::*;
use windows::Win32::Graphics::Dxgi::*;

/// The lines `victory gpu` prints.
pub fn report() -> Vec<String> {
    let mut out = Vec::new();
    // SAFETY: plain D3D12/DXGI calls on objects made here; `options` is written
    // with its own size.
    unsafe {
        let mut device: Option<ID3D12Device> = None;
        // The same card a run gets (the high-performance one), so the launcher names it and tests its
        // Resizable BAR, not integrated graphics Windows happens to list first.
        let made = match crate::host::run_adapter() {
            Some(adapter) => D3D12CreateDevice(&adapter, D3D_FEATURE_LEVEL_12_0, &mut device),
            None => D3D12CreateDevice(None, D3D_FEATURE_LEVEL_12_0, &mut device),
        };
        if let Err(e) = made {
            out.push("device=none".to_owned());
            out.push(format!("error={e}"));
            return out;
        }
        let Some(device) = device else {
            out.push("device=none".to_owned());
            return out;
        };
        out.push("device=ok".to_owned());
        let adapter = CreateDXGIFactory2::<IDXGIFactory4>(DXGI_CREATE_FACTORY_FLAGS(0))
            .and_then(|f| f.EnumAdapterByLuid::<IDXGIAdapter1>(device.GetAdapterLuid()));
        if let Ok(desc) = adapter.and_then(|a| a.GetDesc1()) {
            let end = desc.Description.iter().position(|&c| c == 0).unwrap_or(desc.Description.len());
            out.push(format!("adapter={}", String::from_utf16_lossy(&desc.Description[..end]).trim()));
            out.push(format!("vendor={:04x}", desc.VendorId));
            out.push(format!("dedicated_mb={}", desc.DedicatedVideoMemory >> 20));
            out.push(format!("shared_mb={}", desc.SharedSystemMemory >> 20));
        }
        let mut options = D3D12_FEATURE_DATA_D3D12_OPTIONS16::default();
        let known = device
            .CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS16, (&raw mut options).cast(), size_of_val(&options) as u32)
            .is_ok();
        // false with `options16_known=false`: Windows' D3D12 is older than GPU upload heaps.
        out.push(format!("options16_known={known}"));
        out.push(format!("gpu_upload_heap={}", known && options.GPUUploadHeapSupported.as_bool()));
    }
    out
}
