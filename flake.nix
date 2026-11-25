{
  description = "Victory Windows MSVC cross-build development shell";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
  };

  outputs = { nixpkgs, ... }:
    let
      system = "x86_64-linux";
      pkgs = import nixpkgs {
        inherit system;
      };
    in
    {
      devShells.${system}.default = pkgs.mkShell {
        packages = with pkgs; [
          rustup
          cargo-xwin
          bash
          coreutils
          curl
          umu-launcher
          util-linux
          llvmPackages_21.clang
          llvmPackages_21.lld
          llvmPackages_21.llvm
        ];

        shellHook = ''
          cat <<'EOF'
          Rust toolchain and target come from rust-toolchain.toml (1.98.1, MSVC).
          If needed: rustup target add x86_64-pc-windows-msvc
          Check: cargo xwin check --locked --workspace --all-targets --target x86_64-pc-windows-msvc
          Build: cargo xwin build --locked --release --target x86_64-pc-windows-msvc -p victory
          Format: cargo fmt --all -- --check
          cargo-xwin provisions the MSVC CRT and Windows SDK on first use.
          umu-run is available; Proton/GE-Proton is selected or downloaded at runtime.
          EOF
        '';
      };
    };
}
