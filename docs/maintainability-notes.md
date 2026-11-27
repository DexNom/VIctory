# Maintainability notes

Suggestions from a static read-through of `main` at `921d8d3`. Nothing was built or run, and no game files were
involved; the numbers are line and occurrence counts over tracked files. Take what is useful and drop the rest.

## 1. Very large files and functions

| File | Lines |
|---|---|
| `crates/victory-shader/src/emit/lower.rs` | 12,056 |
| `crates/victory-gpu/src/host.rs` | 7,714 |
| `crates/victory-gpu/src/resource.rs` | 4,229 |
| `crates/victory-gpu/src/draw.rs` | 4,084 |
| `crates/victory-gpu/src/descriptor/host.rs` | 3,938 |

In `lower.rs`, `valu_op` is about 870 lines, `image_with_sampler` about 760, `buffer` about 490 and `sop` about
465. Splitting the lowering by instruction family into submodules of `emit/` (vector ALU, scalar ops, image and
sampler, buffer access), and moving the `mod tests` block at the end of the file into its own file, would make
reviews smaller and merges from the other forks easier.

## 2. Workspace lints relaxed for the whole fork

`Cargo.toml` allows eight clippy lints across the workspace, and `undocumented_unsafe_blocks` is allowed because
eleven blocks lack a `SAFETY` comment. With about 1,270 `unsafe {` blocks in the tree, that lint is the one worth
keeping on: an `#[allow]` on those eleven blocks (or the missing comments) would let the workspace warn again for
every new block.

## 3. Formatting is not checked in CI

`rustfmt.toml` exists and `rust-toolchain.toml` installs `rustfmt`, but `ci.yml` runs clippy and tests only. A
`cargo fmt --all --check` step would keep diffs from several contributors free of formatting noise.

## 4. Panics in runtime paths

The tree has 927 `.unwrap()`, 163 `.expect(` and 84 `panic!(` (tests included; roughly 810 of the `unwrap` calls
are in files that are not test files). In the GPU and HLE crates a panic takes the game down with it. Where the
failure can be reported, returning the existing error types (`Why`, `anyhow`) or logging and refusing the one
resource would be kinder to players than an abort; `clippy::unwrap_used` scoped to `victory-gpu` and `victory-hle`
would show where the remaining ones are.

## 5. Dependencies are not watched

There is no Dependabot configuration and no `cargo audit` or `cargo deny` step. The dependency list is short and
pinned through `Cargo.lock`, so a weekly advisory check would be cheap.

## 6. Documentation

- `docs/` holds 244 lines for about 124,600 lines of Rust. `architecture.md` is a good start; a page per crate on
  what it owns and what it must not do would help new contributors most in `victory-gpu` and `victory-shader`.
- The README carries the change list of the current snapshot. A `CHANGELOG.md` would keep that history when the
  next snapshot replaces it, and leave the README to the setup steps.
- There is no `CONTRIBUTING.md`. With issues disabled, it is the natural place to say how to report a problem and
  what a pull request must include (what was tested, on which GPU and driver).

## What already looks good

Actions pinned by commit, a pinned toolchain and `--locked` builds, `unsafe_op_in_unsafe_fn` denied, `SAFETY`
comments on most unsafe blocks, the `cargo xtask policy` allowlist with its pre-commit hook, and the pull-request
screen workflow that reads a diff before anything is built.
