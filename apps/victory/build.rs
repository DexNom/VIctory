//! Link settings for the Windows executable.
//!
//! The Agility SDK's two names are read out of the executable's export table
//! by the system `d3d12.dll`, and a static alone does not put a name there:
//! the linker has to be told. Only the `victory` binary makes the device.
//!
//! The stack is reserved larger than the linker's 1 MiB because host
//! graphics drivers compile the title's large shaders on threads that take
//! the executable's default stack size.

fn main() {
    if std::env::var("CARGO_CFG_TARGET_ENV").as_deref() == Ok("msvc") {
        println!("cargo:rustc-link-arg-bins=/STACK:16777216");
    }
    println!("cargo:rustc-link-arg-bin=victory=/EXPORT:D3D12SDKVersion");
    println!("cargo:rustc-link-arg-bin=victory=/EXPORT:D3D12SDKPath");
}
