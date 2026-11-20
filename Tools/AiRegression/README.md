# Fixed native AI regressions

Run the fixed scenarios through the hidden Unity batch launcher.

```text
python Tools/benchmark_duel_ai.py fixed-scenarios --fixed-regressions
```

The launcher copies the versioned fixtures and pinned duel data to an isolated Unity worker. `YGO_AI_REGRESSION_OUT` selects the local output directory when invoking the editor entry point directly. `YGO_AI_REGRESSION_CASES` accepts an optional comma-separated list of case IDs. The default run checks all six cases. A failed gate, missing prompt, native retry, script error, mismatched hash, or exhausted response or time budget returns a nonzero editor exit code.

Each fixture contains four exact native seeds, ordered main and extra deck code arrays, the accepted original prefix, the expected prompt at the release boundary, and original benchmark provenance. Prefix responses are checked against native turn, phase, player, message and hint before submission. After the boundary, the current Smart AI chooses every action and target for both players with replay-backed combo planning enabled.

| ID | Historical match | Release step | Required native behavior |
| --- | --- | ---: | --- |
| sage | m1/s18/c1 | 48 | Defend Sage and skip the zero-ATK direct attack; preserve 8000 LP through T6. |
| coyote | m11/s6/c1 | 122 | Pyro attacks disabled Singularity, then Coyote attacks Micro Coder; both are removed without own T4 battle damage. |
| ddd | m8/s5/c0 | 172 | Decline a fatal third Contract at T7 Main2 and survive the next own upkeep/Main9. |
| snake | m2/s5/c1 | 107 | Nurse + Gift Card produces a real native T6 win. |
| monarch | m1/s12/c1 | 15 | Preserve the Prime/Pantheism setup and retain both Tribute Summoned Erebus and active Domain. |
| connector | m3/s16/c0 | 50 | Use the legal graveyard revival in defense after hidden Ash interrupted the summon; preserve LP and the recorded hand/Extra/graveyard payoff. |

`manifest.json` pins fixture bytes, `ygoengine.dll`, native card properties, Lua scripts, Korean card text, and effect categories. Runtime deck arrays must also match their pinned presets. Review and regenerate prefixes and pins after an intentional data upgrade. `sourceInputSha256` identifies the original full benchmark capture.

Outputs include a machine-readable summary with final native state and timing, a readable proof log, and per-case JSONL of responses written only after native submission succeeds with Retry=0 and ScriptErrors=0. Current AI source, policy, and engine fingerprints identify the tested implementation. The planning watchdog is an operational stop; measured compute/planning timings still need the separate 100/300/1000 ms adoption gates and full fixed-seed performance benchmark.

The versioned fixtures reproduce known decision points on another PC. Use the full fixed-seed benchmark for overall win rates and runtime performance. Historical captures remain under `Logs/AiBenchmark`.

To compare a pinned previous AI and the candidate in the same Unity process, use an interleaved run. Both play against immutable Baseline-v1. Execution order alternates by fixture and seat.

```text
python Tools/benchmark_duel_ai.py paired-smoke --fixture 10:0 --paired-previous-revision REV
python Tools/benchmark_duel_ai.py paired-screen --full-suite --seeds 3 --stage screen --paired-previous-revision REV --capture-replays
python Tools/benchmark_duel_ai.py paired-confirm --full-suite --seeds 10 --stage confirm --paired-previous-revision REV --screen-run Logs/AiBenchmark/paired-screen --regression-run Logs/AiBenchmark/fixed-scenarios --random-floor-run Logs/AiBenchmark/random-floor
```

Replace `REV` with the previous Git revision. For an A/A run, omit `--stage` and use `--candidate-revision REV` as well. The previous arm writes reports under `<run>/previous`, which can also be supplied to `--previous-run`. Confirmation checks both source and policy hashes against the passed screen before launching. The paired option applies to duel suites and single fixtures.
