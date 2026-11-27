# CPU audio transform optimization

The XMA decoder runs its inverse audio transform on the CPU. Its three
supported block sizes are 128, 256 and 512 samples. Previously every channel
and subframe recomputed all pre- and post-rotation sine/cosine pairs and
allocated two FFT scratch buffers.

The transform now shares those fixed rotations through a `OnceLock` cache
and uses 4 KiB of bounded stack scratch. The rotations occupy about 14 KiB
across all decoder instances. Initialization happens on first use; subsequent
transforms allocate no heap memory. The FFT and sample arithmetic retain their
original operation order and precision.

## Local measurements, 26 November 2026

The comparison used base `8114ff7`, Rust 1.98.1, the release profile and an
ARM64 Linux development host. Both versions ran the same CPU-only ignored
benchmark. After warm-up, each execution measured five batches of 10,000
transforms per block size. The table shows the median of three executions per
version, interleaved to reduce order effects.

| Samples per transform | Original, microseconds | Optimized, microseconds | Less transform time |
| --- | ---: | ---: | ---: |
| 128 | 4.138 | 2.148 | 48.1% |
| 256 | 8.160 | 3.170 | 61.1% |
| 512 | 22.754 | 6.867 | 69.8% |

These timings cover the warmed-up inverse transform. Whole-decoder CPU time,
Windows runtime behavior and gameplay frame rates need separate measurements.
The implementation and tests require no GPU adapter or GPU dispatch.

## Verification

- The format-library suite passed in debug and release: 27 passed, 5 ignored.
  The ignored tests are two microbenchmarks and three tests requiring external
  reference data.
- Tests compare with a direct mathematical inverse transform and reconstruct a
  windowed signal. Added coverage checks silence, impulses at three positions,
  all block sizes, a negative scale and the decoder's small output scale.
- A supplemental local differential harness compared the original and optimized
  transform bodies on 256 inputs per block size at four scales, including
  silence, impulses and deterministic random coefficients. All 917,504 output
  samples matched bit for bit. Allocation counting measured two heap
  allocations per original transform and zero after cache initialization with
  the optimized transform.
- Clippy passed with warnings denied, including test targets.
- Windows MSVC cross-target checking passed, including tests.

Run the reproducible checks with the repository's pinned toolchain:

```sh
cargo test -p victory-formats --locked
cargo test -p victory-formats --release --locked
cargo clippy -p victory-formats --all-targets --locked -- -D warnings
cargo check -p victory-formats --target x86_64-pc-windows-msvc --tests --locked
cargo test -p victory-formats --release --locked xma::tests::imdct_throughput -- --exact --ignored --nocapture --test-threads=1
```

The existing format-table setup must have supplied `bcpack-tables.bin` before
building the crate. For a baseline comparison, use a separate worktree at
`8114ff7`, carry over only the `imdct_throughput` benchmark test, and alternate
the two release test executables with other CPU-heavy work stopped.
