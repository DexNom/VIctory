//! Console programs that reach us compiled already, outside a container,
//! and the container that holds such a program as the title's own do.
//!
//! A title can make a pipeline from a program it has put in its own memory
//! (device methods 0x218 for compute, 0x1c8 for graphics). Such a program
//! comes as its code, a block of registers and a resource table. The table,
//! as observed (the title's mip generation programs, against their
//! containers):
//!
//! ```text
//! +0x00 u32 kind (bit 10: 8 bytes more of header), u32 size of the rest
//!       (from +0x0c, or +0x14), u32, u32 ..., the last u32 of the header
//!       `n << 16 | 0x440` (n is not always the count of 0x41 entries: 11
//!       for 12 seen, so the entries are told apart by their tag)
//! entries of 0x20 bytes from the end of the header, 0x41 ones first:
//!   0x41  a descriptor the program reads: u32 0x41, u32 root type << 8 |
//!         resource type (0 SRV, 1 sampler, 2 CBV, 3 UAV), u32 table offset
//!         (dwords), u32 shader register, u32 stride (bytes; 0 for a CBV,
//!         4 for an image), u32 root slot << 16 | first user-data dword << 8
//!         | descriptor dwords, then u32 -1, u32 0
//!   0x42  a table range (not read: the 0x41 entries name the same ranges)
//!   0x43  the program's 128-bit hash, last
//! ```
//!
//! The user-data dword is the one byte that agrees across a table: the
//! root slots take the dwords from 4 on in their order, one each but two
//! for a root descriptor, which the program's `COMPUTE_PGM_RSRC2` user-SGPR
//! count covers, and a fixed sampler 0. The u32 before it is 0x10 or 0x20
//! for buffer SRVs (a structured stride), which no user-SGPR count covers.
//!
//! Root type 0 is root constants (resource type 2): the program reads them
//! as values straight from its user SGPRs (`v_mul_f32 v7, s5, v7`). They
//! give a root parameter and no used resource, as nothing is loaded through
//! them. Their descriptor dwords byte is 4 whatever their count (1 to 4 in
//! the root signatures captured with them), so the table alone gives the
//! first one only; their count comes from the root signature.
//!
//! Root type 1 is a root descriptor, a 64-bit buffer address in two user
//! SGPRs (descriptor dwords 2). Seen as an SRV (`t1`, user data 7 and 8,
//! the next slot at 9), which the program makes a buffer descriptor around
//! and loads through: `s_mov_b32 s20, s7`, `s_mov_b32 s21, s8`,
//! `s_mov_b32 s22, -1`, `s_mov_b32 s23, 0x31016fac`, then
//! `s_buffer_load_dword s27, s[20:23], null`. It is a root parameter of two
//! SGPRs and a used resource, as the title's containers give one.
//!
//! The registers of a compute program, 0x1c bytes: u32 flags (bit 5: 32
//! lanes, as the container record's flags), u32, u32, u16 x3 thread-group
//! size and u16 whose bit 4 is set for a program of 32 lanes, u32
//! `COMPUTE_PGM_RSRC1`, u32 `COMPUTE_PGM_RSRC2`. The flags' bit 5 is clear
//! in every 0x218 program seen; the programs with that u16 at 0x10 move
//! `exec_lo` alone, keep scalars in `vcc_hi` and use VGPRs up to the 32-lane
//! count of `COMPUTE_PGM_RSRC1` (v143 of 18 granules), and the others move
//! the whole `exec` and stay within the 64-lane count.

use super::{
    ABSENT, CS_RSRC2, CS_THREADS, ComputeIo, ENTRY_SIZE, MAX_UNPACKED, FSSM_HEADER, FSSM_VERSION, GRAPHICS_SLOT_COUNT, HwStage,
    KIND_NATIVE, NativeProgram, RECORD_COUNTS, RECORD_FLAGS, RECORD_LAYOUT, RECORD_REGS,
    RECORD_SIZE, RECORD_USED, ResourceType, RootParam, RootType, SLOTS, StageIo, UsedResource,
    XBCH_HEADER, XBCH_VERSION, XDAT_OFFSET,
};
use crate::ShaderError::{self, Malformed};
use crate::dxbc::u32_at;
use crate::{FourCC, dxbc};

const S_CODE_END: u32 = 0xBF9F_0000;

/// In the registers' depth word, above the depth: a program of 32 lanes.
const WAVE32_DEPTH_BIT: u32 = 0x10 << 16;

/// The descriptors a console resource table names, and the root parameters
/// they come through (one per table range, and one per fixed sampler).
fn resources(table: &[u8]) -> Result<(Vec<RootParam>, Vec<UsedResource>), ShaderError> {
    let kind = u32_at(table, 0)?;
    let header = if kind & 0x400 != 0 { 0x18 } else { 0x10 };
    let size = u32_at(table, 4)? as usize;
    if table.len().checked_sub(header - 4) != Some(size) {
        return Err(Malformed("resource table size disagrees"));
    }
    let (entries, tail) = table[header..].as_chunks::<0x20>();
    let Some((hash, entries)) = entries.split_last() else {
        return Err(Malformed("missing resource table hash"));
    };
    if !tail.is_empty() || u32_at(hash, 0)? != 0x43 {
        return Err(Malformed("incomplete resource table"));
    }
    let count = entries.iter().take_while(|entry| super::le32(entry.as_slice(), 0) == 0x41).count();
    let (descriptors, ranges) = entries.split_at(count);
    if ranges.iter().any(|entry| super::le32(entry, 0) != 0x42) {
        return Err(Malformed("unexpected resource range"));
    }
    let (mut layout, mut used) = (Vec::new(), Vec::new());
    for entry in descriptors {
        let word = |k: usize| u32::from_le_bytes(entry[4 * k..4 * k + 4].try_into().unwrap());
        let (types, offset, register, stride, packed) =
            (word(1), word(2), word(3), word(4), word(5));
        let root = RootType::from_raw(types >> 8);
        let slot = u8::try_from(packed >> 16).map_err(|_| Malformed("implausible root slot"))?;
        let user_data = (packed >> 8) as u8;
        if root == RootType::Constants {
            if packed & 0xFF != 4 {
                return Err(Malformed("implausible root constants entry"));
            }
            // The first constant: the count is the root signature's.
            layout.push(RootParam {
                root,
                table_offset_dwords: 0,
                sgprs: 1,
                base_register: register,
                user_data,
                slot,
            });
            continue;
        }
        used.push(UsedResource {
            kind: match types & 0xFF {
                0 => ResourceType::Srv,
                1 => ResourceType::Sampler,
                2 => ResourceType::Cbv,
                3 => ResourceType::Uav,
                n => ResourceType::Other(n as u8),
            },
            root,
            table_offset_dwords: offset,
            lower_bound: register,
            stride,
            dwords: packed as u8,
            user_data,
            slot,
        });
        let sgprs = match root {
            RootType::Table => 1,
            RootType::Descriptor => 2,
            RootType::ImmediateSampler => 0,
            _ => {
                return Err(Malformed(
                    "a console resource table entry of a root type not seen yet",
                ));
            }
        };
        layout.push(RootParam {
            root,
            table_offset_dwords: offset,
            sgprs,
            base_register: register,
            user_data,
            slot,
        });
    }
    Ok((layout, used))
}

/// The code without the `s_code_end` the title pads it with.
fn trimmed(code: &[u8]) -> &[u8] {
    let mut end = code.len();
    while end >= 4 && u32_at(code, end - 4).ok() == Some(S_CODE_END) {
        end -= 4;
    }
    &code[..end]
}

/// A root parameter of a serialized root signature: what it is, the
/// user-data dwords it takes and its shader register.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
struct SerializedParam {
    root: RootType,
    dwords: u32,
    register: u32,
    /// A root SRV: taken only where the table places it itself.
    srv: bool,
}

/// `D3D12_ROOT_SIGNATURE_FLAG_CBV_SRV_UAV_HEAP_DIRECTLY_INDEXED` and
/// `D3D12_ROOT_SIGNATURE_FLAG_SAMPLER_HEAP_DIRECTLY_INDEXED`.
const HEAPS_INDEXED: u32 = 0xC00;

/// The parameters and flags of a serialized root signature (an `RTS0`
/// part, alone or in a container): a header of u32 version (1 to 3),
/// parameter count, parameter offset, sampler count, sampler offset and
/// flags, then per parameter u32 type, visibility and payload offset. A
/// table (0) takes one user-data dword, root constants (1) as many as their
/// count (the payload's third u32), a root CBV (2) two, a root SRV (3) two
/// as well (a captured table places one in dwords 7 and 8, the next slot
/// at 9). Root UAVs (4) are refused: no captured table places one.
fn root_signature(blob: &[u8]) -> Result<(Vec<SerializedParam>, u32), ShaderError> {
    let rts0 = match crate::Container::parse(blob) {
        Ok(c) => {
            c.part(FourCC::RTS0)
                .ok_or(Malformed("a root signature container without RTS0"))?
                .data
        }
        Err(_) => blob,
    };
    if !(1..=3).contains(&u32_at(rts0, 0)?) {
        return Err(Malformed("unknown root signature version"));
    }
    let (count, at, flags) = (u32_at(rts0, 4)?, u32_at(rts0, 8)? as usize, u32_at(rts0, 20)?);
    if count > 64 {
        return Err(Malformed("implausible root parameter count"));
    }
    let params = (0..count as usize)
        .map(|k| {
            let entry = at + 12 * k;
            let payload = u32_at(rts0, entry + 8)? as usize;
            let kind = u32_at(rts0, entry)?;
            let (root, dwords) = match kind {
                0 => (RootType::Table, 1),
                1 => (RootType::Constants, u32_at(rts0, payload + 8)?),
                2 | 3 => (RootType::Descriptor, 2),
                4 => {
                    return Err(Malformed("a root UAV, whose user data no capture places"));
                }
                _ => return Err(Malformed("unknown root parameter type")),
            };
            let register = if root == RootType::Table { 0 } else { u32_at(rts0, payload)? };
            Ok(SerializedParam { root, dwords, register, srv: kind == 3 })
        })
        .collect::<Result<_, _>>()?;
    Ok((params, flags))
}

/// The root parameters a resource table leaves out, from the root
/// signature the title passes with it: a 0x218 program reads user SGPRs
/// that no table entry names (`slot218-m007`: a descriptor loaded from
/// s[10:11] plus s9 << 5, with entries for dwords 4 to 6 only).
///
/// Every captured table places the parameters from user-data dword 4 on,
/// in order, a table taking one dword, root constants their count, a root
/// CBV two (slot 0 at 4 even when unnamed, a root CBV at 7 taking 7 and 8,
/// the constants after it at 9), so the others are placed the same way.
/// 200 descriptions captured in game with their signatures agree: 196 end
/// their parameters exactly at their user-SGPR count. Nothing is guessed:
/// the program is refused when the table places a parameter elsewhere or
/// a parameter is cut by the end of the program's user SGPRs, and user
/// SGPRs left past the parameters stay unnamed (the 4 such captures, with
/// a directly indexed heap, never read theirs; one that does is refused by
/// the translator).
fn completed(
    layout: &[RootParam],
    params: &[SerializedParam],
    flags: u32,
    user_sgprs: u32,
) -> Result<Vec<RootParam>, ShaderError> {
    let mut at = Vec::with_capacity(params.len());
    let mut end = 4u32;
    for p in params {
        at.push(end);
        end = end
            .checked_add(p.dwords)
            .ok_or(Malformed("implausible root constants size"))?;
    }
    let named = |k: usize| {
        layout
            .iter()
            .filter(|l| l.root != RootType::ImmediateSampler)
            .any(|l| usize::from(l.slot) == k)
    };
    let agrees = layout
        .iter()
        .filter(|l| l.root != RootType::ImmediateSampler)
        .all(|l| {
            let k = usize::from(l.slot);
            params.get(k).is_some_and(|p| p.root == l.root) && at[k] == u32::from(l.user_data)
        });
    if !agrees {
        return Err(Malformed(
            "a resource table places a root parameter where its root signature does not",
        ));
    }
    let mut out = layout.to_vec();
    // Root constants the table names: it gives the first only, the
    // signature their count.
    for l in out.iter_mut().filter(|l| l.root == RootType::Constants) {
        let p = &params[usize::from(l.slot)];
        if u32::from(l.user_data) + p.dwords > user_sgprs {
            return Err(Malformed(
                "a root parameter cut by the end of the program's user SGPRs",
            ));
        }
        l.sgprs = p.dwords;
    }
    for (k, p) in params.iter().enumerate() {
        let (first, past) = (at[k], at[k] + p.dwords);
        if named(k) || first >= user_sgprs {
            continue;
        }
        if past > user_sgprs {
            return Err(Malformed(
                "a root parameter cut by the end of the program's user SGPRs",
            ));
        }
        if p.srv {
            // A root SRV the table does not name: what it is read as (and
            // so its used resource) is not known.
            return Err(Malformed("a root SRV the resource table does not place"));
        }
        out.push(RootParam {
            root: p.root,
            table_offset_dwords: 0,
            sgprs: p.dwords,
            base_register: p.register,
            user_data: first as u8,
            slot: k as u8,
        });
    }
    // A directly indexed heap takes one user-data dword past the root
    // parameters, the view heap's first, then the sampler heap's: in two
    // game runs' captures, every program with the view heap flag has one
    // user SGPR more than its parameters take, and every one with both
    // flags two, and nothing else has any (the first is the view heap: the
    // programs load buffer descriptors from it, `s_load_dwordx4` at the
    // dword plus an offset, and read their stride). User SGPRs that this
    // does not account for exactly are left unnamed: a program that reads
    // one is refused by the translator ("a hardware SGPR input").
    let heaps: Vec<RootType> = [(0x400, RootType::ResourceHeap), (0x800, RootType::SamplerHeap)]
        .into_iter()
        .filter(|&(bit, _)| flags & HEAPS_INDEXED & bit != 0)
        .map(|(_, root)| root)
        .collect();
    if end + heaps.len() as u32 == user_sgprs {
        for (k, root) in heaps.into_iter().enumerate() {
            out.push(RootParam {
                root,
                table_offset_dwords: 0,
                sgprs: 1,
                base_register: 0,
                user_data: (end + k as u32) as u8,
                slot: (params.len() + k) as u8,
            });
        }
    }
    Ok(out)
}

/// A compute program from its registers (0x1c bytes), resource table and
/// code, as the title hands them to device method 0x218.
fn console_compute(
    registers: &[u8],
    table: &[u8],
    code: &[u8],
) -> Result<NativeProgram, ShaderError> {
    if registers.len() < 0x1c {
        return Err(Malformed("compute registers too short"));
    }
    let flags = u32_at(registers, 0)?;
    let size = u32_at(registers, 0x0c)?;
    // The depth is the low half of its word; the high half says 32 lanes.
    let depth = u32_at(registers, 0x10)?;
    let threads = [size & 0xFFFF, size >> 16, depth & 0xFFFF];
    let rsrc1 = u32_at(registers, 0x14)?;
    let rsrc2 = u32_at(registers, 0x18)?;
    if threads.contains(&0)
        || threads[2] > 64
        || threads
            .into_iter()
            .try_fold(1u32, u32::checked_mul)
            .is_none_or(|lanes| lanes > 1024)
    {
        return Err(Malformed("implausible thread-group size"));
    }
    let wave64 = flags & super::FLAG_WAVE32 == 0 && depth & WAVE32_DEPTH_BIT == 0;
    // COMPUTE_PGM_RSRC1.VGPRS: granules of 4 (64 lanes) or 8 (32 lanes), less one.
    let vgprs = ((rsrc1 & 0x3F) + 1) * if wave64 { 4 } else { 8 };
    if vgprs > 256 || !code.len().is_multiple_of(4) {
        return Err(Malformed("invalid compute register count or code length"));
    }
    let (layout, used) = resources(table)?;
    // COMPUTE_PGM_RSRC2.USER_SGPR: what the root parameters can name.
    let user_sgprs = rsrc2 >> 1 & 0x1F;
    if layout.iter().any(|p| u32::from(p.user_data) + p.sgprs > user_sgprs) {
        return Err(Malformed("a resource table names user data the program does not have"));
    }
    let code = trimmed(code);
    if code.is_empty() {
        return Err(Malformed("no code"));
    }
    Ok(NativeProgram {
        stage: HwStage::Cs,
        wave64,
        vgprs: vgprs as u16,
        layout,
        used,
        io: StageIo::Cs(ComputeIo { rsrc2, threads }),
        code: code.to_vec(),
    })
}

fn root_raw(root: RootType) -> u32 {
    match root {
        RootType::Constants => 0,
        RootType::Descriptor => 1,
        RootType::Table => 3,
        RootType::ImmediateSampler => 4,
        RootType::ResourceHeap => 5,
        RootType::SamplerHeap => 6,
        RootType::Other(n) => n,
    }
}

fn kind_raw(kind: ResourceType) -> u8 {
    match kind {
        ResourceType::Srv => 0,
        ResourceType::Sampler => 1,
        ResourceType::Cbv => 2,
        ResourceType::Uav => 3,
        ResourceType::Other(n) => n,
    }
}

/// An internal shader container built from the three blocks in a native compute description.
pub fn console_compute_container(
    registers: &[u8],
    table: &[u8],
    code: &[u8],
) -> Result<Vec<u8>, ShaderError> {
    console_compute_container_rooted(registers, table, code, None)
}

/// [`console_compute_container`], the program's layout completed from the
/// serialized root signature the description names ([`completed`]), or
/// refused when the signature cannot be read or places something no
/// capture shows. Without a signature, the table's layout alone, as
/// [`console_compute_container`].
pub fn console_compute_container_rooted(
    registers: &[u8],
    table: &[u8],
    code: &[u8],
    root: Option<&[u8]>,
) -> Result<Vec<u8>, ShaderError> {
    let mut program = console_compute(registers, table, code)?;
    if let Some(root) = root
        && let StageIo::Cs(cs) = &program.io
    {
        let (params, flags) = root_signature(root)?;
        program.layout = completed(&program.layout, &params, flags, cs.rsrc2 >> 1 & 0x1F)?;
    }
    compute_container(&program)
}

/// A container holding `program` (a compute program) as the title's
/// containers hold theirs, for everything that reads programs from
/// containers: [`super::native_programs`] gives `program` back.
fn compute_container(program: &NativeProgram) -> Result<Vec<u8>, ShaderError> {
    let StageIo::Cs(cs) = &program.io else {
        return Err(Malformed("not a compute program"));
    };
    let put32 =
        |v: &mut Vec<u8>, at: usize, x: u32| v[at..at + 4].copy_from_slice(&x.to_le_bytes());
    let put64 =
        |v: &mut Vec<u8>, at: usize, x: u64| v[at..at + 8].copy_from_slice(&x.to_le_bytes());
    // payload: slot count and record offsets, the record, its stage data, code, tables
    let mut payload = vec![0u8; SLOTS.iter().map(|s| s.1).max().unwrap() + 8];
    put64(&mut payload, 0, GRAPHICS_SLOT_COUNT);
    for (_, at) in SLOTS {
        put64(&mut payload, at, ABSENT);
    }
    let record = payload.len();
    put64(
        &mut payload,
        super::SLOTS.iter().find(|s| s.0 == HwStage::Cs).unwrap().1,
        record as u64,
    );
    payload.resize(record + RECORD_SIZE as usize + CS_THREADS + 12, 0);
    let stage = record + RECORD_SIZE as usize;
    put32(&mut payload, stage + CS_RSRC2, cs.rsrc2);
    for (k, n) in cs.threads.iter().enumerate() {
        put32(&mut payload, stage + CS_THREADS + 4 * k, *n);
    }
    let code_at = payload.len();
    payload.extend_from_slice(&program.code);
    let layout_at = payload.len();
    for p in &program.layout {
        let mut e = [0u8; ENTRY_SIZE as usize];
        e[0..4].copy_from_slice(&root_raw(p.root).to_le_bytes());
        e[4..8].copy_from_slice(&p.table_offset_dwords.to_le_bytes());
        e[8..12].copy_from_slice(&p.sgprs.to_le_bytes());
        e[12..16].copy_from_slice(&p.base_register.to_le_bytes());
        e[16] = p.user_data;
        e[17] = p.slot;
        payload.extend_from_slice(&e);
    }
    let used_at = payload.len();
    for u in &program.used {
        let mut e = [0u8; ENTRY_SIZE as usize];
        e[0] = kind_raw(u.kind);
        e[1] = root_raw(u.root) as u8;
        e[4..8].copy_from_slice(&u.table_offset_dwords.to_le_bytes());
        e[8..12].copy_from_slice(&u.lower_bound.to_le_bytes());
        e[12..16].copy_from_slice(&u.stride.to_le_bytes());
        e[16] = u.dwords;
        e[17] = u.user_data;
        e[18] = u.slot;
        payload.extend_from_slice(&e);
    }
    put64(&mut payload, record, code_at as u64);
    put64(&mut payload, record + 8, program.code.len() as u64);
    put32(
        &mut payload,
        record + RECORD_FLAGS,
        if program.wave64 {
            0
        } else {
            super::FLAG_WAVE32
        },
    );
    put64(&mut payload, record + RECORD_LAYOUT, layout_at as u64);
    put64(&mut payload, record + RECORD_USED, used_at as u64);
    put32(
        &mut payload,
        record + RECORD_COUNTS,
        program.layout.len() as u32,
    );
    put32(
        &mut payload,
        record + RECORD_COUNTS + 4,
        program.used.len() as u32,
    );
    put32(&mut payload, record + RECORD_REGS, u32::from(program.vgprs));
    // FSSM, then XBCH around its zlib stream
    let mut fssm = b"FSSM".to_vec();
    fssm.extend(FSSM_VERSION.to_le_bytes());
    fssm.extend((payload.len() as u64).to_le_bytes());
    debug_assert_eq!(fssm.len(), FSSM_HEADER);
    fssm.extend(payload);
    if fssm.len() as u64 > MAX_UNPACKED {
        return Err(Malformed(
            "native compute program exceeds the container size limit",
        ));
    }
    let mut compressed = vec![0; zlib_rs::compress_bound(fssm.len())];
    let config = zlib_rs::DeflateConfig {
        level: 0,
        ..Default::default()
    };
    let (stream, status) = zlib_rs::compress_slice(&mut compressed, &fssm, config);
    if status != zlib_rs::ReturnCode::Ok {
        return Err(Malformed("could not encode native compute program"));
    }
    let mut xbch = b"XBCH".to_vec();
    xbch.extend(XBCH_VERSION.to_le_bytes());
    xbch.extend((stream.len() as u64).to_le_bytes());
    xbch.extend((fssm.len() as u64).to_le_bytes());
    debug_assert_eq!(xbch.len(), XBCH_HEADER);
    xbch.extend_from_slice(stream);
    // the DXIL part: a header up to the index, the index of one entry, the blob
    let mut dxil = vec![0u8; XDAT_OFFSET];
    dxil.extend_from_slice(b"XDAT");
    dxil.extend(1u32.to_le_bytes());
    dxil.extend(0x24u32.to_le_bytes());
    let blob_at = dxil.len() + 12;
    dxil.extend(KIND_NATIVE.to_le_bytes());
    dxil.extend((blob_at as u32).to_le_bytes());
    dxil.extend((xbch.len() as u32).to_le_bytes());
    dxil.extend(xbch);
    Ok(dxbc::build(&[(FourCC::DXIL, &dxil)]))
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A synthetic table with a fixed
    /// sampler and a table of an SRV, two UAVs and a CBV, then a range and
    /// the hash.
    fn table() -> Vec<u8> {
        let mut t = Vec::new();
        for w in [0x403u32, 0, 0, 0, 0, 5 << 16] {
            t.extend(w.to_le_bytes());
        }
        let mut entry = |words: [u32; 8]| {
            for w in words {
                t.extend(w.to_le_bytes());
            }
        };
        entry([0x41, 0x401, 0, 3, 0, 0x0002_0004, u32::MAX, 0]);
        entry([0x41, 0x300, 0, 9, 4, 0x408, u32::MAX, 0]);
        entry([0x41, 0x303, 8, 2, 4, 0x408, u32::MAX, 0]);
        entry([0x41, 0x303, 16, 3, 4, 0x408, u32::MAX, 0]);
        entry([0x41, 0x302, 24, 1, 0, 0x404, u32::MAX, 0]);
        entry([0x42, 0, 0, 9, 1, 0, u32::MAX, 0]);
        entry([0x43, 1, 2, 3, 4, 0, 0, 0]);
        let size = (t.len() - 0x14) as u32;
        t[4..8].copy_from_slice(&size.to_le_bytes());
        t
    }

    fn registers() -> Vec<u8> {
        [0u32, 0, 0, 8 << 16 | 8, 1, 3, 5 << 1]
            .iter()
            .flat_map(|w| w.to_le_bytes())
            .collect()
    }

    #[test]
    fn a_console_compute_program_reads_its_registers_and_table() {
        let code = [0x7E00_0280u32, 0xBF81_0000, S_CODE_END, S_CODE_END];
        let code: Vec<u8> = code.iter().flat_map(|w| w.to_le_bytes()).collect();
        let p = console_compute(&registers(), &table(), &code).unwrap();
        assert!(p.wave64);
        assert_eq!(p.vgprs, 16);
        assert_eq!(p.code.len(), 8, "the s_code_end padding is left out");
        let StageIo::Cs(cs) = p.io else { panic!() };
        assert_eq!(
            (cs.rsrc2, cs.threads, cs.user_sgprs()),
            (5 << 1, [8, 8, 1], 5)
        );
        assert_eq!(p.used.len(), 5);
        assert_eq!(
            p.used[2],
            UsedResource {
                kind: ResourceType::Uav,
                root: RootType::Table,
                table_offset_dwords: 8,
                lower_bound: 2,
                stride: 4,
                dwords: 8,
                user_data: 4,
                slot: 0,
            }
        );
        assert_eq!(
            (p.used[0].kind, p.used[0].root, p.used[0].slot),
            (ResourceType::Sampler, RootType::ImmediateSampler, 2)
        );
        assert_eq!(p.layout[4].table_offset_dwords, 24);
    }

    #[test]
    fn incomplete_or_unknown_descriptions_are_refused() {
        let t = table();
        let code = 0xBF81_0000u32.to_le_bytes();
        let regs = registers();
        for end in 0..t.len() {
            assert!(console_compute(&regs, &t[..end], &code).is_err());
        }
        // Root constants of no size, an unknown tag, no hash.
        for (offset, value) in [(t.len() - 64, 0x41u32), (24, 0x44), (t.len() - 32, 0x42)] {
            let mut bad = t.clone();
            bad[offset..offset + 4].copy_from_slice(&value.to_le_bytes());
            assert!(console_compute(&regs, &bad, &code).is_err());
        }
        assert!(console_compute(&regs, &t, &code[..3]).is_err());
        assert!(console_compute(&regs, &t, &S_CODE_END.to_le_bytes()).is_err());
        let mut large = regs;
        large[12..16].copy_from_slice(&(64u32 << 16 | 32).to_le_bytes());
        assert!(console_compute(&large, &t, &code).is_err());
    }

    /// As a title's table (a program with seven user SGPRs): a header count
    /// one short of the 0x41 entries, two root slots whose entries name
    /// user-data dwords 4 and 5, structured buffer SRVs of a 16-byte stride.
    #[test]
    fn entries_name_their_user_data_and_stride_as_a_title_does() {
        let mut t = Vec::new();
        for w in [0x403u32, 0, 0, 0, 0, 2 << 16 | 0x440] {
            t.extend(w.to_le_bytes());
        }
        let mut entry = |words: [u32; 8]| t.extend(words.iter().flat_map(|w| w.to_le_bytes()));
        entry([0x41, 0x401, 0, 0, 0, 2 << 16 | 4, u32::MAX, 0]);
        entry([0x41, 0x300, 0x130, 0x48, 0x10, 0x404, u32::MAX, 0]);
        entry([0x41, 0x303, 0x28, 0x16, 4, 1 << 16 | 0x508, u32::MAX, 0]);
        entry([0x42, 0, 0xc_0001, 0x48, 0, 0, u32::MAX, 0]);
        entry([0x43, 1, 2, 3, 4, 0, 0, 0]);
        let size = (t.len() - 0x14) as u32;
        t[4..8].copy_from_slice(&size.to_le_bytes());
        let mut regs = registers();
        regs[0x18..0x1c].copy_from_slice(&(7u32 << 1).to_le_bytes());
        let code = 0xBF81_0000u32.to_le_bytes();
        let p = console_compute(&regs, &t, &code).unwrap();
        let named: Vec<_> = p.used.iter().map(|u| (u.slot, u.user_data, u.stride)).collect();
        assert_eq!(named, [(2, 0, 0), (0, 4, 16), (1, 5, 4)]);
        assert_eq!(p.layout.iter().map(|l| l.user_data).collect::<Vec<_>>(), [0, 4, 5]);
        // Five user SGPRs do not reach dword 5.
        regs[0x18..0x1c].copy_from_slice(&(5u32 << 1).to_le_bytes());
        assert!(console_compute(&regs, &t, &code).is_err());
    }

    /// A title's gamma pass: a UAV table in dword 4 and one root constant
    /// in dword 5, read as a value; and a depth word whose high half is not
    /// the depth but says 32 lanes.
    #[test]
    fn root_constants_are_a_root_parameter_and_the_depth_its_low_half() {
        let mut t = Vec::new();
        for w in [0x403u32, 0, 0, 0, 0, 2 << 16 | 0x440] {
            t.extend(w.to_le_bytes());
        }
        let mut entry = |words: [u32; 8]| t.extend(words.iter().flat_map(|w| w.to_le_bytes()));
        entry([0x41, 0x303, 0, 0x37, 4, 0x408, u32::MAX, 0]);
        entry([0x41, 0x002, 0, 0x1f6, 0, 1 << 16 | 0x504, u32::MAX, 0]);
        entry([0x42, 0, 0x2_0101, 0x37, 1, 0, u32::MAX, 0]);
        entry([0x43, 1, 2, 3, 4, 0, 0, 0]);
        let size = (t.len() - 0x14) as u32;
        t[4..8].copy_from_slice(&size.to_le_bytes());
        let mut regs = registers();
        regs[0x0c..0x10].copy_from_slice(&(1u32 << 16 | 32).to_le_bytes());
        regs[0x10..0x14].copy_from_slice(&0x0010_0001u32.to_le_bytes());
        regs[0x18..0x1c].copy_from_slice(&0x98cu32.to_le_bytes());
        let code = 0xBF81_0000u32.to_le_bytes();
        let p = console_compute(&regs, &t, &code).unwrap();
        let StageIo::Cs(cs) = p.io else { panic!() };
        assert_eq!(cs.threads, [32, 1, 1]);
        // 0x10 above the depth: 32 lanes, VGPRs in granules of 8.
        assert!(!p.wave64);
        assert_eq!(p.vgprs, 32);
        assert_eq!(p.used.len(), 1, "constants are not a resource read");
        assert_eq!(
            p.layout[1],
            RootParam {
                root: RootType::Constants,
                table_offset_dwords: 0,
                sgprs: 1,
                base_register: 0x1f6,
                user_data: 5,
                slot: 1,
            }
        );
    }

    /// As a title's table: a table in dword 4, a root SRV (t1) in dwords 5
    /// and 6, its 64-bit address, then a root constant in dword 7 (slot 2:
    /// the root SRV's two dwords push it one past 4 + 2).
    #[test]
    fn a_root_descriptor_takes_two_user_data_dwords() {
        let mut t = Vec::new();
        for w in [0x403u32, 0, 0, 0, 0, 3 << 16 | 0x440] {
            t.extend(w.to_le_bytes());
        }
        let mut entry = |words: [u32; 8]| t.extend(words.iter().flat_map(|w| w.to_le_bytes()));
        entry([0x41, 0x303, 0, 7, 4, 0x408, u32::MAX, 0]);
        entry([0x41, 0x100, 0, 1, 0, 1 << 16 | 0x502, u32::MAX, 0]);
        entry([0x41, 0x002, 0, 0x1f6, 0, 2 << 16 | 0x704, u32::MAX, 0]);
        entry([0x42, 0, 0xa_0101, 7, 0, 0, u32::MAX, 0]);
        entry([0x43, 1, 2, 3, 4, 0, 0, 0]);
        let size = (t.len() - 0x14) as u32;
        t[4..8].copy_from_slice(&size.to_le_bytes());
        let mut regs = registers();
        regs[0x18..0x1c].copy_from_slice(&(8u32 << 1).to_le_bytes());
        let code = 0xBF81_0000u32.to_le_bytes();
        let p = console_compute(&regs, &t, &code).unwrap();
        assert_eq!(
            p.layout[1],
            RootParam {
                root: RootType::Descriptor,
                table_offset_dwords: 0,
                sgprs: 2,
                base_register: 1,
                user_data: 5,
                slot: 1,
            }
        );
        assert_eq!(
            p.used[1],
            UsedResource {
                kind: ResourceType::Srv,
                root: RootType::Descriptor,
                table_offset_dwords: 0,
                lower_bound: 1,
                stride: 0,
                dwords: 2,
                user_data: 5,
                slot: 1,
            }
        );
        assert_eq!(p.used.len(), 2, "constants are not a resource read");
        let c = compute_container(&p).unwrap();
        let c = crate::Container::parse(&c).unwrap();
        assert_eq!(
            super::super::native_programs(c.part(FourCC::DXIL).unwrap().data).unwrap(),
            vec![p]
        );
        // Six user SGPRs hold the address's low half only (without the
        // constant, the root SRV the last entry).
        regs[0x18..0x1c].copy_from_slice(&(6u32 << 1).to_le_bytes());
        t.drain(0x58..0x78);
        let size = (t.len() - 0x14) as u32;
        t[4..8].copy_from_slice(&size.to_le_bytes());
        assert!(console_compute(&regs, &t, &code).is_err());
        regs[0x18..0x1c].copy_from_slice(&(7u32 << 1).to_le_bytes());
        assert!(console_compute(&regs, &t, &code).is_ok());
    }

    #[test]
    fn short_header_wave32_and_multiblock_code_round_trip() {
        let mut t = table();
        t.drain(8..16);
        t[..4].copy_from_slice(&3u32.to_le_bytes());
        let mut regs = registers();
        regs[..4].copy_from_slice(&super::super::FLAG_WAVE32.to_le_bytes());
        let code = vec![0; 65536];
        let p = console_compute(&regs, &t, &code).unwrap();
        assert!(!p.wave64);
        assert_eq!(p.vgprs, 32);
        let bytes = console_compute_container(&regs, &t, &code).unwrap();
        let c = crate::Container::parse(&bytes).unwrap();
        assert_eq!(
            super::super::native_programs(c.part(FourCC::DXIL).unwrap().data).unwrap(),
            vec![p]
        );
    }

    /// A serialized root signature (version 1.1): per parameter its type and
    /// payload (register, space, and for root constants their count).
    fn rts0(params: &[(u32, [u32; 3])], flags: u32) -> Vec<u8> {
        let n = params.len() as u32;
        let mut words = vec![2, n, 24, 0, 24 + 24 * n, flags];
        for (k, (kind, _)) in params.iter().enumerate() {
            words.extend([*kind, 0, 24 + 12 * n + 12 * k as u32]);
        }
        for (_, payload) in params {
            words.extend(payload);
        }
        words.iter().flat_map(|w| w.to_le_bytes()).collect()
    }

    /// As the title's `slot218-m007`: tables in user-data dwords 4 and 5 and
    /// a root constant in 6 named, eleven user SGPRs.
    fn named() -> Vec<RootParam> {
        let p = |root, user_data, sgprs, slot| RootParam {
            root,
            table_offset_dwords: 0,
            sgprs,
            base_register: 0,
            user_data,
            slot,
        };
        vec![
            p(RootType::Table, 4, 1, 0),
            p(RootType::Table, 5, 1, 1),
            p(RootType::Constants, 6, 1, 2),
        ]
    }

    #[test]
    fn a_root_signature_completes_what_the_table_leaves_out() {
        // m007's eleven user SGPRs: three constants in 7 to 9 would leave
        // dword 10 to a heap.
        let params = [(0, [0; 3]), (0, [0; 3]), (1, [0x1f5, 0, 1]), (1, [0x1f6, 0, 3])];
        let (four, flags) = root_signature(&rts0(&params, 0)).unwrap();
        let layout = completed(&named(), &four, flags, 10).unwrap();
        assert_eq!(&layout[..3], named().as_slice());
        assert_eq!(
            layout[3..],
            [RootParam {
                root: RootType::Constants,
                table_offset_dwords: 0,
                sgprs: 3,
                base_register: 0x1f6,
                user_data: 7,
                slot: 3,
            }]
        );
        // Without the user SGPRs for it, nothing more.
        assert_eq!(completed(&named(), &four, flags, 7).unwrap(), named());
        let refused = |r: Result<Vec<RootParam>, ShaderError>| match r {
            Err(Malformed(why)) => why,
            r => panic!("{r:?}"),
        };
        // The view heap in the dword past the parameters, then the sampler
        // heap; user SGPRs the heaps do not account for exactly stay
        // unnamed.
        let heap = |root, user_data, slot| RootParam {
            root,
            table_offset_dwords: 0,
            sgprs: 1,
            base_register: 0,
            user_data,
            slot,
        };
        let (_, views) = root_signature(&rts0(&params, 0x400)).unwrap();
        let mut with_view = layout.clone();
        with_view.push(heap(RootType::ResourceHeap, 10, 4));
        assert_eq!(completed(&named(), &four, views, 11).unwrap(), with_view);
        let mut with_both = with_view.clone();
        with_both.push(heap(RootType::SamplerHeap, 11, 5));
        assert_eq!(completed(&named(), &four, 0xC00, 12).unwrap(), with_both);
        assert_eq!(completed(&named(), &four, 0, 11).unwrap(), layout);
        assert_eq!(completed(&named(), &four, views, 12).unwrap(), layout);
        assert_eq!(completed(&named(), &four, 0xC00, 11).unwrap(), layout);
        // Constants cut by the end of the user SGPRs.
        assert!(refused(completed(&named(), &four, 0, 8)).contains("cut by the end"));
        // A table that places a parameter elsewhere, or names another type.
        let mut moved = named();
        moved[2].user_data = 7;
        assert!(refused(completed(&moved, &four, 0, 10)).contains("places a root parameter"));
        let (tables, _) = root_signature(&rts0(&[(0, [0; 3]); 4], 0)).unwrap();
        assert!(refused(completed(&named(), &tables, 0, 10)).contains("places a root parameter"));
        // Root UAVs, which no table places, and short blobs.
        assert!(root_signature(&rts0(&[(4, [0; 3])], 0)).is_err());
        assert!(root_signature(&rts0(&params, 0)[..20]).is_err());
    }

    /// As the title's `slot218-m084`: the table names root constants in
    /// dword 6 with its usual byte 4, the signature gives three of them.
    #[test]
    fn root_constants_take_their_count_from_the_signature() {
        let p = |root, user_data, slot| RootParam {
            root,
            table_offset_dwords: 0,
            sgprs: 1,
            base_register: 0,
            user_data,
            slot,
        };
        let table = vec![p(RootType::Table, 4, 0), p(RootType::Table, 5, 1), p(RootType::Constants, 6, 2)];
        let (sig, flags) =
            root_signature(&rts0(&[(0, [0; 3]), (0, [0; 3]), (1, [0x1f6, 0, 3])], 0)).unwrap();
        let layout = completed(&table, &sig, flags, 9).unwrap();
        assert_eq!(layout[2].sgprs, 3);
        // Three constants from dword 6 do not fit in eight user SGPRs.
        assert!(completed(&table, &sig, flags, 8).is_err());
    }

    /// A root SRV is taken where the table places it itself (as the title's
    /// t1 in dwords 5 and 6 after a table in 4), and refused where only the
    /// signature names it.
    #[test]
    fn a_root_srv_is_taken_only_where_the_table_places_it() {
        let p = |root, user_data, sgprs, slot| RootParam {
            root,
            table_offset_dwords: 0,
            sgprs,
            base_register: 0,
            user_data,
            slot,
        };
        let table = vec![p(RootType::Table, 4, 1, 0), p(RootType::Descriptor, 5, 2, 1)];
        let (srv, flags) = root_signature(&rts0(&[(0, [0; 3]), (3, [1, 0, 0])], 0)).unwrap();
        assert!(srv[1].srv);
        assert_eq!(completed(&table, &srv, flags, 7).unwrap(), table);
        // The same signature with only the table named: the root SRV is not
        // taken from the signature alone.
        let refused = completed(&table[..1], &srv, flags, 7);
        assert!(matches!(refused, Err(Malformed(why)) if why.contains("root SRV")));
    }

    #[test]
    fn the_container_holds_what_the_root_signature_adds() {
        let mut regs = registers();
        regs[0x18..0x1c].copy_from_slice(&(10u32 << 1).to_le_bytes());
        let code = 0xBF81_0000u32.to_le_bytes();
        // A table entry in dword 4 for slot 0; the signature's root CBV and
        // constants follow, the second table being slot 1 at 5.
        let mut t = Vec::new();
        for w in [0x403u32, 0, 0, 0, 0, 1 << 16 | 0x440] {
            t.extend(w.to_le_bytes());
        }
        let mut entry = |words: [u32; 8]| t.extend(words.iter().flat_map(|w| w.to_le_bytes()));
        entry([0x41, 0x303, 0, 2, 4, 0x408, u32::MAX, 0]);
        entry([0x43, 1, 2, 3, 4, 0, 0, 0]);
        let size = (t.len() - 0x14) as u32;
        t[4..8].copy_from_slice(&size.to_le_bytes());
        let root = rts0(&[(0, [0; 3]), (0, [0; 3]), (2, [3, 0, 0]), (1, [7, 0, 2])], 0);
        let root = dxbc::build(&[(FourCC::RTS0, &root)]);
        let bytes = console_compute_container_rooted(&regs, &t, &code, Some(&root)).unwrap();
        let c = crate::Container::parse(&bytes).unwrap();
        let p = super::super::native_programs(c.part(FourCC::DXIL).unwrap().data).unwrap();
        let placed: Vec<_> = p[0].layout.iter().map(|l| (l.root, l.slot, l.user_data, l.sgprs)).collect();
        assert_eq!(
            placed,
            [
                (RootType::Table, 0, 4, 1),
                (RootType::Table, 1, 5, 1),
                (RootType::Descriptor, 2, 6, 2),
                (RootType::Constants, 3, 8, 2),
            ]
        );
        // Without a signature, the table alone; one that cannot be read is
        // refused.
        let plain = console_compute_container_rooted(&regs, &t, &code, None).unwrap();
        assert_eq!(plain, console_compute_container(&regs, &t, &code).unwrap());
        assert!(console_compute_container_rooted(&regs, &t, &code, Some(b"RTS0")).is_err());
    }

    #[test]
    fn its_container_gives_the_program_back() {
        let code: Vec<u8> = [0x7E00_0280u32, 0xBF81_0000]
            .iter()
            .flat_map(|w| w.to_le_bytes())
            .collect();
        let p = console_compute(&registers(), &table(), &code).unwrap();
        let container = compute_container(&p).unwrap();
        let c = crate::Container::parse(&container).unwrap();
        let back = super::super::native_programs(c.part(FourCC::DXIL).unwrap().data).unwrap();
        assert_eq!(back, vec![p]);
    }
}
