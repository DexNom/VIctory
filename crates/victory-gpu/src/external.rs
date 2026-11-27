//! Host work that a host library records into a title's command list in
//! place of a console one (the host's FidelityFX upscaler, say): the host
//! device, a title texture's host copy in the state the library is told,
//! and the host list itself.

use windows::Win32::Graphics::Direct3D12::{
    ID3D12Device, ID3D12GraphicsCommandList, ID3D12Resource,
};

use crate::object::Object;

/// The host D3D12 device, created on first use; `None` without a host GPU.
pub fn host_device() -> Option<ID3D12Device> {
    crate::host::host().map(|h| h.device.clone())
}

/// A title texture's host copy, moved to the host resource `state` in the
/// title list `list`, and the state it is in: `state` without the bits the
/// host copy cannot take (see `host::legal_state`). What the list records
/// next finds it there and must leave it there. The list holds the texture
/// until it is reset. `None` for a buffer, or without a host list.
pub fn host_texture(
    list: *const Object,
    resource: *const Object,
    state: u32,
) -> Option<(ID3D12Resource, u32)> {
    let host = crate::list::host_list(list)?;
    let texture = crate::resource::host_resource(resource)?.clone();
    crate::list::ensure_state(host, resource, state);
    let state = crate::host::legal_state(&texture, state);
    Some((texture, state))
}

/// Prepare title-authored inputs using the same alias, mip upload and depth
/// twin rules as a compute shader-resource view in the renderer.
pub fn prepare_input(list: *const Object, resource: *const Object) -> Option<()> {
    let list = crate::list::host_list(list)?;
    let texture = crate::resource::host_resource(resource)?;
    let aliased = crate::resource::alias_of(resource)
        .is_some_and(|source| crate::texture::alias(list, resource, source));
    if !aliased && crate::texture::title_written(texture) {
        crate::texture::upload_from(list, resource, texture, 0);
    }
    list.twin_use(resource, false, false);
    Some(())
}

/// The host list while a title command list is being recorded.
pub struct Recording<'a>(&'a crate::host::HostList);

impl Recording<'_> {
    /// The open D3D12 list for the provider to record into.
    pub fn list(&self) -> &ID3D12GraphicsCommandList {
        &self.0.list
    }

    /// At a host upscale recorded here: its depth and motion textures
    /// (title resources) and constants feed frame generation for the next
    /// present, once this list has run. Nothing when it is off.
    #[cfg(feature = "diagnostics")]
    pub fn frame_generation_inputs(
        &self,
        depth: *const Object,
        motion: *const Object,
        frame: FrameInputs,
    ) {
        if !crate::framegen::enabled() {
            return;
        }
        let (Some(depth), Some(motion)) = (
            crate::resource::host_copy(depth),
            crate::resource::host_copy(motion),
        ) else {
            return;
        };
        crate::framegen::record(
            self.0,
            crate::framegen::Inputs {
                depth,
                motion,
                frame,
                pose: None,
            },
        );
    }
}

/// What frame generation needs of an upscaled frame besides its depth and
/// motion vectors: the upscale's constants (as its description has them)
/// and the motion and depth bits of its creation flags.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
#[cfg(feature = "diagnostics")]
pub struct FrameInputs {
    pub render: [u32; 2],
    pub jitter: [f32; 2],
    pub motion_scale: [f32; 2],
    pub frame_time_ms: f32,
    pub reset: bool,
    /// Near, far, vertical field of view, metres per view-space unit.
    pub camera: [f32; 4],
    pub flags: u32,
    /// The FidelityFX descriptions of the depth and motion textures.
    pub depth_desc: [u32; 8],
    pub motion_desc: [u32; 8],
}

/// Turns on AMD frame generation for upscaled frames (diagnostics) with
/// `amd_fidelityfx_framegeneration_dx12.dll` from `dir`: the presenter's
/// swap chain becomes its proxy. The DLL's path, or why not.
#[cfg(feature = "diagnostics")]
pub fn use_frame_generation(dir: &std::path::Path, ml: bool) -> Result<String, String> {
    crate::framegen::load(dir, ml)
}

/// The producer recorded its output on the host's command list.
pub fn output_written(resource: *const Object) {
    crate::texture::host_written(resource);
}

/// Settle submitted title work before releasing a provider's private GPU resources.
pub fn finish_submitted() {
    crate::host::settle_for_fence();
}

/// Runs `f` with the host list behind the title list `list` (open for
/// recording); `None` without one. What `f` binds (descriptor heaps, root
/// signatures, pipeline) is replaced before the list's next draw or
/// dispatch.
pub fn record<R>(
    list: *const Object,
    f: impl FnOnce(&ID3D12GraphicsCommandList) -> R,
) -> Option<R> {
    record_upscale(list, |recording| f(recording.list()))
}

/// As [`record`], with access to the frame generation inputs for a
/// successfully recorded upscale.
pub fn record_upscale<R>(list: *const Object, f: impl FnOnce(Recording<'_>) -> R) -> Option<R> {
    let mut out = None;
    crate::list::with_state(list, |host, state| {
        out = Some(f(Recording(host)));
        state.host_state_lost();
    });
    out
}
