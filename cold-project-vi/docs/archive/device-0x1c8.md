# Device method 0x1c8: pipelines from native code

Decoded on 2026-11-26 from description dumps of an RTX 5070 Ti run (`save_description`
in `crates/victory-gpu/src/device.rs` logs them as `pipeline description bytes`). The
method is still `unknown_pipeline` here: the title goes without these pipelines and their
draws (over 16,384 creations in the first minutes, 100-800 dropped operations a frame).

## Call

`(this, description, iid, out)`, asks for `ID3D12PipelineState`.

## Description (16 words seen)

| word | meaning |
|---|---|
| 0 | an object (vtable at +0; the same per root signature, likely it) |
| 1, 2 | state blob, 0x17c-0x200 bytes: packed hardware registers (below) |
| 3, 4 | vertex input signature, 0x50-0xbc bytes (names `POSITION`, `COLOR`, `TEXCOORD`) |
| 5, 6, 7, 8 | first program: code, code size, metadata, metadata size |
| 9, 10, 11, 12 | second program: the same; either may be 0 (absent) |
| 13-15 | 0 |

The metadata blobs lie together; the code is 0x100-aligned elsewhere.

## Programs

GFX10 machine code, compiled for the pipeline (the console compiler's output, not a
container). Disassemble with `cargo run -p victory-shader --example gfx10_disasm -- <file>`.

- Each begins with three `v_nop`s carrying literals: the shader hash (also in the 0x43 record).
- The pixel shader's exports are already for the render target's format
  (`exp mrt0 ... compr`), so no export patches are needed.
- The vertex shader **fetches its own vertices**: user SGPRs s[0:1] hold the address of a
  table of vertex buffer descriptors, s2 the base vertex (`v_add v0, s2, v0`), and it loads
  with `buffer_load_dwordx3 ... idxen` / `tbuffer_load_format ...`. The root signature's
  table address is in s8 (`s_load ... s[8:9]` with `s9 = 0`). There is no fetch shader and
  no VGPR input mapping as in FSSM records.

## Program metadata

The FSPX kind-7 layout (`native::fspx_program`) with a shorter header:

```text
+0x00 u32 2 (FSPX: 0x403)    +0x04 u32 size - 0x0c    +0x08 u32 code dwords
+0x0c u8 0x40, u8 first user-data dword (bit 7 set on vertex programs), u8 count of
      0x41 records, u8 0
+0x10 32-byte records: 0x41 resource read, 0x42 view declaration, 0x43 shader hash (last)
```

0x41 records are FSSM used-resource entries (type, root type, table offset, register,
view dimension, dwords, user-data dword, root slot). Example (a pixel shader): a fixed
sampler s14 (root type 4, slot 2), an SRV buffer t13 at table offset 0 (slot 0, user data
8) and a 2D SRV t0 at table offset 0x10.

## State blob (not decoded)

Register values for the whole pipeline: eight repeated `e5040504` and `01540154` words
(per render target), `ffffffff` masks, `0x0000000f` (a write mask). Needed for blend,
rasterizer, depth and the pixel shader's interpolation (`SPI_PS_INPUT_CNTL_n`).

## What implementing it takes

1. Map the state blob's registers to D3D12 blend/raster/depth state and render target
   formats.
2. Translate the vertex program with its own vertex fetch: give it the vertex buffer
   descriptor table (s[0:1]) and the base vertex (s2) from `IASetVertexBuffers` and the
   draw, a path the translator does not have (FSSM vertex programs get their attributes
   in VGPRs).
3. Build `NativeProgram`s from the metadata (layout and used resources as
   `fspx_program` does), pixel inputs from the state blob, then create the host pipeline
   as `pipeline::create_stream` does.
4. Call 16384 seen: one program of 0x4a40 bytes with 0x5b0 of metadata and a 0x200 state
   blob, another kind (likely a mesh or compute pipeline); 0x218 is a different method
   (96-byte array entries ending in a 64-bit hash).

## Measured 2026-11-26: the title has a fallback

With `VICTORY_1C8=stand-in` (a pipeline without shaders handed out) the title draws with
these pipelines, about 1,000 binds a frame in the Series X world, and the whole picture is
black. When 0x1c8 fails (the default) the title makes the same pipelines another way that
works: Series X runs render the world without them. In the Series S profile the intro is
drawn with 0x1c8 pipelines and stays black either way. So 0x1c8 matters for Series S, not
for Series X; the stand-in is for measuring only.
