//! Prints the largest shader-visible view heap the PC's adapter makes, and
//! what it reports (`MaxViewDescriptorHeapSize`, resource binding tier):
//! `cargo run -p victory-gpu --example heap_probe`.
fn main() {
    #[cfg(windows)]
    unsafe {
        use windows::Win32::Graphics::Direct3D::D3D_FEATURE_LEVEL_12_0;
        use windows::Win32::Graphics::Direct3D12::*;
        let mut device: Option<ID3D12Device> = None;
        D3D12CreateDevice(None, D3D_FEATURE_LEVEL_12_0, &mut device).expect("device");
        let device = device.unwrap();
        let mut o = D3D12_FEATURE_DATA_D3D12_OPTIONS::default();
        let _ = device.CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS, (&raw mut o).cast(), size_of_val(&o) as u32);
        println!("resource binding tier {}", o.ResourceBindingTier.0);
        let mut o19 = D3D12_FEATURE_DATA_D3D12_OPTIONS19::default();
        match device.CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS19, (&raw mut o19).cast(), size_of_val(&o19) as u32) {
            Ok(()) => println!("MaxViewDescriptorHeapSize {}", o19.MaxViewDescriptorHeapSize),
            Err(e) => println!("OPTIONS19 not answered: {e}"),
        }
        let fits = |n: u32| {
            device
                .CreateDescriptorHeap::<ID3D12DescriptorHeap>(&D3D12_DESCRIPTOR_HEAP_DESC {
                    Type: D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
                    NumDescriptors: n,
                    Flags: D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE,
                    NodeMask: 0,
                })
                .is_ok()
        };
        let (mut lo, mut hi) = (1_000_000u32, 1u32 << 23);
        if !fits(lo) {
            println!("even 1,000,000 does not fit");
            return;
        }
        while hi - lo > 1024 {
            let mid = lo + (hi - lo) / 2;
            if fits(mid) { lo = mid } else { hi = mid }
        }
        println!("largest shader-visible view heap made: about {lo}");
    }
}
