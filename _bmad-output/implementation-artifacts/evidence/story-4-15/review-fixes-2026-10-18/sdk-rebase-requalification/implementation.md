# Source Debug requalification at the pushed SDK gitlink

Review decision 2026-10-18 (post-reopen delta review), option 1. After the review target `89811c7` pinned verified SDK `b9e2d2aa` (`77d9af18` plus the create-only receipt-conflict correction), `/pushall` rebased the correction to `216b7906` on `37bdc90a` and rewrote Works as `fbcacfb`, followed by `ac3771e`. The correction patch is identical (`git patch-id --stable`), but `37bdc90a` adds 14 upstream SDK commits, so source Debug at Works HEAD needed its own run. Scope rule: a failure caused by the upstream SDK commits would be recorded as a Story 4.16 input, not debugged in 4.15. No lane failed.

## Bound source

[heads.json](heads.json) records Works HEAD `ac3771ec9ae950fa3b86644279b6bc3d95110d9b`, SDK gitlink and clean checkout `216b790628574d39802adcc8f4fea0d4034ef9f0` (`v3.124.0-18-g216b7906`), and every root submodule at its HEAD gitlink, including the Parties and Platform pointers moved by `ac3771e`. The tested `Hexalith.EventStore.Client.dll` carries informational version `3.124.0+216b790628574d39802adcc8f4fea0d4034ef9f0` and SHA-256 `56dcec13aa070fe10ea9398d78c1d8238981612543f778dd20f28ccb54909f10`. No Client source file is newer than it. All eight restart host starts and both callback host starts (works and eventstore) loaded that exact assembly; see `raw/debug-restart-bindings.json.gz` and `raw/debug-callback-bindings.json.gz`. [debug-binaries.json](debug-binaries.json) hashes every Hexalith assembly in the test output.

The verified pre-rebase commit stays fetchable as EventStore tag `archive/works-fbcacfb` -> `b9e2d2aae484a00b938745f9d7d15e8ae5626550` (pushed to `origin`). Its tree is also reproducible from `origin/main` ancestor `77d9af18` plus `216b7906`'s patch.

## Results

[run.py](run.py) is the Debug-only form of the retained `root-closeout/raw/run.py.gz` wrapper, with the same commands, environment, approved Dapr configuration copy and temporary watch-limit adjustment. [results.json](results.json) records every command, exit and xUnit summary.

| Lane | Result |
| --- | --- |
| Debug builds (integration, unit, architecture, property) | exit 0, 0 warnings, 0 errors |
| Integration (`-class- *SmokeTests -class- *Platform*`) | 848/848, 0 failed/errors/skipped/not-run |
| Live restart (`*SdkRemindersAndCascadeConvergeThroughPersistedReceiptsAcrossRestart`) | 1/1, 488.993 s |
| Callback (`*SuccessfulCallbacksRegisterSdkReminderAndProcessWithoutPeriodicRecovery`) | 1/1, 35.651 s |
| Unit / Architecture / Property | 589 / 269 / 3, all passing |

The restart runtime trace passes the reviewed `verify_restart` assertions from `../verify-evidence.py` (all ten persisted phases). The callback end state holds one `Armed` witness entry with a process candidate. Cleanup restored `fs.inotify.max_user_watches` to 1048576, `aspire ps` is empty, and only the base Dapr containers remain ([cleanup.json](cleanup.json)).

The wrapper's final compression step first failed with `TypeError: open() got an unexpected keyword argument 'mtime'` after every lane and the cleanup had completed. The call was corrected to `gzip.GzipFile`, and only the compression was rerun. [raw-artifacts.json](raw-artifacts.json) indexes the uncompressed size and SHA-256 of each raw file.

Candidate Release evidence is unaffected: the immutable `3.124.1-story415.20261018.1` packages were built from `77d9af18` plus the correction. Publication and requalification of the default public 3.124.0 pin remain separate gates.
