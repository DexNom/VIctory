# Contributing

Thanks for helping. This page covers what a pull request needs to pass and
the rules every change follows. For what to work on, see
[Things to Be Done](README.md#things-to-be-done) in the README and the
[benchmark notes](BENCHMARK.md) for how to measure a change.

## Rule #1: no game data

Nothing from the game, or from any other proprietary title, is ever stored in
this repository: no assets, no parts of the executable, no shader dumps, no
screenshots, and nothing derived from them. Read
[docs/asset-policy.md](docs/asset-policy.md) before your first commit, and
install the hook that enforces it:

```powershell
cargo xtask install-hooks
```

Every commit then runs `cargo xtask policy --staged`. Keep logs you attach to
an issue or pull request free of your game path and Windows user name.

## Build

You need 64-bit Windows, Rust and Visual Studio Build Tools with the x64 MSVC
toolset and a Windows SDK. The Rust version is pinned in
`rust-toolchain.toml`. Then, in the checkout:

```powershell
rustup toolchain install
.\Initialize-FormatTables.ps1
cargo build --locked --release -p victory --features diagnostics --bins
```

Point the tools at your own files with `victory.toml`
(copy [victory.example.toml](victory.example.toml)). Every configured path
must be outside the repository.

## Checks a pull request must pass

CI runs these on Windows without a graphics card. Run them locally first:

```powershell
cargo clippy --workspace --all-targets --locked -- -D warnings
cargo clippy --workspace --all-targets --all-features --locked -- -D warnings
cargo test --workspace --locked
```

Clippy warnings fail the build in both feature sets. Tests that need a real
GPU fail on the software adapter unless `VICTORY_TESTS_ALLOW_SOFTWARE_SKIP=1`
is set, which only CI does. Tests that need game files read their paths from
environment variables and skip when those are unset.

A second check, **Screen**, fails on purpose when a pull request touches files
that must be read before anyone builds it: `Cargo.toml`, `Cargo.lock`,
`rust-toolchain.toml`, `.cargo/`, `build.rs`, `xtask/`, `.github/`, scripts
(`.ps1`, `.cmd`, `.sh`, `.py` and similar), or binary files. A red Screen is
not a rejection; it means a maintainer reads the whole diff before merging.
Leave those files out of a change that doesn't need them.

## Pull requests

- Keep one change per pull request, so it can be reviewed and reverted on its
  own.
- Start the title with the area it touches, as the history does: `GPU:`,
  `Shaders:`, `HLE:`, `Tests:`, `Docs:`.
- Say what you measured and on which hardware: GPU, driver, scene, and
  before-and-after numbers where performance is the point. Say so plainly if
  you couldn't run the change on a GPU.
- Findings and test reports that aren't meant to be merged are welcome too;
  mark them as such in the title.
