# Contributing

## Build and check your changes

Use the Windows build prerequisites in the [installation guide](installation.md#requirements).
The Rust toolchain is pinned in the repository. From the root, prepare the
format tables and run the same Cargo commands used by CI.

~~~powershell
.\Initialize-FormatTables.ps1
cargo clippy --workspace --all-targets --locked -- -D warnings
cargo clippy --workspace --all-targets --all-features --locked -- -D warnings
cargo test --workspace --locked
~~~

Run `cargo xtask policy` to check tracked files against the
[source-only asset policy](asset-policy.md).

> [!NOTE]
> CI can skip tests that require a hardware GPU when it encounters WARP.
> Verify rendering changes on a hardware adapter and include its model
> and driver in the results. [CI workflow](../.github/workflows/ci.yml)

## Performance priorities

- **GPU readback.** Track the pages that GPU writers actually touch and read back only ranges needed by CPU consumers. The ray-tracing header builder is a starting point. Check fences, CPU writes, aliases, and remaps when narrowing synchronization. [Submission and readback](../crates/victory-gpu/src/host.rs) · [Buffer snapshots](../crates/victory-gpu/src/shadow.rs) · [Guest memory directory](../crates/victory-gpu/src/memory.rs)
- **Texture memory and streaming.** Measure dedicated and shared GPU memory separately. Look for duplicate resources, oversized staging allocations, and repeated mip uploads. Keep a usable mip resident until its replacement is ready. For black materials, trace the draw's base color to the bound texture and mip. [Texture residency](../crates/victory-gpu/src/texture.rs) · [Texture descriptors](../crates/victory-gpu/src/descriptor/host.rs)
- **Frame-time stability.** Measure cold pipeline compilation and warm gameplay separately. Look for CPU waits on buffer publication, repeated uploads, and pipeline work that can move ahead of the first draw. Compare pipeline counts and the rendered scene. [Shader translation](../crates/victory-shader/src/emit/mod.rs) · [Pipeline creation](../crates/victory-gpu/src/pipeline.rs)

## Report a bug or benchmark

In a pull request or test report, include the commit, Windows version, CPU,
GPU actually used, driver, launch profile, scene, and steps to reproduce.
Share relevant log excerpts with local paths removed. [Find the logs](troubleshooting.md#open-the-latest-log)

For a performance comparison, keep the checkpoint, camera, driver, and
settings fixed. The default comparison profile is Series X, RT on, and
FSR Quality from 1440p to 4K. Record the sample size and distinguish cold
from warm runs. Compare these numbers where available:

| Measurement | What to record |
| --- | --- |
| Frame time | Median and 95th percentile |
| GPU memory | Peak dedicated and shared usage |
| Transfers | Upload and readback time and bytes |
| Rendering | Dropped draws, pending pipelines, and visible changes |

See the [existing benchmark results](../BENCHMARK.md) for examples and the
settings behind them.
