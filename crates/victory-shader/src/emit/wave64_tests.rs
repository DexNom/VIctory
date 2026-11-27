//! A console wave64 compute program on a host of 32-lane waves: a wave reduction inside a
//! per-lane branch, with that branch taken wave-wide ([`crate::structure::structure_wide`])
//! so that the two host waves can exchange their halves inside it; and programs in the
//! paired form (one host wave for each console wave, two lanes a thread: `super::paired`),
//! whose LDS, lane reads and masks cross the halves without barriers, checked against what
//! the console's 64 lanes compute. Lowered, validated by `dxil.dll` when present, and run on
//! the host GPU when it takes waves of 32 (WARP's are 4 lanes: neither form runs there).

use std::collections::HashSet;

use super::host::{Dispatch, Host, Root, View};
use super::{HostShader, lower, validator};
use crate::native::{
    ComputeIo, HwStage, NativeProgram, ResourceType, RootParam, RootType, StageIo, UsedResource,
};
use crate::{ShaderError, io, ir, resource, structure};

const S_ENDPGM: u32 = 0xBF81_0000;

/// 64 threads in one console wave. Each thread t holds the bit `1 << (t >> 1)`; the threads
/// below s0 (a root constant) OR theirs over the wave inside `if (t < s0) { ... }` and store
/// the result to dword 0 of the raw UAV.
/// `skip`: the branch past the body when no thread is below s0: `s_cbranch_execz`, or
/// `s_cbranch_scc0` on the SCC `s_and_saveexec` sets ("some lane is left").
fn program(skip: u32) -> NativeProgram {
    let mut words = vec![
        0x7E0A_0281, // v_mov_b32 v5, 1
        0x2C0C_0081, // v_lshrrev_b32 v6, 1, v0
        0x340E_0B06, // v_lshlrev_b32 v7, v6, v5
        0x7D88_0000, // v_cmp_gt_u32 vcc, s0, v0
        0xBE8C_246A, // s_and_saveexec_b64 s[12:13], vcc
        skip | 23,   // the branch to the restore of EXEC
    ];
    words.extend_from_slice(&super::wave::WAVE_OR_V7_TO_S7);
    words.extend_from_slice(&[
        0x7E00_0207, // v_mov_b32 v0, s7
        0x38 << 26 | 0x1C << 18,
        0x80 << 24 | 2 << 16, // buffer_store_dword v0, off, s[8:11], 0
        0xBEFE_040C, // s_mov_b64 exec, s[12:13]
    ]);
    native(&words)
}

/// A wave64 compute program of 64 threads (one console wave; the thread index in v0) with 4
/// root constants in s0..s3 and a raw UAV's descriptor in s[8:11], then `body`.
fn native(body: &[u32]) -> NativeProgram {
    native_of(body, true, 64)
}

/// The same for a console of 32-lane waves when `wave64` is not set, with `threads` threads.
fn native_of(body: &[u32], wave64: bool, threads: u32) -> NativeProgram {
    native_with(body, wave64, threads, 0)
}

/// [`native_of`] with `lds` bytes of LDS (512-byte granules).
fn native_with(body: &[u32], wave64: bool, threads: u32, lds: u32) -> NativeProgram {
    let mut words = vec![
        0xBE85_0380, // s_mov_b32 s5, 0
        0x3D << 26 | 0x02 << 18 | 8 << 6 | 2,
        125 << 25, // s_load_dwordx4 s[8:11], s[4:5], 0
    ];
    words.extend_from_slice(body);
    words.push(S_ENDPGM);
    NativeProgram {
        stage: HwStage::Cs,
        wave64,
        vgprs: 16,
        layout: vec![
            RootParam {
                root: RootType::Constants,
                table_offset_dwords: 0,
                sgprs: 4,
                base_register: 0,
                user_data: 0,
                slot: 0,
            },
            RootParam {
                root: RootType::Table,
                table_offset_dwords: 0,
                sgprs: 1,
                base_register: 0,
                user_data: 4,
                slot: 1,
            },
        ],
        used: vec![UsedResource {
            kind: ResourceType::Uav,
            root: RootType::Table,
            table_offset_dwords: 0,
            lower_bound: 0,
            stride: 0,
            dwords: 4,
            user_data: 4,
            slot: 1,
        }],
        io: StageIo::Cs(ComputeIo {
            rsrc2: 5 << 1 | lds.div_ceil(512) << 15, // 5 user SGPRs: s0..s4
            threads: [threads, 1, 1],
        }),
        code: words.iter().flat_map(|w| w.to_le_bytes()).collect(),
    }
}

/// `program()` for a host of 32-lane waves, with the branches closing `wide` taken wave-wide.
fn lowered(native: &NativeProgram, p: &ir::Program, wide: &HashSet<usize>) -> Result<HostShader, ShaderError> {
    lowered_for_width(native, p, wide, 32)
}

fn lowered_for_width(native: &NativeProgram, p: &ir::Program, wide: &HashSet<usize>, host_wave: u32) -> Result<HostShader, ShaderError> {
    lowered_for_dispatch(native, p, wide, host_wave, false)
}

fn lowered_for_dispatch(native: &NativeProgram, p: &ir::Program, wide: &HashSet<usize>, host_wave: u32, partial: bool) -> Result<HostShader, ShaderError> {
    let st = structure::structure_wide(p, wide).expect("structure");
    let res = resource::track(p, &native.layout, native.stage, Some(&st)).expect("resource::track");
    let io = io::raise(p, native).expect("io::raise");
    let was = lower::TEST_HOST_WAVE.replace(Some(host_wave));
    let lowered = lower::lower_with_thread_limit(&lower::Parts {
        native,
        p,
        st: &st,
        io: &io,
        res: &res,
        inputs: Vec::new(),
        outputs: Vec::new(),
        prims: Vec::new(),
        mesh_stride: 0,
    }, partial);
    lower::TEST_HOST_WAVE.set(was);
    let lowered = lowered?;
    Ok(HostShader {
        stage: HwStage::Cs,
        dxil: lowered.dxil,
        user_data: lowered.user_data,
        samplers: lowered.samplers,
        static_samplers: Vec::new(),
        root_cbvs: lowered.root_cbvs,
        root_srvs: lowered.root_srvs,
        root_uavs: lowered.root_uavs,
        written_root_uavs: lowered.written_root_uavs,
        writes_guest_memory: lowered.writes_guest_memory,
        root_indexed_views: res.root_indexed_views,
    })
}

#[test]
fn partial_thread_dispatch_masks_the_final_groups_memory_writes() {
    // Each active thread increments counter[local X]. With two groups the
    // tail's first N lanes report two increments, the others only one.
    let native = native(&[
        0x3402_0082, // v_lshlrev_b32 v1, 2, v0
        0x7e04_0281, // v_mov_b32 v2, 1
        0xe0c8_1000, 0x8002_0201, // buffer_atomic_add v2, v1, s[8:11], 0 offen
    ]);
    let p = ir::build(&native.code, native.wave64, native.vgprs).unwrap();
    let shader = lowered_for_dispatch(&native, &p, &HashSet::new(), 64, true).unwrap();
    let limit = shader.user_data.iter().find(|u| u.root == RootType::Other(super::USER_DISPATCH_THREADS_X)).unwrap();
    assert_eq!((limit.slot, limit.dword), (0, 64));
    let mut validator = validator::Validator::load(validator::Validator::default_path().as_deref()).unwrap();
    validator.validate(&mut shader.dxil.clone()).unwrap();
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Ok(host) = Host::new(true) else { return };
    let (root, pso) = host.compute_pipeline(&shader).unwrap();
    for count in [0u32, 1, 7, 63, 64, 65, 74, 77, 127, 128] {
        let mut constants = vec![0; shader.user_data_dwords() as usize];
        constants[limit.index as usize] = count;
        let result = host.dispatch(&root, &pso, &Dispatch {
            roots: vec![(0, Root::Constants(constants))],
            views: &[View::RawUav(68)],
            samplers: &[],
            groups: [2, 1, 1],
        }).unwrap();
        let active = if count == 0 { 128 } else { count };
        for (i, &word) in result[0].iter().enumerate() {
            let expected = if i < 64 {
                u32::from((i as u32) < active) + u32::from((i as u32 + 64) < active)
            } else { 0 };
            assert_eq!(word, expected, "count {count}, word {i}");
        }
    }
}

/// Move a joined scalar mask through two SGPR pairs while only four lanes
/// execute. Both copied words must still describe all 64 console lanes.
#[test]
fn copied_mask_keeps_both_numeric_words_inside_a_lane_branch() {
    let native = native(&[
        0x7D88_0000, // v_cmp_gt_u32 vcc, s0, v0
        0xBE94_046A, // s_mov_b64 s[20:21], vcc
        0xBF06_8001, // s_cmp_eq_u32 s1, 0
        0xBF85_0001, // s_cbranch_scc1 to join
        0xBE94_0402, // s_mov_b64 s[20:21], s[2:3]
        0xBE96_0414, // join: s_mov_b64 s[22:23], s[20:21]
        0x7D88_0084, // v_cmp_gt_u32 vcc, 4, v0
        0xBE8C_246A, // s_and_saveexec_b64 s[12:13], vcc
        0xBF88_0009, // s_cbranch_execz to restore
        0xBE98_0416, // s_mov_b64 s[24:25], s[22:23]
        0xBE9A_0418, // s_mov_b64 s[26:27], s[24:25]
        0x87FE_1A7E, // s_and_b64 exec, exec, s[26:27]
        0x7E02_021A, // v_mov_b32 v1, s26
        0x7E04_021B, // v_mov_b32 v2, s27
        0xE070_0000, 0x8002_0100, // buffer_store_dword v1, off, s[8:11], 0
        0xE070_0004, 0x8002_0200, // buffer_store_dword v2, off, s[8:11], 4
        0xBEFE_040C, // restore: s_mov_b64 exec, s[12:13]
    ]);
    let p = ir::build(&native.code, native.wave64, native.vgprs).unwrap();
    let shader = lowered_for_width(&native, &p, &HashSet::new(), 64).expect("lower");
    if let Ok(mut validator) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        validator.validate(&mut shader.dxil.clone()).expect("validate");
    }
    let _lock = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Ok(host) = Host::new(false) else { eprintln!("no hardware compute device"); return };
    let Ok((root, pipeline)) = host.compute_pipeline(&shader) else {
        eprintln!("hardware does not take this 64-lane pipeline"); return;
    };
    for (constants, expected) in [
        ([37, 0, 0, 0], [u32::MAX, 31]),
        ([9, 0, 0, 0], [511, 0]),
        ([1, 1, 0x8000_0001, 0xa5a5_55aa], [0x8000_0001, 0xa5a5_55aa]),
        ([1, 1, 0x3456_7892, 0x8020_1000], [0x3456_7892, 0x8020_1000]),
    ] {
        let mut ud = vec![0; shader.user_data_dwords().max(1) as usize];
        for input in &shader.user_data {
            if input.root == RootType::Constants {
                ud[input.index as usize] = constants[input.dword as usize];
            }
        }
        let out = host.dispatch(&root, &pipeline, &Dispatch {
            roots: vec![(0, Root::Constants(ud))], views: &[View::RawUav(2)],
            samplers: &[], groups: [1,1,1],
        }).expect("dispatch and read both words");
        assert_eq!(out[0][..2], expected);
    }
    eprintln!("four native wave64 low/high word readbacks passed");
}

/// 64 threads in one console wave: thread t holds the 16-bit float with bits 0x3C00 + 37 t
/// (1.0 and up, with odd mantissas, so that additions round); all of them store the wave's sum
/// (`WaveActiveSum` of halves) to dword 0. With the console's sum (its tree) and the terms'
/// sum added in a row, which differs from it.
fn half_sum_program() -> (NativeProgram, u16, u16) {
    let mut words = vec![
        0x340C_0085, // v_lshlrev_b32 v6, 5, v0
        0x340E_0082, // v_lshlrev_b32 v7, 2, v0
        0x4A0E_0F06, // v_add_nc_u32 v7, v6, v7
        0x4A0E_0F00, // v_add_nc_u32 v7, v0, v7
        0x4A0E_0EFF,
        0x0000_3C00, // v_add_nc_u32 v7, 0x3c00, v7
    ];
    // (the wave OR's words with v_add_f16 for v_or_b32: opcode 50 for 28)
    words.extend(super::wave::WAVE_OR_V7_TO_S7.iter().map(|&w| if w >> 25 == 28 { w & 0x01FF_FFFF | 50 << 25 } else { w }));
    words.extend_from_slice(&[
        0x7E00_0207, // v_mov_b32 v0, s7
        0x38 << 26 | 0x1C << 18,
        0x80 << 24 | 2 << 16, // buffer_store_dword v0, off, s[8:11], 0
    ]);
    // the console's sum: lanes 2k and 2k+1, then pairs, ... each addition rounded to 16 bits
    // (to nearest, ties to even); values here are normal numbers
    let wide = |h: u16| f32::from_bits((u32::from(h >> 10) + 112) << 23 | u32::from(h & 0x3FF) << 13);
    let narrow = |f: f32| {
        let b = f.to_bits();
        let h = ((b >> 23) - 112) << 10 | (b >> 13 & 0x3FF);
        let rest = b & 0x1FFF;
        (h + u32::from(rest > 0x1000 || rest == 0x1000 && h & 1 == 1)) as u16
    };
    let add = |a: u16, b: u16| narrow(wide(a) + wide(b));
    let terms: Vec<u16> = (0..64u16).map(|t| 0x3C00 + 37 * t).collect();
    let mut tree = terms.clone();
    while tree.len() > 1 {
        tree = tree.chunks(2).map(|c| add(c[0], c[1])).collect();
    }
    let in_a_row = terms.iter().copied().reduce(add).unwrap();
    (native(&words), tree[0], in_a_row)
}

#[test]
fn a_sum_of_halves_over_the_wave_is_the_consoles_tree_bit_for_bit() {
    let (native, tree, in_a_row) = half_sum_program();
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    assert_eq!(p.blocks.iter().flat_map(|b| &b.insts).filter(|i| i.name() == "v_add_f16").count(), 6);
    let shader = lowered(&native, &p, &HashSet::new()).expect("lower");
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        let mut copy = shader.dxil.clone();
        v.validate(&mut copy).expect("DXIL validation failed");
    }
    assert_ne!(tree, in_a_row, "these terms tell the tree from another order");
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Ok(host) = Host::new(false) else {
        eprintln!("no SM 6.6 hardware device; not run");
        return;
    };
    let (root, pso) = match host.compute_pipeline(&shader) {
        Ok(x) => x,
        Err(e) => {
            eprintln!("the hardware device does not take this program (waves of 32?): {e}; not run");
            return;
        }
    };
    let ud = vec![0u32; shader.user_data_dwords().max(1) as usize];
    let out = host
        .dispatch(
            &root,
            &pso,
            &Dispatch {
                roots: vec![(0, Root::Constants(ud))],
                views: &[View::RawUav(1)],
                samplers: &[],
                groups: [1, 1, 1],
            },
        )
        .unwrap_or_else(|e| panic!("dispatch failed: {e}"));
    // (the high half is lane 0's term's: zero)
    assert_eq!(out[0][0], u32::from(tree), "tree {tree:#x}, in a row {in_a_row:#x}");
    eprintln!("ran on the hardware device: the sum {tree:#x} of the console's tree");
}

/// One `v_add_f16` whose exact result lies between two halves: 1 + 3/1024 (0x3C03) plus 2
/// (0x4000) is 3 + 1.5 steps of that range, which the console rounds up to 0x4202. The
/// host's `f32tof16` may give 0x4201 (toward zero); with `VICTORY_F16_NEAREST=1` the
/// translation gives the console's whatever the host does.
#[test]
fn a_16_bit_sum_rounds_to_nearest_when_asked_to() {
    let words = [
        0x7E0E_02FF,
        0x0000_3C03, // v_mov_b32 v7, 0x3c03
        0x7E0C_02FF,
        0x0000_4000, // v_mov_b32 v6, 0x4000
        0x6400_0D07, // v_add_f16 v0, v7, v6
        0x38 << 26 | 0x1C << 18,
        0x80 << 24 | 2 << 16, // buffer_store_dword v0, off, s[8:11], 0
    ];
    let native = native(&words);
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    assert!(p.blocks.iter().flat_map(|b| &b.insts).any(|i| i.name() == "v_add_f16"));
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    for nearest in [false, true] {
        let was = lower::TEST_F16_NEAREST.replace(Some(nearest));
        let shader = lowered(&native, &p, &HashSet::new());
        lower::TEST_F16_NEAREST.set(was);
        let shader = shader.expect("lower");
        if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
            let mut copy = shader.dxil.clone();
            v.validate(&mut copy).expect("DXIL validation failed");
        }
        let Ok(host) = Host::new(false) else {
            eprintln!("no SM 6.6 hardware device; not run");
            return;
        };
        let Ok((root, pso)) = host.compute_pipeline(&shader) else {
            eprintln!("the hardware device does not take this program; not run");
            return;
        };
        let ud = vec![0u32; shader.user_data_dwords().max(1) as usize];
        let out = host
            .dispatch(
                &root,
                &pso,
                &Dispatch {
                    roots: vec![(0, Root::Constants(ud))],
                    views: &[View::RawUav(1)],
                    samplers: &[],
                    groups: [1, 1, 1],
                },
            )
            .unwrap_or_else(|e| panic!("dispatch failed: {e}"));
        let got = out[0][0] & 0xFFFF;
        eprintln!("v_add_f16 0x3c03 + 0x4000 on the hardware device, nearest {nearest}: {got:#x}");
        if nearest {
            assert_eq!(got, 0x4202);
        } else {
            assert!(got == 0x4201 || got == 0x4202, "{got:#x}");
        }
    }
}

#[test]
fn a_wave_or_inside_a_lane_branch_needs_the_branch_wave_wide() {
    for (skip, name) in [(0xBF88_0000, "s_cbranch_execz"), (0xBF84_0000, "s_cbranch_scc0")] {
        a_wave_or_inside(skip, name);
    }
}

fn a_wave_or_inside(skip: u32, name: &str) {
    let native = program(skip);
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    let branch = (0..p.blocks.len())
        .find(|&b| p.blocks[b].insts.last().is_some_and(|i| i.name() == name))
        .expect("the branch");
    // per lane, the two host waves cannot meet inside the branch
    lower::take_needs();
    let per_lane = lowered(&native, &p, &HashSet::new());
    assert!(
        matches!(&per_lane, Err(ShaderError::Unsupported { what, .. }) if *what == "a wave reduction where not every lane runs"),
        "{:?}",
        per_lane.err()
    );
    // ... and the lowering names the block, around which that branch is the one to widen
    let needs = lower::take_needs();
    let st = structure::structure(&p).expect("structure");
    assert_eq!(lower::branches_around(&st, &needs), Some(vec![branch]));
    // (taken, past the body, when every lane's bit is clear)
    assert_eq!(st.wave_any[branch], Some(false));
    // wave-wide, every lane runs the reduction (EXEC stays a guard on what it does there)
    let wide = HashSet::from([branch]);
    let st = structure::structure_wide(&p, &wide).expect("structure");
    let inside = match p.blocks[branch].exit {
        ir::Exit::Branch { not_taken, .. } => not_taken,
        _ => unreachable!(),
    };
    assert!(st.guards[inside].iter().any(Option::is_some));
    let shader = lowered(&native, &p, &wide).expect("lower");
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        let mut copy = shader.dxil.clone();
        v.validate(&mut copy).expect("DXIL validation failed");
    }
    // On a GPU that takes waves of 32 (two host waves for the console's one): the OR is of
    // both halves, whichever half's lanes take the branch.
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Ok(host) = Host::new(false) else {
        eprintln!("no SM 6.6 hardware device; not run");
        return;
    };
    let (root, pso) = match host.compute_pipeline(&shader) {
        Ok(x) => x,
        Err(e) => {
            eprintln!("the hardware device does not take this program (waves of 32?): {e}; not run");
            return;
        }
    };
    // (threads below s0 take the branch; thread t gives bit t >> 1)
    for (below, expected) in [(64u32, u32::MAX), (40, 0x000F_FFFF), (33, 0x0001_FFFF), (10, 0x1F), (1, 1)] {
        let mut ud = vec![0u32; shader.user_data_dwords().max(1) as usize];
        for u in &shader.user_data {
            if let (RootType::Constants, 0, 0) = (u.root, u.slot, u.dword) {
                ud[u.index as usize] = below;
            }
        }
        let out = host
            .dispatch(
                &root,
                &pso,
                &Dispatch {
                    roots: vec![(0, Root::Constants(ud))],
                    views: &[View::RawUav(1)],
                    samplers: &[],
                    groups: [1, 1, 1],
                },
            )
            .unwrap_or_else(|e| panic!("dispatch failed: {e}"));
        assert_eq!(out[0][0], expected, "{name}: {below} threads in the branch");
    }
    eprintln!("ran on the hardware device ({name}): the OR of both halves in every case");
}

/// Runs `shader` on the hardware device with the root constants `s0`, `s1` and gives dword 0
/// of the UAV; None (and a line) without a device that takes it.
fn on_the_card(shader: &HostShader, constants: [u32; 2]) -> Option<u32> {
    let Ok(host) = Host::new(false) else {
        eprintln!("no SM 6.6 hardware device; not run");
        return None;
    };
    let Ok((root, pso)) = host.compute_pipeline(shader) else {
        eprintln!("the hardware device does not take this program; not run");
        return None;
    };
    let mut ud = vec![0u32; shader.user_data_dwords().max(1) as usize];
    for u in &shader.user_data {
        if let (RootType::Constants, 0, d @ 0..=1) = (u.root, u.slot, u.dword) {
            ud[u.index as usize] = constants[d as usize];
        }
    }
    let out = host
        .dispatch(
            &root,
            &pso,
            &Dispatch {
                roots: vec![(0, Root::Constants(ud))],
                views: &[View::RawUav(1)],
                samplers: &[],
                groups: [1, 1, 1],
            },
        )
        .unwrap_or_else(|e| panic!("dispatch failed: {e}"));
    Some(out[0][0])
}

/// The fewest lanes a wave of the device [`on_the_card`] runs on has, if there is one.
fn wave_lanes() -> Option<u32> {
    use windows::Win32::Graphics::Direct3D12::{D3D12_FEATURE_D3D12_OPTIONS1, D3D12_FEATURE_DATA_D3D12_OPTIONS1};
    let host = Host::new(false).ok()?;
    let mut options = D3D12_FEATURE_DATA_D3D12_OPTIONS1::default();
    // SAFETY: a plain query that fills a structure of the given size.
    unsafe {
        host.device
            .CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS1, (&raw mut options).cast(), size_of_val(&options) as u32)
            .ok()?
    };
    Some(options.WaveLaneCountMin)
}

/// A 32-lane console wave. A register holds a lane mask on one path (the lanes below s0)
/// and a number on the other, and is read as a number after the join, as the trace programs
/// keep their lanes' masks and their node numbers on one stack
/// (`deferred_tiled:CS_LaserLightShadowRaytracing`, s46 at 0x104, 0x75c and 0x188). The
/// mask arrives as the ballot of its lanes.
#[test]
fn a_lane_mask_joined_with_a_number_is_read_as_the_number_it_is() {
    let words = [
        0x7D88_0000, // v_cmp_gt_u32 vcc_lo, s0, v0
        0xBE94_036A, // s_mov_b32 s20, vcc_lo
        0xBF06_8001, // s_cmp_eq_u32 s1, 0
        0xBF85_0001, // s_cbranch_scc1 over the next
        0xBE94_0385, // s_mov_b32 s20, 5
        0x7E00_0214, // v_mov_b32 v0, s20
        0x38 << 26 | 0x1C << 18,
        0x80 << 24 | 2 << 16, // buffer_store_dword v0, off, s[8:11], 0
    ];
    let native = native_of(&words, false, 32);
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    let shader = lowered(&native, &p, &HashSet::new()).expect("lower");
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        let mut copy = shader.dxil.clone();
        v.validate(&mut copy).expect("DXIL validation failed");
    }
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    // The program is a 32-lane wave's: a device whose waves are narrower
    // (the software adapter's have 4 lanes) runs it as several waves, and
    // a ballot of 32 lanes is no mask of one.
    if let Some(lanes) = wave_lanes().filter(|&lanes| lanes < 32) {
        eprintln!("the device's waves have {lanes} lanes, fewer than this program's 32; not run");
        return;
    }
    for (below, other, expected) in [(32u32, 0u32, u32::MAX), (10, 0, 0x3FF), (0, 0, 0), (10, 1, 5)] {
        let Some(got) = on_the_card(&shader, [below, other]) else { return };
        assert_eq!(got, expected, "{below} lanes in the mask, s1 = {other}");
    }
    eprintln!("ran on the hardware device: the mask's bits, or the number");
}

/// A 32-lane console wave. `if (t < s0) { ... SCC = (s1 == 7) }` and a branch on SCC after
/// the join: the skip brings the SCC of `s_and_saveexec` ("some lane is left") along
/// (`rtdirectionalshadows:CS_RTShadowTrace32HQ_TiledFarOnly`, 0x360-0x39c). On the console
/// the skip is taken by the wave, so with some lane inside every lane sees the inner test.
/// A lane that skipped on its own would see "some lane is left" instead: refused per lane,
/// right with the branch taken wave-wide. Lane 31 stores what it saw.
#[test]
fn scc_of_a_mask_operation_joined_with_a_scalar_test_needs_the_skip_wave_wide() {
    let words = [
        0x7D88_0000, // v_cmp_gt_u32 vcc_lo, s0, v0
        0xBE8D_3C6A, // s_and_saveexec_b32 s13, vcc_lo
        0xBF88_0001, // s_cbranch_execz over the next
        0xBF06_8701, // s_cmp_eq_u32 s1, 7
        0xBEFE_030D, // s_mov_b32 exec_lo, s13
        0xBF85_0002, // s_cbranch_scc1 to "seven"
        0x7E0A_0281, // v_mov_b32 v5, 1
        0xBF82_0001, // s_branch over the next
        0x7E0A_0282, // seven: v_mov_b32 v5, 2
        0x7DA4_009F, // v_cmpx_eq_u32 exec_lo, 31, v0
        0x38 << 26 | 0x1C << 18,
        0x80 << 24 | 2 << 16 | 5 << 8, // buffer_store_dword v5, off, s[8:11], 0
    ];
    let native = native_of(&words, false, 32);
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    let branch = (0..p.blocks.len())
        .find(|&b| p.blocks[b].insts.last().is_some_and(|i| i.name() == "s_cbranch_execz"))
        .expect("the skip");
    lower::take_needs();
    let per_lane = lowered(&native, &p, &HashSet::new());
    assert!(
        matches!(&per_lane, Err(ShaderError::Unsupported { what, .. }) if *what == "a lane mask read as data"),
        "{:?}",
        per_lane.err()
    );
    let st = structure::structure(&p).expect("structure");
    assert_eq!(lower::branches_around(&st, &lower::take_needs()), Some(vec![branch]));
    let shader = lowered(&native, &p, &HashSet::from([branch])).expect("lower");
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        let mut copy = shader.dxil.clone();
        v.validate(&mut copy).expect("DXIL validation failed");
    }
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    // (lanes below s0 inside, s1) -> what lane 31 stores: 2 if SCC was set at the branch
    for (below, s1, expected) in [(0u32, 7u32, 1u32), (10, 7, 2), (10, 3, 1), (32, 3, 1), (32, 7, 2)] {
        let Some(got) = on_the_card(&shader, [below, s1]) else { return };
        assert_eq!(got, expected, "{below} lanes inside, s1 = {s1}");
    }
    eprintln!("ran on the hardware device: the inner test's SCC for every lane once some lane is inside");
}

/// The console's prefix over per-wave totals (a 512-thread scan): each console wave past the
/// first sums the totals of the waves before it from LDS in a loop that only its lanes run,
/// and `v_readfirstlane` copies the running sum, made from scalars and LDS alone, back into an
/// SGPR. Every lane holds the same sum there, so each host wave reads its own first lane: no
/// exchange, which could not meet inside the loop.
#[test]
fn a_first_lane_read_of_a_scalar_built_sum_inside_a_lane_loop_takes_the_host_waves_own() {
    let mut native = native_of(
        &[
            0x2C0E_0086, // v_lshrrev_b32 v7, 6, v0 (the console wave of the thread)
            0x7E10_0280, // v_mov_b32 v8, 0
            0xBE80_047E, // s_mov_b64 s[0:1], exec
            0x7DAA_0E80, // v_cmpx_ne_u32 exec, 0, v7
            0xBF88_0014, // s_cbranch_execz past the loop
            0xBE82_047E, // s_mov_b64 s[2:3], exec
            0xBE86_047E, // s_mov_b64 s[6:7], exec
            0xBE92_0380, // s_mov_b32 s18, 0
            0xBE91_0380, // s_mov_b32 s17, 0
            0xBF82_0003, // s_branch to the body
            0xBF80_0000, // (padding)
            0x87FE_0610, // s_and_b64 exec, s[16:17], s[6:7]
            0x7E22_0508, // v_readfirstlane_b32 s17, v8
            0x8F10_8212, // s_lshl_b32 s16, s18, 2
            0x8112_8112, // s_add_i32 s18, s18, 1
            0x7E10_0210, // v_mov_b32 v8, s16
            0xD8D8_0000, 0x0800_0008, // ds_read_b32 v8, v8
            0xBF8C_C07F, // s_waitcnt lgkmcnt(0)
            0x4A10_1011, // v_add_nc_u32 v8, s17, v8
            0xBE90_047E, // s_mov_b64 s[16:17], exec
            0x7DA4_0E12, // v_cmpx_eq_u32 exec, s18, v7
            0xBF80_0000, // s_nop 0
            0x8A86_7E06, // s_andn2_b64 s[6:7], s[6:7], exec
            0xBF85_FFF2, // s_cbranch_scc1 back
            0xBEFE_0400, // s_mov_b64 exec, s[0:1]
            0x7E0C_0308, // v_mov_b32 v6, v8
            0xE070_0000, 0x0002_0600, // buffer_store_dword v6, v0, s[8:11], s0
        ],
        true,
        512,
    );
    if let StageIo::Cs(cs) = &mut native.io {
        cs.rsrc2 |= 1 << 15; // 128 dwords of LDS
    }
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    lower::take_needs();
    let shader = lowered(&native, &p, &HashSet::new()).expect("a scalar-built first-lane read");
    assert!(lower::take_needs().is_empty());
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        let mut copy = shader.dxil.clone();
        v.validate(&mut copy).expect("DXIL validation failed");
    }
}

/// `ds_add_u32` of a negated value (and `ds_sub_u32`): host compilers turn an atomic addition
/// of `0 - x` on group-shared memory into a subtraction DXIL does not have, and NVIDIA's
/// driver and WARP then refuse the pipeline (E_OUTOFMEMORY; compute 7f61193d2c0e5f22). The
/// lowering makes it a compare-exchange loop instead. 32 threads each take 1 from 64.
#[test]
fn an_lds_addition_of_a_negated_value_is_a_compare_exchange_loop() {
    for (atomic, name) in [(0xD800_0000u32, "ds_add_u32 of 0 - 1"), (0xD804_0000, "ds_sub_u32 of 0 - 1 (adds)")] {
        let words = [
            0x7E06_0280, // v_mov_b32 v3, 0
            0x7E08_02C0, // v_mov_b32 v4, 64
            0xD834_0000, 0x0000_0403, // ds_write_b32 v3, v4
            0xBF8C_C07F, // s_waitcnt lgkmcnt(0)
            0xBF8A_0000, // s_barrier
            0x7E0A_0281, // v_mov_b32 v5, 1
            0x4C02_0A80, // v_sub_nc_u32 v1, 0, v5
            atomic, 0x0000_0103, // ds_add_u32 / ds_sub_u32 v3, v1
            0xBF8C_C07F, // s_waitcnt lgkmcnt(0)
            0xBF8A_0000, // s_barrier
            0xD8D8_0000, 0x0600_0003, // ds_read_b32 v6, v3
            0xBF8C_C07F, // s_waitcnt lgkmcnt(0)
            0x38 << 26 | 0x1C << 18,
            0x80 << 24 | 2 << 16 | 6 << 8, // buffer_store_dword v6, off, s[8:11], 0
        ];
        let mut native = native_of(&words, false, 32);
        if let StageIo::Cs(cs) = &mut native.io {
            cs.rsrc2 |= 1 << 15; // 128 dwords of LDS
        }
        let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
        let shader = lowered_for_width(&native, &p, &HashSet::new(), 32).expect(name);
        if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
            let mut copy = shader.dxil.clone();
            v.validate(&mut copy).expect("DXIL validation failed");
        }
        let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
        let Some(got) = on_the_card(&shader, [0, 0]) else { return };
        // ds_sub of -1 adds 1 per thread
        let expected = if name.starts_with("ds_add") { 32 } else { 96 };
        assert_eq!(got, expected, "{name}");
    }
}

/// One console wave of 64 threads, thread t holding t + 1. A branch on s0 (the wave's) picks
/// `WaveActiveMax` (s0 not 0) or `WaveActiveMin`, and the two reductions share their end:
/// each arm saves EXEC and reduces up to the read of lane 32, the join reads lane 0 and
/// restores EXEC (`deferred_tiled:CS_WaterTransmittanceOpticalDepthProcessing`, 0xb3c-0xbe0).
/// Neither block holds a whole section; `v_permlanex16_b32` lowered on its own reads the lanes
/// the host has, so the translation is the one with the join's code in each arm
/// ([`ir::whole_sections`]), where each section is whole.
#[test]
fn two_reductions_that_share_their_end_translate_as_two_sections() {
    // an arm: save EXEC, the identity `init`, then `op` over the wave up to lane 32's read
    let arm = |init: u32, op: u32| {
        let mut w = vec![
            0xBE8C_25C1, // s_or_saveexec_b64 s[12:13], -1
            0x7E04_02FF,
            init,        // v_mov_b32 v2, init
            0xD501_0002,
            0x0032_0902, // v_cndmask_b32 v2, v2, v4, s[12:13]
        ];
        for dpp in [0xDE54_C102, 0x9360_1202, 0x0009_2402] {
            w.extend([op << 25 | 0x0004_04E9, dpp]); // v_<op>_f32 v2, v2, v2 dpp8:[...]
        }
        w.extend([
            op << 25 | 0x0004_04FA,
            0xFF09_4002, // v_<op>_f32 v2, v2, v2 row_mirror bound_ctrl:1
            0xD778_0003,
            0x0201_0102,               // v_permlanex16_b32 v3, v2, 0, 0
            op << 25 | 0x0004_0503,    // v_<op>_f32 v2, v3, v2
            0xD760_000E,
            0x0001_4102,               // v_readlane_b32 s14, v2, 32
            op << 25 | 0x0004_040E,    // v_<op>_f32 v2, s14, v2
        ]);
        w
    };
    let mut words = vec![
        0x7E08_0D00, // v_cvt_f32_u32 v4, v0
        0x0608_08F2, // v_add_f32 v4, 1.0, v4
        0xBF06_8000, // s_cmp_eq_u32 s0, 0
        0xBF85_0014, // s_cbranch_scc1 to the minimum
    ];
    words.extend(arm(0xFF80_0000, 0x10)); // the maximum from -inf
    words.push(0xBF82_0013); // s_branch to the join
    words.extend(arm(0x7F80_0000, 0x0F)); // the minimum from +inf
    words.extend([
        0xD760_000E,
        0x0001_0102, // v_readlane_b32 s14, v2, 0
        0xBEFE_040C, // s_mov_b64 exec, s[12:13]
        0x7E00_020E, // v_mov_b32 v0, s14
        0x38 << 26 | 0x1C << 18,
        0x80 << 24 | 2 << 16, // buffer_store_dword v0, off, s[8:11], 0
    ]);
    let native = native(&words);
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    // the section starts in the arms and ends in the join; afterwards each block holds whole ones
    let sections = |p: &ir::Program| (0..p.blocks.len()).map(|b| super::wave::sections(p, b).len()).sum::<usize>();
    assert_eq!(sections(&p), 0);
    let q = ir::whole_sections(&p).expect("sections to join");
    assert_eq!(sections(&q), 2);
    let st = structure::structure(&p).expect("structure");
    let res = resource::track(&p, &native.layout, native.stage, Some(&st)).expect("resource::track");
    let io = io::raise(&p, &native).expect("io::raise");
    let bytes = crate::dxbc::build(&[]);
    let c = super::Container::parse(&bytes).expect("container");
    let st_q = structure::structure(&q).expect("structure");
    let io_q = io::raise(&q, &native).expect("io::raise");
    let res_q = resource::track(&q, &native.layout, native.stage, Some(&st_q)).expect("track");
    let was = lower::TEST_HOST_WAVE.replace(Some(32));
    let joined = super::emit_any(&c, &native, &q, &st_q, &io_q, &res_q);
    let shader = super::emit(&c, &native, &p, &st, &io, &res);
    lower::TEST_HOST_WAVE.set(was);
    let shader = shader.expect("emit");
    let joined = joined.expect("the joined sections");
    assert!(shader.dxil == joined.dxil, "the split sections' translation was taken");
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        let mut copy = shader.dxil.clone();
        v.validate(&mut copy).expect("DXIL validation failed");
    }
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    for (s0, expected) in [(1u32, 64.0f32), (0, 1.0)] {
        let Some(got) = on_the_card(&shader, [s0, 0]) else { return };
        assert_eq!(f32::from_bits(got), expected, "s0 = {s0}");
    }
    eprintln!("ran on the hardware device: the maximum or the minimum over the console's 64 lanes");
}

/// One console wave of 64 threads summing doubles over the lanes below s0 as the title's
/// water volumetrics count their lanes (`water_shading:CS_TransmissionVolumetrics`,
/// 0x5c4-0x68c): `v_add_f64` of each dword pair moved by DPP, `v_permlanex16` and lane 32,
/// the sum moved to s[20:21] and taken back as a float (`v_cvt_f32_f64`). The terms here are
/// the thread index as a double (`v_cvt_f64_f32`), so the sum is s0 (s0 - 1) / 2.
#[test]
fn a_wave_sum_of_doubles_is_the_consoles() {
    let mut words = vec![
        0x7E3E_0D00, // v_cvt_f32_u32 v31, v0
        0x7E3A_211F, // v_cvt_f64_f32 v[29:30], v31
        0x7D88_0000, // v_cmp_gt_u32 vcc, s0, v0
        0xBE8C_046A, // s_mov_b64 s[12:13], vcc
        0xBE90_25C1, // s_or_saveexec_b64 s[16:17], -1
        0x7E04_0280, // v_mov_b32 v2, 0
        0x7E06_0280, // v_mov_b32 v3, 0
        0x8792_100C, // s_and_b64 s[18:19], s[12:13], s[16:17]
        0xD501_0002,
        0x004A_3B02, // v_cndmask_b32 v2, v2, v29, s[18:19]
        0xD501_0003,
        0x004A_3D03, // v_cndmask_b32 v3, v3, v30, s[18:19]
    ];
    // v_mov_b32 v4, v2 / v5, v3 with each DPP pattern, then v_add_f64 v[2:3], v[4:5], v[2:3]
    for (code, dpp) in [(0xE9, 0xDE54_C100), (0xE9, 0x9360_1200), (0xE9, 0x0009_2400), (0xFA, 0xFF09_4000)] {
        words.extend([0x7E08_0200 | code, dpp | 2, 0x7E0A_0200 | code, dpp | 3, 0xD564_0002, 0x0002_0504]);
    }
    words.extend([
        0xD778_0004,
        0x0201_0102, // v_permlanex16_b32 v4, v2, 0, 0
        0xD778_0005,
        0x0201_0103, // v_permlanex16_b32 v5, v3, 0, 0
        0xD564_0002,
        0x0002_0504, // v_add_f64 v[2:3], v[4:5], v[2:3]
        0xD760_0012,
        0x0001_4102, // v_readlane_b32 s18, v2, 32
        0xD760_0013,
        0x0001_4103, // v_readlane_b32 s19, v3, 32
        0xD564_0002,
        0x0002_0412, // v_add_f64 v[2:3], s[18:19], v[2:3]
        0xD760_0012,
        0x0001_0102, // v_readlane_b32 s18, v2, 0
        0xD760_0013,
        0x0001_0103, // v_readlane_b32 s19, v3, 0
        0xBE94_0312, // s_mov_b32 s20, s18
        0xBE95_0313, // s_mov_b32 s21, s19
        0xBEFE_0410, // s_mov_b64 exec, s[16:17]
        0x7E00_1E14, // v_cvt_f32_f64 v0, s[20:21]
        0x38 << 26 | 0x1C << 18,
        0x80 << 24 | 2 << 16, // buffer_store_dword v0, off, s[8:11], 0
    ]);
    let native = native(&words);
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    // the section leaves the sum's two dwords in s20 and s21
    let block = (0..p.blocks.len()).find(|&b| !super::wave::sections(&p, b).is_empty()).expect("a section");
    let sums = super::wave::sections(&p, block)
        .into_values()
        .flat_map(|s| s.shapes.into_values())
        .filter(|s| matches!(s, super::wave::Shape::Sum64 { .. }))
        .count();
    assert!(sums >= 2, "{sums} dwords of 64-bit sums");
    let st = structure::structure(&p).expect("structure");
    let res = resource::track(&p, &native.layout, native.stage, Some(&st)).expect("resource::track");
    let io = io::raise(&p, &native).expect("io::raise");
    let bytes = crate::dxbc::build(&[]);
    let c = super::Container::parse(&bytes).expect("container");
    let was = lower::TEST_HOST_WAVE.replace(Some(32));
    let shader = super::emit(&c, &native, &p, &st, &io, &res);
    lower::TEST_HOST_WAVE.set(was);
    let shader = shader.expect("emit");
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        let mut copy = shader.dxil.clone();
        v.validate(&mut copy).expect("DXIL validation failed");
    }
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    for s0 in [64u32, 40, 33, 1, 0] {
        let Some(got) = on_the_card(&shader, [s0, 0]) else { return };
        let want = (s0 * s0.saturating_sub(1) / 2) as f32;
        assert_eq!(f32::from_bits(got), want, "the lanes below {s0}");
    }
    eprintln!("ran on the hardware device: the sums of doubles over the console's 64 lanes");
}

// ---- the paired form: a host wave of 32 for each console wave, two lanes a thread ----

// Operands as the hardware numbers them: SGPR n is n, VCC 106 (its high dword 107), EXEC 126,
// an inline integer n 128 + n (-1: 193), a literal (the next dword) 255, VGPR n 256 + n.
const VCC: u32 = 106;
const EXEC: u32 = 126;
const LIT: u32 = 255;

const fn v(n: u32) -> u32 {
    256 + n
}

const fn k(n: u32) -> u32 {
    128 + n
}

const fn sop1(op: u32, sdst: u32, src0: u32) -> u32 {
    0xBE80_0000 | sdst << 16 | op << 8 | src0
}

const fn sop2(op: u32, sdst: u32, src0: u32, src1: u32) -> u32 {
    0x8000_0000 | op << 23 | sdst << 16 | src1 << 8 | src0
}

/// A branch by `imm` dwords from the next instruction (or `s_barrier`, `imm` 0).
const fn sopp(op: u32, imm: i16) -> u32 {
    0xBF80_0000 | op << 16 | imm as u16 as u32
}

const fn vop1(op: u32, vdst: u32, src0: u32) -> u32 {
    0x7E00_0000 | vdst << 17 | op << 9 | src0
}

const fn vop2(op: u32, vdst: u32, src0: u32, vsrc1: u32) -> u32 {
    op << 25 | vdst << 17 | vsrc1 << 9 | src0
}

const fn vopc(op: u32, src0: u32, vsrc1: u32) -> u32 {
    0x7C00_0000 | op << 17 | vsrc1 << 9 | src0
}

const fn vop3(op: u32, dst: u32, src0: u32, src1: u32) -> [u32; 2] {
    [0xD400_0000 | op << 16 | dst, src0 | src1 << 9]
}

const fn ds(op: u32, vdst: u32, addr: u32, data: u32, offset: u32) -> [u32; 2] {
    [0xD800_0000 | op << 18 | offset, vdst << 24 | data << 8 | addr]
}

/// `buffer_store_dword v<data>, v<addr>, s[8:11], 0 offen offset:<offset>`
const fn store(data: u32, addr: u32, offset: u32) -> [u32; 2] {
    [0xE070_1000 | offset, 0x8002_0000 | data << 8 | addr]
}

/// `v<regs>` stored as each thread's dwords of the UAV, the thread's index (v0) times 16 on.
fn stores(regs: [u32; 4]) -> Vec<u32> {
    let mut w = vec![vop2(0x1A, 15, k(4), 0)]; // v_lshlrev_b32 v15, 4, v0
    for (n, r) in regs.into_iter().enumerate() {
        w.extend(store(r, 15, 4 * n as u32));
    }
    w
}

/// `native` in the paired form on a host of 32-lane waves, validated by `dxil.dll` when
/// present. Where no other form gives the host the program (each still asks for 64-lane
/// waves, or fails), it is also what [`super::emit`] gives it.
fn paired(native: &NativeProgram) -> HostShader {
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    let st = structure::structure(&p).expect("structure");
    let res = resource::track(&p, &native.layout, native.stage, Some(&st)).expect("resource::track");
    let io = io::raise(&p, native).expect("io::raise");
    let bytes = crate::dxbc::build(&[]);
    let c = super::Container::parse(&bytes).expect("container");
    let was = lower::TEST_HOST_WAVE.replace(Some(32));
    let others = super::emit_forms(&c, native, &p, &st, &io, &res);
    let shader = super::paired(&c, native, &p, &io, &res);
    let chosen = super::emit(&c, native, &p, &st, &io, &res);
    lower::TEST_HOST_WAVE.set(was);
    let (shader, wave) = shader.expect("the paired form");
    assert_eq!(wave, Some(32));
    if !matches!(others, Ok((_, wave)) if wave != Some(64)) {
        assert_eq!(chosen.expect("emit").dxil, shader.dxil, "emit's choice");
    }
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        let mut copy = shader.dxil.clone();
        v.validate(&mut copy).expect("DXIL validation failed");
    }
    shader
}

/// `shader` on the hardware device: one group, root constants `s0..s3`, a raw UAV of `dwords`
/// zeroed dwords, which come back; None (and a line) without a device that takes it.
fn run_paired(shader: &HostShader, constants: [u32; 4], dwords: u32) -> Option<Vec<u32>> {
    let Ok(host) = Host::new(false) else {
        eprintln!("no SM 6.6 hardware device; not run");
        return None;
    };
    let (root, pso) = match host.compute_pipeline(shader) {
        Ok(x) => x,
        Err(e) => {
            eprintln!("the hardware device does not take this program: {e}; not run");
            return None;
        }
    };
    let mut ud = vec![0u32; shader.user_data_dwords().max(1) as usize];
    for u in &shader.user_data {
        if let (RootType::Constants, 0, d @ 0..=3) = (u.root, u.slot, u.dword) {
            ud[u.index as usize] = constants[d as usize];
        }
    }
    let mut out = host
        .dispatch(
            &root,
            &pso,
            &Dispatch {
                roots: vec![(0, Root::Constants(ud))],
                views: &[View::RawUav(dwords)],
                samplers: &[],
                groups: [1, 1, 1],
            },
        )
        .unwrap_or_else(|e| panic!("dispatch failed: {e}"));
    Some(out.swap_remove(0))
}

/// Panics at the first dword of `got` that is not the console's (`want`), naming the thread
/// and which of its four dwords.
fn same_as_console(got: &[u32], want: &[u32]) {
    assert_eq!(got.len(), want.len());
    if let Some(n) = (0..want.len()).find(|&n| got[n] != want[n]) {
        panic!("thread {}, dword {}: {:#x}, the console's {:#x}", n / 4, n % 4, got[n], want[n]);
    }
}

/// One console wave of 64 threads with 256 bytes of LDS. The even lanes write their dword in
/// one arm of a per-lane branch; the odd lanes theirs in the other arm, then, in that arm and
/// with no barrier, read their mirror lane's (63 - n, an even lane's); the even lanes read
/// theirs (an odd lane's) after the join. Then lanes 5 and 40 are read (`v_readlane_b32`), the
/// lanes holding more than 250 are counted (`s_bcnt1_i32_b64` of the compare's mask) and
/// counted below each lane (`v_mbcnt_lo/hi`): each of these takes lanes of both halves.
#[test]
fn paired_lanes_share_lds_and_lanes_across_halves_without_barriers() {
    let mut w = vec![vop2(0x1A, 1, k(2), 0)]; // v_lshlrev_b32 v1, 2, v0: this lane's dword
    w.extend(vop3(0x169, 2, v(0), k(7))); // v_mul_lo_u32 v2, v0, 7
    w.extend([
        vop2(0x26, 5, k(63), 0), // v_sub_nc_u32 v5, 63, v0: the mirror lane
        vop2(0x1A, 5, k(2), 5),  // v_lshlrev_b32 v5, 2, v5: its dword
        vop2(0x1B, 3, k(1), 0),  // v_and_b32 v3, 1, v0
        vopc(0xC2, k(0), 3),     // v_cmp_eq_u32 vcc, 0, v3: the even lanes
        sop1(0x24, 12, VCC),     // s_and_saveexec_b64 s[12:13], vcc
        sopp(0x08, 4),           // s_cbranch_execz past the even lanes' arm
        vop2(0x25, 4, LIT, 2),   // v_add_nc_u32 v4, 100, v2
        100,
    ]);
    w.extend(ds(0x0D, 0, 1, 4, 0)); // ds_write_b32 v1, v4
    w.extend([
        sop2(0x15, EXEC, 12, EXEC), // s_andn2_b64 exec, s[12:13], exec: the odd lanes
        sopp(0x08, 6),              // s_cbranch_execz past their arm
        vop2(0x25, 4, LIT, 2),      // v_add_nc_u32 v4, 200, v2
        200,
    ]);
    w.extend(ds(0x0D, 0, 1, 4, 0)); // ds_write_b32 v1, v4
    w.extend(ds(0x36, 6, 5, 0, 0)); // ds_read_b32 v6, v5: an even lane's, from the other arm
    w.extend([
        sop1(0x04, EXEC, 12), // s_mov_b64 exec, s[12:13]
        sop1(0x24, 12, VCC),  // s_and_saveexec_b64 s[12:13], vcc: the even lanes again
    ]);
    w.extend(ds(0x36, 6, 5, 0, 0)); // ds_read_b32 v6, v5: an odd lane's
    w.push(sop1(0x04, EXEC, 12)); // s_mov_b64 exec, s[12:13]
    w.extend(vop3(0x360, 14, v(6), k(5))); // v_readlane_b32 s14, v6, 5
    w.extend(vop3(0x360, 15, v(6), k(40))); // v_readlane_b32 s15, v6, 40
    w.extend([
        sop2(0x00, 14, 14, 15), // s_add_u32 s14, s14, s15
        vopc(0xC1, LIT, 6),     // v_cmp_lt_u32 vcc, 250, v6
        250,
        sop1(0x10, 16, VCC), // s_bcnt1_i32_b64 s16, vcc
    ]);
    w.extend(vop3(0x365, 7, VCC, k(0))); // v_mbcnt_lo_u32_b32 v7, vcc_lo, 0
    w.extend(vop3(0x366, 7, VCC + 1, v(7))); // v_mbcnt_hi_u32_b32 v7, vcc_hi, v7
    w.extend([
        vop1(0x01, 9, 14),  // v_mov_b32 v9, s14
        vop1(0x01, 10, 16), // v_mov_b32 v10, s16
    ]);
    w.extend(stores([6, 9, 10, 7]));
    let shader = paired(&native_with(&w, true, 64, 256));
    // the console: lane n's dword is 7n + 100 (even n) or 7n + 200, and lane n reads 63 - n's
    let dword = |n: u32| 7 * n + if n.is_multiple_of(2) { 100 } else { 200 };
    let read: Vec<u32> = (0..64).map(|n| dword(63 - n)).collect();
    let big = (0..64).filter(|&n| read[n] > 250).fold(0u64, |m, n| m | 1 << n);
    assert!(big as u32 != 0 && big >> 32 != 0, "lanes of both halves counted");
    let want: Vec<u32> = (0..64)
        .flat_map(|n| [read[n], read[5] + read[40], big.count_ones(), (big & ((1 << n) - 1)).count_ones()])
        .collect();
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Some(got) = run_paired(&shader, [0; 4], 256) else { return };
    same_as_console(&got, &want);
    eprintln!("ran on the hardware device: LDS, lane reads and masks across the halves as on the console");
}

/// Two console waves of 64 in a group of 128 (two host waves of 32, 512 bytes of LDS). In
/// each, lane 0 zeroes the wave's dword of LDS; every lane loops (lane & 3) + 1 + wave times,
/// adding its lane number to its sum and 1 to the wave's dword each round (`ds_add_u32`; the
/// loop's branch is the wave's: it goes round while some lane is left); after the loop each
/// lane reads the wave's total, with no barrier; lanes 40-63 read their first lane's sum
/// (`v_readfirstlane_b32`: lane 40, an upper lane), then lanes 20-63 (lane 20, a lower one,
/// though upper lanes have one too); past the barrier, each lane reads the other wave's total.
#[test]
fn paired_lanes_loop_apart_and_meet_the_other_wave_at_the_barrier() {
    let mut w = vec![
        vop2(0x16, 1, k(6), 0),  // v_lshrrev_b32 v1, 6, v0: the console wave
        vop2(0x1B, 2, k(63), 0), // v_and_b32 v2, 63, v0: the lane in it
        vop2(0x1A, 11, k(2), 1), // v_lshlrev_b32 v11, 2, v1: the wave's dword
        vop1(0x01, 12, k(0)),    // v_mov_b32 v12, 0
        sop1(0x04, 22, EXEC),    // s_mov_b64 s[22:23], exec
        vopc(0xD2, k(0), 2),     // v_cmpx_eq_u32 exec, 0, v2: lane 0
    ];
    w.extend(ds(0x0D, 0, 11, 12, 0)); // ds_write_b32 v11, v12
    w.extend([
        sop1(0x04, EXEC, 22),   // s_mov_b64 exec, s[22:23]
        vop2(0x1B, 3, k(3), 2), // v_and_b32 v3, 3, v2
        vop2(0x25, 3, k(1), 3), // v_add_nc_u32 v3, 1, v3
        vop2(0x25, 3, v(1), 3), // v_add_nc_u32 v3, v1, v3: the rounds
        vop1(0x01, 4, k(0)),    // v_mov_b32 v4, 0
        vop1(0x01, 13, k(1)),   // v_mov_b32 v13, 1
        sop1(0x04, 20, EXEC),   // s_mov_b64 s[20:21], exec
        vop2(0x25, 4, v(2), 4), // loop: v_add_nc_u32 v4, v2, v4
    ]);
    w.extend(ds(0x00, 0, 11, 13, 0)); // ds_add_u32 v11, v13
    w.extend([
        vop2(0x25, 3, 193, 3),       // v_add_nc_u32 v3, -1, v3
        vopc(0xC5, k(0), 3),         // v_cmp_ne_u32 vcc, 0, v3
        sop2(0x0F, EXEC, EXEC, VCC), // s_and_b64 exec, exec, vcc
        sopp(0x09, -7),              // s_cbranch_execnz loop
        sop1(0x04, EXEC, 20),        // s_mov_b64 exec, s[20:21]
    ]);
    w.extend(ds(0x36, 5, 11, 0, 0)); // ds_read_b32 v5, v11: the wave's total
    w.extend([
        sop1(0x04, 24, EXEC), // s_mov_b64 s[24:25], exec
        vopc(0xD1, k(39), 2), // v_cmpx_lt_u32 exec, 39, v2: lanes 40-63
        vop1(0x02, 6, v(4)),  // v_readfirstlane_b32 s6, v4
        sop1(0x04, EXEC, 24), // s_mov_b64 exec, s[24:25]
        vopc(0xD1, k(19), 2), // v_cmpx_lt_u32 exec, 19, v2: lanes 20-63
        vop1(0x02, 7, v(4)),  // v_readfirstlane_b32 s7, v4
        sop1(0x04, EXEC, 24), // s_mov_b64 exec, s[24:25]
        vop1(0x01, 6, 6),     // v_mov_b32 v6, s6
        vop1(0x01, 9, 7),     // v_mov_b32 v9, s7
        0xD400_0000 | 0x346 << 16 | 6, // v_lshl_add_u32 v6, v9, 16, v6
        v(9) | k(16) << 9 | v(6) << 18,
        sopp(0x0A, 0),            // s_barrier
        vop2(0x1D, 14, k(4), 11), // v_xor_b32 v14, 4, v11: the other wave's dword
    ]);
    w.extend(ds(0x36, 7, 14, 0, 0)); // ds_read_b32 v7, v14
    w.extend(stores([4, 5, 6, 7]));
    let shader = paired(&native_with(&w, true, 128, 512));
    let rounds = |wave: u32, lane: u32| (lane & 3) + 1 + wave;
    let total = |wave: u32| (0..64).map(|lane| rounds(wave, lane)).sum::<u32>();
    let first = |wave: u32, lane: u32| lane * rounds(wave, lane);
    let want: Vec<u32> = (0..128)
        .flat_map(|t| {
            let (wave, lane) = (t / 64, t % 64);
            [first(wave, lane), total(wave), first(wave, 20) << 16 | first(wave, 40), total(1 - wave)]
        })
        .collect();
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Some(got) = run_paired(&shader, [0; 4], 512) else { return };
    same_as_console(&got, &want);
    eprintln!("ran on the hardware device: two console waves, their loops and LDS as on the console");
}

/// One console wave: `ds_bpermute_b32` from the mirror lane; a DPP row shift whose row mask
/// leaves rows 0 and 3 alone (rows 2 and 3 are lanes 32-63: rows are counted in console lanes); a
/// `ds_swizzle_b32` swapping the two 16-lane rows of each 32 lanes; `v_writelane_b32` of s0
/// into lane 45.
#[test]
fn paired_lanes_permute_shift_and_write_lanes_of_both_halves() {
    let mut w = vop3(0x169, 1, v(0), k(3)).to_vec(); // v_mul_lo_u32 v1, v0, 3
    w.extend([
        vop2(0x25, 1, k(1), 1),  // v_add_nc_u32 v1, 1, v1
        vop2(0x26, 2, k(63), 0), // v_sub_nc_u32 v2, 63, v0
        vop2(0x1A, 2, k(2), 2),  // v_lshlrev_b32 v2, 2, v2: the mirror lane's address
    ]);
    w.extend(ds(0xB3, 3, 2, 1, 0)); // ds_bpermute_b32 v3, v2, v1
    w.extend([
        vop1(0x01, 4, LIT), // v_mov_b32 v4, 1000
        1000,
        vop1(0x01, 4, 0xFA), // v_mov_b32 v4, v1 row_shr:1 row_mask:0x6 bank_mask:0xf
        0x6 << 28 | 0xF << 24 | 0x111 << 8 | 1,
    ]);
    w.extend(ds(0x35, 5, 1, 0, 0x401F)); // ds_swizzle_b32 v5, v1 offset:swizzle(BITMASK_PERM, xor 16)
    w.push(vop1(0x01, 6, v(0))); // v_mov_b32 v6, v0
    w.extend(vop3(0x361, 6, 0, k(45))); // v_writelane_b32 v6, s0, 45
    w.extend(stores([3, 4, 5, 6]));
    let shader = paired(&native_with(&w, true, 64, 0));
    let s0 = 0xABCD_1234;
    let x = |n: u32| 3 * n + 1;
    let want: Vec<u32> = (0..64)
        .flat_map(|n| {
            let shifted = if matches!(n >> 4, 1 | 2) && n & 15 != 0 { x(n - 1) } else { 1000 };
            let swizzled = x(n & 32 | (n & 31) ^ 16);
            [x(63 - n), shifted, swizzled, if n == 45 { s0 } else { n }]
        })
        .collect();
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Some(got) = run_paired(&shader, [s0, 0, 0, 0], 256) else { return };
    same_as_console(&got, &want);
    eprintln!("ran on the hardware device: permutes, row masks, swizzles and lane writes as on the console");
}

/// [`program`]'s wave OR inside a per-lane branch, in the paired form: the OR of both halves'
/// lanes, whichever take the branch; nothing stored when none does.
#[test]
fn paired_lanes_or_a_wave_inside_a_lane_branch() {
    for (skip, name) in [(0xBF88_0000, "s_cbranch_execz"), (0xBF84_0000, "s_cbranch_scc0")] {
        let shader = paired(&program(skip));
        let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
        let cases = [(64u32, u32::MAX), (40, 0x000F_FFFF), (33, 0x0001_FFFF), (10, 0x1F), (1, 1), (0, 0)];
        for (below, expected) in cases {
            let Some(got) = on_the_card(&shader, [below, 0]) else { return };
            assert_eq!(got, expected, "{name}: {below} threads in the branch");
        }
    }
    eprintln!("ran on the hardware device: the OR of both halves in every case");
}

/// One console wave of 64 threads, thread t holding the bit `1 << (t >> 1)`: the threads below
/// s0 OR theirs over the wave inside `if (t < s0) { ... }` ([`super::wave::WAVE_OR_V7_TO_S7`]
/// into s7, which holds 0x77 before), and every thread stores s7 after the branch: the wave's
/// OR, or 0x77 when no thread took the branch and the wave skipped it. A wave-wide value used
/// past a per-lane branch, as the title's tile classification and light clustering programs
/// use theirs: no form in two host waves takes it (they would meet inside the branch, and the
/// skip cannot be left out: s7 would no longer keep 0x77), and the approximate form, each host
/// wave a wave of its own, gives each half its own OR, 0x77 to the upper half while s0 <= 32.
/// The title's 39 such programs ran approximate; [`super::emit`] now gives the paired form.
#[test]
fn a_wave_value_used_past_a_lane_branch_takes_the_paired_form_not_an_approximate_one() {
    let mut w = vec![
        sop1(0x03, 7, LIT), // s_mov_b32 s7, 0x77
        0x77,
        vop1(0x01, 5, k(1)),    // v_mov_b32 v5, 1
        vop2(0x16, 6, k(1), 0), // v_lshrrev_b32 v6, 1, v0
        vop2(0x1A, 7, v(6), 5), // v_lshlrev_b32 v7, v6, v5
        vopc(0xC4, 0, 0),       // v_cmp_gt_u32 vcc, s0, v0
        sop1(0x24, 12, VCC),    // s_and_saveexec_b64 s[12:13], vcc
        sopp(0x08, 20),         // s_cbranch_execz past the OR
    ];
    w.extend_from_slice(&super::wave::WAVE_OR_V7_TO_S7);
    w.extend([
        sop1(0x04, EXEC, 12), // s_mov_b64 exec, s[12:13]
        vop1(0x01, 8, 7),     // v_mov_b32 v8, s7
    ]);
    w.extend(stores([8, 7, 6, 0]));
    let native = native_with(&w, true, 64, 0);
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    let st = structure::structure(&p).expect("structure");
    let res = resource::track(&p, &native.layout, native.stage, Some(&st)).expect("resource::track");
    let io = io::raise(&p, &native).expect("io::raise");
    let bytes = crate::dxbc::build(&[]);
    let c = super::Container::parse(&bytes).expect("container");
    let was = lower::TEST_HOST_WAVE.replace(Some(32));
    let pairs = super::paired(&c, &native, &p, &io, &res);
    let chosen = super::emit(&c, &native, &p, &st, &io, &res);
    // (as before: the paired form only where no other form takes the program)
    let off = super::TEST_PAIRS.replace(Some(false));
    let approximate = super::emit(&c, &native, &p, &st, &io, &res);
    super::TEST_PAIRS.set(off);
    lower::TEST_HOST_WAVE.set(was);
    let (pairs, wave) = pairs.expect("the paired form");
    assert_eq!(wave, Some(32));
    let (chosen, approximate) = (chosen.expect("emit"), approximate.expect("an approximate form"));
    assert!(chosen.dxil == pairs.dxil, "emit's choice is not the paired form");
    assert!(approximate.dxil != pairs.dxil, "without the paired form, emit gives it anyway");
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        let mut copy = chosen.dxil.clone();
        v.validate(&mut copy).expect("DXIL validation failed");
    }
    // the console: every thread stores the OR of the threads below s0, or 0x77 with none
    let want = |s0: u32| -> Vec<u32> {
        let or = (0..64u32).filter(|&t| t < s0).fold(0u32, |m, t| m | 1 << (t >> 1));
        let s7 = if s0 == 0 { 0x77 } else { or };
        (0..64u32).flat_map(|t| [s7, 1 << (t >> 1), t >> 1, t]).collect()
    };
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    for s0 in [64u32, 40, 33, 32, 10, 1, 0] {
        let Some(got) = run_paired(&chosen, [s0, 0, 0, 0], 256) else { return };
        same_as_console(&got, &want(s0));
    }
    // each half on its own: with s0 = 10 the upper half took no branch and kept 0x77
    let Some(halves) = run_paired(&approximate, [10, 0, 0, 0], 256) else { return };
    assert_eq!((want(10)[0], want(10)[4 * 40]), (0x1F, 0x1F));
    assert_eq!((halves[0], halves[4 * 40]), (0x1F, 0x77), "the approximate form's lanes 0 and 40");
    eprintln!("ran on the hardware device: the wave's OR past the branch in every lane, not each half's");
}

/// [`half_sum_program`] in the paired form: each half's tree over its 32 lanes, then the two
/// halves' sums, the console's last addition.
#[test]
fn paired_lanes_sum_halves_as_the_consoles_tree() {
    let (native, tree, _) = half_sum_program();
    let shader = paired(&native);
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Some(got) = on_the_card(&shader, [0, 0]) else { return };
    assert_eq!(got, u32::from(tree));
    eprintln!("ran on the hardware device: the sum {tree:#x} of the console's tree");
}

/// The lens-flare scans' allocation (`lens_flares:CS_ScanMip1`, 0x5c0-0x61c): in a branch
/// of the lanes below s0, the first lane adds the wave's count times a size (s1) to a
/// counter, and each lane takes base + below × size with `v_mad_u32_u24`. That is the
/// idiom a host wave may run alone ([`super::idiom::wave_local`]): each half reserves its own
/// count's slots. The same with the count where the base goes is not.
#[test]
fn an_allocation_in_units_of_a_size_is_the_idiom() {
    let program = |base: u32| -> Vec<u8> {
        let words = [
            0x7D88_0000, // v_cmp_gt_u32 vcc, s0, v0
            0xBE8C_246A, // s_and_saveexec_b64 s[12:13], vcc
            0xBF88_0016, // s_cbranch_execz past the allocation
            0xBE82_047E, // s_mov_b64 s[2:3], exec
            0xBE84_147E, // s_ff1_i32_b64 s4, exec
            0xD765_0004,
            0x0001_00C1, // v_mbcnt_lo_u32_b32 v4, -1, 0
            0xD766_0004,
            0x0002_08C1, // v_mbcnt_hi_u32_b32 v4, -1, v4: the lane
            0xBE86_047E, // s_mov_b64 s[6:7], exec
            0x7DA4_0804, // v_cmpx_eq_u32 exec, s4, v4: the first lane
            0xBE82_1002, // s_bcnt1_i32_b64 s2, s[2:3]: the count
            0x7E02_0201, // v_mov_b32 v1, s1: the size
            0x160A_0202, // v_mul_u32_u24 v5, s2, v1
            0x7E16_0280, // v_mov_b32 v11, 0
            0xE0C8_6000,
            0x8002_050B, // buffer_atomic_add v5, v11, s[8:11], 0 idxen glc
            0xBEFE_0406, // s_mov_b64 exec, s[6:7]
            0xD765_0004,
            0x0001_007E, // v_mbcnt_lo_u32_b32 v4, exec_lo, 0
            0xD766_0004,
            0x0002_087F, // v_mbcnt_hi_u32_b32 v4, exec_hi, v4: the lanes below
            0x7E0A_0505, // v_readfirstlane_b32 s5, v5: the base
            0xD543_0006,
            0x0002_0304 | base << 18, // v_mad_u32_u24 v6, v4, v1, s<base>
            0xBEFE_040C, // s_mov_b64 exec, s[12:13]
            S_ENDPGM,
        ];
        words.iter().flat_map(|w| w.to_le_bytes()).collect()
    };
    let idiom = |code: &[u8]| {
        let p = ir::build(code, true, 256).unwrap();
        let st = structure::structure(&p).unwrap();
        super::idiom::wave_local(&p, &st)
    };
    assert_eq!(idiom(&program(5)), Ok(()));
    assert!(idiom(&program(2)).is_err(), "the count as the base");
}

// ---- a lane read inside a whole-wave section of a value from before it ----

/// One console wave of 64 threads: inside a whole-wave section, lane 3 of v1 = t + 1, set
/// before the section, read to s14 and stored by every thread to its dword (4 every thread).
/// The section's simulation knows v1 only as each lane's own value, not lane 3's: it may not
/// give s14 as each lane's v1. Each form that takes the program stores what the console does.
#[test]
fn a_lane_read_in_a_section_of_a_value_from_before_it_is_that_lane() {
    let w = [
        vop2(0x25, 1, k(1), 0), // v_add_nc_u32 v1, 1, v0
        sop1(0x25, 12, k(65)),  // s_or_saveexec_b64 s[12:13], -1
        0xD760_000E,
        0x0001_0701,             // v_readlane_b32 s14, v1, 3
        sop1(0x04, EXEC, 12),    // s_mov_b64 exec, s[12:13]
        vop1(0x01, 3, 14),       // v_mov_b32 v3, s14
        vop2(0x1A, 15, k(2), 0), // v_lshlrev_b32 v15, 2, v0
        store(3, 15, 0)[0],
        store(3, 15, 0)[1],
    ];
    let native = native(&w);
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    let st = structure::structure(&p).expect("structure");
    let res = resource::track(&p, &native.layout, native.stage, Some(&st)).expect("resource::track");
    let io = io::raise(&p, &native).expect("io::raise");
    let bytes = crate::dxbc::build(&[]);
    let c = super::Container::parse(&bytes).expect("container");
    let was = lower::TEST_HOST_WAVE.replace(Some(32));
    let narrow = super::emit(&c, &native, &p, &st, &io, &res);
    let pair = super::paired(&c, &native, &p, &io, &res).map(|(s, _)| s);
    lower::TEST_HOST_WAVE.set(was);
    let wide = lowered_for_width(&native, &p, &HashSet::new(), 64);
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    for (shader, form) in [(wide, "64-lane waves"), (narrow, "the form emit chooses"), (pair, "the paired form")] {
        let Ok(shader) = shader else {
            eprintln!("{form}: not translated");
            continue;
        };
        match run_once(false, &shader, 64) {
            Some(got) => assert!(got.iter().all(|&x| x == 4), "{form}: {got:?}, the console's 4 in every thread"),
            None => eprintln!("no device that takes {form}; not run"),
        }
    }
}

/// `shader` on WARP or the hardware device: one group, zero root constants, a raw UAV of
/// `dwords` zeroed dwords, which come back; None (and a line) without a device that takes it.
fn run_once(warp: bool, shader: &HostShader, dwords: u32) -> Option<Vec<u32>> {
    let host = Host::new(warp).map_err(|e| eprintln!("device: {e}")).ok()?;
    let (root, pso) = host.compute_pipeline(shader).map_err(|e| eprintln!("pipeline: {e}")).ok()?;
    let ud = vec![0u32; shader.user_data_dwords().max(1) as usize];
    let mut out = host
        .dispatch(&root, &pso, &Dispatch {
            roots: vec![(0, Root::Constants(ud))],
            views: &[View::RawUav(dwords)],
            samplers: &[],
            groups: [1, 1, 1],
        })
        .unwrap_or_else(|e| panic!("dispatch failed: {e}"));
    Some(out.swap_remove(0))
}

// ---- a lane of a reduction read past the restore of EXEC ----

/// One console wave of 64 threads; the threads below 40 sum t + 1 over the wave as the title's
/// programs do (`v_cndmask` of the saved EXEC, DPP and `v_permlanex16` over each half, lane 0
/// added to every lane), but read lane 63, the only one holding the whole sum, after restoring
/// EXEC and after `s_and_saveexec` to the threads below 20 (0x218 m133 at 0x124-0x128). Those
/// store it to dword 0 of their two (8 bytes at 8 t) and t + 1 to dword 1.
fn lane_read_past_the_restore_program() -> (NativeProgram, Vec<u32>) {
    let mut w = vec![
        vop2(0x25, 1, k(1), 0),  // v_add_nc_u32 v1, 1, v0
        vopc(0xD4, k(40), 0),    // v_cmpx_gt_u32 exec, 40, v0
        sop1(0x25, 12, k(65)),   // s_or_saveexec_b64 s[12:13], -1
        0xD501_0002,
        0x0032_0280, // v_cndmask_b32 v2, 0, v1, s[12:13]
    ];
    for dpp in [0xDE54_C102u32, 0x9360_1202, 0x0009_2402] {
        w.extend([vop2(0x25, 2, 0xE9, 2), dpp]); // v_add_nc_u32 v2, v2, v2 dpp8:[...]
    }
    w.extend([
        vop2(0x25, 2, 0xFA, 2),
        0xFF09_4002, // v_add_nc_u32 v2, v2, v2 row_mirror bound_ctrl:1
        0xD778_0003,
        0x0201_0102,             // v_permlanex16_b32 v3, v2, 0, 0
        vop2(0x25, 2, v(3), 2),  // v_add_nc_u32 v2, v3, v2
        0xD760_000E,
        0x0001_0102,             // v_readlane_b32 s14, v2, 0
        vop2(0x25, 2, 14, 2),    // v_add_nc_u32 v2, s14, v2
        sop1(0x04, EXEC, 12),    // s_mov_b64 exec, s[12:13]
        vopc(0xC4, k(20), 0),    // v_cmp_gt_u32 vcc, 20, v0
        sop1(0x24, 16, VCC),     // s_and_saveexec_b64 s[16:17], vcc
        0xD760_000F,
        0x0001_7F02,             // v_readlane_b32 s15, v2, 63
        vop1(0x01, 3, 15),       // v_mov_b32 v3, s15
        vop2(0x1A, 15, k(3), 0), // v_lshlrev_b32 v15, 3, v0
    ]);
    w.extend(store(3, 15, 0));
    w.extend(store(1, 15, 4));
    let sum: u32 = (1..=40).sum();
    let console = (0..64u32).flat_map(|t| if t < 20 { [sum, t + 1] } else { [0, 0] }).collect();
    (native(&w), console)
}

#[test]
fn a_lane_of_a_reduction_read_past_the_restore_is_the_consoles_sum() {
    let (native, console) = lane_read_past_the_restore_program();
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    let wide = lowered_for_width(&native, &p, &HashSet::new(), 64).expect("lower for 64 lanes");
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        v.validate(&mut wide.dxil.clone()).expect("DXIL validation failed");
    }
    let narrow = chosen_for_32_lanes(&native);
    let pair = paired(&native);
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    for (warp, shader, form) in [
        (true, &wide, "64-lane waves on WARP"),
        (false, &wide, "64-lane waves on the hardware device"),
        (false, &narrow, "the form emit chooses for 32-lane waves"),
        (false, &pair, "the paired form"),
    ] {
        match run_once(warp, shader, 64 * 2) {
            Some(got) => {
                if let Some(n) = (0..console.len()).find(|&n| got[n] != console[n]) {
                    panic!("{form}: thread {}, dword {}: {:#x}, the console's {:#x}", n / 2, n % 2, got[n], console[n]);
                }
                eprintln!("{form}: as on the console");
            }
            None => eprintln!("no device that takes {form}; not run"),
        }
    }
}

// ---- lane masks the code sets to constant lanes ----

/// One console wave of 64 threads; thread t stores t + 1 to dwords 0..7 of its sixteen (64
/// bytes at 64 t), each under EXEC set to constant lanes one of the ways the title's programs
/// do it: `s_bfm_b64 exec, 32, 0` before a reduction over LDS; the halves moved apart
/// (`s_mov_b32 exec_lo, 17`, `exec_hi`); `s_bitreplicate_b64_b32 exec, lit`; the low half alone
/// after `s_mov_b64 exec, -1` (`s_mov_b32`, `s_pack_ll_b32_b16 exec_lo, 1, 1`); and the other
/// 32-bit constants (`s_movk_i32`, `s_brev_b32`, `s_pack_lh/hh_b32_b16`). It stores EXEC as
/// copied in the first two ways as numbers to dwords 8..11. The halves differ, so that lanes
/// 32-63 taking the low half's bits shows.
fn constant_exec_program() -> (NativeProgram, Vec<u32>) {
    const ALL: u64 = u64::MAX;
    let replicate = 0x0F0F_3C3Cu32;
    let replicated = (0..32).filter(|n| replicate >> n & 1 == 1).fold(0u64, |r, n| r | 3 << (2 * n));
    // (the code setting EXEC after `s_mov_b64 exec, -1`, the console's EXEC)
    let cases: [(Vec<u32>, u64); 8] = [
        (vec![sop2(0x25, EXEC, k(32), k(0))], 0xFFFF_FFFF), // s_bfm_b64 exec, 32, 0
        (
            vec![
                sop1(0x03, EXEC, k(17)),   // s_mov_b32 exec_lo, 17
                sop1(0x03, EXEC + 1, LIT), // s_mov_b32 exec_hi, 0x110000
                0x0011_0000,
            ],
            0x0011_0000_0000_0011,
        ),
        (vec![sop1(0x3B, EXEC, LIT), replicate], replicated), // s_bitreplicate_b64_b32 exec, lit
        (vec![sop1(0x03, EXEC, LIT), 0x0055_0055], ALL << 32 | 0x0055_0055), // s_mov_b32 exec_lo, lit
        (vec![sop2(0x32, EXEC, k(1), LIT), 0x8003], ALL << 32 | 0x8003_0001), // s_pack_ll_b32_b16 exec_lo, 1, lit
        (
            vec![
                0xB000_0000 | EXEC << 16 | 0x80FF, // s_movk_i32 exec_lo, 0x80ff: sign-extended
                sop1(0x0B, EXEC + 1, k(15)),       // s_brev_b32 exec_hi, 15
            ],
            0xF000_0000_FFFF_80FF,
        ),
        // s_pack_hh_b32_b16 exec_hi, lit, -1
        (vec![sop2(0x34, EXEC + 1, LIT, k(65)), 0x0009_0000], 0xFFFF_0009_FFFF_FFFF),
        // s_pack_lh_b32_b16 exec_lo, lit, -16
        (vec![sop2(0x33, EXEC, LIT, k(80)), 0x0000_0C03], ALL << 32 | 0xFFFF_0C03),
    ];
    let mut w = vec![
        vop2(0x1A, 15, k(6), 0), // v_lshlrev_b32 v15, 6, v0: the thread's 64 bytes
        vop2(0x25, 1, k(1), 0),  // v_add_nc_u32 v1, 1, v0
    ];
    for (n, (code, _)) in cases.iter().enumerate() {
        w.push(sop1(0x04, EXEC, k(65))); // s_mov_b64 exec, -1 (k(65): the inline -1)
        w.extend(code);
        w.extend(store(1, 15, 4 * n as u32));
        if n < 2 {
            w.push(sop1(0x04, 20 + 2 * n as u32, EXEC)); // s_mov_b64 s[20 + 2n:21 + 2n], exec
        }
    }
    w.push(sop1(0x04, EXEC, k(65))); // s_mov_b64 exec, -1
    for (n, s) in [20u32, 21, 22, 23].into_iter().enumerate() {
        w.push(vop1(0x01, 2, s)); // v_mov_b32 v2, s<s>
        w.extend(store(2, 15, 32 + 4 * n as u32));
    }
    let console: Vec<u32> = (0..64u32)
        .flat_map(|t| {
            let mut d: Vec<u32> = cases.iter().map(|&(_, m)| if m >> t & 1 == 1 { t + 1 } else { 0 }).collect();
            d.extend([0xFFFF_FFFF, 0, 0x11, 0x0011_0000, 0, 0, 0, 0]);
            d
        })
        .collect();
    (native(&w), console)
}

/// `native` as [`super::emit`] gives it to a host of 32-lane waves, validated by `dxil.dll`
/// when present.
fn chosen_for_32_lanes(native: &NativeProgram) -> HostShader {
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    let st = structure::structure(&p).expect("structure");
    let res = resource::track(&p, &native.layout, native.stage, Some(&st)).expect("resource::track");
    let io = io::raise(&p, native).expect("io::raise");
    let bytes = crate::dxbc::build(&[]);
    let c = super::Container::parse(&bytes).expect("container");
    let was = lower::TEST_HOST_WAVE.replace(Some(32));
    let shader = super::emit(&c, native, &p, &st, &io, &res);
    lower::TEST_HOST_WAVE.set(was);
    let shader = shader.expect("emit");
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        let mut copy = shader.dxil.clone();
        v.validate(&mut copy).expect("DXIL validation failed");
    }
    shader
}

#[test]
fn exec_set_to_constant_lanes_guards_both_halves_as_on_the_console() {
    let (native, console) = constant_exec_program();
    // the structurisation sees the constants, not masks it cannot see into
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    let st = structure::structure(&p).expect("structure");
    let opaque = st.guards.iter().flatten().flatten().any(|&g| {
        st.bools.support(g).iter().any(|a| matches!(a, structure::Atom::Opaque(_)))
    });
    assert!(!opaque, "a constant EXEC taken as an opaque mask");
    // a host of 64-lane waves: the console's waves
    let wide = lowered_for_width(&native, &p, &HashSet::new(), 64).expect("lower for 64 lanes");
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        v.validate(&mut wide.dxil.clone()).expect("DXIL validation failed");
    }
    let narrow = chosen_for_32_lanes(&native);
    let pair = paired(&native);
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let run = |warp: bool, shader: &HostShader| -> Option<Vec<u32>> {
        let host = Host::new(warp).map_err(|e| eprintln!("device: {e}")).ok()?;
        let (root, pso) = host.compute_pipeline(shader).map_err(|e| eprintln!("pipeline: {e}")).ok()?;
        let ud = vec![0u32; shader.user_data_dwords().max(1) as usize];
        let mut out = host
            .dispatch(&root, &pso, &Dispatch {
                roots: vec![(0, Root::Constants(ud))],
                views: &[View::RawUav(64 * 16)],
                samplers: &[],
                groups: [1, 1, 1],
            })
            .unwrap_or_else(|e| panic!("dispatch failed: {e}"));
        Some(out.swap_remove(0))
    };
    // (WARP's waves have 4 lanes: none of these forms runs there)
    for (warp, shader, form) in [
        (true, &wide, "64-lane waves on WARP"),
        (false, &wide, "64-lane waves on the hardware device"),
        (false, &narrow, "the form emit chooses for 32-lane waves"),
        (false, &pair, "the paired form"),
    ] {
        match run(warp, shader) {
            Some(got) => same_in_sixteen(&got, &console, form),
            None => eprintln!("no device that takes {form}; not run"),
        }
    }
}

/// Panics at the first dword of `got` that is not the console's, naming the thread and which
/// of its eight dwords.
fn same_in_sixteen(got: &[u32], want: &[u32], run: &str) {
    assert_eq!(got.len(), want.len());
    if let Some(n) = (0..want.len()).find(|&n| got[n] != want[n]) {
        panic!("{run}: thread {}, dword {}: {:#x}, the console's {:#x}", n / 16, n % 16, got[n], want[n]);
    }
    eprintln!("{run}: as on the console");
}

// ---- SCC of a mask operation read as a number ----

/// One console wave of 64 threads. `s_and_b64 s[12:13], vcc, exec` of five compares sets SCC
/// when some lane of the 64 has its bit, which the code reads as a number, as 0x218 m020 does
/// at 0xf8-0x100 (`s_add_u32 s16, src_scc, s16`): by `s_cselect_b32`, `src_scc` and the carry
/// of `s_addc_u32`. The masks are lanes 40-63 (the upper half alone), none, lanes 0-7 (the
/// lower half alone), lanes 48-63 and lanes 56-63; their answers make bits 0-4 of s20, which
/// every thread stores to its dword.
fn scc_read_as_a_number_program() -> (NativeProgram, Vec<u32>) {
    let some = |cmp: u32, c: u32| [vopc(cmp, k(c), 0), sop2(0x0F, 12, VCC, EXEC)]; // s_and_b64 s[12:13], vcc, exec
    let mut w = vec![sop1(0x03, 20, k(0))]; // s_mov_b32 s20, 0
    w.extend(some(0xC1, 39)); // v_cmp_lt_u32 vcc, 39, v0
    w.extend([sop2(0x0A, 14, k(1), k(0)), sop2(0x10, 20, 20, 14)]); // s_cselect_b32 s14, 1, 0; s_or_b32 s20, s20, s14
    w.extend(some(0xC1, 63));
    w.extend([sop2(0x0A, 14, k(2), k(0)), sop2(0x10, 20, 20, 14)]);
    w.extend(some(0xC4, 8)); // v_cmp_gt_u32 vcc, 8, v0
    w.extend([sop2(0x0A, 14, k(4), k(0)), sop2(0x10, 20, 20, 14)]);
    w.extend(some(0xC1, 47));
    w.extend([sop2(0x1E, 14, 253, k(3)), sop2(0x10, 20, 20, 14)]); // s_lshl_b32 s14, src_scc, 3
    w.extend(some(0xC1, 55));
    w.extend([sop2(0x04, 14, k(0), k(0)), sop2(0x1E, 14, 14, k(4)), sop2(0x10, 20, 20, 14)]); // s_addc_u32 s14, 0, 0; << 4
    w.extend([vop1(0x01, 1, 20), vop2(0x1A, 15, k(2), 0)]); // v_mov_b32 v1, s20; v_lshlrev_b32 v15, 2, v0
    w.extend(store(1, 15, 0));
    // the console: bit n when the 64-bit mask of case n is not zero
    let masks: [fn(u32) -> bool; 5] = [|t| t > 39, |t| t > 63, |t| t < 8, |t| t > 47, |t| t > 55];
    let scc = |f: fn(u32) -> bool| (0..64).filter(|&t| f(t)).fold(0u64, |m, t| m | 1 << t) != 0;
    let bits = masks.iter().enumerate().map(|(n, &f)| u32::from(scc(f)) << n).sum();
    (native(&w), vec![bits; 64])
}

#[test]
fn scc_of_a_mask_operation_read_as_a_number_is_whether_some_console_lane_has_its_bit() {
    let (native, console) = scc_read_as_a_number_program();
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    let wide = lowered_for_width(&native, &p, &HashSet::new(), 64).expect("lower for 64 lanes");
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        v.validate(&mut wide.dxil.clone()).expect("DXIL validation failed");
    }
    let narrow = chosen_for_32_lanes(&native);
    let pair = paired(&native);
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    for (warp, shader, form) in [
        (true, &wide, "64-lane waves on WARP"),
        (false, &wide, "64-lane waves on the hardware device"),
        (false, &narrow, "the form emit chooses for 32-lane waves"),
        (false, &pair, "the paired form"),
    ] {
        match run_once(warp, shader, 64) {
            Some(got) => {
                if let Some(t) = (0..64).find(|&t| got[t] != console[t]) {
                    panic!("{form}: thread {t}: {:#b}, the console's {:#b}", got[t], console[t]);
                }
                eprintln!("{form}: as on the console");
            }
            None => eprintln!("no device that takes {form}; not run"),
        }
    }
}

/// Inside `if (t < 16)`, `s_not_b64 s[12:13], vcc` of `t < 16` taken over the wave holds lanes
/// 16-63, none of which runs there: its SCC is set on the console, while the running lanes have
/// no bit. Where host waves see only the running lanes that SCC is refused; the paired form,
/// whose host wave runs every console lane, gives the console's. Threads 0-15 store 1.
fn scc_of_lanes_elsewhere_program() -> (NativeProgram, Vec<u32>) {
    let mut w = vec![
        vop2(0x1A, 15, k(2), 0),  // v_lshlrev_b32 v15, 2, v0
        vopc(0xC4, k(16), 0),     // v_cmp_gt_u32 vcc, 16, v0
        sop1(0x24, 16, VCC),      // s_and_saveexec_b64 s[16:17], vcc
        sopp(0x08, 5),            // s_cbranch_execz past the store
        sop1(0x08, 12, VCC),      // s_not_b64 s[12:13], vcc
        sop2(0x0A, 14, k(1), k(0)), // s_cselect_b32 s14, 1, 0
        vop1(0x01, 1, 14),        // v_mov_b32 v1, s14
    ];
    w.extend(store(1, 15, 0));
    w.push(sop1(0x04, EXEC, 16)); // s_mov_b64 exec, s[16:17]
    // the console: SCC of !(lanes 0-15) over 64 lanes, stored by threads 0-15
    let scc = !(0..16).fold(0u64, |m, t| m | 1 << t) != 0;
    (native(&w), (0..64).map(|t| u32::from(t < 16 && scc)).collect())
}

#[test]
fn scc_of_a_mask_with_lanes_elsewhere_read_as_a_number_takes_the_paired_form() {
    let (native, console) = scc_of_lanes_elsewhere_program();
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    let err = lowered_for_width(&native, &p, &HashSet::new(), 64).map(|_| ()).expect_err("refused");
    assert!(err.to_string().contains(lower::LANE_MASK), "{err}");
    let narrow = chosen_for_32_lanes(&native);
    let pair = paired(&native);
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    for (shader, form) in [(&narrow, "the form emit chooses for 32-lane waves"), (&pair, "the paired form")] {
        match run_once(false, shader, 64) {
            Some(got) => {
                if let Some(t) = (0..64).find(|&t| got[t] != console[t]) {
                    panic!("{form}: thread {t}: {}, the console's {}", got[t], console[t]);
                }
                eprintln!("{form}: as on the console");
            }
            None => eprintln!("no device that takes {form}; not run"),
        }
    }
}

// ---- a section that reads a lane of an earlier section's result ----

/// `native`, translated for 64-lane waves, by what [`super::emit`] gives 32-lane waves and in
/// the paired form, stores the console's `console` (`dwords` per thread, `dwords` a power of
/// two) on WARP and the hardware device.
fn as_on_the_console(native: &NativeProgram, console: &[u32], dwords: usize) {
    let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
    let wide = lowered_for_width(native, &p, &HashSet::new(), 64).expect("lower for 64 lanes");
    if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
        v.validate(&mut wide.dxil.clone()).expect("DXIL validation failed");
    }
    let narrow = chosen_for_32_lanes(native);
    let pair = paired(native);
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    for (warp, shader, form) in [
        (true, &wide, "64-lane waves on WARP"),
        (false, &wide, "64-lane waves on the hardware device"),
        (false, &narrow, "the form emit chooses for 32-lane waves"),
        (false, &pair, "the paired form"),
    ] {
        match run_once(warp, shader, console.len() as u32) {
            Some(got) => {
                if let Some(n) = (0..console.len()).find(|&n| got[n] != console[n]) {
                    panic!("{form}: thread {}, dword {}: {:#x}, the console's {:#x}", n / dwords, n % dwords, got[n], console[n]);
                }
                eprintln!("{form}: as on the console");
            }
            None => eprintln!("no device that takes {form}; not run"),
        }
    }
}

/// One console wave of 64 threads; the threads below 40 sum t + 1 over the wave in a
/// whole-wave section as [`lane_read_past_the_restore_program`] does, and read lane 63 of the
/// sum as the first step of the next section (0x218 m092 at 0x130-0x134), which ends by
/// restoring EXEC or, with `lane0`, by setting it to s[18:19] = 1 (m128 at 0x1a4): lane 0
/// alone then stores. The threads that store put the sum in dword 0 of their two and t + 1 in
/// dword 1.
fn next_section_program(lane0: bool) -> (NativeProgram, Vec<u32>) {
    let mut w = vec![
        vop2(0x25, 1, k(1), 0), // v_add_nc_u32 v1, 1, v0
        vopc(0xD4, k(40), 0),   // v_cmpx_gt_u32 exec, 40, v0
        sop1(0x04, 18, k(1)),   // s_mov_b64 s[18:19], 1
        sop1(0x25, 12, k(65)),  // s_or_saveexec_b64 s[12:13], -1
        0xD501_0002,
        0x0032_0280, // v_cndmask_b32 v2, 0, v1, s[12:13]
    ];
    for dpp in [0xDE54_C102u32, 0x9360_1202, 0x0009_2402] {
        w.extend([vop2(0x25, 2, 0xE9, 2), dpp]); // v_add_nc_u32 v2, v2, v2 dpp8:[...]
    }
    w.extend([
        vop2(0x25, 2, 0xFA, 2),
        0xFF09_4002, // v_add_nc_u32 v2, v2, v2 row_mirror bound_ctrl:1
        0xD778_0003,
        0x0201_0102,            // v_permlanex16_b32 v3, v2, 0, 0
        vop2(0x25, 2, v(3), 2), // v_add_nc_u32 v2, v3, v2
        0xD760_000E,
        0x0001_0102,            // v_readlane_b32 s14, v2, 0
        vop2(0x25, 2, 14, 2),   // v_add_nc_u32 v2, s14, v2
        sop1(0x04, EXEC, 12),   // s_mov_b64 exec, s[12:13]
        sop1(0x25, 12, k(65)),  // s_or_saveexec_b64 s[12:13], -1
        0xD760_000F,
        0x0001_7F02, // v_readlane_b32 s15, v2, 63
        sop1(0x04, EXEC, if lane0 { 18 } else { 12 }), // s_mov_b64 exec, s[18:19] or s[12:13]
        vop1(0x01, 3, 15),       // v_mov_b32 v3, s15
        vop2(0x1A, 15, k(3), 0), // v_lshlrev_b32 v15, 3, v0
    ]);
    w.extend(store(3, 15, 0));
    w.extend(store(1, 15, 4));
    let sum: u32 = (1..=40).sum();
    let stores = |t: u32| if lane0 { t == 0 } else { t < 40 };
    let console = (0..64u32).flat_map(|t| if stores(t) { [sum, t + 1] } else { [0, 0] }).collect();
    (native(&w), console)
}

#[test]
fn a_lane_of_a_sum_read_in_the_next_section_is_the_consoles_sum() {
    let (native, console) = next_section_program(false);
    as_on_the_console(&native, &console, 2);
}

#[test]
fn a_section_left_for_lane_0_alone_stores_the_consoles_sum_from_lane_0() {
    let (native, console) = next_section_program(true);
    as_on_the_console(&native, &console, 2);
}

// ---- VCC on entry ----

/// VCC on entry, which a compute wave starts with as the previous wave left it, reaching a
/// mask only on the `execz` edge, where EXEC is 0 (run4 0x218 m159 at 0xe8-0x15c and 0x21c,
/// m160 and m162 at 0xf4-0x168): `v_cmpx_gt_u32 exec, s0, v0`, a skip past
/// `v_cmp_gt_u32 vcc, s1, v0` when no thread is below s0, then `s_orn2 s20, vcc, exec`,
/// `s_not s21, s20` and EXEC set to s21: the threads below s0 and not below s1 store 1, and
/// after the skip none does, whatever VCC held. The title's form (32-lane console waves,
/// `_b32`) and the 64-lane one, as `emit` gives them to a host of 32-lane waves.
#[test]
fn vcc_on_entry_masked_off_by_exec_is_not_read() {
    for wave64 in [false, true] {
        let (lanes, b64) = if wave64 { (64, 1) } else { (32, 0) };
        let mut w = vec![
            vop2(0x1A, 15, k(2), 0),         // v_lshlrev_b32 v15, 2, v0
            vop1(0x01, 1, k(1)),             // v_mov_b32 v1, 1
            vopc(0xD4, 0, 0),                // v_cmpx_gt_u32 exec, s0, v0
            sopp(0x08, 1),                   // s_cbranch_execz past the compare
            vopc(0xC4, 1, 0),                // v_cmp_gt_u32 vcc, s1, v0
            sop2(0x16 + b64, 20, VCC, EXEC), // s_orn2_b32/b64 s20, vcc, exec
            sop1(0x07 + b64, 22, 20),        // s_not_b32/b64 s22, s20
            sop1(0x03 + b64, EXEC, 22),      // s_mov_b32/b64 exec, s22
        ];
        w.extend(store(1, 15, 0));
        let native = native_of(&w, wave64, lanes);
        let shader = chosen_for_32_lanes(&native);
        let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
        for (s0, s1) in [(0, 0), (0, 40), (20, 10), (20, 30), (64, 64), (33, 5), (1, 0)] {
            let Some(got) = run_paired(&shader, [s0, s1, 0, 0], lanes) else { return };
            let console: Vec<u32> = (0..lanes).map(|t| u32::from(t < s0 && t >= s1)).collect();
            assert_eq!(got, console, "wave64 {wave64}, s0 {s0}, s1 {s1}");
        }
    }
}

/// The thread-group info SGPR (s6 after 5 user SGPRs and the group's number in x) as the
/// 0x218 programs read it (`slot218-m001` at 0xa4, `m020` at 0x68): `s_and_b32 s10, s6,
/// 0xfc0`, the wave's index times 64, then `v_mbcnt_lo/hi(-1, s10)`, the thread's index in the
/// group. Each thread stores that (or, without `mbcnt`, its thread index), the wave count
/// (bits 0-5) and the low 12 bits; each form the host may get matches the console's waves of
/// `wave` lanes taking the flat ids in order.
fn group_info_program(wave64: bool, threads: u32, mbcnt: bool) -> NativeProgram {
    let mut w = vec![sop2(0x0E, 12, 6, LIT), 0xFC0]; // s_and_b32 s12, s6, 0xfc0
    if mbcnt {
        w.extend(vop3(0x365, 1, k(65), 12)); // v_mbcnt_lo_u32_b32 v1, -1, s12
        w.extend(vop3(0x366, 1, k(65), v(1))); // v_mbcnt_hi_u32_b32 v1, -1, v1
    } else {
        w.push(vop1(0x01, 1, v(0))); // v_mov_b32 v1, v0 (the thread index)
    }
    w.extend([
        sop2(0x0E, 13, 6, k(63)), // s_and_b32 s13, s6, 63
        sop2(0x0E, 14, 6, LIT),   // s_and_b32 s14, s6, 0xfff
        0xFFF,
        vop1(0x01, 2, 13), // v_mov_b32 v2, s13
        vop1(0x01, 3, 14), // v_mov_b32 v3, s14
        vop1(0x01, 4, 12), // v_mov_b32 v4, s12
    ]);
    w.extend(stores([1, 2, 3, 4]));
    let mut native = native_with(&w, wave64, threads, 0);
    if let StageIo::Cs(cs) = &mut native.io {
        cs.rsrc2 |= 1 << 7 | 1 << 10; // the group's number in x (s5), the group info (s6)
    }
    native
}

/// The console's: thread t's [index in the group as the program computes it, wave count,
/// low 12 bits, wave index * 64].
fn group_info_console(wave: u32, threads: u32, mbcnt: bool) -> Vec<u32> {
    let waves = threads.div_ceil(wave);
    (0..threads)
        .flat_map(|t| {
            let base = (t / wave) << 6;
            let index = if mbcnt { base + t % wave } else { t };
            [index, waves, base | waves, base]
        })
        .collect()
}

#[test]
fn the_group_info_sgpr_is_the_consoles_wave_index_and_count() {
    let lower_at = |native: &NativeProgram, host_wave: u32| {
        let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
        let shader = lowered_for_width(native, &p, &HashSet::new(), host_wave).expect("lower");
        if let Ok(mut v) = validator::Validator::load(validator::Validator::default_path().as_deref()) {
            v.validate(&mut shader.dxil.clone()).expect("DXIL validation failed");
        }
        shader
    };
    let mut forms = vec![
        // one host wave of 32 for each console wave of 64
        ("paired", paired(&group_info_program(true, 192, true)), 64, 192, true),
        // two host waves of 32 for each console wave of 64, exchanging through LDS
        ("halves", lower_at(&group_info_program(true, 192, true), 32), 64, 192, true),
        // host waves of 64 (a device that takes them)
        ("64 lanes", lower_at(&group_info_program(true, 192, true), 64), 64, 192, true),
        // a console of 32-lane waves, the last one partial
        ("wave32", lower_at(&group_info_program(false, 100, true), 32), 32, 100, true),
    ];
    // no lane operation: the host's thread ids, whatever its waves
    for (name, wave64, threads) in [("no lanes, wave64", true, 192), ("no lanes, wave32", false, 100)] {
        let wave = if wave64 { 64 } else { 32 };
        forms.push((name, lower_at(&group_info_program(wave64, threads, false), 32), wave, threads, false));
    }
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    for (name, shader, wave, threads, mbcnt) in &forms {
        let Some(got) = run_paired(shader, [0; 4], 4 * threads) else { continue };
        eprintln!("{name}: ran on the hardware device");
        same_as_console(&got, &group_info_console(*wave, *threads, *mbcnt));
    }
}

/// Bits above 11 of the group info SGPR are not known: a read that keeps them is refused.
#[test]
fn the_group_info_sgpr_read_whole_is_refused() {
    for (first, mask) in [(sop1(0x03, 12, 6), None), (sop2(0x0E, 12, 6, LIT), Some(0x1FC0))] {
        let mut w = vec![first];
        w.extend(mask);
        w.push(vop1(0x01, 1, 12));
        w.extend(stores([1, 1, 1, 1]));
        let mut native = native_with(&w, true, 64, 0);
        if let StageIo::Cs(cs) = &mut native.io {
            cs.rsrc2 |= 1 << 7 | 1 << 10;
        }
        let p = ir::build(&native.code, native.wave64, native.vgprs).expect("ir::build");
        let err = lowered_for_width(&native, &p, &HashSet::new(), 32).map(|_| ()).expect_err("refused");
        assert!(err.to_string().contains("thread-group info SGPR read past"), "{err}");
    }
}

/// One console wave of 64 threads (the paired form): every lane zeroes the dword at byte 4 of
/// LDS, the even lanes `ds_append` there (0x218 programs `8646605da9720718`, `f7451d949afb803e`)
/// and add their count below (`v_mbcnt`): each even lane n gets n / 2, the slot the console
/// gives it, the odd lanes keep 0xffff, and every lane then reads the 32 the wave added.
#[test]
fn an_append_counts_the_console_waves_lanes_once() {
    let mut w = vec![vop1(0x01, 1, k(0)), vop1(0x01, 2, LIT), 0xFFFF]; // v1 = 0, v2 = 0xffff
    w.extend(ds(0x0D, 0, 1, 1, 4)); // ds_write_b32 v1, v1 offset:4
    w.extend([
        0xBF8C_C07F,            // s_waitcnt lgkmcnt(0)
        vop2(0x1B, 3, k(1), 0), // v_and_b32 v3, 1, v0
        vopc(0xC2, k(0), 3),    // v_cmp_eq_u32 vcc, 0, v3: the even lanes
        sop1(0x24, 12, VCC),    // s_and_saveexec_b64 s[12:13], vcc
    ]);
    w.extend(ds(0x3E, 2, 0, 0, 4)); // ds_append v2 offset:4
    w.push(0xBF8C_C07F); // s_waitcnt lgkmcnt(0)
    w.extend(vop3(0x365, 7, EXEC, k(0))); // v_mbcnt_lo_u32_b32 v7, exec_lo, 0
    w.extend(vop3(0x366, 7, EXEC + 1, v(7))); // v_mbcnt_hi_u32_b32 v7, exec_hi, v7
    w.extend([
        vop2(0x25, 2, v(2), 7), // v_add_nc_u32 v2, v2, v7
        sop1(0x04, EXEC, 12),   // s_mov_b64 exec, s[12:13]
    ]);
    w.extend(ds(0x36, 8, 1, 0, 4)); // ds_read_b32 v8, v1 offset:4
    w.push(0xBF8C_C07F); // s_waitcnt lgkmcnt(0)
    w.extend(stores([2, 8, 2, 8]));
    let shader = paired(&native_with(&w, true, 64, 256));
    let want: Vec<u32> = (0..64u32)
        .flat_map(|n| {
            let slot = if n % 2 == 0 { n / 2 } else { 0xFFFF };
            [slot, 32, slot, 32]
        })
        .collect();
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Some(got) = run_paired(&shader, [0; 4], 256) else { return };
    same_as_console(&got, &want);
    eprintln!("ran on the hardware device: one append for the console wave, as on the console");
}

/// A whole-wave section (the wave OR of `super::wave::WAVE_OR_V7_TO_S7`, lane t giving bit
/// t >> 1) with scalar work inside it: `s_lshr_b32 s20, s7, 1` of the OR's lane read, read past
/// the section (0x218 program `2de0c233d150046c` shifts its reduction's lane read so). The
/// scalar is lowered as anywhere else: every thread of the console wave gets the OR >> 1.
#[test]
fn scalar_work_inside_a_whole_wave_section_takes_the_sections_value() {
    let mut w = vec![
        vop2(0x16, 3, k(1), 0), // v_lshrrev_b32 v3, 1, v0
        vop1(0x01, 6, k(1)),    // v_mov_b32 v6, 1
    ];
    w.push(vop2(0x1A, 7, v(3), 6)); // v_lshlrev_b32 v7, v3, v6: 1 << (t >> 1)
    let section = super::wave::WAVE_OR_V7_TO_S7;
    w.extend_from_slice(&section[..section.len() - 1]);
    w.push(sop2(0x20, 20, 7, k(1))); // s_lshr_b32 s20, s7, 1
    w.push(section[section.len() - 1]); // s_mov_b64 exec, s[4:5]
    w.push(vop1(0x01, 9, 20)); // v_mov_b32 v9, s20
    w.extend(stores([9, 9, 9, 9]));
    let shader = paired(&native_with(&w, true, 64, 256));
    let want = vec![u32::MAX >> 1; 256];
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Some(got) = run_paired(&shader, [0; 4], 256) else { return };
    same_as_console(&got, &want);
    eprintln!("ran on the hardware device: the section's scalar work as on the console");
}

/// A whole-wave section with no restore (the wave OR of `super::wave::WAVE_OR_V7_TO_S7`), left
/// with `s_mov_b64 exec, 1`, lane 0 alone, which stores the OR (0x218 program
/// `2bb44e9a9a710ad2`: lane 0 then works and stores past such a section), and EXEC is set
/// again later from a copy made before the section. The section ends at the constant write
/// (it took the store in, and lost it, up to the later write); thread 0 stores the OR of every
/// lane's bit, the other threads nothing.
#[test]
fn a_section_left_for_lane_0_alone_by_a_constant_exec_ends_there() {
    let mut w = vec![
        vop2(0x16, 3, k(1), 0), // v_lshrrev_b32 v3, 1, v0
        vop1(0x01, 6, k(1)),    // v_mov_b32 v6, 1
        vop2(0x1A, 7, v(3), 6), // v_lshlrev_b32 v7, v3, v6: 1 << (t >> 1)
        sop1(0x04, 2, EXEC),    // s_mov_b64 s[2:3], exec
    ];
    let section = super::wave::WAVE_OR_V7_TO_S7;
    w.extend_from_slice(&section[..section.len() - 1]);
    w.push(sop1(0x04, EXEC, k(1))); // s_mov_b64 exec, 1
    w.push(vop1(0x01, 9, 7)); // v_mov_b32 v9, s7
    w.extend(stores([9, 9, 9, 9]));
    w.push(sop1(0x04, EXEC, 2)); // s_mov_b64 exec, s[2:3]: EXEC again, but not the section's
    let shader = paired(&native_with(&w, true, 64, 256));
    let mut want = vec![0u32; 256];
    want[..4].fill(u32::MAX);
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Some(got) = run_paired(&shader, [0; 4], 256) else { return };
    same_as_console(&got, &want);
    eprintln!("ran on the hardware device: lane 0 alone past the section, as on the console");
}
