# DexNom RayTime documentation

## Run the runtime

- [Installation](installation.md) covers prerequisites, setup, updates, and local files.
- [Graphics modes](graphics.md) covers the Radeon FSR Quality baseline, ray tracing, and experimental NVIDIA launches.
- [Troubleshooting](troubleshooting.md) covers startup problems, shader warmup, and finding logs.

## Develop and test

- [Contributing](CONTRIBUTING.md) covers build checks, performance priorities, and benchmark reporting.
- [Benchmarks](../BENCHMARK.md) records the tested hardware, game scenes, and rendering results.
- [Architecture](architecture.md) maps the runtime, crates, and shader pipeline.
- [Ray tracing](fork-ray-tracing.md) describes how the fork translates the console's ray-tracing shaders.
- [Asset policy](asset-policy.md) explains which files can be committed and how setup verifies the external BCPack tables.

## Credits

- **Marvisak and the VIctory contributors** created the original Windows compatibility runtime. [VIctory](https://github.com/Marvisak/VIctory)
- **ZSlayerHQ and the victory-world contributors**, including **triippywhale** and **asdaasdsaddf**, supplied the Radeon rendering baseline for this fork. [victory-world-runtime](https://github.com/ZSlayerHQ/victory-world-runtime) · [Integrated revision](https://github.com/ZSlayerHQ/victory-world-runtime/commit/99f8834aa16b98d7f3943b8de262a8d4857bda7b)
- **The XCL contributors** published an independent compatibility layer that informed work on shader translation and CPU/GPU memory synchronization. [XCL](https://github.com/xcl-dev/xcl)
- **AMD** supplies the FSR upscaler used by the Quality 4K profile. [AMD FSR SDK](https://gpuopen.com/amd-fsr-sdk/)

The source is licensed under GPL-3.0-or-later. Upstream supplies the
BCPack tables separately from the GPL source. Setup verifies the tables
before building. [License](../LICENSE) · [Asset policy](asset-policy.md) · [Upstream README and historical credits](../README.upstream.md)
