//! A title's wave64 compute program run two ways with the same inputs: as one
//! console wave of 64 lanes (`WaveSize(64)`, on WARP, which runs waves of up
//! to 128) and as two host waves of 32 (the form NVIDIA cards get), to find
//! where the second departs from the first.
//!
//! `VICTORY_DIFF_CONTAINER=<file.cdxil>` (a container as the title has it,
//! from `victory translate` with `VICTORY_DUMP_MATCH`); skipped without it.
//! Game-derived files stay outside the repository.

use super::{HostShader, translate};

fn both(container: &[u8]) -> (Result<HostShader, String>, Result<HostShader, String>) {
    let at = |lanes| {
        let was = super::lower::TEST_HOST_WAVE.replace(Some(lanes));
        let r = translate(container).map_err(|e| e.to_string());
        super::lower::TEST_HOST_WAVE.set(was);
        r
    };
    (at(64), at(32))
}

#[test]
fn a_title_program_in_two_host_waves_matches_one_console_wave() {
    let Some(path) = std::env::var_os("VICTORY_DIFF_CONTAINER") else {
        eprintln!("no VICTORY_DIFF_CONTAINER; skipped");
        return;
    };
    let bytes = std::fs::read(&path).expect("container");
    {
        let c = super::Container::parse(&bytes).expect("container");
        let part = c.part(super::FourCC::DXIL).or(c.part(super::FourCC::ACCC)).expect("part");
        let native = crate::native_programs(part.data).expect("programs").into_iter().next().expect("program");
        eprintln!("io: {:?}, wave64 {}, vgprs {}", native.io, native.wave64, native.vgprs);
        let p = crate::spill::build(&native).expect("ir");
        let st = crate::structure::structure(&p).ok();
        let res = crate::resource::track(&p, &native.layout, native.stage, st.as_ref()).expect("resources");
        for d in &res.descriptors {
            eprintln!("  descriptor {d:?}");
        }
    }
    let (wide, split) = both(&bytes);
    for (name, s) in [("wave64", &wide), ("2x32", &split)] {
        match s {
            Ok(s) => {
                eprintln!("{name}: {} bytes of DXIL", s.dxil.len());
                eprintln!("  user data: {:?}", s.user_data);
                eprintln!("  samplers: {}, root cbvs {:?}, srvs {:?}, uavs {:?}", s.samplers.len(), s.root_cbvs, s.root_srvs, s.root_uavs);
                eprintln!("  indexed: {:?}", s.root_indexed_views);
            }
            Err(e) => eprintln!("{name}: {e}"),
        }
    }
}

/// The tile-bounds part of `CS_TransmissionVolumetrics_Tiled`, its own words (memory loads
/// replaced by thread-index values): thread (0,0) sets the tile's bounds in LDS, every thread
/// on screen (`t < s0`) puts its key in with `ds_min`/`ds_max`, all read them back; then a
/// sum over every lane the group started with, the console's way (DPP rows, `v_permlanex16`,
/// `v_readlane` 32 and 0). Each thread stores max, min and the sum. Run as one wave of 64 on
/// WARP and as two host waves of 32 on the hardware device.
fn tile_bounds_program() -> crate::native::NativeProgram {
    use crate::native::{ComputeIo, HwStage, NativeProgram, ResourceType, RootParam, RootType, StageIo, UsedResource};
    let body: Vec<u32> = vec![
        0x3826_0101, // v_or_b32 v19, v1, v0
        0xBE8A_047E, // s_mov_b64 s[10:11], exec
        0x7DA4_2680, // v_cmpx_eq_u32 exec, 0, v19
        0x7E26_0280, // v_mov_b32 v19, 0
        0x7E28_02C1, // v_mov_b32 v20, -1
        0xD838_0100, 0x0014_1313, // ds_write2_b32 v19, v19, v20 offset1:1
        0xBEFE_040A, // s_mov_b64 exec, s[10:11]
        0xD76F_000F, 0x0401_1101, // v_lshl_or_b32 v15, v1, 8, v0
        0x161A_3CA5, // v_mul_u32_u24 v13, 37, v30
        0x361A_1AFF, 0x0000_00FF, // v_and_b32 v13, 0xff, v13
        0x341A_1A90, // v_lshlrev_b32 v13, 16, v13
        0x381A_1B0F, // v_or_b32 v13, v15, v13
        0x7E20_0280, // v_mov_b32 v16, 0
        0xD81C_0004, 0x0000_0D10, // ds_min_u32 v16, v13 offset:4
        0xD820_0000, 0x0000_0D10, // ds_max_u32 v16, v13
        0xBF8C_C07F, // s_waitcnt lgkmcnt(0)
        0x7E2C_0280, // v_mov_b32 v22, 0
        0xD8DC_0100, 0x1300_0016, // ds_read2_b32 v[19:20], v22 offset1:1
        0xBF8C_C07F, // s_waitcnt lgkmcnt(0)
        0x7E2C_0D1E, // v_cvt_f32_u32 v22, v30
        0xBE86_25C1, // s_or_saveexec_b64 s[6:7], -1
        0x8788_064A, // s_and_b64 s[8:9], s[74:75], s[6:7]
        0xD501_0002, 0x0022_2C80, // v_cndmask_b32 v2, 0, v22, s[8:9]
        0x0604_04E9, 0xDE54_C102, // v_add_f32 v2, v2, v2 dpp8:[1,0,3,2,5,4,7,6]
        0x0604_04E9, 0x9360_1202, // dpp8:[2,2,0,0,6,6,4,4]
        0x0604_04E9, 0x0009_2402, // dpp8:[4,4,4,4,0,0,0,0]
        0x0604_04FA, 0xFF09_4002, // v_add_f32 v2, v2, v2 row_mirror
        0xD778_0003, 0x0201_0102, // v_permlanex16_b32 v3, v2, 0, 0
        0x0604_0503, // v_add_f32 v2, v3, v2
        0xD760_0008, 0x0001_4102, // v_readlane_b32 s8, v2, 32
        0x0604_0408, // v_add_f32 v2, s8, v2
        0xD760_000E, 0x0001_0102, // v_readlane_b32 s14, v2, 0
        0xBEFE_0406, // s_mov_b64 exec, s[6:7]
        0x3450_3C84, // v_lshlrev_b32 v40, 4, v30
        0xE070_1000, 0x8005_1328, // buffer_store_dword v19, v40, s[20:23] offen
        0xE070_1004, 0x8005_1428, // buffer_store_dword v20, v40, s[20:23] offen offset:4
        0x7E2A_020E, // v_mov_b32 v21, s14
        0xE070_1008, 0x8005_1528, // buffer_store_dword v21, v40, s[20:23] offen offset:8
    ];
    let mut words = vec![
        0xBE85_0380, // s_mov_b32 s5, 0
        0x3D << 26 | 0x02 << 18 | 8 << 6 | 2,
        125 << 25,   // s_load_dwordx4 s[8:11], s[4:5], 0
        0xBF8C_C07F, // s_waitcnt lgkmcnt(0)
        0xBE94_0408, // s_mov_b64 s[20:21], s[8:9]
        0xBE96_040A, // s_mov_b64 s[22:23], s[10:11]
        0x7E3C_0300, // v_mov_b32 v30, v0
        0x3600_3C87, // v_and_b32 v0, 7, v30
        0x2C02_3C83, // v_lshrrev_b32 v1, 3, v30
        0xBECA_047E, // s_mov_b64 s[74:75], exec
        0x7D88_3C00, // v_cmp_gt_u32 vcc, s0, v30
        0xBE80_246A, // s_and_saveexec_b64 s[0:1], vcc
        0xBF88_0000 | body.len() as u32, // s_cbranch_execz past the body
    ];
    words.extend_from_slice(&body);
    words.push(0xBEFE_0400); // s_mov_b64 exec, s[0:1]
    words.push(0xBF81_0000); // s_endpgm
    NativeProgram {
        stage: HwStage::Cs,
        wave64: true,
        vgprs: 48,
        layout: vec![
            RootParam { root: RootType::Constants, table_offset_dwords: 0, sgprs: 4, base_register: 0, user_data: 0, slot: 0 },
            RootParam { root: RootType::Table, table_offset_dwords: 0, sgprs: 1, base_register: 0, user_data: 4, slot: 1 },
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
        // 5 user SGPRs, 128 dwords of LDS
        io: StageIo::Cs(ComputeIo { rsrc2: 5 << 1 | 1 << 15, threads: [64, 1, 1] }),
        code: words.iter().flat_map(|w| w.to_le_bytes()).collect(),
    }
}

/// The program as the runtime translates it for a host of `lanes`-lane waves, the wave-wide
/// form allowed (`VICTORY_WAVE_WIDE=1`, as the digest list gives `_Tiled`).
fn runtime_form(native: &crate::native::NativeProgram, lanes: u32) -> Result<HostShader, String> {
    let p = crate::spill::build(native).map_err(|e| e.to_string())?;
    let st = crate::structure::structure(&p).map_err(|e| e.to_string())?;
    let res = crate::resource::track(&p, &native.layout, native.stage, Some(&st)).map_err(|e| e.to_string())?;
    let io = crate::io::raise(&p, native).map_err(|e| e.to_string())?;
    let c = crate::dxbc::Container { digest: [0; 16], version: (1, 0), parts: Vec::new() };
    let was = super::lower::TEST_HOST_WAVE.replace(Some(lanes));
    // SAFETY: test-only; the other tests that read it do not run in this binary's threads at once
    // in a way that matters (they set it themselves or leave it unset).
    unsafe { std::env::set_var("VICTORY_WAVE_WIDE", "1") };
    let r = super::emit(&c, native, &p, &st, &io, &res).map_err(|e| e.to_string());
    unsafe { std::env::remove_var("VICTORY_WAVE_WIDE") };
    super::lower::TEST_HOST_WAVE.set(was);
    r
}

#[test]
fn tiled_bounds_two_host_waves_match_one_console_wave() {
    use super::host::{Dispatch, Host, Root, View};
    let native = tile_bounds_program();
    let run = |host: &Host, s: &HostShader, active: u32| -> Result<Vec<u32>, String> {
        let (root, pso) = host.compute_pipeline(s)?;
        let mut ud = vec![0u32; s.user_data_dwords().max(1) as usize];
        for u in &s.user_data {
            if u.root == crate::native::RootType::Constants && u.dword == 0 {
                ud[u.index as usize] = active;
            }
        }
        let out = host.dispatch(&root, &pso, &Dispatch {
            roots: vec![(0, Root::Constants(ud))],
            views: &[View::RawUav(256)],
            samplers: &[],
            groups: [1, 1, 1],
        })?;
        Ok(out[0].clone())
    };
    let wide = runtime_form(&native, 64).expect("wave64 form");
    let split = runtime_form(&native, 32).expect("two-wave form");
    let _one = super::valu_tests::ONE_AT_A_TIME.lock().unwrap_or_else(|e| e.into_inner());
    let Ok(warp) = Host::new(true) else {
        eprintln!("no WARP; skipped");
        return;
    };
    // Not every WARP runs a fixed wave of 64 (the CI runner's refuses the
    // pipeline: "The parameter is incorrect."), as in `diff_tests`.
    if let Err(e) = warp.compute_pipeline(&wide) {
        eprintln!("WARP runs no wave of 64 here ({e}); skipped");
        return;
    }
    let hw = Host::new(false).ok();
    for active in [64u32, 40, 33, 32, 20, 1] {
        // what the console computes
        let key = |t: u32| ((37 * t) & 0xFF) << 16 | (t >> 3) << 8 | (t & 7);
        let (mx, mn) = (0..active).map(key).fold((0u32, u32::MAX), |(a, b), k| (a.max(k), b.min(k)));
        let sum = 2016f32.to_bits();
        let reference = run(&warp, &wide, active).expect("WARP wave64");
        let mut bad = 0;
        for t in 0..active as usize {
            let got = &reference[t * 4..t * 4 + 3];
            if got != [mx, mn, sum] {
                bad += 1;
                if bad < 4 {
                    eprintln!("WARP wave64 active={active} t={t}: {got:x?}, want {:x?}", [mx, mn, sum]);
                }
            }
        }
        eprintln!("active={active}: WARP wave64 differs from the console in {bad} threads");
        if let Some(hw) = &hw {
            match run(hw, &split, active) {
                Ok(out) => {
                    let mut diff = 0;
                    for t in 0..64usize {
                        if out[t * 4..t * 4 + 4] != reference[t * 4..t * 4 + 4] {
                            diff += 1;
                            if diff < 6 {
                                eprintln!("  2x32 active={active} t={t}: {:x?} vs wave64 {:x?}", &out[t * 4..t * 4 + 3], &reference[t * 4..t * 4 + 3]);
                            }
                        }
                    }
                    eprintln!("active={active}: hardware 2x32 differs from wave64 in {diff} threads");
                }
                Err(e) => eprintln!("active={active}: hardware 2x32 did not run: {e}"),
            }
        }
    }
}
