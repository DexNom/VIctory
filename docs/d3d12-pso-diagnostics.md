# D3D12 pipeline validation diagnostics

Build with `--features diagnostics`. Set `VICTORY_D3D12_PSO_TRACE=1` before launching to request the CPU D3D12 debug layer and log pipeline validation. Install Windows Graphics Tools if the debug interface is unavailable. GPU-based validation is separately controlled by the existing `VICTORY_D3D12_GBV` option and is not required.

Optionally set `VICTORY_D3D12_PSO_ONLY` to a comma-separated list of compute shader hashes to restrict scoped output. The callback still reports a bounded number of unassigned messages. `PSOTRACE1` logs distinguish root, pipeline-library load and new pipeline creation phases. A library miss can be normal. Cache-miss messages use debug level; successful calls use info. The message severity field retains the original D3D12 severity. Quotas bound the number of messages and log when exhausted.

For example, validation message ID 622 during `phase=create` may report that a shader requires 64 lanes but the device supports at most 32. This diagnoses PSO rejection; successful creation alone does not validate shader output.

The callback does not call D3D12 and has no borrowed context. It is registered after successful device initialization and remains registered for the lifetime of the process-wide Host singleton. Concurrent callbacks can run on different threads. Same-thread messages are temporally associated with the current call; other messages are unassigned. Queue fallback attribution is ambiguous. Logging may affect debug-run timing; do not use these captures as uninstrumented performance baselines.
