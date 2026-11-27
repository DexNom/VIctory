# Configurable write-watch range grouping

## Motivation and scope

Grouping nearby watched ranges reduces syscall overhead, but large gaps can cause GetWriteWatch to scan substantially more address space than consumers request. This change exposes the existing grouping distance as an experimental process-wide setting, `VICTORY_AHEAD_GAP_KIB`. Accepted values are multiples of 64 KiB from 0 through 8192 inclusive. Missing or invalid values retain the original 8192 KiB setting. The setting is read once per process; the existing maximum group span is unchanged. No default tuning change is proposed.

The contribution is based on DexNom/VIctory commit `64bd58f`. It includes synthetic tests of journal coherence, allocation lifecycle and query/submission costs; timing and counting instrumentation is test-only.

## Reproduction (Windows, PowerShell)

Prepare the verified format tables according to the repository instructions. Use separate processes for each policy. The ignored tests must run in isolation because the journal is global.

```powershell
cargo test --locked --release -p victory-gpu --features diagnostics --lib -- --test-threads=1
$env:VICTORY_ASK_ONCE = '1'
$env:VICTORY_VIEW_GUARD = '1'
foreach ($gap in 0,512,768,2048,8192) {
    $env:VICTORY_AHEAD_GAP_KIB = "$gap"
    foreach ($run in 1..5) {
        foreach ($test in 'grouping_preserves_write_coherence','grouping_preserves_reused_addresses') {
            cargo test --locked --release -p victory-gpu --features diagnostics --lib "watch::tests::$test" -- --exact --ignored --nocapture --test-threads=1
            if ($LASTEXITCODE -ne 0) { throw "Test failed: $gap $run $test" }
        }
    }
}
$configs = @(@{N=32;Gap=8192},@{N=32;Gap=512},@{N=128;Gap=8192},@{N=128;Gap=512})
foreach ($run in 0..9) {
    foreach ($offset in 0..3) {
        $c = $configs[($run+$offset)%4]
        $env:VICTORY_E025_BUFFERS = "$($c.N)"
        $env:VICTORY_AHEAD_GAP_KIB = "$($c.Gap)"
        cargo test --locked --release -p victory-gpu --features diagnostics --lib shadow::measures_grouped_shadow_scaling -- --exact --ignored --nocapture --test-threads=1
        if ($LASTEXITCODE -ne 0) { throw "Benchmark failed" }
    }
}
# Optional interval-only benchmark, not included in the updated results below.
$env:VICTORY_AHEAD_GAP_KIB = '512'
cargo test --locked --release -p victory-gpu --features diagnostics --lib watch::tests::measures_write_query_grouping -- --exact --ignored --nocapture --test-threads=1
Remove-Item Env:VICTORY_AHEAD_GAP_KIB,Env:VICTORY_E025_BUFFERS,Env:VICTORY_ASK_ONCE,Env:VICTORY_VIEW_GUARD -ErrorAction SilentlyContinue
```

## Synthetic submission benchmark

One Windows system (Ryzen 5 5600X, Radeon RX 6700 XT, 32 GB RAM). Both policies use the same patched Release diagnostics build. Each configuration is repeated ten times in rotating order. Each run covers nine layout/write-density combinations, with five warmups and 30 measured synchronous submissions per combination. The benchmark uses independent 64 KiB upload shadows and verifies readback outside the timed section. It measures host submission including GPU copies and synchronization, not a rendered frame. Kernel timing counts submitting-thread GetWriteWatch calls; it does not measure all kernel activity.

40/40 runs passed, with 360 scenario summaries, 10,800 measured submissions and 187,200 measured GetWriteWatch calls. No reported kernel failures or readback mismatches.

The table reports medians of ten per-run medians, in microseconds. Positive reduction means lower time with 512 KiB. The reference is the patched build configured with 8192 KiB, not a separately compiled unmodified checkout.

| Buffers | Layout | Writes | Total 8192 | Total 512 | Reduction | Kernel 8192 | Kernel 512 |
|---:|---|---|---:|---:|---:|---:|---:|
| 32 | contiguous | clean | 328.625 | 329.350 | -0.22% | 3.900 | 4.150 |
| 32 | contiguous | sparse | 358.750 | 372.050 | -3.71% | 5.550 | 5.900 |
| 32 | contiguous | full | 508.225 | 528.100 | -3.91% | 11.000 | 12.225 |
| 32 | mixed | clean | 343.775 | 336.250 | +2.19% | 17.225 | 6.900 |
| 32 | mixed | sparse | 398.875 | 390.575 | +2.08% | 19.200 | 9.225 |
| 32 | mixed | full | 531.500 | 525.300 | +1.17% | 26.300 | 16.475 |
| 32 | dispersed | clean | 380.225 | 353.325 | +7.07% | 52.300 | 19.325 |
| 32 | dispersed | sparse | 417.600 | 411.350 | +1.50% | 54.250 | 21.750 |
| 32 | dispersed | full | 577.550 | 557.500 | +3.47% | 65.475 | 34.875 |
| 128 | contiguous | clean | 895.625 | 905.550 | -1.11% | 9.625 | 10.725 |
| 128 | contiguous | sparse | 974.675 | 995.075 | -2.09% | 14.000 | 16.475 |
| 128 | contiguous | full | 1635.625 | 1683.950 | -2.95% | 43.850 | 46.100 |
| 128 | mixed | clean | 973.775 | 941.900 | +3.27% | 70.650 | 29.600 |
| 128 | mixed | sparse | 1046.850 | 1019.500 | +2.61% | 71.050 | 36.800 |
| 128 | mixed | full | 1715.025 | 1698.900 | +0.94% | 109.925 | 73.600 |
| 128 | dispersed | clean | 1105.175 | 997.675 | +9.73% | 206.975 | 67.450 |
| 128 | dispersed | sparse | 1245.400 | 1086.700 | +12.74% | 259.850 | 73.700 |
| 128 | dispersed | full | 1952.350 | 1790.800 | +8.27% | 313.925 | 136.525 |

Equal-weight geometric mean of the nine scenario time ratios per buffer count:

| Buffers | Total time reduction | Kernel time reduction |
|---:|---:|---:|
| 32 | 1.12% | 38.85% |
| 128 | 3.64% | 41.48% |

For 128 dispersed buffers with sparse writes, kernel time decreased from 259.85 to 73.70 us (71.64%), while total submission time decreased by 12.74%. The queried span decreased from 262 MiB to 8 MiB (96.95%) while calls increased from one to 128. Mixed layouts also benefit. All six contiguous cases measured higher total times with 512 KiB despite identical grouping and queried bytes; differences may reflect measurement variability but are not discarded. The optimal threshold is workload dependent.

## Correctness and limitations

- The unmodified baseline GPU suite passed sequentially (239 passed, 4 ignored). The patched suite passed before and after local formatting (240 passed, 8 ignored).
- Both isolated correctness tests passed five times per policy across five policies (50/50 executions).
- The full workspace sequential Release diagnostics run reported 708 passed, 0 failed, 16 ignored before the final formatting-only adjustment.
- Parallel GPU runs: unmodified baseline 8/10 successful (two access violations); patched default 8/10 (one assertion failure, one access violation); patched 512 KiB 8/10 (two access violations). The small sample does not establish equivalence or prove the absence of a new concurrency issue. Existing instability is outside this patch's scope.
- GPU Release diagnostics Clippy completed without reported warnings; asset policy passed before the final formatting adjustment. Global rustfmt has pre-existing differences; the final local formatting restored the baseline count of reported diff blocks, not a globally clean rustfmt result.
- These synthetic results are not evidence of game FPS gains or universal improvements. No external title assets are required by these tests.

Development and analysis were AI-assisted; test execution was performed on the contributor's Windows machine.
