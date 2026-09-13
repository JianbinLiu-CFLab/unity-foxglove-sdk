# ADDITIVE CHECKPOINT 2026-09-13 — FINAL I-SERIES CLOSURE REFS

Status: `I_SERIES_COMPLETE / PR332_MERGED / PR333_MERGED / PR334_MERGED / MAIN_SYNCED / NO_SHUTDOWN`

- Final local `main`, `HEAD`, and `origin/main`: `d130cda2374d5c8b60dbebc4297310b93d115fe3` after PR #334. PR #332 merge `8a8bc7e79af95a0621d693d613d03ac996a3b260`; PR #333 merge `da36efafd976d000eacd5de5f96589d0ef028868`; PR #334 merge `d130cda2374d5c8b60dbebc4297310b93d115fe3`. Remote heads contain only `refs/heads/main`; all I correction branches were deleted after ancestry verification.
- The complete prior AGENTS content is preserved below as the exact suffix.

# ADDITIVE TERMINAL CORRECTION 2026-09-12 — I QUARTET PATH RECONCILIATION

- The terminal checkpoint was reopened and corrected to distinguish the shared transaction directory `build/phase187-round4/r4.1-real-environment-revalidation/i-series/behavioral-repair-20260912/` from the direct I09-009 quartet directory `build/phase187-round4/r4.1-real-environment-revalidation/i-series/I09-009-transaction/`. Both retain `MODIFIED_FILE`, `DIFF_FILE`, `VERIFICATION.txt`, and executable `ROLLBACK.sh` with the recorded rollback outputs.
- Corrected checkpoint SHA-256: `3f90ae334ebe5d47b5ff6263a494314b1f6d53ade821dbbc85c93a920cd479ac` (`build/phase187-round4/r4.1-real-environment-revalidation/i-series/CHECKPOINT_I_SERIES_FINAL_20260912.md`).

AGENTS_PREVIOUS_SHA256=d49282fc2a6fe75c63fe75982511dfaa0445ddd4a9e2f99daa1c0908acab2abe

# ADDITIVE TERMINAL CHECKPOINT 2026-09-12 — R4.1 I SERIES COMPLETE / PR331 MERGED / MAIN SYNCED

Status: `I_SERIES_COMPLETE / THREE_ROLE_AUDIT_PASS / FULL_CI_PASSED / PR331_MERGED / MAIN_SYNCED / I_BRANCH_CLEAN / NO_SHUTDOWN`

- Live canonical I queue remains 264 rows, selected_order 189..452. R4.1 overlay is terminal: REFUTED=230, SATISFIED_BY_ROUND3=13, SATISFIED_BY_ROUND4=10, CONFIRMED=10, CONFIRMED_FIXED=1, PENDING=0. Overlay: `build/phase187-round4/r4.1-real-environment-revalidation/i-series/R4.1-I_REAL_ENV_REVALIDATION_OPENING_OVERLAY.tsv`; SHA-256 `d82aaf1bca71773d5564796d2b04b860766dcadbde4dd283389d53aa97563a23`. Queue SHA-256 `7200f81b1cabd8d5fc4196128fa749535f398b5dccd2b8a29384a1996486e891`; execution-ledger SHA-256 `0a943ba4324074ff154dee55afbb3a6183a949d9b96613d39bf66524b57aaa30`.
- Final checkpoint: `build/phase187-round4/r4.1-real-environment-revalidation/i-series/CHECKPOINT_I_SERIES_FINAL_20260912.md`; SHA-256 `041a2a38ed1190e5a893c57996ea88a81706d6391c0099bc3fec5bf0c6a84733`. It records the complete serial commit list, disposition counts, all transaction quartet paths, rollback results, CI, remote checks, merge, refs, and preserved dirty files.
- Exactly three final review roles were run and adjudicated PASS: mutation/adversarial report SHA-256 `28f7867d7b1134d4688db3c787fce614f7cbdc79295dd31efe6ace42ae8bb795`; correctness/runtime `d118c01ede644709b8066b4ec36fb14c4dab44c59a85b9a29fca7171d7e895f2`; scope/evidence `c2cd81bf78de06004428c5082f7b275831d9fbf4f17bddd0f488931c079a8938`.
- Serial source branch `feature/round4/i-series-real-environment-revalidation` ended at `51c0fb3dd0dbb02c616952cca76158465589603a`; PR #331 checks all passed (docs, check, test, analyzer-freshness, optional ROS2 Native, optional ROS2 adapter). Normal merge SHA `9f3620526fb5acdbc70e85b85c400fd39e080ba4`; PR: `https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk/pull/331`.
- Full local CI: `python -B Scripts/release/run_ci.py`; log `build/phase187-round4/r4.1-real-environment-revalidation/i-series/final-ci-20260912-v4.log`; runner exit `0`, literal `All CI checks passed.`, all 13 jobs passed. The final CI log SHA-256 is `ba36ff5d629890d3625461b15dfc7078018a3386abfe7838d8350348c629a0d3`.
- Final refs: local `main`, `HEAD == main == origin/main == 9f3620526fb5acdbc70e85b85c400fd39e080ba4`, tree `951a233dbe3cbb50ed2a47a7689ac84791bdfc78`; `git ls-remote --heads origin` contains only `refs/heads/main`. Merged local/remote I branches were deleted after ancestry verification.
- Protected pre-existing dirty files are preserved unchanged: `Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs` SHA `de447dc56ade5b1ceecc59fc7b15c7c07d05b850fff8e2202d0f603b9617e759`, `Unity2Foxglove/Packages/manifest.json` SHA `db9983a07e81f1f8087d76528f9f6d8a14c991147dd075b683d91ad01f0aec15`, `Unity2Foxglove/Packages/packages-lock.json` SHA `a09ac8fdb69e0eb82a94fcee975b2249f140ca34baabef8a556b02652e8f7b5c`. `git ls-files build` remains empty; historical worktrees/evidence remain preserved. No reset, stash, clean, broad deletion, or shutdown was performed.

AGENTS_PREVIOUS_SHA256=27dac99368bd0712193d1e961cde064a97f58a5cf015a202d8772b88b1e6e4bb

# ADDITIVE CORRECTION 2026-09-12 — I SERIES STILL IN PROGRESS / REAL BEHAVIORAL EVIDENCE

Status: `I_SERIES_IN_PROGRESS_NOT_COMPLETE / NO_PUSH / NO_SHUTDOWN`

- Live I queue remains 264 rows, selected_order 189..452. Do not mistake H completion or I GR-02 checks for full I completion.
- Twelve early overlay closures were reopened because their previous RED/GREEN/controls were source-string checks and diff --check was mislabeled independent review. Historical artifacts and commits remain preserved.
- Current serial branch: feature/round4/i-series-real-environment-revalidation; HEAD 3104a5a945872d3a207229be625452b9144280ce. This window committed 3864d630fc58e32a20453e49a0a113dc657087c3, 23a738c71dfc42969e21d81b0e62c84e0b7a134c, 3b53694bfc0bac360ca8f692b0bc3af577e81fa7, bec48fef812b5d82be146034244eac6863c45723, 1135698754c3988eb93a2c9bb9878056b1c5d4e2, 2292351be4de0072fbaa2f44e1198162f2e7ebf8, 99869c8575dcaeaba1b11351757b114404445f22, 47c792339529a2b1020b58d2264f49afbb665353, b1369430db4d54adc11bc302a524cfe9bc181f03, 09a490308145d6dce08f839683fd7cd7e45419e0, 92eca77b846ae97ec8ea2edc90e9804611a95af6, 38c894a039dd13ef73dabca6f0f763416b1cebe5, cdb10073f3330d6c0d2d098af9651d371d0de7da, 594a452a848c62e38aca29773b8536a713ebf3f5, f84c502d8b2f8e4f4c7c97d31f1e2ba6e3779a14, 09523c28aeba1001f7acd172807cf4e5ae6daf23, 3104a5a945872d3a207229be625452b9144280ce. Main/origin/main remain a223fb968c441dcb24821befb94ad38a15be7585; no push or merge.
- Log repair: five genuine baseline failures -> focused GREEN; Windows owner 49 tests passed (2 POSIX skips), WSL owner 49 tests passed after a separately recorded DrvFs fixture correction. Restoring the original source returns the same five failing assertions. Both transactions returned ROLLBACK_OK then ROLLBACK_NOOP and all three byte-preservation flags were true.
- Real Unity 6000.3.14f1 exposed a production CS1061 missing System.Linq import in FoxgloveManager.FoxRunTransportProviders.cs. The one-import fix passed 31 focused/owner tests; the independent source rollback returned CS1061 again. Quartet: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\unity-i01-entry\LINQ_IMPORT_TRANSACTION.
- The isolated 2601-file SDK snapshot and real Editor now execute a native IMGUI matrix: publisher raw values -1,999,0,1,2,3 are preserved on passive repaint after the helper fix; Manager serialized and dirty state remain unchanged; cleanup true, Editor exit 0. Native window events in batchmode had zero draws; those failed harness attempts are retained, not counted green.
- Overlay now records 11 confirmed local repairs (10 CONFIRMED plus I09-009 CONFIRMED_FIXED), 10 SATISFIED_BY_ROUND4 behavioral repairs, 230 REFUTED probes, and 13 inherited Round3 statuses; all 264 I rows are terminal. Final mutation/correctness/scope reviews passed, and complete local CI exited 0 with `All CI checks passed.`; remote integration remains pending.
- Full machine-readable checkpoint, exact commands, outputs, exits, hashes and quartet paths: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\behavioral-repair-20260912\CHECKPOINT_I_R41_BEHAVIORAL_PROGRESS_20260912.json; SHA256=00b7b4b7db1226368196ac7efb76117f6706be59d3d16fc6c4ce244cd82c5bdc.
- Next: push the final serial I branch once, wait for all remote checks, merge normally, sync main and clean only the merged I branch. Preserve the three Unity dirty files, canonical queue/ledger, ROS2/R2FU Junctions, old worktrees and evidence. No reset/stash/clean/broad cleanup. All new scratch stays under i-series. Cancelled shutdown remains cancelled.

AGENTS_PREVIOUS_SHA256=73ca73fd9fa3f998f4c5d011330b951bd6287637ef31ed2ecf9e3a3d5deda222

# ADDITIVE CORRECTION 2026-09-12 — I SERIES STILL IN PROGRESS / REAL BEHAVIORAL EVIDENCE

Status: `I_SERIES_IN_PROGRESS_NOT_COMPLETE / NO_PUSH / NO_SHUTDOWN`

- Live I queue remains 264 rows, selected_order 189..452. Do not mistake H completion or I GR-02 checks for full I completion.
- Twelve early overlay closures were reopened because their previous RED/GREEN/controls were source-string checks and diff --check was mislabeled independent review. Historical artifacts and commits remain preserved.
- Current serial branch: feature/round4/i-series-real-environment-revalidation; HEAD 09523c28aeba1001f7acd172807cf4e5ae6daf23. This window committed 3864d630fc58e32a20453e49a0a113dc657087c3, 23a738c71dfc42969e21d81b0e62c84e0b7a134c, 3b53694bfc0bac360ca8f692b0bc3af577e81fa7, bec48fef812b5d82be146034244eac6863c45723, 1135698754c3988eb93a2c9bb9878056b1c5d4e2, 2292351be4de0072fbaa2f44e1198162f2e7ebf8, 99869c8575dcaeaba1b11351757b114404445f22, 47c792339529a2b1020b58d2264f49afbb665353, b1369430db4d54adc11bc302a524cfe9bc181f03, 09a490308145d6dce08f839683fd7cd7e45419e0, 92eca77b846ae97ec8ea2edc90e9804611a95af6, 38c894a039dd13ef73dabca6f0f763416b1cebe5, cdb10073f3330d6c0d2d098af9651d371d0de7da, 594a452a848c62e38aca29773b8536a713ebf3f5, f84c502d8b2f8e4f4c7c97d31f1e2ba6e3779a14, 09523c28aeba1001f7acd172807cf4e5ae6daf23. Main/origin/main remain a223fb968c441dcb24821befb94ad38a15be7585; no push or merge.
- Log repair: five genuine baseline failures -> focused GREEN; Windows owner 49 tests passed (2 POSIX skips), WSL owner 49 tests passed after a separately recorded DrvFs fixture correction. Restoring the original source returns the same five failing assertions. Both transactions returned ROLLBACK_OK then ROLLBACK_NOOP and all three byte-preservation flags were true.
- Real Unity 6000.3.14f1 exposed a production CS1061 missing System.Linq import in FoxgloveManager.FoxRunTransportProviders.cs. The one-import fix passed 31 focused/owner tests; the independent source rollback returned CS1061 again. Quartet: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\unity-i01-entry\LINQ_IMPORT_TRANSACTION.
- The isolated 2601-file SDK snapshot and real Editor now execute a native IMGUI matrix: publisher raw values -1,999,0,1,2,3 are preserved on passive repaint after the helper fix; Manager serialized and dirty state remain unchanged; cleanup true, Editor exit 0. Native window events in batchmode had zero draws; those failed harness attempts are retained, not counted green.
- Overlay now records 11 confirmed local repairs (10 CONFIRMED plus I09-009 CONFIRMED_FIXED), 10 SATISFIED_BY_ROUND4 behavioral repairs, 230 REFUTED probes, and 13 inherited Round3 statuses; all 264 I rows are terminal. Final three-role review is running; full local CI and remote integration remain pending.
- Full machine-readable checkpoint, exact commands, outputs, exits, hashes and quartet paths: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\behavioral-repair-20260912\CHECKPOINT_I_R41_BEHAVIORAL_PROGRESS_20260912.json; SHA256=6853065c16a122d3d6770345be0b563dde277c416748532d3df87d792a7ab786.
- Next: adjudicate final three-role reports, run complete CI, then push/merge/sync and clean only the merged I branch. Preserve the three Unity dirty files, canonical queue/ledger, ROS2/R2FU Junctions, old worktrees and evidence. No reset/stash/clean/broad cleanup. All new scratch stays under i-series. Cancelled shutdown remains cancelled.

AGENTS_PREVIOUS_SHA256=8490f1df7daadf8479b39f39e8d14a831f515d0a949938681201c517fc3343e2

# ADDITIVE CORRECTION 2026-09-12 — I SERIES STILL IN PROGRESS / REAL BEHAVIORAL EVIDENCE

Status: `I_SERIES_IN_PROGRESS_NOT_COMPLETE / NO_PUSH / NO_SHUTDOWN`

- Live I queue remains 264 rows, selected_order 189..452. Do not mistake H completion or I GR-02 checks for full I completion.
- Twelve early overlay closures were reopened because their previous RED/GREEN/controls were source-string checks and diff --check was mislabeled independent review. Historical artifacts and commits remain preserved.
- Current serial branch: feature/round4/i-series-real-environment-revalidation; HEAD 09523c28aeba1001f7acd172807cf4e5ae6daf23. This window committed 3864d630fc58e32a20453e49a0a113dc657087c3, 23a738c71dfc42969e21d81b0e62c84e0b7a134c, 3b53694bfc0bac360ca8f692b0bc3af577e81fa7, bec48fef812b5d82be146034244eac6863c45723, 1135698754c3988eb93a2c9bb9878056b1c5d4e2, 2292351be4de0072fbaa2f44e1198162f2e7ebf8, 99869c8575dcaeaba1b11351757b114404445f22, 47c792339529a2b1020b58d2264f49afbb665353, b1369430db4d54adc11bc302a524cfe9bc181f03, 09a490308145d6dce08f839683fd7cd7e45419e0, 92eca77b846ae97ec8ea2edc90e9804611a95af6, 38c894a039dd13ef73dabca6f0f763416b1cebe5, cdb10073f3330d6c0d2d098af9651d371d0de7da, 594a452a848c62e38aca29773b8536a713ebf3f5, f84c502d8b2f8e4f4c7c97d31f1e2ba6e3779a14, 09523c28aeba1001f7acd172807cf4e5ae6daf23. Main/origin/main remain a223fb968c441dcb24821befb94ad38a15be7585; no push or merge.
- Log repair: five genuine baseline failures -> focused GREEN; Windows owner 49 tests passed (2 POSIX skips), WSL owner 49 tests passed after a separately recorded DrvFs fixture correction. Restoring the original source returns the same five failing assertions. Both transactions returned ROLLBACK_OK then ROLLBACK_NOOP and all three byte-preservation flags were true.
- Real Unity 6000.3.14f1 exposed a production CS1061 missing System.Linq import in FoxgloveManager.FoxRunTransportProviders.cs. The one-import fix passed 31 focused/owner tests; the independent source rollback returned CS1061 again. Quartet: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\unity-i01-entry\LINQ_IMPORT_TRANSACTION.
- The isolated 2601-file SDK snapshot and real Editor now execute a native IMGUI matrix: publisher raw values -1,999,0,1,2,3 are preserved on passive repaint after the helper fix; Manager serialized and dirty state remain unchanged; cleanup true, Editor exit 0. Native window events in batchmode had zero draws; those failed harness attempts are retained, not counted green.
- Overlay now records 10 CONFIRMED local repairs (including I02-005/I02-006 workspace path authority), 10 SATISFIED_BY_ROUND4 behavioral repairs (I01 multi-object/security and I02 process/discovery seams), 3 REFUTED probes, 228 pending revalidations, and 13 inherited Round3 statuses. Independent final reviews, full local CI and remote integration remain NOT_RUN.
- Full machine-readable checkpoint, exact commands, outputs, exits, hashes and quartet paths: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\behavioral-repair-20260912\CHECKPOINT_I_R41_BEHAVIORAL_PROGRESS_20260912.json; SHA256=7330acab4cac13f8e9f69e5729204211d052b9fbde6287d81ce5b82703f8407f.
- Next: expand I01-001 and remaining I01 P1 real Editor matrices, then continue live queue. Preserve the three Unity dirty files, canonical queue/ledger, ROS2/R2FU Junctions, old worktrees and evidence. No reset/stash/clean/broad cleanup. All new scratch stays under i-series. Cancelled shutdown remains cancelled.

AGENTS_PREVIOUS_SHA256=b16e31afdddd849ba6a9bea7860e680e69a5960d472b1ec595439c6dea6a4fa1

# ADDITIVE CORRECTION 2026-09-12 — I SERIES STILL IN PROGRESS / REAL BEHAVIORAL EVIDENCE

Status: `I_SERIES_IN_PROGRESS_NOT_COMPLETE / NO_PUSH / NO_SHUTDOWN`

- Live I queue remains 264 rows, selected_order 189..452. Do not mistake H completion or I GR-02 checks for full I completion.
- Twelve early overlay closures were reopened because their previous RED/GREEN/controls were source-string checks and diff --check was mislabeled independent review. Historical artifacts and commits remain preserved.
- Current serial branch: feature/round4/i-series-real-environment-revalidation; HEAD 92eca77b846ae97ec8ea2edc90e9804611a95af6. This window committed 3864d630fc58e32a20453e49a0a113dc657087c3, 23a738c71dfc42969e21d81b0e62c84e0b7a134c, 3b53694bfc0bac360ca8f692b0bc3af577e81fa7, bec48fef812b5d82be146034244eac6863c45723, 1135698754c3988eb93a2c9bb9878056b1c5d4e2, 2292351be4de0072fbaa2f44e1198162f2e7ebf8, 99869c8575dcaeaba1b11351757b114404445f22, 47c792339529a2b1020b58d2264f49afbb665353, b1369430db4d54adc11bc302a524cfe9bc181f03, 09a490308145d6dce08f839683fd7cd7e45419e0, 92eca77b846ae97ec8ea2edc90e9804611a95af6, 38c894a039dd13ef73dabca6f0f763416b1cebe5, cdb10073f3330d6c0d2d098af9651d371d0de7da, 594a452a848c62e38aca29773b8536a713ebf3f5, f84c502d8b2f8e4f4c7c97d31f1e2ba6e3779a14. Main/origin/main remain a223fb968c441dcb24821befb94ad38a15be7585; no push or merge.
- Log repair: five genuine baseline failures -> focused GREEN; Windows owner 49 tests passed (2 POSIX skips), WSL owner 49 tests passed after a separately recorded DrvFs fixture correction. Restoring the original source returns the same five failing assertions. Both transactions returned ROLLBACK_OK then ROLLBACK_NOOP and all three byte-preservation flags were true.
- Real Unity 6000.3.14f1 exposed a production CS1061 missing System.Linq import in FoxgloveManager.FoxRunTransportProviders.cs. The one-import fix passed 31 focused/owner tests; the independent source rollback returned CS1061 again. Quartet: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\unity-i01-entry\LINQ_IMPORT_TRANSACTION.
- The isolated 2601-file SDK snapshot and real Editor now execute a native IMGUI matrix: publisher raw values -1,999,0,1,2,3 are preserved on passive repaint after the helper fix; Manager serialized and dirty state remain unchanged; cleanup true, Editor exit 0. Native window events in batchmode had zero draws; those failed harness attempts are retained, not counted green.
- Overlay now records 10 CONFIRMED local repairs (including I02-005/I02-006 workspace path authority), 10 SATISFIED_BY_ROUND4 behavioral repairs (I01 multi-object/security and I02 process/discovery seams), 2 REFUTED probes, 229 pending revalidations, and 13 inherited Round3 statuses. Independent final reviews, full local CI and remote integration remain NOT_RUN.
- Full machine-readable checkpoint, exact commands, outputs, exits, hashes and quartet paths: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\behavioral-repair-20260912\CHECKPOINT_I_R41_BEHAVIORAL_PROGRESS_20260912.json; SHA256=c93c7812b89d5153c8f38d5d81219b43f289b943b33b25c5bf52ad0cb739d01e.
- Next: expand I01-001 and remaining I01 P1 real Editor matrices, then continue live queue. Preserve the three Unity dirty files, canonical queue/ledger, ROS2/R2FU Junctions, old worktrees and evidence. No reset/stash/clean/broad cleanup. All new scratch stays under i-series. Cancelled shutdown remains cancelled.

AGENTS_PREVIOUS_SHA256=c4f047618ef297f016089a4e3698aeb24796b2869419bf62c21492789a0d1d9d

# ADDITIVE CORRECTION 2026-09-12 — I SERIES STILL IN PROGRESS / REAL BEHAVIORAL EVIDENCE

Status: `I_SERIES_IN_PROGRESS_NOT_COMPLETE / NO_PUSH / NO_SHUTDOWN`

- Live I queue remains 264 rows, selected_order 189..452. Do not mistake H completion or I GR-02 checks for full I completion.
- Twelve early overlay closures were reopened because their previous RED/GREEN/controls were source-string checks and diff --check was mislabeled independent review. Historical artifacts and commits remain preserved.
- Current serial branch: feature/round4/i-series-real-environment-revalidation; HEAD 92eca77b846ae97ec8ea2edc90e9804611a95af6. This window committed 3864d630fc58e32a20453e49a0a113dc657087c3, 23a738c71dfc42969e21d81b0e62c84e0b7a134c, 3b53694bfc0bac360ca8f692b0bc3af577e81fa7, bec48fef812b5d82be146034244eac6863c45723, 1135698754c3988eb93a2c9bb9878056b1c5d4e2, 2292351be4de0072fbaa2f44e1198162f2e7ebf8, 99869c8575dcaeaba1b11351757b114404445f22, 47c792339529a2b1020b58d2264f49afbb665353, b1369430db4d54adc11bc302a524cfe9bc181f03, 09a490308145d6dce08f839683fd7cd7e45419e0, 92eca77b846ae97ec8ea2edc90e9804611a95af6, 38c894a039dd13ef73dabca6f0f763416b1cebe5, cdb10073f3330d6c0d2d098af9651d371d0de7da, 594a452a848c62e38aca29773b8536a713ebf3f5, f84c502d8b2f8e4f4c7c97d31f1e2ba6e3779a14. Main/origin/main remain a223fb968c441dcb24821befb94ad38a15be7585; no push or merge.
- Log repair: five genuine baseline failures -> focused GREEN; Windows owner 49 tests passed (2 POSIX skips), WSL owner 49 tests passed after a separately recorded DrvFs fixture correction. Restoring the original source returns the same five failing assertions. Both transactions returned ROLLBACK_OK then ROLLBACK_NOOP and all three byte-preservation flags were true.
- Real Unity 6000.3.14f1 exposed a production CS1061 missing System.Linq import in FoxgloveManager.FoxRunTransportProviders.cs. The one-import fix passed 31 focused/owner tests; the independent source rollback returned CS1061 again. Quartet: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\unity-i01-entry\LINQ_IMPORT_TRANSACTION.
- The isolated 2601-file SDK snapshot and real Editor now execute a native IMGUI matrix: publisher raw values -1,999,0,1,2,3 are preserved on passive repaint after the helper fix; Manager serialized and dirty state remain unchanged; cleanup true, Editor exit 0. Native window events in batchmode had zero draws; those failed harness attempts are retained, not counted green.
- Overlay now records 10 CONFIRMED local repairs (including I02-005/I02-006 workspace path authority), 6 SATISFIED_BY_ROUND4 manager/security repairs (I01-006/I01-008/I01-015/I01-016 plus inherited), 5 REFUTED probes (including current POSIX SIGTERM ownership), 230 pending revalidations, and 13 inherited Round3 statuses. Independent final reviews, full local CI and remote integration remain NOT_RUN.
- Full machine-readable checkpoint, exact commands, outputs, exits, hashes and quartet paths: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\behavioral-repair-20260912\CHECKPOINT_I_R41_BEHAVIORAL_PROGRESS_20260912.json; SHA256=9f18582b40cb8618cf5758f7ce453e4491fa8e92be882b036a46704f5ed626d9.
- Next: expand I01-001 and remaining I01 P1 real Editor matrices, then continue live queue. Preserve the three Unity dirty files, canonical queue/ledger, ROS2/R2FU Junctions, old worktrees and evidence. No reset/stash/clean/broad cleanup. All new scratch stays under i-series. Cancelled shutdown remains cancelled.

AGENTS_PREVIOUS_SHA256=82ab23d5f302af82a39e12d5f04f8a89692ad4dba0ab83e1c65f8c1615eaf5e6

# ADDITIVE CORRECTION 2026-09-12 — I SERIES STILL IN PROGRESS / REAL BEHAVIORAL EVIDENCE

Status: `I_SERIES_IN_PROGRESS_NOT_COMPLETE / NO_PUSH / NO_SHUTDOWN`

- Live I queue remains 264 rows, selected_order 189..452. Do not mistake H completion or I GR-02 checks for full I completion.
- Twelve early overlay closures were reopened because their previous RED/GREEN/controls were source-string checks and diff --check was mislabeled independent review. Historical artifacts and commits remain preserved.
- Current serial branch: feature/round4/i-series-real-environment-revalidation; HEAD 09a490308145d6dce08f839683fd7cd7e45419e0. This window committed 3864d630fc58e32a20453e49a0a113dc657087c3, 23a738c71dfc42969e21d81b0e62c84e0b7a134c, 3b53694bfc0bac360ca8f692b0bc3af577e81fa7, bec48fef812b5d82be146034244eac6863c45723, 1135698754c3988eb93a2c9bb9878056b1c5d4e2, 2292351be4de0072fbaa2f44e1198162f2e7ebf8, 99869c8575dcaeaba1b11351757b114404445f22, 47c792339529a2b1020b58d2264f49afbb665353, b1369430db4d54adc11bc302a524cfe9bc181f03, 09a490308145d6dce08f839683fd7cd7e45419e0. Main/origin/main remain a223fb968c441dcb24821befb94ad38a15be7585; no push or merge.
- Log repair: five genuine baseline failures -> focused GREEN; Windows owner 49 tests passed (2 POSIX skips), WSL owner 49 tests passed after a separately recorded DrvFs fixture correction. Restoring the original source returns the same five failing assertions. Both transactions returned ROLLBACK_OK then ROLLBACK_NOOP and all three byte-preservation flags were true.
- Real Unity 6000.3.14f1 exposed a production CS1061 missing System.Linq import in FoxgloveManager.FoxRunTransportProviders.cs. The one-import fix passed 31 focused/owner tests; the independent source rollback returned CS1061 again. Quartet: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\unity-i01-entry\LINQ_IMPORT_TRANSACTION.
- The isolated 2601-file SDK snapshot and real Editor now execute a native IMGUI matrix: publisher raw values -1,999,0,1,2,3 are preserved on passive repaint after the helper fix; Manager serialized and dirty state remain unchanged; cleanup true, Editor exit 0. Native window events in batchmode had zero draws; those failed harness attempts are retained, not counted green.
- Overlay now records 8 CONFIRMED local repairs (I01-002 publisher encoding; I02-008/I02-016 log paths; I02-017 interrupted Windows acquisition; I02-012 bounded interval; I02-011 Hub version validation; I02-018 POSIX escaped-descendant ownership), 4 REFUTED probes (metadata FIFO, I02-015 acquisition interrupt, I02-019 stale PGID, plus prior), plus pending and inherited statuses. Independent final reviews, full local CI and remote integration remain NOT_RUN.
- Full machine-readable checkpoint, exact commands, outputs, exits, hashes and quartet paths: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\behavioral-repair-20260912\CHECKPOINT_I_R41_BEHAVIORAL_PROGRESS_20260912.json; SHA256=ade8370bea45438002ce6a0c351744526635c709cf604a9dda0a16e7ae3a2cdc.
- Next: expand I01-001 and remaining I01 P1 real Editor matrices, then continue live queue. Preserve the three Unity dirty files, canonical queue/ledger, ROS2/R2FU Junctions, old worktrees and evidence. No reset/stash/clean/broad cleanup. All new scratch stays under i-series. Cancelled shutdown remains cancelled.

AGENTS_PREVIOUS_SHA256=a6aae6d3fda02a6ec817531f7e541020aaac895c4314b55d8ac6a77bb2271d5a

# ADDITIVE CORRECTION 2026-09-12 — I SERIES STILL IN PROGRESS / REAL BEHAVIORAL EVIDENCE

Status: `I_SERIES_IN_PROGRESS_NOT_COMPLETE / NO_PUSH / NO_SHUTDOWN`

- Live I queue remains 264 rows, selected_order 189..452. Do not mistake H completion or I GR-02 checks for full I completion.
- Twelve early overlay closures were reopened because their previous RED/GREEN/controls were source-string checks and diff --check was mislabeled independent review. Historical artifacts and commits remain preserved.
- Current serial branch: feature/round4/i-series-real-environment-revalidation; HEAD b1369430db4d54adc11bc302a524cfe9bc181f03. This window committed 3864d630fc58e32a20453e49a0a113dc657087c3, 23a738c71dfc42969e21d81b0e62c84e0b7a134c, 3b53694bfc0bac360ca8f692b0bc3af577e81fa7, bec48fef812b5d82be146034244eac6863c45723, 1135698754c3988eb93a2c9bb9878056b1c5d4e2, 2292351be4de0072fbaa2f44e1198162f2e7ebf8, 99869c8575dcaeaba1b11351757b114404445f22, 47c792339529a2b1020b58d2264f49afbb665353, b1369430db4d54adc11bc302a524cfe9bc181f03. Main/origin/main remain a223fb968c441dcb24821befb94ad38a15be7585; no push or merge.
- Log repair: five genuine baseline failures -> focused GREEN; Windows owner 49 tests passed (2 POSIX skips), WSL owner 49 tests passed after a separately recorded DrvFs fixture correction. Restoring the original source returns the same five failing assertions. Both transactions returned ROLLBACK_OK then ROLLBACK_NOOP and all three byte-preservation flags were true.
- Real Unity 6000.3.14f1 exposed a production CS1061 missing System.Linq import in FoxgloveManager.FoxRunTransportProviders.cs. The one-import fix passed 31 focused/owner tests; the independent source rollback returned CS1061 again. Quartet: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\unity-i01-entry\LINQ_IMPORT_TRANSACTION.
- The isolated 2601-file SDK snapshot and real Editor now execute a native IMGUI matrix: publisher raw values -1,999,0,1,2,3 are preserved on passive repaint after the helper fix; Manager serialized and dirty state remain unchanged; cleanup true, Editor exit 0. Native window events in batchmode had zero draws; those failed harness attempts are retained, not counted green.
- Overlay now records 8 CONFIRMED local repairs (I01-002 publisher encoding; I02-008/I02-016 log paths; I02-017 interrupted Windows acquisition; I02-012 bounded interval; I02-011 Hub version validation; I02-018 POSIX escaped-descendant ownership), 4 REFUTED probes (metadata FIFO, I02-015 acquisition interrupt, I02-019 stale PGID, plus prior), plus pending and inherited statuses. Independent final reviews, full local CI and remote integration remain NOT_RUN.
- Full machine-readable checkpoint, exact commands, outputs, exits, hashes and quartet paths: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\behavioral-repair-20260912\CHECKPOINT_I_R41_BEHAVIORAL_PROGRESS_20260912.json; SHA256=446452a5d2ce33e8687618570d94efe4c4b91b8f72348b7e154d378264cd135b.
- Next: expand I01-001 and remaining I01 P1 real Editor matrices, then continue live queue. Preserve the three Unity dirty files, canonical queue/ledger, ROS2/R2FU Junctions, old worktrees and evidence. No reset/stash/clean/broad cleanup. All new scratch stays under i-series. Cancelled shutdown remains cancelled.

AGENTS_PREVIOUS_SHA256=11370e00a8354aa4e0bf79358e8130b52fac3c918a12dbf08aa39248cdeeeca4

# ADDITIVE CORRECTION 2026-09-12 — I SERIES STILL IN PROGRESS / REAL BEHAVIORAL EVIDENCE

Status: `I_SERIES_IN_PROGRESS_NOT_COMPLETE / NO_PUSH / NO_SHUTDOWN`

- Live I queue remains 264 rows, selected_order 189..452. Do not mistake H completion or I GR-02 checks for full I completion.
- Twelve early overlay closures were reopened because their previous RED/GREEN/controls were source-string checks and diff --check was mislabeled independent review. Historical artifacts and commits remain preserved.
- Current serial branch: feature/round4/i-series-real-environment-revalidation; HEAD 47c792339529a2b1020b58d2264f49afbb665353. This window committed 3864d630fc58e32a20453e49a0a113dc657087c3, 23a738c71dfc42969e21d81b0e62c84e0b7a134c, 3b53694bfc0bac360ca8f692b0bc3af577e81fa7, bec48fef812b5d82be146034244eac6863c45723, 1135698754c3988eb93a2c9bb9878056b1c5d4e2, 2292351be4de0072fbaa2f44e1198162f2e7ebf8. Main/origin/main remain a223fb968c441dcb24821befb94ad38a15be7585; no push or merge.
- Log repair: five genuine baseline failures -> focused GREEN; Windows owner 49 tests passed (2 POSIX skips), WSL owner 49 tests passed after a separately recorded DrvFs fixture correction. Restoring the original source returns the same five failing assertions. Both transactions returned ROLLBACK_OK then ROLLBACK_NOOP and all three byte-preservation flags were true.
- Real Unity 6000.3.14f1 exposed a production CS1061 missing System.Linq import in FoxgloveManager.FoxRunTransportProviders.cs. The one-import fix passed 31 focused/owner tests; the independent source rollback returned CS1061 again. Quartet: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\unity-i01-entry\LINQ_IMPORT_TRANSACTION.
- The isolated 2601-file SDK snapshot and real Editor now execute a native IMGUI matrix: publisher raw values -1,999,0,1,2,3 are preserved on passive repaint after the helper fix; Manager serialized and dirty state remain unchanged; cleanup true, Editor exit 0. Native window events in batchmode had zero draws; those failed harness attempts are retained, not counted green.
- Overlay now records 4 CONFIRMED local repairs (I01-002 publisher encoding, I02-008/I02-016 log paths, I02-017 interrupted Windows acquisition), one REFUTED metadata FIFO probe, plus pending and inherited statuses. Independent final reviews, full local CI and remote integration remain NOT_RUN.
- Full machine-readable checkpoint, exact commands, outputs, exits, hashes and quartet paths: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\behavioral-repair-20260912\CHECKPOINT_I_R41_BEHAVIORAL_PROGRESS_20260912.json; SHA256=3e7fd08888599643708e59eda8176e99ca7dd4d22c655f87bfcf812c15b848f3.
- Next: expand I01-001 and remaining I01 P1 real Editor matrices, then continue live queue. Preserve the three Unity dirty files, canonical queue/ledger, ROS2/R2FU Junctions, old worktrees and evidence. No reset/stash/clean/broad cleanup. All new scratch stays under i-series. Cancelled shutdown remains cancelled.

AGENTS_PREVIOUS_SHA256=ae918e8b6f557b305f67d6b3bbcc63ad6260c7519adbe26f5b9855a2fdfa5fb4

# ADDITIVE CORRECTION 2026-09-12 — I SERIES STILL IN PROGRESS / REAL BEHAVIORAL EVIDENCE

Status: `I_SERIES_IN_PROGRESS_NOT_COMPLETE / NO_PUSH / NO_SHUTDOWN`

- Live I queue remains 264 rows, selected_order 189..452. Do not mistake H completion or I GR-02 checks for full I completion.
- Twelve early overlay closures were reopened because their previous RED/GREEN/controls were source-string checks and diff --check was mislabeled independent review. Historical artifacts and commits remain preserved.
- Current serial branch: feature/round4/i-series-real-environment-revalidation; HEAD 2292351be4de0072fbaa2f44e1198162f2e7ebf8. This window committed 3864d630fc58e32a20453e49a0a113dc657087c3, 23a738c71dfc42969e21d81b0e62c84e0b7a134c, 3b53694bfc0bac360ca8f692b0bc3af577e81fa7, bec48fef812b5d82be146034244eac6863c45723, 1135698754c3988eb93a2c9bb9878056b1c5d4e2, 2292351be4de0072fbaa2f44e1198162f2e7ebf8. Main/origin/main remain a223fb968c441dcb24821befb94ad38a15be7585; no push or merge.
- Log repair: five genuine baseline failures -> focused GREEN; Windows owner 49 tests passed (2 POSIX skips), WSL owner 49 tests passed after a separately recorded DrvFs fixture correction. Restoring the original source returns the same five failing assertions. Both transactions returned ROLLBACK_OK then ROLLBACK_NOOP and all three byte-preservation flags were true.
- Real Unity 6000.3.14f1 exposed a production CS1061 missing System.Linq import in FoxgloveManager.FoxRunTransportProviders.cs. The one-import fix passed 31 focused/owner tests; the independent source rollback returned CS1061 again. Quartet: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\unity-i01-entry\LINQ_IMPORT_TRANSACTION.
- The isolated 2601-file SDK snapshot and real Editor now execute a native IMGUI matrix: publisher raw values -1,999,0,1,2,3 are preserved on passive repaint after the helper fix; Manager serialized and dirty state remain unchanged; cleanup true, Editor exit 0. Native window events in batchmode had zero draws; those failed harness attempts are retained, not counted green.
- Overlay now records 4 CONFIRMED local repairs (I01-002 publisher encoding, I02-008/I02-016 log paths, I02-017 interrupted Windows acquisition), one REFUTED metadata FIFO probe, plus pending and inherited statuses. Independent final reviews, full local CI and remote integration remain NOT_RUN.
- Full machine-readable checkpoint, exact commands, outputs, exits, hashes and quartet paths: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\behavioral-repair-20260912\CHECKPOINT_I_R41_BEHAVIORAL_PROGRESS_20260912.json; SHA256=98251dbccf1014ade453be123d6f5119791e1779d639294030552ebd1eca83f6.
- Next: expand I01-001 and remaining I01 P1 real Editor matrices, then continue live queue. Preserve the three Unity dirty files, canonical queue/ledger, ROS2/R2FU Junctions, old worktrees and evidence. No reset/stash/clean/broad cleanup. All new scratch stays under i-series. Cancelled shutdown remains cancelled.

AGENTS_PREVIOUS_SHA256=c6c78e6758ec7eb8dd3363b93a409fe1bae0f26cb2cb46575a80e1b5e55d2590

# ADDITIVE CORRECTION 2026-09-12 — I SERIES STILL IN PROGRESS / REAL BEHAVIORAL EVIDENCE

Status: `I_SERIES_IN_PROGRESS_NOT_COMPLETE / NO_PUSH / NO_SHUTDOWN`

- Live I queue remains 264 rows, selected_order 189..452. Do not mistake H completion or I GR-02 checks for full I completion.
- Twelve early overlay closures were reopened because their previous RED/GREEN/controls were source-string checks and diff --check was mislabeled independent review. Historical artifacts and commits remain preserved.
- Current serial branch: feature/round4/i-series-real-environment-revalidation; HEAD 3b53694bfc0bac360ca8f692b0bc3af577e81fa7. This window committed 3864d630fc58e32a20453e49a0a113dc657087c3, 23a738c71dfc42969e21d81b0e62c84e0b7a134c, 3b53694bfc0bac360ca8f692b0bc3af577e81fa7. Main/origin/main remain a223fb968c441dcb24821befb94ad38a15be7585; no push or merge.
- Log repair: five genuine baseline failures -> focused GREEN; Windows owner 49 tests passed (2 POSIX skips), WSL owner 49 tests passed after a separately recorded DrvFs fixture correction. Restoring the original source returns the same five failing assertions. Both transactions returned ROLLBACK_OK then ROLLBACK_NOOP and all three byte-preservation flags were true.
- Real Unity 6000.3.14f1 exposed a production CS1061 missing System.Linq import in FoxgloveManager.FoxRunTransportProviders.cs. The one-import fix passed 31 focused/owner tests; the independent source rollback returned CS1061 again. Quartet: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\unity-i01-entry\LINQ_IMPORT_TRANSACTION.
- The isolated 2601-file SDK snapshot and real Editor now execute a native IMGUI-container smoke: two Layout/Repaint draws, both Managers' serialized and dirty state unchanged, cleanup true, Editor exit 0. This is an entry smoke only, NOT full I01 or I-series closure. Native window events in batchmode had zero draws; those failed harness attempts are retained, not counted green.
- Overlay currently has 2 CONFIRMED locally repaired log findings plus 245 PENDING_REVALIDATION and 17 inherited terminal statuses awaiting appropriate evidence reuse. Independent final reviews, full local CI and remote integration remain NOT_RUN.
- Full machine-readable checkpoint, exact commands, outputs, exits, hashes and quartet paths: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\build\phase187-round4\r4.1-real-environment-revalidation\i-series\behavioral-repair-20260912\CHECKPOINT_I_R41_BEHAVIORAL_PROGRESS_20260912.json; SHA256=6c61755e2b041e1490a40ef9e196fed027acc67a1b6da03f7a0679eb9a777dbe.
- Next: expand I01-001 and I01-002 real Editor matrices, then continue live queue. Preserve the three Unity dirty files, canonical queue/ledger, ROS2/R2FU Junctions, old worktrees and evidence. No reset/stash/clean/broad cleanup. All new scratch stays under i-series. Cancelled shutdown remains cancelled.

AGENTS_PREVIOUS_SHA256=54bda7eea65d76d71ecb06480f01c21d8fe6ec12b6d26eabbbf7eef4916ae525

# ADDITIVE TERMINAL CORRECTION 2026-09-09 — CONCURRENT WORKTREE DIFF PRESERVED

Status: `ROOT_SCRATCH_ARCHIVE_STILL_VALID / CONCURRENT_CODE_EDIT_PRESERVED`

- The root-scratch relocation completed with the original tracked-diff snapshot `b8eaf541c21a1d4c266e4d8d3423e5eeb81609dcf3ba5e96939b6f69068a1fc5`.
- A separate window subsequently produced an uncommitted edit in `Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/test_origin_suppression.cpp`; the live tracked-diff snapshot now contains four files and has SHA-256 `9115f8093793f6802d4d86fc09c27d46a67356f99529907c276e5e950c6c18de`.
- The cleanup transaction leaves that source file in place and preserves its live snapshot; no staging, commit, revert, or deletion is part of this cleanup.
- `HEAD == main == origin/main == efcf89c2c5122beda7552a6dcf06b8eac241fb59`; the remote tree and remote heads remain unchanged.
- The original validator result is retained as historical evidence; the concurrent-aware final validator uses `CONCURRENT_LIVE_SNAPSHOT.json` and checks the archive, refs, remote tree, and current diff independently.
- Transaction: `build/phase187-round4/cleanup-root-scratch-20260909/transaction/`; see `CONCURRENT_LIVE_SNAPSHOT.json`, `FINAL_ARTIFACT_HASHES.txt`, and the quartet.

AGENTS_PREVIOUS_SHA256=`0347069e8c1eee9478eef3c1dee864b7afd8aeba859e8a0d594d766d85523ac9`

# ADDITIVE TERMINAL CHECKPOINT 2026-09-09 — ROOT SCRATCH ARCHIVE

Status: `ROOT_SCRATCH_ARCHIVE_COMPLETE / REMOTE_MATCH_VERIFIED / ROLLBACK_REHEARSED`

- Moved 265 root-level agent scratch/output files (4409929 bytes) into `build/phase187-round4/cleanup-root-scratch-20260909/archive/`; every moved file retained its original SHA-256.
- Remote comparison before and after cleanup: `HEAD == main == origin/main`; the tracked diff against `origin/main` remained the three pre-existing Unity files only. No remote ref or tracked product byte changed.
- `baseline-worktree` remains a registered historical worktree and was not removed; Unity `.meta` files and the three pre-existing tracked dirty files remain in place.
- Future scratch/output files belong under `build/phase187-round4/scratch/<task-id>/`; root-level `work_*`, ad-hoc logs, probes, and redirect artifacts are prohibited by process convention.
- Transaction: `build/phase187-round4/cleanup-root-scratch-20260909/transaction/`; see `MODIFIED_FILE`, `DIFF_FILE`, `VERIFICATION.txt`, and executable `ROLLBACK.sh`.

AGENTS_PREVIOUS_SHA256=`d0cfc87549613c0eb199901a06efec2448ffd29f74a20c5c0c73cc0f37d1625e`

# ADDITIVE TERMINAL CHECKPOINT 2026-09-09 — H/I RE-AUDIT REPAIRS MERGED / MAIN SYNCED

Status: `R4_HI_REAUDIT_COMPLETE / THREE_ROLE_AUDIT_PASS / FULL_CI_PASSED / PR326_MERGED / MAIN_SYNCED / H_BRANCH_CLEAN`

- Three-role re-audit reports: mutation 3/3 killed (`d06f791987dd2a594a66d2cf82253aabf03de6c9419e5b3ed3846cca6424442d`), correctness PASS after fixes (`56148d3eb5dfe2ff466cf53bb47bf073db234be9988ef3a6c03e322c766cf744`), scope/evidence PASS with stale historical snapshot rebound (`75f0d9679ce8c49f1c4529722a8c53146e269f499a206893876c6e2d835a7641`). I GR-02 remained SATISFIED_BY_ROUND4 with no code change.
- Confirmed defects repaired serially: H02-021 duplicate `--next-revision` guard plus C# local-shadow compile error; H02-008 rollback on preflight and staging failures; H04-025 Jazzy/Lyrical external output handling. Commits: `59d6f8746`, `c36586c76`, `4c7fe224a`, `cd4a81653`, `9551c4c36`, `f3cf826a4`.
- Follow-up overlay: `build/phase187-round4/hi-series-review/H_I_FOLLOWUP_OVERLAY.tsv`; SHA-256 `63b74c7e0f3587060c8964d2765db0d84c4408f1578f5e1e95561bb6f8a2e942`.
- Full local CI command: `python -B Scripts/release/run_ci.py`; run `308684-57e65231`; runner exit `0`; literal `All CI checks passed.`; all 13 jobs passed.
- Remote PR #326: `https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk/pull/326`; checks all passed: test run `34303205667`, check run `34303205716`, docs run `34303205669`; analyzer-freshness, test, optional ROS2 Native, optional ROS2 adapter all success. Merge SHA `efcf89c2c5122beda7552a6dcf06b8eac241fb59`.
- Final main/origin/main/HEAD: `efcf89c2c5122beda7552a6dcf06b8eac241fb59`; tree `5094e2bf81731effce8cd3dd79ecc2d60a75ac54`; remote heads only `refs/heads/main`; local and remote H follow-up branch deleted after merge.
- Transaction quartets (independent-copy rollback each returned `ROLLBACK_OK` then `ROLLBACK_NOOP`, exits `0,0`; seeded_equals_modified=true; restored_equals_original=true; live_modified_remains=true):
  - H02-008-precopy-rollback-v2: MODIFIED_FILE=eb17854cfb0d595286571aaf99bef2159653ec189f64034dc0a813cbb14bb2a9; DIFF_FILE=8c9c482f74b1ae92449d817cfb959377f33d76b51b3e068334b4361cf33e48dc; VERIFICATION.txt=b33b4824628f8791a9891374c17eb8737f430b03080ef9037cd2b2e32a85e3fe; ROLLBACK.sh=93d4d7bdcbb434aeef3f230321532daabd1c2a2d29a02c6537da0c2c661fa4d2
  - H02-021-duplicate-guard-v2: MODIFIED_FILE=cf9d663b626ae1309c05e33b135228854a9a4eb306e955617122333d7ac89501; DIFF_FILE=f2928d18d4f9f3a38c5b65581ee3bc266bcbcc95c5de3e5ddba7bb9a75e52f7d; VERIFICATION.txt=5d41f3ad7b9616d796272a6c0c6fb851a1f38a7da34e34aacf51262cd2f9947c; ROLLBACK.sh=93d4d7bdcbb434aeef3f230321532daabd1c2a2d29a02c6537da0c2c661fa4d2
  - H04-025-inspector-variants-v2: MODIFIED_FILE=219b4fce99ccf4fd7d18f04ad418976c6c187113ed28991c64ac72ddecc0a46a; DIFF_FILE=59d1d9cb896ecd766fa3e0d0db3459e5ca87e8049d46110743dd90e8b90949f4; VERIFICATION.txt=d7f5e32e3ca410ffc7c8370a358550ddd07bf65147a085cbe63c6bf9f779d6c5; ROLLBACK.sh=93d4d7bdcbb434aeef3f230321532daabd1c2a2d29a02c6537da0c2c661fa4d2
- Pre-existing dirty files remain unchanged: `Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs`, `Unity2Foxglove/Packages/manifest.json`, `Unity2Foxglove/Packages/packages-lock.json`. `git ls-files build` remains empty.
- Previous AGENTS exact suffix SHA-256: `bc0556db6870959f0f3382822285b7e0a57f905491c966e3ab7f04e89251fbc8`.

# ADDITIVE TERMINAL CHECKPOINT 2026-09-09 — R4 I GR-02 SATISFIED

Status: `R4_I_GR02_SATISFIED_NO_CODE_CHANGE / THREE_ROLE_AUDIT_PASS / FULL_CI_PASSED / NO_PUSH_REQUIRED`

- Final checkpoint: `build/phase187-round4/checkpoints/CHECKPOINT_20260909_R4_I_GR02_SATISFIED.md`, SHA-256 `2851260a873298640c2197781912900561f6a42a6fb8bc396df854af09306d86`.
- I queue: 264 rows; 247 BLOCKED retained, 13 SATISFIED_BY_ROUND3, 2 REFUTED, and GR-02 findings `187-R2-I05-006` (236) plus `187-R2-I04-030` (349) now `SATISFIED_BY_ROUND4`; no remaining FIX_NOW. Queue SHA-256 `7200f81b...` (full hash in checkpoint); execution ledger SHA-256 `0a943ba4...` (full hash in checkpoint).
- Current `run_ci.py` rejects empty result maps; focused regression passed exit 0, so RED did not reproduce and no production code/quartet was created.
- Mutation, correctness, and scope/evidence audits all PASS. Reports and full hashes are recorded in the final checkpoint.
- Full local CI `python -B Scripts/release/run_ci.py`; run `288524-764fbd42`; exit 0; literal `All CI checks passed.`; all 13 jobs passed.
- No source diff from main; no push/PR/merge was performed because both findings were already satisfied by current source. Temporary branch `feature/round4/release-ci-result-integrity` was ancestry-verified and deleted. Final `main == origin/main == e3d4aed388dfc96fa99d78d2032c90da89e01dd9`; tree `37db2ee2736bcb1d20840bec64e4ddac1591d3d9`; remote heads only main.
- Pre-existing dirty Unity files, historical worktrees, and evidence remain preserved; `git ls-files build` is 0. All prior AGENTS.md bytes remain an exact suffix.

AGENTS_PREVIOUS_SHA256=`4f84252c73a1623f6b587c5a7e139439d8d5e971efebacbaf8a26db513527971`

# ADDITIVE TERMINAL CHECKPOINT 2026-09-08 — R4 H SERIES FINAL

Status: `R4_H_COMPLETE / THREE_ROLE_AUDIT_COMPLETE / FULL_CI_PASSED / PR325_MERGED / MAIN_SYNCED / H_BRANCHES_CLEAN`

- Final H checkpoint: `build/phase187-round4/checkpoints/CHECKPOINT_20260908_R4_H_SERIES_FINAL.md`, SHA-256 `c2ed82eec05f62e34bafaccc622a86f80c4f291f3715be06ecbbf42db072ecb0`.
- H queue: 94 rows, selected_order 95..188; H01=10 H02=23 H03=15 H04=26 H05=2 H06=3 H07=13 H08=2. Final dispositions 90 BLOCKED, 4 SATISFIED_BY_ROUND4. Queue SHA-256 `d59dd2c03521cede5601aefb97e871a18236e0e2ff6f51f8708c8c4e25987a84`; execution ledger SHA-256 `2558292fca7bd04b4947f784cf4cb7dd8bf23a32c883001879a1e9ab1a8f67f0`.
- H02-015 / GR-01-GENERATOR-ATTRIBUTE-ALIAS reused complete PR #318 evidence; no duplicate code change. Confirmed fixes H02-008, H04-025, H02-021 serial commits `426be4dd0`, `92cd0c820`, `46d14f7fa`, `4e0982a4c`; merged by PR #325 commit `e3d4aed388dfc96fa99d78d2032c90da89e01dd9`.
- Three-role follow-up reports: `build/phase187-round4/h-series/agents/mutation-report-followup.md` SHA-256 `49CFE09EB7A538B502FF36BA0707A3FA3F8BA895AC9C1FC953C82961CD9F6365`; `correctness-report-followup.md` `5385108b87c782a41b1f311befa975aaa106319710edd50d1ec57a4d1588bfbd`; `scope-evidence-report-followup.md` `7f87b2f23c95effd0a8b84ce712c1caf0706798f5e7de9f71ced2fe66fbd81e9`; adjudication found no remaining confirmed finding.
- Full local CI command `python -B Scripts/release/run_ci.py`; run `162584-9f2cbda4`; exit `0`; literal `All CI checks passed.`; 13/13 jobs. Remote PR #325 checks all passed on rerun `34252601640` after initial artifact-finalization 403.
- Final refs `main == origin/main == e3d4aed388dfc96fa99d78d2032c90da89e01dd9`; tree `37db2ee2736bcb1d20840bec64e4ddac1591d3d9`; remote heads only `main`; local/remote H source branch deleted after ancestry verification.
- Evidence paths and full quartet hashes are in the final checkpoint cited above; rollback yielded `ROLLBACK_OK` then `ROLLBACK_NOOP`, exits `0,0`, `seeded_equals_modified=true`, `restored_equals_original=true`, `live_modified_remains=true`. `git ls-files build` returned zero rows. Pre-existing dirty Unity files remain unchanged and excluded from H commits.
- All prior AGENTS.md bytes remain an exact suffix.

AGENTS_PREVIOUS_SHA256=`89d5d265a9ca8cedcceae8dc45d2be4747c061c7477ae2c7989cf4244a6d49de`

# ADDITIVE CLEANUP CHECKPOINT 2026-09-08

- `git worktree prune --verbose` removed four stale worktree registrations (`baseline-worktree3` through `baseline-worktree6`). No working-tree files were deleted.
- Final repository boundary remains `main == origin/main == 2b6ba204bb2f239cbddc9eb1cc41edba5133fa86`; remote heads contain only `main`.
- Historical detached audit worktrees, evidence directories, unrelated branches, and pre-existing scratch/meta files remain preserved for traceability.

AGENTS_PREVIOUS_SHA256=`d5068a942a1a4b2ebcbc211d184a59f042cedbf0cf6ac011a79c12c5a3c1c2d0`

# ADDITIVE WINDOW HANDOFF 2026-09-08 — READY FOR H-SERIES

Status: `R4_C_D_RECONCILIATION_MERGED / H_NEXT / MAIN_SYNCED / REMOTE_HEADS_CLEAN`

- Current boundary: `main == origin/main == 2b6ba204bb2f239cbddc9eb1cc41edba5133fa86`; tree `d50149182f60983662aca135c42ea95c9baae07d`. Remote verification retains only `refs/heads/main`.
- C/D residual reconciliation is complete through PR #324. Final evidence: `build/phase187-round4/reviews/D-C-residual-reconciliation-20260908/FINAL_HANDOFF_20260908.md`, SHA-256 `dbdaa8463cf5bd4392fbc996e42adf8c054c00f0980dfa5fdefa9bdde3eb420e`.
- The final C/D serial branches and their remote refs were deleted after normal merge. Unrelated historical review/archive branches remain intentionally preserved; no broad branch or file cleanup is authorized.
- Existing workspace modifications remain preserved: `Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs`, `Unity2Foxglove/Packages/manifest.json`, and `Unity2Foxglove/Packages/packages-lock.json`, plus prior untracked evidence/scratch/meta files. Do not use `git clean`, broad deletion, reset, stash, or ref rewrite.
- `git ls-files build` returns zero. Build evidence remains local/ignored.

## NEXT WINDOW: COMPLETE THE PREVIOUSLY REVIEWED H-SERIES FINDINGS

1. Read this file to EOF, then read `build/phase187-round4/reviews/D-C-residual-reconciliation-20260908/FINAL_HANDOFF_20260908.md`, the authoritative Round4 plan, queue, execution ledger, and the imported Claude `24-root evidence reconciliation` report/session. Use live refs and current source; do not infer H counts or order from stale prose.
2. Bind the H work to a meaningful serial branch created from current `main`, for example `feature/round4/h-<descriptive-purpose>`. Keep `main` untouched and process H roots in authoritative queue order.
3. For each H finding that Claude already reviewed, perform the requested second audit only: rebind the current source anchor, compare the actual code path/state transition with Claude's claim, classify confirmed/refuted/blocked, and preserve the original evidence. Do not restart a full unrelated review.
4. For every confirmed error, use the same root transaction sequence as G: freeze/hash the original source, capture root-specific RED before edits, write the narrowest fix, run focused GREEN and negative/adjacent controls, run independent review, create `MODIFIED_FILE`, `DIFF_FILE`, `VERIFICATION.txt`, and executable `ROLLBACK.sh`, rehearse rollback on an independent copy (`ROLLBACK_OK` then `ROLLBACK_NOOP`), update the evidence/ledger overlay, and commit the serial branch. Leave the live modified source changed.
5. Continue through the entire H queue before dispatching review. Do not dispatch agents after an individual H root. When all H roots are complete, dispatch exactly three agents once for the whole H series: **Mutation/adversarial** (break the fixes and prove tests turn red), **Correctness** (independent code-path, state, concurrency/lifecycle/security-boundary review), and **Scope/evidence** (files, anchors, hashes, RED/GREEN logs, quartet, ledger, and CI evidence). Do not start extra agents.
6. Reconcile all three reports. If any finding is confirmed, create a new meaningful serial H repair branch, apply the smallest fix with RED/GREEN/controls and rollback quartet, then repeat the three-role audit only as needed until no confirmed error remains.
7. After the H series and audit repairs are settled, run one complete local gate for the batch: `python -B Scripts/release/run_ci.py`. Collect all job failures before a unified repair; only exit `0` with literal `All CI checks passed.` permits push. Do not use `--only`, `--skip-analyzer`, or the diagnostic `--all-ci-safe` run as a release substitute.
8. Commit before push, verify `git ls-files build` is empty, push only the final serial H branch once, wait for every remote check (`docs`, `check`, `test`, `analyzer-freshness`, optional ROS2 Native, optional ROS2 adapter), and do not merge while any check is pending or failed. If remote failures occur, collect all failed logs, fix them on a new serial branch, rerun the complete local gate, then push again.
9. Merge normally with `gh pr merge <PR> --merge --delete-branch=false`; return to local `main`, fetch, fast-forward to `origin/main`, verify merge/tree/remote heads, delete the merged local and remote H branches, and append a new additive terminal checkpoint here. Preserve unrelated historical branches and workspace files.
10. End the H window only after reopening and hashing the final checkpoint, final review, ledger overlay, all four transaction artifacts, local CI log, and remote merge evidence.

AGENTS_PREVIOUS_SHA256=`2a3aed6ca96d65cb851c5afbeebdc4ec16a502ff8ef47eedd342fa2ca0517a64`

# ADDITIVE TERMINAL CHECKPOINT 2026-09-08 — C/D RESIDUAL RECONCILIATION COMPLETE

Status: `R4_C_D_RECONCILIATION_MERGED / PR324_REMOTE_CI_PASSED / MAIN_SYNCED / BRANCHES_CLEAN`

- Final `main`, `origin/main`, and `HEAD`: `2b6ba204bb2f239cbddc9eb1cc41edba5133fa86`; tree: `d50149182f60983662aca135c42ea95c9baae07d`.
- Claude C/D residual findings were re-audited twice and then reviewed by exactly three final roles: Mutation, Correctness, and Scope/Evidence. Confirmed fixes were serially integrated; no additional review agents were started.
- Final serial branch: `feature/round4/d-c-remote-ci-repair`; PR #324 normal merge: `https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk/pull/324`, merge time `2026-09-08T14:47:30Z`; local and remote source branches deleted after merge.
- Remote heads: only `refs/heads/main` at `2b6ba204bb2f239cbddc9eb1cc41edba5133fa86`.
- PR #323 failures were all the same root-discovery defect in the new C3 source-contract test: `DirectoryNotFoundException: Repository root was not found from the test base directory.` The repair changed `FindRepositoryRoot` to use tracked `Packages/dev.unity2foxglove.sdk/Runtime/Protocol/BinaryEncoding.cs`, removing the untracked `AGENTS.md` dependency.
- PR #324 remote checks all passed: `analyzer-freshness`, `check`, `docs`, `test`, `optional ROS2 Native gate`, and `optional ROS2 adapter gate`.
- Complete local CI: `python -B Scripts/release/run_ci.py`; run `163596-1b17a6f3`; exit `0`; literal `All CI checks passed.`; 13/13 jobs passed.
- Final evidence handoff: `build/phase187-round4/reviews/D-C-residual-reconciliation-20260908/FINAL_HANDOFF_20260908.md`; SHA-256 `dbdaa8463cf5bd4392fbc996e42adf8c054c00f0980dfa5fdefa9bdde3eb420e`.
- Final remote-root transaction quartet: `build/phase187-round4/reviews/D-C-residual-reconciliation-20260908/transaction-repository-root-discovery/MODIFIED_FILE` SHA-256 `9280e6a5d31fb1564e7f4629ca1fde1946b8cd7c5d49a90bd5fd56cc2b1a514f`; `DIFF_FILE` SHA-256 `104e0af506c408b5a3635ad686ad449fc782c144d77ff8c2db3e095b86a1543f`; `VERIFICATION.txt` SHA-256 `ef2eddcef512d715e185cbf42fac3560ac5feeee957950ca43b541355169a84f`; executable `ROLLBACK.sh` SHA-256 `4b204b99646c226c91b167eedd0b7f2e3b75f292d322bcea7f319615eded5f5f`. Independent-copy rollback returned `ROLLBACK_OK` then `ROLLBACK_NOOP`, exits `0,0`, restored behavior/status confirmed.
- `git ls-files build` remains `0`; the pre-existing dirty tracked files and untracked evidence/scratch files remain preserved.
- Next opening boundary: re-read this file to EOF and the final handoff before starting the next requested series.

AGENTS_PREVIOUS_SHA256=`38596a5fdd4f897cae4af61d43a3dcd1983f209c5f309925b0f9e8cd3361a203`

# ADDITIVE TERMINAL CHECKPOINT 2026-09-08 — R4 G COMPLETE / PR322 MERGED / MAIN SYNCED

Status: `R4_G_COMPLETE / THREE_AGENT_AUDIT_COMPLETE / FULL_CI_PASSED / PR322_MERGED / MAIN_SYNCED / G_BRANCHES_CLEAN`

- Current branch: `main`.
- Current main/origin/main: `771e7a00a94c3583a2090a8878fab51847fb1f6f`; tree: `b1d5f4c08da5fa0834b97972776237ebde2e720e`.
- G-series: 36/36 roots terminal across G01=4, G02=8, G03=19, G04=5; queue orders 59..94. G05 is absent from the live queue.
- Three-agent audit covered all 36 roots. Mutation, correctness, and scope/evidence reports remain under `build/phase187-round4/reviews/G-series-agents/`; final report: `build/phase187-round4/reviews/G-series-final-20260908/G-SERIES-FINAL-20260908-v3.md`, SHA-256 `0da9797495cb370db637b733f4d9cf862fb433c6024f722b61918a7249035a0d`.
- Final serial branch tip: `4a6bf7f36f8c1c88c922833cd4bbc9b579571b79`; PR #322 merged normally as `771e7a00a94c3583a2090a8878fab51847fb1f6f`. Remote checks run `34214686734` test/analyzer/native/adapter, `34214686719` check, and `34214686755` docs all passed.
- Final local full gate: `python -B Scripts/release/run_ci.py`; run `150624-cc95f726`; exit `0`; literal `All CI checks passed.`; all 13 jobs passed. Log: `build/phase187-round4/ci-audit/g-series-final-20260908-final8.log`.
- Final G04 fixture closes all inherited Unix descriptors for the child stdin pipe; the WSL probe changed from `SECOND_WRITE_OK` under the weak fixture to `SECOND_WRITE_IOException` with descriptor closure. G04 admission tests pass 4/4.
- Local cleanup: G serial branches `feature/round4/g-series-review-repair-final`, `feature/round4/g-series-review-repairs`, and `feature/round4/g04-high-rate-backpressure-evidence` were deleted after ancestry verification; the remote G source branch was deleted. `git ls-remote --heads origin` retains `main` as the only remote head.
- `git ls-files build` returns zero rows. Existing unrelated dirty files and pre-existing untracked scratch/meta files remain preserved. Do not start H in this window.

AGENTS_PREVIOUS_SHA256=`d7363e1bcc3f202071352b8a28ecf8a99cfe02d52e9556a4c6ff0ea186dd7266`
# ADDITIVE TERMINAL CHECKPOINT 2026-09-05 — R4 D/E/F COMPLETE; G READY

Status: `R4_D_E_F_COMPLETE / G_READY / MAIN_SYNCED / REMOTE_HEADS_CLEAN`

This additive block records the verified D/E/F terminal state and the G-series opening boundary. Every prior AGENTS.md byte remains an exact suffix.

- Current branch: `main`.
- Current main/origin/main: `50f2f4bf77debf16e11b9393707c50a6a0f33b20`; tree: `e8cc268722fa7956421983eeb0d7419d0c91547d`.
- D-series: 18/18 roots terminal; PR #316 merge `c36ec9e2a90d1f78a46fa43e500b31e6663b42f4`; full CI `35980-af04c45f`, exit `0`, literal `All CI checks passed.`.
- E-series: 17 E rows plus retained GR-01 overlay row, 18 total; PR #318 merge `454bb946fe315fa5f32eed0753e029c97a7225c7`; full CI `174036-4b08b371`, exit `0`, literal `All CI checks passed.`, remote checks 6/6 passed.
- F-series: 22/22 roots across F01–F04; PR #319 merge `6f2869ce48a278d9325f27ed8fdbb42cb6b9f0dc`; full CI `2204-361951ba`, exit `0`, literal `All CI checks passed.`, remote checks 6/6 passed. Final audit: `build/phase187-round4/reviews/F-series-evidence/FINAL-INTEGRATION-AUDIT-20260901.md`, SHA-256 `604b3b0821a90fefee2b093df35481d9d631fce68e3e6bbfbd1f2f571f05d318`.
- F ledger reconciliation is additive only: `build/phase187-round4/reviews/F-series-evidence/F_LEDGER_RECONCILIATION_20260905.tsv`, 22 rows, SHA-256 `0f8f60d784c6a21f66c2ab81e5b26d60e38866662e5324ee5ee9c10597bd2c96`. The canonical ledger remains unchanged; the overlay binds all F rows to PR #319 and `MERGED_AND_VERIFIED`.
- Remote cleanup: `refs/heads/main` is the only remote head; local C1–C9 serial branch was deleted. Historical review branches and local evidence remain preserved.
- Protected `.meta` SHA-256 remains `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`; `git ls-files build` returns zero rows.
- G-series is ready to start, not complete: 36 queue roots, closures G01=4, G02=8, G03=19, G04=5, selected order 59..94. First root is `R4-0059-ROOT-G01_001` / `187-R2-G01-001`, disposition `FIX_NOW`, next action `current_check`.
- G report is static review evidence only: `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-G01.md`, SHA-256 `23af50e99e7a7c4bace4bef4dee95b0e51c132210b9e2325f21f1287fdb4ad89`. New G work must rebind the current source anchor and reproduce a root-specific RED before edits.
- Full handoff: `build/phase187-round4/checkpoints/CHECKPOINT_20260905_R4_D_E_F_G_READY.md`, SHA-256 `D6F05E388A4E28B2AD791B83AFB2EFB8D326CDDF2A17E2B8E2E93847B19E51F6`.
- New-window order: read this file to EOF, verify live refs/protected artifact, read the handoff checkpoint plus Round4 plan/queue/ledger, create a descriptive `feature/round4/` G branch, then process roots serially 59..94. Preserve current-check, RED, minimal fix, GREEN, controls, independent review, rollback quartet, commit, checkpoint, cumulative CI, remote checks, normal merge, main sync, and branch cleanup.

AGENTS_PREVIOUS_SHA256=`73eea22405257f3c23b69faa67e8e0035def8cc7eea2d89a6a215bede5651af9`

# ADDITIVE TERMINAL CHECKPOINT 2026-08-30 - R4 D SERIES

Status: `R4_D_SERIES_COMPLETE / FULL_CI_PASSED / PR316_MERGED / LOCAL_MAIN_SYNCED / SERIAL_BRANCHES_CLEANED`.

This additive block records the terminal D-series integration; every prior AGENTS.md byte remains an exact suffix.

- PR/merge SHA: `c36ec9e2a90d1f78a46fa43e500b31e6663b42f4` (PR #316, normal merge; no admin bypass).
- Final tree: `89c4d8ceee06e25dd48b050d4970c65ec8d40569`.
- Source integration branch: `feature/round4/native-artifact-contract-compatibility`; source tip: `3866d097be249466b5bad2d8da9e1859bb06b536`.
- D coverage: 18/18 roots (D01=2, D02=4, D03=5, D04=3, D05=4), plus D01 follow-ups `1b5cdf94f`, `893aac0e5`, `8e32f4f61`, `bb3ab41f9`.
- Complete local CI command: `python -B Scripts/release/run_ci.py`.
- Complete local CI run id/result: `35980-af04c45f`; exit `0`; literal `All CI checks passed.`; all 13 jobs passed.
- Remote checks: PR #316 all six checks passed (`docs`, `check`, `test`, `analyzer-freshness`, `optional ROS2 Native gate`, `optional ROS2 adapter gate`); merged at `2026-08-30T04:14:56Z`.
- D-series execution-ledger SHA-256: `aef4c9d992b061deac40ee7858c31caaf34b92edf56358fddcd2f909d392dfff`.
- D-series terminal checkpoint SHA-256: `11017f01808b3904c4a5cc956a25f119f6f41d8f07a3aa0888aaaadc2c26296b` (`build/phase187-round4/checkpoints/CHECKPOINT_20260829_R4_D_SERIES_LOCAL_COMPLETE.md`).
- Local cleanup: `main = origin/main = c36ec9e2a90d1f78a46fa43e500b31e6663b42f4`; five D-series serial branches were verified ancestors and deleted with `git branch -d` (no `-D`); historical ROOT-D01_001 worktree and runtime-restart-disposal-lifecycle branch retained; remote source branch retained by explicit `--delete-branch=false`.
- Build boundary: `git ls-files build` returned zero rows; build evidence remains local/ignored; protected `.meta` unchanged.
- B6 status: `NOT_RUN`.

## CI BATCH GATE POLICY (2026-08-29) - AUTHORITATIVE ADDITIVE BLOCK

本块固化每个变更批次的本地与远端 CI 顺序；旧版 AGENTS.md 的全部字节必须作为精确后缀保留。
说明：先让一次完整 run_ci.py 跑完，收齐所有失败，统一修改；不要遇到一个问题跑一次完整 CI，也不要按单个问题分别运行完整 CI。
远端检查若有失败，先收齐全部远端失败；远端有失败时不要合并。统一修改后重新跑一次完整 CI，然后再 push；所有远端检查成功后才 merge。

### 本地完整 CI 批次门禁

1. 从 `feature/` 分支工作，保持 `main` 不直接推送。
2. 针对当前变更批次只启动一次完整门禁，并等待所有 job 完成：
   `python -B Scripts/release/run_ci.py`
   Use the no-argument command above for the release gate. `--only` and `--skip-analyzer` are diagnostic shortcuts only and never replace the complete gate.
   A PowerShell capture that preserves the native exit code is:
   ```powershell
   $ciLog = "build/phase187-round4/ci-audit/run-ci-batch.log"
   python -B Scripts/release/run_ci.py *> $ciLog
   $ciExit = $LASTEXITCODE
   Get-Content -LiteralPath $ciLog
   "CI_EXIT=$ciExit"
   ```
   Keep the printed run id and per-job log directory; do not pipe to `head` or terminate the process before every job reports.
3. 不在首个失败处停止。读取这一次运行的全部 job、日志、输入和退出码，收齐所有失败后再定位原因。
4. 将收集到的原因作为一个批次统一修改。诊断期间可以运行窄范围测试，但窄测试作为诊断，完整门禁仍需执行。
5. 批量修改完成后再完整运行一次 `python -B Scripts/release/run_ci.py`。只有退出码 `0` 且出现字面量 `All CI checks passed.` 才能进入 push。若仍失败，收齐本次完整运行的全部失败，继续统一修复；不要按单个问题分别运行完整 CI。

### push、远端检查与合并

6. 本地完整门禁通过后才 commit/push `feature/` 分支；先确认 `git ls-files build` 输出为空，`build/` 证据只留本地，stage 和 push 均跳过。
7. push 后等待 `gh pr checks <PR>` 的每一个 required、optional、documentation 结果；pending 不是可合并状态。
8. 任一远端检查失败时不要合并。先等待当前检查集合结束，收齐全部远端失败，并对每个失败 run 执行 `gh run view <run-id> --log-failed`；把原因统一修复后，完整运行一次 `python -B Scripts/release/run_ci.py`，确认退出码 `0` 和 `All CI checks passed.`，再 commit/push。
9. 只有所有远端检查成功才允许使用正常合并命令：
   `gh pr merge <PR> --merge --delete-branch=false`
   不使用 `--admin` 绕过检查；合并后核对 merge commit，并让本地 `main` 对齐 `origin/main`。

### 证据与边界

记录每次完整门禁、远端检查和合并的精确 command/input/literal output/exit status。每次代码事务保留 `MODIFIED_FILE`、`DIFF_FILE`、`VERIFICATION.txt`、可执行 `ROLLBACK.sh` 四件套，并在独立副本演练回滚。

### 当前验证边界（2026-08-29）

当前本地 `main` 与 `origin/main` 已对齐：HEAD=`d62bea65bba6ac3399608aa729921d48844259ba`，tree=`d9a6451cceabbe82a571aada840c44810f9444df`。
最近一次完整本地门禁 run id=`47908-2d4015de`，13/13 jobs，退出码 `0`，字面量 `All CI checks passed.`；对应远端 PR #315 的全部检查均成功后才完成正常合并。

CI_BATCH_POLICY_STATUS=ACTIVE
CI_BATCH_POLICY_RULE=COLLECT_ALL_FAILURES_THEN_ONE_BATCH_FIX_AND_ONE_COMPLETE_RERUN
CI_BATCH_POLICY_BUILD_BOUNDARY=BUILD_LOCAL_ONLY_GIT_LS_FILES_BUILD_EMPTY

## ROUND4 D01 CLOSURE TERMINAL CHECKPOINT (2026-08-29)
Status: `R4_D01_CLOSURE_MERGED_AND_VERIFIED / STOP_BEFORE_D02`.
Boundary: HEAD `ecc27acc2959ddf9a21a866249d3a6b05e8bdd7f`; tree `5fe022176d59ccfbe0abe44be39f69976ed65f5d`; branch `main`; origin/main `ecc27acc2959ddf9a21a866249d3a6b05e8bdd7f`; tracked/staged diff `0`.
`ROOT-D01_001` and `ROOT-D01_002` are both `SATISFIED_BY_ROUND4` in the Round4 overlay. D01_002 serial commit `caa48b393cd108a57d9937862f3075775453f3d9` is integrated by PR #314 merge commit `ecc27acc2959ddf9a21a866249d3a6b05e8bdd7f`; the meaningful integration branch was `feature/round4/manager-session-lifecycle`, then local and remote refs were cleaned.
D01_002 RED reproduced the throwing-transport cleanup seam (exit `1`); focused GREEN, root controls, independent review, and post-merge checks exited `0`. The source/test boundary is exactly the three D01_002 files in serial commit; no public API, wire shape, schema, package layout, or unrelated source changed.
Local `python -B Scripts/release/run_ci.py` and remote PR checks retain known baseline failures (Phase16/Phase173101/source-generator/release-tooling/docs findings); package structure check passed. The native `Access denied` was caused by Huorong quarantine: both generated DLL copies were manually restored and now match SHA-1 `774FA89D6562DBD81BF204366182ABAAABF0C7F2` / SHA-256 `33b6ea61a4297c2ac4ec1c5068e1a394090930724861aa7da01a529b3ce221f2`; restored native tests loaded the assembly and no longer report access denied.
All Round4 evidence and transaction artifacts remain under ignored `build/` paths and are not part of the remote repository. Protected `.meta` remains the only untracked status entry with SHA-256 `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
The stale `.git/REBASE_HEAD` and seven fully merged `feature/global-remediation/root-i03_*` branches remain record-only; no frozen plan/register/history edit, D02/E/I/B1 action, reset, stash, or ref rewrite occurred.
Queue counts are `BLOCKED=247`, `FIX_NOW=188`, `REFUTED=2`, `SATISFIED_BY_ROUND3=13`, `SATISFIED_BY_ROUND4=2`; ledger execution statuses are `NOT_STARTED=450`, `MERGED_AND_VERIFIED=2`. Next ordered candidate is `187-R2-D02-001` / `ROOT-D02_001`; it is not started.
This additive block preserves every prior AGENTS.md byte as an exact suffix.

## ROUND4 ROOT-D01_001 TERMINAL CHECKPOINT (2026-08-29)
Status: `R4_ROOT_D01_001_MERGED_AND_VERIFIED / STOP_BEFORE_D01_002`.
Boundary: HEAD `1a8d232303a562e3d67de995c2fe8824b63b0879`; tree `b9340a0b0fcb78d140ff9c5a01138b4915beed5b`; branch `main`; merge `1a8d232303a562e3d67de995c2fe8824b63b0879`; serial `c3f0b4600bcf865073b4158ef0da06cc929177e8`.
ROOT-D01_001 (`187-R2-D01-001`) current disposition is `SATISFIED_BY_ROUND4`; RED was behavioral and exit `1`, focused GREEN and post-merge controls exited `0`.
The Round4 overlay now has `BLOCKED=247`, `FIX_NOW=189`, `REFUTED=2`, `SATISFIED_BY_ROUND3=13`, `SATISFIED_BY_ROUND4=1`; no second root, E/I family, B1, push, reset, stash, or ref rewrite was started.
The stale `.git/REBASE_HEAD` and seven merged `feature/global-remediation/root-i03_*` branches remain record-only anomalies; no metadata or branch was touched.
This additive terminal block preserves every prior AGENTS.md byte as an exact suffix.

## ROUND4 DIRECT REMEDIATION HANDOFF (2026-08-29)
AGENTS_I03_010_CHECKPOINT=ALREADY_PRESENT
Status: `R4_HANDOFF / R4_STAGE0_BASELINE_SEALED_PENDING_QUEUE_RECONCILIATION`.
Boundary observed before handoff: HEAD `cf44cde51c68117310523b39bb5beec18914a22c`; tree `ecf5b8c9db8aa9c1cf6dfdea8002366faf71e246`; branch `main`; origin/main `8d1223fc4cacf988da419b97c51a0f87bd965443`.
The existing I03_010 terminal checkpoint remains authoritative and is not duplicated.
This additive block preserves every prior AGENTS.md byte as an exact suffix.
Round4 product/test bytes remain untouched during Stage0; unexecuted product tests remain literal `NOT RUN`.

## FINAL LIVE CHECKPOINT (2026-08-28) — ROOT-I03_010 TERMINAL HASH RECONCILIATION

Status: `PLAN_SEALED / RED_FIRST24_RECONCILED / B0_IN_PROGRESS / ROOT-I03_010_MERGED_AND_VERIFIED`.
This newest additive correction supersedes only stale audit references in the immediately lower additive block; every prior AGENTS.md byte remains an exact suffix.

Boundary: HEAD `cf44cde51c68117310523b39bb5beec18914a22c`; tree `ecf5b8c9db8aa9c1cf6dfdea8002366faf71e246`; branch `main`; origin/main `8d1223fc4cacf988da419b97c51a0f87bd965443`; tracked/staged diff `0`; protected `.meta` SHA-256 `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.

Only `ROOT-I03_010` (`R3-B0-135-ROOT-I03_010`, `OWNER_PACKAGE`) was processed. The pre-existing Pilot-B branch `feature/global-remediation/root-i03_011` remains `0676e4cf3ddc069fb9b0f3c5641e01c178544e1a` and remains an ancestor of `main`; no I03_011 or other root, B1, Pilot B rerun, I02_010 reopen, DeepWiki, frozen-artifact edit, push, reset, stash, or ref rewrite occurred.

The terminal checkpoint `build/phase187-round3/checkpoints/CHECKPOINT_20260828_ROOT-I03_010_TERMINAL.md` is SHA-256 `4a32ded69755fc0b6a4e50be8340b95ca97ba0734ff44fd07fdea61aa981a9f0`. The post-bind command `python -B build/phase187-round3/roots/ROOT-I03_010/final_reopen_audit.py` returned literal `FINAL_REOPEN_HASH_AUDIT_PASS`, exit `0`, JSON SHA-256 `760e2182539adb5f071063749ae19ed6b1e7df2eab37437aed22da3787435d60`, text SHA-256 `0072fcdec8f938c1cdf27bfc10f166c3fabb04c77ca364a6d8fce31376ef597d`, `reopened_artifact_count=50`, and all invariants true; the checkpoint hash is included in that 50-artifact set.

The source transaction quartet remains `MODIFIED_FILE=10eb4a9d5ca4e4e9d95d5232d5fc304c5993f8722a8971cf760c9d6d67ed9c51`, `DIFF_FILE=29e47bfc5705bd5b2da525a4365624e7faf635539590293de4b95c580e3680b0`, `VERIFICATION.txt=1704f2ec9045b22a14051341f140be5aca86f4f7e4b961685f79614c205e7bab`, `ROLLBACK.sh=fb0f4d3d0ac3546c759b91a535d28aeb9bd8f5e8a369cc9c77e97055616abf35`; separate-copy rollback returned literal `ROLLBACK_OK` then `ROLLBACK_NOOP`, exits `0,0`, restored copy matched originals and live modified files remained changed.

Serial commit is `ad402df8dab6395e958cc46ee8c76ec7f1613bd1`; no-ff merge is `cf44cde51c68117310523b39bb5beec18914a22`. Ledger/state are `3a5812f93aedb372ff31323dc7b3bbffa8a4761479d38ff15d478c0c10b4d889` and `f32d2bcb352407607e79f5a2ef59db500012aad384794615c2897434657d48d5`; sealed fields remain `red_status=NOT RUN`, `global_red_status=RED_FIRST24_RECONCILED`, `red_result=CONFIRMED`. The final status sweep returned `FINAL_STATUS_SWEEP_PASS`, exit `0`: B0 total `259`, terminal `13`, nonterminal `246`, `CONFIRMED=14`, `SATISFIED_BY_EXISTING_FIX=0`, `REFUTED=0`, `BLOCKED=245`; first ordered pending row is `GR-02-RUN-CI-SKIP-EMPTY-RESULT` and was not started.

AGENTS_PREVIOUS_SHA256=3250601cb1de39ac3076433432f78a3b864fd4c03ddb919acd6070748bf1d0b9
## FINAL LIVE CHECKPOINT (2026-08-28) — ROOT-I03_010 TERMINAL

Status: `PLAN_SEALED / RED_FIRST24_RECONCILED / B0_IN_PROGRESS / ROOT-I03_010_MERGED_AND_VERIFIED`.
This additive block is authoritative for this single-root window; every prior AGENTS.md byte remains an exact suffix.

### Boundary and scope

HEAD `cf44cde51c68117310523b39bb5beec18914a22c`; tree `ecf5b8c9db8aa9c1cf6dfdea8002366faf71e246`; branch `main`; origin/main `8d1223fc4cacf988da419b97c51a0f87bd965443`; tracked/staged diff `0`. The only status entry is the pre-existing protected `.meta` path, whose SHA-256 is `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.

Only `ROOT-I03_010` (`R3-B0-135-ROOT-I03_010`, owner `OWNER_PACKAGE`, closure `187-R2-I03`) was processed. No other root, ROOT-I03_011, B1, Pilot B rerun, ROOT-I02_010 reopen, DeepWiki, Round 2 closure review, push, reset, stash, ref rewrite, or frozen register/order/plan/history edit occurred. The pre-existing Pilot-B serial branch `feature/global-remediation/root-i03_011` remains at `0676e4cf3ddc069fb9b0f3c5641e01c178544e1a` and is an ancestor of main; it was not started or changed in this window.

### Revalidation and RED

Frozen source locator `Scripts/package/validate_unity_package.py@c8c0b57ba6ad785ae9f5f90f75efcaecbbe25363:123-157` was rebound by comparing range bytes and the `check_third_party_notices` body. The current function is lines `752-814`, body SHA-256 `1f307b650502b5bd0641b1594cd8ca7e6be17e49864493a8c04368582c571782`; frozen body SHA-256 is `4d73c82657aaf5f87a5bdc58f5872c3633c9d7f8ca0d29e4011ad998bbf46cca`. The bounded re-derivation found the added DLL inventory but unchanged global token matching, so the claim surface remained confirmed.

Fixture SHA-256 is `8670785ba748251c4b6b89f06f6aea5786fcf0fe5e99b0d62ec2a9b9a43a5709`; typed oracle SHA-256 is `a2a93fc921be12d87f8a025db04b5070225b3af65582de1421e85f8a641979b2`; review artifact SHA-256 is `4d73affc21bddb80544218ac8ae1318b790edf31e66373e8c906d7e10b00a148`. Current-tree command `python -B build/phase187-round3/triage/ROOT-I03_010/current-revalidation/run_current_oracle.py` returned literal `RED_BASELINE_FAIL; SOURCE_POSITIVE_PASS; misattributed_global_acceptance=True; attribution_required_but_unchecked=True; negative_missing_token_rejected=True; TEMP_SANDBOX_REMOVED`, exit `1`; stdout SHA-256 `5bd2d0b149fe7ce10c606e29dfb6ff78ecc802d7fd683e250e9439095eaa67f5`; revalidation JSON SHA-256 `a053f3c59eba705a4d902a0997aff3b961204e03224e33b8d3ada4c1a3d42467`; bounded artifact SHA-256 `be75259595e8c8316ed0c82de69988729afb9dfca879964d690e2efb247027ff`.

The test-first RED command `python -B -m unittest -v Scripts.package.regression_checks.test_validate_unity_package.ValidatePackageTests.test_third_party_notices_require_tokens_in_one_attributable_section` returned literal `Ran 1 test; FAILED (failures=1); AssertionError: True is not false`, exit `1`; evidence SHA-256 `adc6855e9c11590464ea7cc90b0129925d8eb13e3445c0f1c6e46e2d1651db7f`. The minimal fix adds level-two notice-section co-location checking and changes only `Scripts/package/validate_unity_package.py` plus its root regression test.

### Verification, transaction, review, and integration

Root controls command `python -B build/phase187-round3/roots/ROOT-I03_010/run_root_controls.py` returned `ROOT_CONTROLS_PASS` with GREEN/POSITIVE/NEGATIVE/ADJACENT all true, exit `0`; JSON SHA-256 `3413c81a09530b6d5d2ada6fcab63b583c1cb6f53f72f3e7f6965f221fce6622`; text SHA-256 `0d3934845b3e80012c82c85adb35a90233708d9ddcaf5ea6490964aa25e223e3`.

Validation command `python -B build/phase187-round3/roots/ROOT-I03_010/run_validation_suite.py` returned `VALIDATION_SUITE_PASS`, exit `0`: root regression `1/1`, OWNER_PACKAGE regression `40/40`, package validator `70/70`, py_compile `0`, and git diff --check `0`; JSON SHA-256 `e1d7b49db1cba9aeb57ff2334eb3d2db23a598099a65ebfe74c751eb4f0ebf29`; text SHA-256 `8b758465d113336d799bfb73a3a4a365b288ab78d2aa74e2d1ba8fc555df1435`.

Independent review command `python -B build/phase187-round3/roots/ROOT-I03_010/run_independent_review.py` returned `INDEPENDENT_FIX_REVIEW_PASS`, exit `0`; JSON SHA-256 `a836e7cbf69721c164e602d70a14ed58d1833bebcf7fe387c9de08c900b1ce72`; text SHA-256 `09b49a44741cffb1f4eeff877ba5c94e294cac8756450e1b5fa8eb33d04a4d8d`. Serial commit `ad402df8dab6395e958cc46ee8c76ec7f1613bd1` contains exactly the source/test pair. `git merge --no-ff feature/global-remediation/root-i03_010 -m "merge(187): integrate ROOT-I03_010 remediation"` returned exit `0` and created merge `cf44cde51c68117310523b39bb5beec18914a22c` with tree `ecf5b8c9db8aa9c1cf6dfdea8002366faf71e246`.

Post-merge command `python -B build/phase187-round3/roots/ROOT-I03_010/run_post_merge.py` returned literal `POST_MERGE_VALIDATION_PASS`, exit `0`; JSON SHA-256 `e0cd79f38315b014fea2fa5eb665eaed134d4a5a41554021e210610274a0e4fa`; text SHA-256 `6f1d0c94eb420163f9cb7220fcd1ef6fc14b9d7874229bdf84dc029c2b69a477`.

Source transaction quartet is `build/phase187-round3/roots/ROOT-I03_010/transaction/MODIFIED_FILE` SHA-256 `10eb4a9d5ca4e4e9d95d5232d5fc304c5993f8722a8971cf760c9d6d67ed9c51`, `DIFF_FILE` SHA-256 `29e47bfc5705bd5b2da525a4365624e7faf635539590293de4b95c580e3680b0`, `VERIFICATION.txt` SHA-256 `1704f2ec9045b22a14051341f140be5aca86f4f7e4b961685f79614c205e7bab`, and executable `ROLLBACK.sh` SHA-256 `fb0f4d3d0ac3546c759b91a535d28aeb9bd8f5e8a369cc9c77e97055616abf35`. The verification records baseline command/input/literal `RED_BASELINE_FAIL` exit `1`, modified command/input/literal `VALIDATION_SUITE_PASS` exit `0`, and separate-copy rollback literal `ROLLBACK_OK` then `ROLLBACK_NOOP`, exits `0,0`; the restored copy equals originals while live modified snapshots remain changed.

### Execution ledger/state and terminal audit

Ledger/state command `python -B build/phase187-round3/roots/ROOT-I03_010/sync_ledger_state.py` returned `LEDGER_STATE_TRANSACTION_APPLIED`, exit `0`; live execution ledger SHA-256 `3a5812f93aedb372ff31323dc7b3bbffa8a4761479d38ff15d478c0c10b4d889`; global state SHA-256 `f32d2bcb352407607e79f5a2ef59db500012aad384794615c2897434657d48d5`; ledger verification SHA-256 `d6a825a779dd89c8fe0cb0c482e78ceff1dbe221724de0394ffe4c6d6c72a745`; ledger manifest SHA-256 `ae8cbe8ed0fbff4b9d8f2d140df8aeea2fbe57c97e3230ca38a69709a2833d98`; ledger rollback SHA-256 `a3cfdf1927e661acd93f41e52f9c261c605b51b7c9b284a35f6aa95541c785e1`. Only the ROOT-I03_010 execution row and global state head/tree/execution hash changed. Sealed literals remain exactly `red_status=NOT RUN`, `global_red_status=RED_FIRST24_RECONCILED`, `red_result=CONFIRMED`; status doc SHA-256 `7774eb2070a025cdf27dd1edc1a5bef5d66d8cea4b64377bba55cbe2561230e8`.

Final reopen command `python -B build/phase187-round3/roots/ROOT-I03_010/final_reopen_audit.py` returned literal `FINAL_REOPEN_HASH_AUDIT_PASS`, exit `0`; JSON SHA-256 `4a86e73ccd7324f07ab7d420890d8124e1e826ea9513a042afc89f4630833847`; text SHA-256 `3e57557757086f1686211494b429daa4481c4cf921a88e26886650177723b19b`; 50 evidence artifacts were reopened and hashed. Final dynamic status sweep command `python -B build/phase187-round3/roots/ROOT-I03_010/final_status_sweep.py` returned `FINAL_STATUS_SWEEP_PASS`, exit `0`; B0 total `259`, execution terminal `13`, nonterminal `246`, classification `CONFIRMED=14`, `SATISFIED_BY_EXISTING_FIX=0`, `REFUTED=0`, `BLOCKED=245`; first ordered pending root is `GR-02-RUN-CI-SKIP-EMPTY-RESULT` (order `1`), and no next root is started.

### Artifact SHA index

- current_revalidation_json: `a053f3c59eba705a4d902a0997aff3b961204e03224e33b8d3ada4c1a3d42467` (`build/phase187-round3/triage/ROOT-I03_010/current-revalidation/CURRENT_REVALIDATION.json`)
- bounded_rederivation_json: `be75259595e8c8316ed0c82de69988729afb9dfca879964d690e2efb247027ff` (`build/phase187-round3/triage/ROOT-I03_010/current-revalidation/BOUNDED_REDERIVATION.json`)
- red_current_tree_stdout: `5bd2d0b149fe7ce10c606e29dfb6ff78ecc802d7fd683e250e9439095eaa67f5` (`build/phase187-round3/triage/ROOT-I03_010/current-revalidation/RED_CURRENT_TREE.stdout.txt`)
- regression_red: `adc6855e9c11590464ea7cc90b0129925d8eb13e3445c0f1c6e46e2d1651db7f` (`build/phase187-round3/roots/ROOT-I03_010/REGRESSION_RED.txt`)
- root_controls_json: `3413c81a09530b6d5d2ada6fcab63b583c1cb6f53f72f3e7f6965f221fce6622` (`build/phase187-round3/roots/ROOT-I03_010/ROOT_CONTROLS.json`)
- root_controls_text: `0d3934845b3e80012c82c85adb35a90233708d9ddcaf5ea6490964aa25e223e3` (`build/phase187-round3/roots/ROOT-I03_010/ROOT_CONTROLS.txt`)
- validation_json: `e1d7b49db1cba9aeb57ff2334eb3d2db23a598099a65ebfe74c751eb4f0ebf29` (`build/phase187-round3/roots/ROOT-I03_010/VALIDATION_SUITE.json`)
- validation_text: `8b758465d113336d799bfb73a3a4a365b288ab78d2aa74e2d1ba8fc555df1435` (`build/phase187-round3/roots/ROOT-I03_010/VALIDATION_SUITE.txt`)
- independent_review_json: `a836e7cbf69721c164e602d70a14ed58d1833bebcf7fe387c9de08c900b1ce72` (`build/phase187-round3/roots/ROOT-I03_010/INDEPENDENT_FIX_REVIEW.json`)
- independent_review_text: `09b49a44741cffb1f4eeff877ba5c94e294cac8756450e1b5fa8eb33d04a4d8d` (`build/phase187-round3/roots/ROOT-I03_010/INDEPENDENT_FIX_REVIEW.txt`)
- post_merge_json: `e0cd79f38315b014fea2fa5eb665eaed134d4a5a41554021e210610274a0e4fa` (`build/phase187-round3/roots/ROOT-I03_010/POST_MERGE_SUMMARY.json`)
- post_merge_text: `6f1d0c94eb420163f9cb7220fcd1ef6fc14b9d7874229bdf84dc029c2b69a477` (`build/phase187-round3/roots/ROOT-I03_010/POST_MERGE_SUMMARY.txt`)
- status_doc: `7774eb2070a025cdf27dd1edc1a5bef5d66d8cea4b64377bba55cbe2561230e8` (`build/phase187-round3/roots/ROOT-I03_010/ROOT-I03_010_EXECUTION_STATUS.md`)
- source_modified: `10eb4a9d5ca4e4e9d95d5232d5fc304c5993f8722a8971cf760c9d6d67ed9c51` (`build/phase187-round3/roots/ROOT-I03_010/transaction/MODIFIED_FILE`)
- source_diff: `29e47bfc5705bd5b2da525a4365624e7faf635539590293de4b95c580e3680b0` (`build/phase187-round3/roots/ROOT-I03_010/transaction/DIFF_FILE`)
- source_verification: `1704f2ec9045b22a14051341f140be5aca86f4f7e4b961685f79614c205e7bab` (`build/phase187-round3/roots/ROOT-I03_010/transaction/VERIFICATION.txt`)
- source_rollback: `fb0f4d3d0ac3546c759b91a535d28aeb9bd8f5e8a369cc9c77e97055616abf35` (`build/phase187-round3/roots/ROOT-I03_010/transaction/ROLLBACK.sh`)
- ledger_modified: `3a5812f93aedb372ff31323dc7b3bbffa8a4761479d38ff15d478c0c10b4d889` (`build/phase187-round3/roots/ROOT-I03_010/ledger-state-transaction/MODIFIED_FILE`)
- ledger_state_modified: `f32d2bcb352407607e79f5a2ef59db500012aad384794615c2897434657d48d5` (`build/phase187-round3/roots/ROOT-I03_010/ledger-state-transaction/MODIFIED_STATE_FILE`)
- ledger_verification: `d6a825a779dd89c8fe0cb0c482e78ceff1dbe221724de0394ffe4c6d6c72a745` (`build/phase187-round3/roots/ROOT-I03_010/ledger-state-transaction/VERIFICATION.txt`)
- ledger_manifest: `ae8cbe8ed0fbff4b9d8f2d140df8aeea2fbe57c97e3230ca38a69709a2833d98` (`build/phase187-round3/roots/ROOT-I03_010/ledger-state-transaction/TRANSACTION_MANIFEST.json`)
- ledger_rollback: `a3cfdf1927e661acd93f41e52f9c261c605b51b7c9b284a35f6aa95541c785e1` (`build/phase187-round3/roots/ROOT-I03_010/ledger-state-transaction/ROLLBACK.sh`)
- final_status_json: `a0bfb0b2d4767b906c51e2cb33dc533d71e0f2deda97122bd6d602b8b59891bb` (`build/phase187-round3/roots/ROOT-I03_010/FINAL_STATUS_SWEEP.json`)
- final_status_text: `405ee98d7a414a68a3920368e9cb5e08e7a76cd6992019515369f14bd25b807e` (`build/phase187-round3/roots/ROOT-I03_010/FINAL_STATUS_SWEEP.txt`)
- final_audit_json: `760e2182539adb5f071063749ae19ed6b1e7df2eab37437aed22da3787435d60` (`build/phase187-round3/roots/ROOT-I03_010/FINAL_REOPEN_HASH_AUDIT.json`)
- final_audit_text: `0072fcdec8f938c1cdf27bfc10f166c3fabb04c77ca364a6d8fce31376ef597d` (`build/phase187-round3/roots/ROOT-I03_010/FINAL_REOPEN_HASH_AUDIT.txt`)
- fixture: `8670785ba748251c4b6b89f06f6aea5786fcf0fe5e99b0d62ec2a9b9a43a5709` (`build/phase187-global-remediation/root-control-fixtures/ROOT-I03_010.json`)
- review_artifact: `4d73affc21bddb80544218ac8ae1318b790edf31e66373e8c906d7e10b00a148` (`build/phase187-global-remediation/root-fix-review-artifacts/ROOT-I03_010.md`)
- cluster_index: `be60dea8b99c65fa6b700a5f5b7cc9d364ff444794932fcebd7f35de4fcfbc4e` (`build/phase187-round3/cluster-index.tsv`)
- cluster_manifest: `bbc255e163544eecfb2c5a6b60628ffcb5b8324b82d3e4093330a19f600055df` (`build/phase187-round3/cluster-manifest.json`)

AGENTS additive checkpoint rule: prepend only; prior bytes remain an exact suffix. No ROOT-I03_011 action starts in this window.


### Post-checkpoint audit binding and source hashes

The terminal checkpoint artifact is `build/phase187-round3/checkpoints/CHECKPOINT_20260828_ROOT-I03_010_TERMINAL.md` with SHA-256 `4a32ded69755fc0b6a4e50be8340b95ca97ba0734ff44fd07fdea61aa981a9f0`. After that artifact was bound, `python -B build/phase187-round3/roots/ROOT-I03_010/final_reopen_audit.py` returned literal `FINAL_REOPEN_HASH_AUDIT_PASS`, exit `0`, JSON SHA-256 `760e2182539adb5f071063749ae19ed6b1e7df2eab37437aed22da3787435d60`, text SHA-256 `0072fcdec8f938c1cdf27bfc10f166c3fabb04c77ca364a6d8fce31376ef597d`, and `reopened_artifact_count=50`; all invariants were true and the terminal checkpoint hash was `4a32ded69755fc0b6a4e50be8340b95ca97ba0734ff44fd07fdea61aa981a9f0`.

Frozen source file SHA-256 is `394638cd1275442c4fd7d1a5682d8166e2831722c001cd2ea0c9e49917f0f4b9`; current pre-fix source SHA-256 was `f253219a0e23ef2c317879654663b5102c231dd37ed236bc48f72cc76a8036d5`; final source SHA-256 is `10eb4a9d5ca4e4e9d95d5232d5fc304c5993f8722a8971cf760c9d6d67ed9c51`. The regression test changed from baseline SHA-256 `12ca19f9b8034fae4f0496a8313255e64eb4dc229ecf603ff0d85afe4d259854` to final SHA-256 `4b75ee8aea2da632517c5551b639a20bb16b6f28104d25762322591f19a785ca`.

Execution status remains terminal only for `ROOT-I03_010`: `fix_status_execution=MERGED_AND_VERIFIED`, `verification_status_execution=GREEN_VERIFIED`, `merge_status_execution=MERGED:cf44cde51c68117310523b39bb5beec18914a22c`; sealed literals remain `red_status=NOT RUN`, `global_red_status=RED_FIRST24_RECONCILED`, `red_result=CONFIRMED`. Dynamic status is B0 total `259`, execution terminal `13`, nonterminal `246`, `CONFIRMED=14`, `SATISFIED_BY_EXISTING_FIX=0`, `REFUTED=0`, `BLOCKED=245`. The first ordered pending row is `GR-02-RUN-CI-SKIP-EMPTY-RESULT` (order `1`); it was not started. No `ROOT-I03_011` action starts in this window.

AGENTS_PREVIOUS_SHA256=ac2dff972487968e5cf9899e7b820a32e623360dca6b5d731e322338b4e234a0
## FINAL LIVE CHECKPOINT (2026-08-28) — ROOT-I03_009 TERMINAL

Status: `PLAN_SEALED / RED_FIRST24_RECONCILED / B0_IN_PROGRESS / ROOT-I03_009_MERGED_AND_VERIFIED`.
This additive block is authoritative for the current single-root handoff; every prior AGENTS.md byte remains an exact suffix.

### Boundary and scope

HEAD `df740967936ab55a3f3dc97eb4f96389c218139c`; tree `b1c160c18c88ff2d97e278e442347cb0f19e3844`; branch `main`; origin/main `8d1223fc4cacf988da419b97c51a0f87bd965443`; tracked/staged diff `0`. Protected `.meta` SHA-256 `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc` is unchanged.

Only `ROOT-I03_009` (`R3-B0-134-ROOT-I03_009`, owner `OWNER_PACKAGE`, closure `187-R2-I03`) was processed in this window. No ROOT-I03_010 or other root, B1, Pilot B rerun, 247-root triage, ROOT-I02_010 reopen, DeepWiki, Round 2 closure review, push, reset, stash, ref rewrite, or frozen register/order/plan/history edit occurred. The next ordered root is `ROOT-I03_010` (order 135) and remains unstarted.

### Current revalidation and RED

Frozen source locator `Scripts/package/validate_local_entrypoints.py@ac2f040050f6cdb642b8d18be8ba2dc53cb7c316:31-60` was rebound on the pre-fix current tree. The bound range/body remained byte-identical; file-OID drift was not treated as invalidation. Fresh current revalidation returned `CONFIRMED` after preserving the initial dispatcher/harness-defect attempt as non-conclusive evidence. Revalidation JSON SHA `139fbf1c95bac94c1671252844481564a0ecce60f22bb55e9c85096fbf410810`; text SHA `e24a0fcd12375e26c8326f49508ac6b4b7ae2075187792ae9a50e3c48fa7bc5f`.

Fixture `build/phase187-global-remediation/root-control-fixtures/ROOT-I03_009.json` SHA `c3e2cbddcf139837859692162e500452a478f1305989aa75dc645eaa75641184` and typed-oracle SHA `2370080e6ce30d63613e6067371ade12475e4fe97b5a90618a6b24fa895b08d7` stayed unchanged. True RED command `python -B build/phase187-round3/roots/ROOT-I03_009/capture_red.py` captured the authorized focused regression on unmodified source: literal `RED_BASELINE_FAIL; ... subprocess.TimeoutExpired ...; Ran 1 test; FAILED (errors=1)`, exit `1`; full literal evidence `REGRESSION_RED.txt` SHA `31ab526fb0b8a61e37a320cad7c3879866e593c6516f3a103570718657b0e0ce`. Earlier invalid fixture attempts are retained and were not treated as product conclusions.

### Minimal fix and verification

The minimal fix changes only the `git_grep_failures` seam in `Scripts/package/validate_local_entrypoints.py`: a bounded `Popen.communicate` deadline, owned process-group/tree cleanup, bounded drain, and actionable timeout/missing-executable errors. The root regression is in `Scripts/package/regression_checks/test_validate_local_entrypoints.py`; no I03_007 mechanism or unrelated source was changed. Final source SHA `1802df4432c63829f74cfa2bd346da975161d89a07f34150cad943bea98771ae`; final test SHA `df8a7a5b9cee0c511e529a5bb5babe78f28dd6b5afe51c3ca46d5db760602c45`.

The final controlled-adapter production-seam probe returned literal `POST_FIX_TIMEOUT_CAUGHT=git grep timed out after 1s`, confirmed holder cleanup and sandbox removal, exit `0`; JSON SHA `042176fa8bc739a37fc99b6c856ae38353b8469aa76df8cc31824b94eba1f29e`, text SHA `a44c1b871c6df109c7ace6441a56340de04bd75faabc25d9323e71bc4f95a14c`. Packet controls returned `PACKET_CONTROLS_PASS` with GREEN/POSITIVE/NEGATIVE/ADJACENT exit `0`; JSON SHA `5a523cb811e180131d5e019156b4340c5a68629d18523d0238f412a4ca7e68ec`, text SHA `d4c9c5bbbb8cff4ce59c8415e5273a250cbf7bf17901420dc3cc4158c0260272`. Owner validation returned `OWNER_VALIDATION_PASS`, exit `0`; JSON SHA `af29abb36d8c78fef72ee9807c983fbf0ed9eb8348e5aab35e48cbe67f4be6a`, text SHA `eb653c3b85554afbe739623c971743ed7189aa4cd28fe50e028f67ea95339f39`. The final Phase186 package matrix returned `PACKAGE_MATRIX_VALIDATION_PASS`, exit `0`; JSON SHA `5a8b521f1b39096b3f8b47062de12e30dff3b4dab54d42aa5bfda6e3231915cd`, text SHA `e3c66e3f7a14ffe2dbe596afc7b4bdc3e1341244b7076c0203f1f3c3044bce88`. The seven managed/native/Windows/Linux/macOS/RMW/provider lanes returned `LANE_MATRIX_PASS`, exit `0`; JSON SHA `2cd5fc4fd6aac00ff3e4c5d9fe77670e271ce3862e4bd7ce87085e9926e17827`, text SHA `564d1832f803c7bfd4251ab8b587b2b737478fe9c8a08cbad79ce6dceb6c3087`.

### Review, commit, merge, and post-merge

Independent worktree review returned `INDEPENDENT_FIX_REVIEW_PASS`, exit `0`; JSON SHA `864b587b481643a2a0d37b68d4fd7c91bb2260dc7f1840bd4ab25bbcceb9a591`, text SHA `d30c6480e69df3a3642123a70db963e3675804c8cb5ef54be2fbf118a6a6d5e3`. The committed-boundary review also returned `COMMITTED_FIX_REVIEW_PASS`, exit `0`; JSON SHA `350d81f42b9b726aa16054bb1545aec10f8a891de78447a2557814d89a26aa92`, text SHA `4c287a7d1c726f50cab95762da9f94fa748ca02ce12ef8d181c0772bd2b716af`. The atomic serial commit is `06d60574e5d1886a3b0bbd83d9a9313f917cfc77` (`fix(187): bound local entrypoint git probes`) and contains exactly the source/test pair. `git merge --no-ff feature/global-remediation/root-i03_009 -m "merge(187): integrate ROOT-I03_009 remediation"` returned exit `0`; merge `df740967936ab55a3f3dc97eb4f96389c218139c` produced tree `b1c160c18c88ff2d97e278e442347cb0f19e3844`.

Post-merge command `python -B build/phase187-round3/roots/ROOT-I03_009/run_post_merge.py` returned `POST_MERGE_VALIDATION_PASS`, exit `0`; summary SHA `b386ba80721304bc0d4214de19ffe8fbabe10626be9da0747b79f7bc2cc1cc6d`; control `63c22445e42e56451ab5dc28ba3d021368424846df73aaa5da45330c736aae03`; owner `e6964b4e323c1acf7660b01a96d76c7615063968ffa1c4e8225b4618565061e9`; package `6dba18579aa39aa097069ed146b1257ce13ceff578ab4afa249e85dbd6ebf681`; local `2b59957f940a5eadbd81afd4374b2506cbd786b9d323427df526aedf34be1360`; py_compile `c10da0415a69996214d10337cf21419148992ef2afdd1555201c374cfaf297cd`. All affected closure validation remained `187-R2-I03`; no temporary residue remains.

### Source and ledger/state transactions

Source transaction quartet: `build/phase187-round3/roots/ROOT-I03_009/transaction/MODIFIED_FILE` SHA `1802df4432c63829f74cfa2bd346da975161d89a07f34150cad943bea98771ae`; `DIFF_FILE` `d33302ed22f5418060c83e51d912328bca4a17af299138d71251d0c318417da2`; `VERIFICATION.txt` `bffb30eac6e1f05711ebd99db6163be2c3e3937958457db54852a9c413d99c82`; executable `ROLLBACK.sh` `fb0f4d3d0ac3546c759b91a535d28aeb9bd8f5e8a369cc9c77e97055616abf35`; manifest `ec5cae46b6388cf8d627bbdf675167ba74fee69f4fcb82941dbd59e4efccfb41`. Its separate-copy rollback emitted `ROLLBACK_OK` then `ROLLBACK_NOOP`, exits `0,0`; restored copy equals originals and live modified snapshots remain changed.

Ledger/state transaction command `python -B build/phase187-round3/roots/ROOT-I03_009/sync_ledger_state.py` returned `LEDGER_STATE_TRANSACTION_APPLIED`, exit `0`. Live ledger SHA `099ca0620db83b45923065d87aa65330988159b3c201c3fdb7df66d4769f2f17` and state SHA `7475b9df14183080d95062710f5e1f388763bcac0ecd1f749bcc8790d7ed32d9`. Ledger diff `de47b15581a97f57b0c2f4b71b3d050935b81fd6ed5df005408f8ca79e1af8c3`, state diff `e29df974aceb1d555c700996be5c12634827a156a3209281eda9c9664ce59f90`, transaction verification `c422c1c69060551b37f5a837d8dbd0a6556712707462760c058af01a3d36947d`, manifest `8616af8870fcfdb3e5f6c54467e8b541f676a2c06dbef930e4b123b3f32cee56`, rollback `a3cfdf1927e661acd93f41e52f9c261c605b51b7c9b284a35f6aa95541c785e1`. The mutation set is limited to the ROOT-I03_009 execution/status/evidence/baseline fields and global `head/tree/execution_ledger_sha256`; frozen values are untouched. Sealed literals remain exactly `red_status=NOT RUN`, `global_red_status=RED_FIRST24_RECONCILED`, `red_result=CONFIRMED`. Ledger/state rollback emitted `ROLLBACK_OK` then `ROLLBACK_NOOP`, exits `0,0`, on a separate copy.

### Dynamic B0 counts and stop

Current execution-ledger B0 inventory was recomputed rather than hard-coded: total `259`, terminal `MERGED_AND_VERIFIED=12`, nonterminal `247`, `red_result=CONFIRMED` `13`, `red_result=BLOCKED` `246`, `SATISFIED_BY_EXISTING_FIX=0`, `REFUTED=0`. The immutable remaining-B0 index remains `247` rows (`CONFIRMED=2`, `BLOCKED=245`) as its provenance snapshot; this root is terminal in the execution ledger and is not started again.

The durable start checkpoint before this AGENTS update is `build/phase187-round3/checkpoints/CHECKPOINT_20260828_ROOT-I03_009_START.md` SHA `c24ae6576b9953b1ca0bbb67589973c42f0b9c237d4b479720260778bbd473a0`. This root is terminal and the window stops now. No repair or merge for `ROOT-I03_010` is started.

AGENTS_PREVIOUS_SHA256=97dd705442d05f3b730738a4de2c7066725d3d1cfa925e537ad6a6c03398acbb
## FINAL LIVE CHECKPOINT (2026-08-28) — ROOT-I03_008 TERMINAL

Status: `PLAN_SEALED / RED_FIRST24_RECONCILED / B0_IN_PROGRESS / ROOT-I03_008_MERGED_AND_VERIFIED`.
This additive block is the terminal checkpoint for the single authorized root; every prior AGENTS.md byte remains an exact suffix.

### Boundary and scope

HEAD `20aaf1b49d356022d1caaebf46ecf8f602c157a8`, tree `2429d9ad1dcaee9c6415f87297e1da38c1e912b3`, branch `main`; origin/main `8d1223fc4cacf988da419b97c51a0f87bd965443`; tracked/staged diff `0`. Protected `.meta` SHA-256 `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc` is unchanged.

Only `ROOT-I03_008` (`R3-B0-023-ROOT-I03_008`, owner `OWNER_PACKAGE`, closure `187-R2-I03`) was processed in this window. No other root, I03_009, B1, Pilot B rerun, 247-root triage, I02_010 reopen, DeepWiki, Round 2 closure review, push, reset, stash, ref rewrite, or frozen register/order/plan/history edit occurred.

### Anchor and bounded re-derivation

Frozen locator `Scripts/package/validate_source_generator_dll.py@66348745e50432de3eda1ad6b5e34e55dcb11fcd:233-264` was rebound after the I03_007 source edit: current bound `234-265`, frozen/current bound range SHA `7228b68df15b0dbd1b50304bdf3ee3e570f889f6464abb5999869e1561bbc76a`, `run_build` body and I03_008 non-update tail remained equal before this fix. `BOUNDARY_REVALIDATION_PASS` and `BOUNDED_REDERIVATION_PASS` were recorded; JSON SHA `a802bab8d64917d24cfef4b478eb694c4813227597267f983e90d439c8db2a5e`, bounded artifact SHA `1c55442122802e2538db351670924e930972e276f40c79c640d44f22de72dfaa`.

Fixture SHA `c504937afddcc64fc7093d4a8d9a9cfa74966c7727dcf757931000440ea78e80`, corrected oracle V3 SHA `aeeab628b213e9eba4d15b3c40c24bab09919323185e3d92141f8d7172cbc7eb`, typed-oracle SHA `6bc911a6f8ace5ca107ecbda2c5928a5d0b5a888db4aae7f2873d131e45d46a7` were revalidated. The initial dispatcher result remains retained as harness-defect evidence and was not used as a product conclusion.

### RED, fix, and verification

True RED command `python -B build/phase187-round3/roots/ROOT-I03_008/run_red_i03_008.py` emitted literal `RED_BASELINE_FAIL; SOURCE_POSITIVE_PASS; target_preexisted=True; target_hash_equal_checked=True; current_build_write_count=0; target_unchanged_after_no_output=True; partial_provenance_failure=True; TEMP_SOURCE_SANDBOX_REMOVED`, exit `1`; JSON SHA `881b44f86d44d23ee152e3f84d42c12d75b062813fa1b9472af951bdd3abbe5c`, text SHA `38c29d15c93e20b9ef0d4e7fe6e9f15a4d395d1d6c8ed5a1f4fe27f769bf886f`.

The minimal fix changes only `Scripts/package/validate_source_generator_dll.py` in `run_build`: it snapshots each expected regular output by `(st_size, st_mtime_ns)` before the build, rejects a missing or unchanged pre-existing output after a zero exit, and leaves stale bytes untouched. The sole root regression test is `test_preexisting_equal_artifact_without_current_build_write_is_rejected`; I03_007 partial-update rollback code and all unrelated seams are unchanged.

Production-seam probe command `python -B build/phase187-round3/roots/ROOT-I03_008/run_post_fix_real_probe.py` emitted `POST_FIX_REAL_PROBE_PASS; stale_rejected=True; stale_unchanged=True; fresh_write_accepted=True; empty_output_rejected=True; different_hash_rejected=True; TEMP_SOURCE_SANDBOX_REMOVED`, exit `0`; JSON SHA `dd54d291efacf072afd14b3eba0bd0888c242ac08f5f7b32eaf3ba0befa2dd4a`, text SHA `e1daa965e21ea3af2e15bd1e1d5f01f4bfdf77df413513d09befb23f86b5ddc8`.

Focused GREEN emitted `GREEN_SOURCE_PROBE_PASS; Ran 1 test; OK`, exit `0`, JSON SHA `a9a2e14cef6bcb0985c0e5fe710960cd63eaa21abae31cc7f9a590b02316e8c3`. Packet controls emitted `PACKET_CONTROLS_PASS; GREEN_CONTROL_PASS; SOURCE_POSITIVE_PASS; SOURCE_NEGATIVE_REJECT; ADJACENT_REGRESSION_PASS; CLEANUP_NO_TEMP_RESIDUE`, exit `0`, JSON SHA `d4d65fdb5594e929b815f394aa8292de39c96373263e7ac8a5add444fb8acc8a`.

Owner/package/static validation emitted `OWNER_PACKAGE_REGRESSION: exit=0 pass=True; PACKAGE_VALIDATOR: exit=0 pass=True; PY_COMPILE: exit=0 pass=True; GIT_DIFF_CHECK: exit=0 pass=True; VALIDATION_SUITE_PASS`, exit `0`, JSON SHA `a4d90217c2c6f792354354766895ea7c04ed3e4c489c73bebc1465e11655609e`. Independent review emitted `INDEPENDENT_FIX_REVIEW_PASS; changed_file_boundary_exact=True; source_added_lines_inside_run_build=True; test_added_lines_inside_root_method=True; stale_rejected=True; fresh_accepted=True`, exit `0`, JSON SHA `244351340d9bbf38373c4f0871a01bf4b9d568fcbf866c6df8b658fa17019f1c`.

### Commit, merge, and post-merge

Serial commit `82ab3ba43e61e99387959b09cbb2178a93c15af1` (`fix(187): authenticate current analyzer outputs`) contains exactly the source/test pair. No-ff merge `20aaf1b49d356022d1caaebf46ecf8f602c157a8` (`merge(187): integrate ROOT-I03_008 remediation`) produced tree `2429d9ad1dcaee9c6415f87297e1da38c1e912b3`. Post-merge command `python -B build/phase187-round3/roots/ROOT-I03_008/run_post_merge.py` emitted `POST_MERGE_VALIDATION_PASS; POST_MERGE_CONTROL=0; OWNER_PACKAGE_REGRESSION=0; PACKAGE_VALIDATOR=0; CLEANUP_NO_TEMP_RESIDUE`, exit `0`; summary SHA `ba7badbbc70804a1e6b48cef369949495b545c8561f716b00aab5a7d01828bbe`, control SHA `c311c62bea9be465617e0b84d7229402e5fef5c1312378d7827fcae0bf912039`, owner SHA `b0db85d18fd8187ddd45eafdd48e602d9cdb08a45d2c65d784d4c11cfac527ee`, package SHA `e92c179d3399b78d3ebef53d8a1d4d338467405a70b356772a7cb6547ac65a85`.

### Transaction and ledger/state evidence

Source transaction quartet paths and hashes: `build/phase187-round3/roots/ROOT-I03_008/transaction/MODIFIED_FILE` `246fd8e7fe8fc674a6f49a4898811b8469baeb188db18c3ce16bcc1341548022`; `DIFF_FILE` `0d13e50d5e501ff569875f71a62ff1fc2cf500aecbe49b9b7485c9ba6a7fdbd6`; `VERIFICATION.txt` `48d84e851133f392c6d8df535926c7185707ad318e5e5f5a17f3991b36399b85`; executable `ROLLBACK.sh` `fb0f4d3d0ac3546c759b91a535d28aeb9bd8f5e8a369cc9c77e97055616abf35`. Separate-copy rollback emitted `ROLLBACK_OK` then `ROLLBACK_NOOP`, exit `0,0`; restored copy equals originals and live modified snapshots remain changed.

Ledger/state transaction command `python -B build/phase187-round3/roots/ROOT-I03_008/sync_ledger_state.py` emitted `LEDGER_STATE_TRANSACTION_APPLIED`, exit `0`. Live ledger SHA `c40c8967f40b3302b0574979086a31ea3fe1ce82f58b369bed3f7bc03abac71b`; live state SHA `dd59a929819e86e13c12fb017f043e68c618aeec91704233f81af606a915d652`; transaction ledger diff `d920479e4694f9cda1a75ef6cd2338947fe19c250a342106b03ad779feac956f`; state diff `f8ce100193813a8e3ac8a391e588e38d110b008d6896cf549d0237d0a9089241`; rollback `a3cfdf1927e661acd93f41e52f9c261c605b51b7c9b284a35f6aa95541c785e1`; verification `73acd5142c9d78c4cd2f727c47d7b66e4cd96119dfd70bb014dcaf55a2dc8341`; manifest `a78a346a16e769adc6e1766c94a4ce409ad23d1221c95805066fc6ba907a73e8`. Its mutation set is limited to the ROOT-I03_008 row execution/source-baseline/notes fields and global state `head/tree/execution_ledger_sha256`; sealed literals remain exactly `red_status=NOT RUN`, `global_red_status=RED_FIRST24_RECONCILED`, `red_result=CONFIRMED`. Ledger/state rollback emitted `ROLLBACK_OK` then `ROLLBACK_NOOP`, exit `0,0` on a separate copy.

### Final hashes and stop

Pre-AGENTS final audit command `python -B build/phase187-round3/roots/ROOT-I03_008/final_reopen_audit.py` emitted `FINAL_REOPEN_HASH_AUDIT_PASS`, exit `0`; JSON SHA `47a8a7a9fed6b2982c4c13f823667888970d7f18f20e570485756ce9a3ddbce3`, text SHA `a91ce41f46ad4ca009ebc159d9e03751a9fb9fe2b5d37681ab80e1b15894aef6`, reopened evidence count `46`. Durable start checkpoint SHA before this block is `dbfbc3304a8a4eaa9acf1fcd59846e0de1f4e04a9c900609931bf666c0ba4784`.

Current source SHA `246fd8e7fe8fc674a6f49a4898811b8469baeb188db18c3ce16bcc1341548022`, test SHA `12ca19f9b8034fae4f0496a8313255e64eb4dc229ecf603ff0d85afe4d259854`, register SHA `ff4eaf066f5356f0ae12667947d2180397267713939729496e87e98fafe29329`, remediation order SHA `3aa265693fa6260e1bbda2cf70128552d5a04be98285da525241d74b2700f58e`, plan SHA `de765649ee98cdc92f30f0c1e954a35c2677c321565e4e793141510e57eae62f`, immutable ledger SHA `08fe94da2d35e2fc842f4cb2504533b15e610648ceaa2055158a5bee1b595a95`, protected `.meta` SHA `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`. Execution row is `fix_status_execution=MERGED_AND_VERIFIED`, `verification_status_execution=GREEN_VERIFIED`, `merge_status_execution=MERGED:20aaf1b49d356022d1caaebf46ecf8f602c157a8`; sealed literals remain `red_status=NOT RUN`, `global_red_status=RED_FIRST24_RECONCILED`, `red_result=CONFIRMED`.

This root is terminal. `ROOT-I03_009` and every other root remain unstarted and require fresh anchor revalidation in a later window. Stop now.

AGENTS_PREVIOUS_SHA256=f433076cf10979709037ce81e5f0f64520d5b0560404a63413e920e197dec76f
## FINAL EVIDENCE RECONCILIATION CHECKPOINT (2026-08-28) — ROOT-I03_007

Status: `PLAN_SEALED / RED_FIRST24_RECONCILED / B0_IN_PROGRESS / ROOT-I03_007_MERGED_AND_VERIFIED`.
This additive block supersedes prior ROOT-I03_007 checkpoint wording where evidence hashes changed; every prior byte remains an exact suffix.

Boundary: HEAD `ab4905adf7839f55416bb3d68eb291fb81733502`; tree `4dca04a4f283fdc3358f4987522e0300673f5fdf`; branch `main`; origin/main `8d1223fc4cacf988da419b97c51a0f87bd965443`; tracked/staged diff `0`; protected `.meta` SHA `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.

Serial commit `4f7b504587e1835d2998a0796f46b6e99e42fce3`; no-ff merge `ab4905adf7839f55416bb3d68eb291fb81733502`.

Evidence reconciliation command `python -B build/phase187-round3/roots/ROOT-I03_007/reconcile_root_evidence.py` produced `ROOT_EVIDENCE_RECONCILIATION_APPLIED`, exit `0`; current source transaction verification SHA `7e6aa2ad360a4c9df21ed6bc4960f851dd316253e5d402e7b9bed90924b0bd4e`; source transaction manifest SHA `cc3b70124edd7b53d0d410e95e7fa42306fcb0ac54e06005302da8401849a7af`; status docs SHA `ac70595de0c2cdfcdfdcd9557e14d8542e01889663f00ebb5c7bb8ef64dfadf1`.

Ledger/state apply `python -B build/phase187-round3/roots/ROOT-I03_007/sync_ledger_state.py` → `LEDGER_STATE_TRANSACTION_APPLIED`, exit `0`; ledger SHA `40cc6f35c1b63dc29ee3fc28632d5b406225b5cbb30c040a415e133bccc77cbe`; state SHA `2f9a766f8c98d3940a9c12d79fde6d3acbe2cf72a27c6ce893ae22f17e0b2daa`; ledger verification SHA `8ba1591109c8d3edf672680c81945382f200643fabc45b6e5208edabbb80211b`; ledger manifest SHA `d0093f4c08d1cb17c1fd54e211810046be3d658307a91e55bcad46bee1c2038a`; rollback log SHA `569b9f4f5a1fd7dad54c4514be94968df1b990ba5db0088d01a62415c6fc254c`; rollback literal `ROLLBACK_OK` then `ROLLBACK_NOOP`, exit `0,0`.

Final reopen `python -B build/phase187-round3/roots/ROOT-I03_007/final_reopen_audit.py` → `FINAL_REOPEN_HASH_AUDIT_PASS`, exit `0`; JSON SHA `052c003bd0d804428427c474b3878ae7ca9c31cd29f6279244125028458a9214`; text SHA `d860278f81896f12715fc7e68454c2f39fc74ed9389cb6b765b721247b5df59d`; reopened artifact count `68`.

AGENTS additive transaction previous SHA `b905d235d60db514bedf486f48ba0d776c39a85b1cba44e1b1cc0cdbd402940f`, suffix preserved; current transaction quartet hashes are recorded in `build/phase187-round3/roots/ROOT-I03_007/agents-transaction/TRANSACTION_MANIFEST.json`. Sealed literals remain `red_status=NOT RUN`, `global_red_status=RED_FIRST24_RECONCILED`, `red_result=CONFIRMED`.

ROOT-I03_007 is terminal. `ROOT-I03_008` and every other root remain unstarted; stop now and require fresh I03_008 anchor revalidation later.

AGENTS_PREVIOUS_SHA256=b905d235d60db514bedf486f48ba0d776c39a85b1cba44e1b1cc0cdbd402940f
## FINAL LIVE CHECKPOINT (2026-08-28) — ROOT-I03_007 TERMINAL HASH RECONCILIATION

Status: `PLAN_SEALED / RED_FIRST24_RECONCILED / B0_IN_PROGRESS / ROOT-I03_007_MERGED_AND_VERIFIED`.
This is an additive checkpoint for the single authorized root; every byte of the prior AGENTS.md remains an exact suffix.

### Final boundary and sealed inputs

HEAD `ab4905adf7839f55416bb3d68eb291fb81733502`, tree `4dca04a4f283fdc3358f4987522e0300673f5fdf`, branch `main`; origin/main `8d1223fc4cacf988da419b97c51a0f87bd965443`; tracked/staged diff `0`; protected `.meta` SHA-256 `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.

Frozen register SHA `ff4eaf066f5356f0ae12667947d2180397267713939729496e87e98fafe29329`; remediation order SHA `3aa265693fa6260e1bbda2cf70128552d5a04be98285da525241d74b2700f58e`; plan SHA `de765649ee98cdc92f30f0c1e954a35c2677c321565e4e793141510e57eae62f`; immutable ledger SHA `08fe94da2d35e2fc842f4cb2504533b15e610648ceaa2055158a5bee1b595a95`.

### Terminal evidence and commands

- Anchor `Scripts/package/validate_source_generator_dll.py@66348745e50432de3eda1ad6b5e34e55dcb11fcd:767-776` remained byte-identical in the frozen range; range SHA `d5f0efd44f9f0657de19cfcf5857170cf29347ed60fc93677d2e3d544770ab69`; fixture SHA `883b4e4be1ab6c7bef8d3536db73ee762eae7c3d0b4dab8cb4805dffc3d8d9e3`; corrected oracle SHA `be0e7fb238bf4ad7fae71eaa5fb376c6301c122aa1fa43893a026c4fa114a980`; typed oracle SHA `91bde3596cfe763b2ed5b16a47e804c3d8a8ed2f40542b52fffda074f2a2f478`.
- RED command `python -B build/phase187-round3/triage/ROOT-I03_007/run_red_i03_007.py` produced literal `RED_BASELINE_FAIL; SOURCE_POSITIVE_PASS; first destination changed=True; second destination unchanged=True; partial_update=True; TEMP_SANDBOX_REMOVED`, exit `1`; RED JSON SHA `4061a786e33792ec08e13281b66f4aefc3abd787a9e0cedbe3c5395ab72bff0a`.
- Minimal fix changed only the package source and ROOT-I03_007 regression test. Serial commit `4f7b504587e1835d2998a0796f46b6e99e42fce3`; no-ff merge commit `ab4905adf7839f55416bb3d68eb291fb81733502`; merge tree `4dca04a4f283fdc3358f4987522e0300673f5fdf`.
- Post-merge command `python -B build/phase187-round3/roots/ROOT-I03_007/run_post_merge.py` produced `POST_MERGE_VALIDATION_PASS`, exit `0`; control SHA `5b0656bcbc793203ea21b44a58cda516d861b91b66044ac5e7601242fa8b5b7b`; owner SHA `b532849abc8f19e37b24096ce9e74e8bbc208a45315c5419592c754de3debe00`; package SHA `55cf2d637368237dcfd0641df10c4b285c9d7f16cdfed4ecddcfd419281b20bd`.
- Ledger/state apply command `python -B build/phase187-round3/roots/ROOT-I03_007/sync_ledger_state.py` produced `LEDGER_STATE_TRANSACTION_APPLIED`, exit `0`; live ledger SHA `40cc6f35c1b63dc29ee3fc28632d5b406225b5cbb30c040a415e133bccc77cbe`; live state SHA `2f9a766f8c98d3940a9c12d79fde6d3acbe2cf72a27c6ce893ae22f17e0b2daa`; ledger verification SHA `8ba1591109c8d3edf672680c81945382f200643fabc45b6e5208edabbb80211b`; transaction manifest SHA `d0093f4c08d1cb17c1fd54e211810046be3d658307a91e55bcad46bee1c2038a`.
- Ledger rollback command `C:\Program Files\Git\bin\bash.exe --noprofile --norc -c 'cd \"/d/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox\" && bash build/phase187-round3/roots/ROOT-I03_007/ledger-state-transaction/ROLLBACK.sh build/phase187-round3/roots/ROOT-I03_007/ledger-state-transaction/rollback-rehearsal-final'` produced `ROLLBACK_OK` then `ROLLBACK_NOOP`, exit `0,0`; rollback log SHA `569b9f4f5a1fd7dad54c4514be94968df1b990ba5db0088d01a62415c6fc254c`; restored copy equals originals; live ledger/state stayed modified.
- Final reopen command `python -B build/phase187-round3/roots/ROOT-I03_007/final_reopen_audit.py` produced `FINAL_REOPEN_HASH_AUDIT_PASS`, exit `0`; JSON SHA `d6e5c2b8b88e5696fce28d9adf7e0beac7c67ab5f24c73af91bc7e91bff4b61d`; text SHA `d860278f81896f12715fc7e68454c2f39fc74ed9389cb6b765b721247b5df59d`; reopened artifacts `68`.

### Execution status and stop

Execution row: `fix_status_execution=MERGED_AND_VERIFIED`, `verification_status_execution=GREEN_VERIFIED`, `merge_status_execution=MERGED:ab4905adf7839f55416bb3d68eb291fb81733502`; sealed literals remain `red_status=NOT RUN`, `global_red_status=RED_FIRST24_RECONCILED`, `red_result=CONFIRMED`. Source transaction quartet: modified `7eb3a25ddcd89bb6b036118a2eeadb371481182abd6dfa02d4d8927efa0c7687`, diff `5e79f6df5027cba8b7e23a4b31dbafb9e81821bb6d8e49325869625a9055ce45`, verification `ff7e015c5b6dbf44ad5e0e0b1063a7808ecd7b453bf524c8b903d44e5d95c898`, rollback `17ea999c30a71be4111301e0adbda14ea442dc73c8e9d2b420d61ae1d7b2dbd7`.

This root is terminal and this window stops now. `ROOT-I03_008` and every other root were not started; no push, reset, stash, ref rewrite, Pilot B rerun, B1, DeepWiki, I02_010 reopen, or frozen-artifact edit occurred. The final AGENTS transaction hash and modified-file SHA are recorded in `build/phase187-round3/roots/ROOT-I03_007/agents-transaction/TRANSACTION_MANIFEST.json`.

AGENTS_PREVIOUS_SHA256=23301ce98c8c316654d019860181463838c51e3b4684dc991573896592a81a36
## LIVE CHECKPOINT (2026-08-28) — ROOT-I03_007 TERMINAL

Status: `PLAN_SEALED / RED_FIRST24_RECONCILED / B0_IN_PROGRESS / ROOT-I03_007_MERGED_AND_VERIFIED`.
This additive block supersedes the previous remaining-B0 triage checkpoint for the single authorized root; every prior AGENTS.md byte remains an exact suffix.

### Boundary and scope

HEAD `ab4905adf7839f55416bb3d68eb291fb81733502`, tree `4dca04a4f283fdc3358f4987522e0300673f5fdf`, branch `main`; origin/main remains `8d1223fc4cacf988da419b97c51a0f87bd965443`; tracked/staged diff is `0`. Protected `.meta` `Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/Phase186ManualInteractionTests.cs.meta` SHA-256 `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc` is unchanged.

Only `ROOT-I03_007` (`R3-B0-022-ROOT-I03_007`, owner `OWNER_PACKAGE`, closure `187-R2-I03`) was processed. No Pilot B rerun, no I03_008 or other root, no B1, no DeepWiki, no I02_010 reopen, no frozen register/order/plan/history edit, and no push/reset/stash/ref rewrite occurred.

### Anchor, RED, and fix

Source anchor `Scripts/package/validate_source_generator_dll.py@66348745e50432de3eda1ad6b5e34e55dcb11fcd:767-776` revalidated as `PRESENT_UNCHANGED_BYTE_IDENTICAL`; range SHA `d5f0efd44f9f0657de19cfcf5857170cf29347ed60fc93677d2e3d544770ab69`, body SHA `f42ad2770e8725b1ce4ddd7ee1657413e66516bab950c35f4e51a0dc6db2f18a`, artifact `build/phase187-round3/triage/ROOT-I03_007/BOUNDARY_REVALIDATION.json` SHA `6c0a8074b0ec61386bcc905eea93330ffd44d3081aa83e2cff3849680d83b059`.

Fixture `build/phase187-global-remediation/root-control-fixtures/ROOT-I03_007.json` SHA `883b4e4be1ab6c7bef8d3536db73ee762eae7c3d0b4dab8cb4805dffc3d8d9e3`; corrected oracle `build/phase187-round3/triage/ROOT-I03_007/CORRECTED_ORACLE_V3.txt` SHA `be0e7fb238bf4ad7fae71eaa5fb376c6301c122aa1fa43893a026c4fa114a980`; typed oracle SHA `91bde3596cfe763b2ed5b16a47e804c3d8a8ed2f40542b52fffda074f2a2f478`.

True RED command `python -B build/phase187-round3/triage/ROOT-I03_007/run_red_i03_007.py` returned literal `RED_BASELINE_FAIL; SOURCE_POSITIVE_PASS; first destination changed=True; second destination unchanged=True; partial_update=True; TEMP_SANDBOX_REMOVED`, exit `1`; evidence `RED_CURRENT_TREE.json` SHA `4061a786e33792ec08e13281b66f4aefc3abd787a9e0cedbe3c5395ab72bff0a`, text SHA `b0422b8708752ccce55f2888bdbcdda175646c4a41986b286326790ef7a5a7d1`; disposition `CONFIRMED`.

Minimal fix changed only `Scripts/package/validate_source_generator_dll.py` and its root regression `Scripts/package/regression_checks/test_validate_unity_package.py`: `validate_or_update` now snapshots checked-in analyzer destinations on `REPO_ROOT`, restores completed copies (or removes newly created destinations) on `OSError`/`shutil.Error`, and leaves the non-update mechanism unchanged. I03_008 mechanism is untouched.

### Verification and integration evidence

Baseline regression: `python -B -m unittest -v Scripts.package.regression_checks.test_validate_unity_package.ValidateSourceGeneratorDllTests.test_analyzer_update_rolls_back_when_a_later_artifact_copy_fails` → `Ran 1 test; FAILED (failures=1); AssertionError: 1 != None`, exit `1`, `build/phase187-round3/roots/ROOT-I03_007/REGRESSION_RED.txt` SHA `ac7e54271bfaeac3d769110fa8724606dbc5e4e6faffa8ffbfbf32e28743933a`.

Post-fix probe `python -B build/phase187-round3/roots/ROOT-I03_007/run_post_fix_probe.py` → `GREEN_SOURCE_PROBE_PASS`, exit `0`, JSON SHA `c35bcd2aa487094275171ca3e04fad17c43b3287c9525b5d5dad82bae954bac5`; packet controls → `GREEN_CONTROL_PASS`, `SOURCE_POSITIVE_PASS`, `SOURCE_NEGATIVE_REJECT`, `ADJACENT_REGRESSION_PASS`, all exit `0`, summary SHA `bb8e0fdea211e4666dfec313f7e2f062f7b6a08c2db73c40578c6c08032f4fd7`.

Owner regression `python -B -m unittest Scripts.package.regression_checks.test_validate_unity_package` → `Ran 38 tests; OK`, exit `0`, SHA `6adbb240919ebd48137866d79a41829255ce7acb3874231ee6ba4f0d39563243`; package validator → `validate_unity_package: 70 check(s) passed.`, exit `0`, SHA `d286640e883fa720139401f032473eb6f0c6bf85006355fe41998ae7fdacf4b1`; py_compile and `git diff --check` exit `0`. Independent review → `INDEPENDENT_FIX_REVIEW_PASS`, exit `0`, SHA `334152fcdf531cba1a47768b9d1461c6adf00a45b054ed92ba70fca03d418d0f`.

Serial commit `4f7b504587e1835d2998a0796f46b6e99e42fce3` (`fix(187): rollback analyzer updates on copy failure`) contained exactly the source/test pair. No-ff merge `ab4905adf7839f55416bb3d68eb291fb81733502` (`merge(187): integrate ROOT-I03_007 remediation`) produced tree `4dca04a4f283fdc3358f4987522e0300673f5fdf`. Post-merge command `python -B build/phase187-round3/roots/ROOT-I03_007/run_post_merge.py` returned `POST_MERGE_VALIDATION_PASS`, exit `0`; control SHA `5b0656bcbc793203ea21b44a58cda516d861b91b66044ac5e7601242fa8b5b7b`, owner SHA `b532849abc8f19e37b24096ce9e74e8bbc208a45315c5419592c754de3debe00`, package SHA `55cf2d637368237dcfd0641df10c4b285c9d7f16cdfed4ecddcfd419281b20bd`.

Source transaction quartet: `build/phase187-round3/roots/ROOT-I03_007/transaction/MODIFIED_FILE` `7eb3a25ddcd89bb6b036118a2eeadb371481182abd6dfa02d4d8927efa0c7687`; `DIFF_FILE` `5e79f6df5027cba8b7e23a4b31dbafb9e81821bb6d8e49325869625a9055ce45`; `VERIFICATION.txt` `ff7e015c5b6dbf44ad5e0e0b1063a7808ecd7b453bf524c8b903d44e5d95c898`; `ROLLBACK.sh` `17ea999c30a71be4111301e0adbda14ea442dc73c8e9d2b420d61ae1d7b2dbd7`. Separate-copy rollback literal outputs were `ROLLBACK_OK` then `ROLLBACK_NOOP`, exit `0,0`; restored snapshots equal originals and live modified files remained changed.

Ledger/state transaction `build/phase187-round3/roots/ROOT-I03_007/ledger-state-transaction`: ledger original `8891e45bb5f5e0c4fa72003578d988f06f5d7301b8439e6b632563869251da57` → modified `40cc6f35c1b63dc29ee3fc28632d5b406225b5cbb30c040a415e133bccc77cbe`; state original `9f643d881ff694116bd63639d1707007315db4f8df4b320e3ffd5a63a3cc65ee` → modified `2f9a766f8c98d3940a9c12d79fde6d3acbe2cf72a27c6ce893ae22f17e0b2daa`; rollback artifact `ROLLBACK_REHEARSAL.txt` SHA `7aec7d6100bb9d688027f2791041f8e842039fa94abdfd7d0730f4cdb8e078b9`, literal `ROLLBACK_OK` then `ROLLBACK_NOOP`, exit `0,0`.

Execution row now is `fix_status_execution=MERGED_AND_VERIFIED`, `verification_status_execution=GREEN_VERIFIED`, `merge_status_execution=MERGED:ab4905adf7839f55416bb3d68eb291fb81733502`; sealed literals remain exactly `red_status=NOT RUN`, `global_red_status=RED_FIRST24_RECONCILED`, `red_result=CONFIRMED`. Global state binds HEAD/tree and execution ledger SHA above; frozen register `ff4eaf066f5356f0ae12667947d2180397267713939729496e87e98fafe29329`, remediation order `3aa265693fa6260e1bbda2cf70128552d5a04be98285da525241d74b2700f58e`, plan `de765649ee98cdc92f30f0c1e954a35c2677c321565e4e793141510e57eae62f`, immutable ledger `08fe94da2d35e2fc842f4cb2504533b15e610648ceaa2055158a5bee1b595a95` remain unchanged.

Final reopen/hash audit returned literal `FINAL_REOPEN_HASH_AUDIT_PASS`, exit `0`; JSON SHA `2520b58f2922d62fc39bc9f578b3f713181b840e4e10f19ad0c9d99e33481bc5`, text SHA `d860278f81896f12715fc7e68454c2f39fc74ed9389cb6b765b721247b5df59d`, with 86 reopened artifacts and exact source/test changed-file boundary.

### Remaining B0 state and stop

The immutable triage index remains the 247-root snapshot (index SHA `be60dea8b99c65fa6b700a5f5b7cc9d364ff444794932fcebd7f35de4fcfbc4e`, manifest SHA `bbc255e163544eecfb2c5a6b60628ffcb5b8324b82d3e4093330a19f600055df`). After this terminal root, pending nonterminal B0 is `246`: `CONFIRMED=1` (`ROOT-I03_008`), `SATISFIED_BY_EXISTING_FIX=0`, `REFUTED=0`, `BLOCKED=245`. First next cluster is `R3-B0-023-ROOT-I03_008`; it was not started and requires fresh current-anchor revalidation in a later window. Stop here.

AGENTS_PREVIOUS_SHA256=8a150a14cb3f9d7011c93ad36f46f42302abdd6f56be348a612c51d103c46412
## LIVE CHECKPOINT (2026-08-28) — ROUND3 REMAINING-B0 TRIAGE TERMINAL

Status: `PLAN_SEALED / RED_FIRST24_RECONCILED / B0_IN_PROGRESS / REMAINING_B0_TRIAGE_COMPLETE`.
This block supersedes every lower checkpoint for current handoff state; all lower blocks remain evidence.
The authorized Task 1 + Task 2 docs-only window is terminal. No product/test fix, feature branch, commit, merge, push, ref rewrite, Pilot B rerun, ROOT-I02_010 reopen, B1 start, DeepWiki pass, or Round 2 closure review occurred.

Current boundary: HEAD `fe4a9f5457c2db0a59d22fa6213a7e22566c7c32`, tree `b5983c96ba2dc3c07119ccbc1e8e17538f596c10`, branch `main`; origin/main remains `8d1223fc4cacf988da419b97c51a0f87bd965443`; tracked/staged diff `0`. Protected `.meta` remains `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.

### Remaining-B0 Task 1 + Task 2 terminal result

The current frozen register/order/execution overlay was re-read rather than using an old count. Dynamic B0 inventory: `259` total, `12` terminal excluded, `247` remaining nonterminal roots. Pilot A/B roots reintroduced into the pending set: `0`.

Classification counts: `CONFIRMED=2`, `SATISFIED_BY_EXISTING_FIX=0`, `REFUTED=0`, `BLOCKED=245`. The two current-tree confirmations are `ROOT-I03_007` and `ROOT-I03_008`; their corrected in-memory probes reached the intended seam after retaining the initial dispatcher harness-defect evidence. Ten other remaining first-RED roots are blocked by missing Unity/POSIX semantics or an unreached probe seam; the other 235 roots retain literal `first_red=NO` and are blocked rather than inferred from source shape.

The index contains `247` independent singleton rows because no two remaining roots share the exact source/mechanism/owner/validation signature. Anchor states: `180` byte-identical, `29` unchanged anchored ranges with file-OID drift only, `24` shifted/changed ranges whose exact frozen fragment remains present, and `14` changed claim surfaces with bounded re-derivation artifacts. File-OID drift alone was not treated as invalidation.

First repairable cluster: `R3-B0-022-ROOT-I03_007`. It is the next action only; this checkpoint does not start a repair. The second confirmed cluster is `R3-B0-023-ROOT-I03_008`.

### Terminal artifacts

- `build/phase187-round3/cluster-index.tsv` / SHA-256 `be60dea8b99c65fa6b700a5f5b7cc9d364ff444794932fcebd7f35de4fcfbc4e`
- `build/phase187-round3/cluster-manifest.json` / SHA-256 `bbc255e163544eecfb2c5a6b60628ffcb5b8324b82d3e4093330a19f600055df`
- `build/phase187-round3/triage/TRIAGE_RESULTS.json` / SHA-256 `fab46b74fa47146fce84a33dbff2f8a9ac11989fd5a2a082417a644745cc1f2b`
- `build/phase187-round3/triage/ANCHOR_TRIAGE_BATCH.json` / SHA-256 `090edfa785375d26447124bade1f8ad8e4fec7ba1d3e0be5b63023ac31504faf`
- `build/phase187-round3/triage/CURRENT_ORACLE_BATCH.json` / SHA-256 `1df63014a0a668e24f395b68a8557cda4fbb9603f90ff1f24cf3a6f80f905ea8`
- `build/phase187-round3/triage/BOUNDED_REDERIVATION_INDEX.json` / SHA-256 `d66fea423c349cd1272467d91dc877b8e16bb1d40da0e3ab5aa469872118327d`
- `build/phase187-round3/triage/FINAL_VALIDATION.json` / SHA-256 `fbe6c35e3cc7b656dcb596d6f938fcc31cb1f846c82dd283a00e87a4f1a1e655`
- `build/phase187-round3/checkpoints/CHECKPOINT_20260828_REMAINING_B0_TRIAGE_VALIDATION.md` / SHA-256 `f0853f18746d1cb3d0197c0fde4f2a9e7db172b796666d210478211cb4e14f28`

Execution overlay synchronization touched only the existing triage evidence/baseline fields for the 247 remaining rows: `red_result`, `red_evidence_path`, `red_evidence_sha256`, `red_cleanup_status`, `source_baseline_head`, and `source_baseline_tree`. The sealed `red_status=NOT RUN`, `global_red_status`, and all fix/verification/merge execution fields remain unchanged. Execution ledger SHA-256 is `8891e45bb5f5e0c4fa72003578d988f06f5d7301b8439e6b632563869251da57`; global state SHA-256 is `9f643d881ff694116bd63639d1707007315db4f8df4b320e3ffd5a63a3cc65ee`.

Fresh final validation returned literal `FINAL_VALIDATION_PASS`; the overlay validator returned `BASELINE_LEDGER_STATE_PASS` and `MODIFIED_LEDGER_STATE_PASS`, and the separate-copy rollback rehearsal returned `ROLLBACK_OK` then `ROLLBACK_NOOP`. Frozen hashes remain register `ff4eaf066f5356f0ae12667947d2180397267713939729496e87e98fafe29329`, order `3aa265693fa6260e1bbda2cf70128552d5a04be98285da525241d74b2700f58e`, plan `de765649ee98cdc92f30f0c1e954a35c2677c321565e4e793141510e57eae62f`, and immutable ledger `08fe94da2d35e2fc842f4cb2504533b15e610648ceaa2055158a5bee1b595a95`.

Docs-only additive rule: this checkpoint is a prepend; the prior `AGENTS.md` bytes (pre-update SHA-256 `0302fdc92300e80de1da8fe44db9aef05d6dca59541584683c5c1a60a67cdd6a`) remain an exact suffix.
## LIVE CHECKPOINT (2026-08-28) — ROUND3 PILOT B TERMINAL / DOCS-ONLY HANDOFF

Status: `PLAN_SEALED / RED_FIRST24_RECONCILED / B0_IN_PROGRESS / PILOT_B_COMPLETE_TERMINAL`.
This block supersedes every lower checkpoint for current handoff state; all lower blocks remain evidence.

`R3-PILOT-B-PACKAGE = PILOT_B_COMPLETE_TERMINAL`.

| root | terminal merge |
|---|---|
| `ROOT-I03_002` | `38e0c353f50a37528a7d8cf7fd929d24c53d01cc` |
| `ROOT-I03_006` | `bb62070d1b0fc1f9e1d5a124531aca207cef0d77` |
| `ROOT-I03_011` | `fe4a9f5457c2db0a59d22fa6213a7e22566c7c32` |

Current boundary: HEAD `fe4a9f5457c2db0a59d22fa6213a7e22566c7c32`, tree
`b5983c96ba2dc3c07119ccbc1e8e17538f596c10`, branch `main`; origin/main remains frozen at
`8d1223fc4cacf988da419b97c51a0f87bd965443`. Execution ledger SHA-256
`ca91bdada2a4ca8bee6874b575e79a2ed5d2703f636d8897807848b2678d474f`; global state SHA-256
`ab6c68215a09161827e070d6728eee3cc3b55c92da39a8d588d8dad4dff713da`.

Pilot B is terminal and **must not be rerun**. Do not rerun its root RED/GREEN, shared owner suite,
independent review, cluster rollback, shared post-merge/global validation, or terminal artifact reopen.

**Next action:** remaining B0 only — build/update the remaining-B0 cluster index, then perform bounded
triage against the current checkout and frozen inputs. This docs-only handoff does not authorize
product code changes.

**Stop rules:** do not start B1. Do not reopen `ROOT-I02_010` or its historical evidence debt. Do
not modify the frozen plan/register/order or historical evidence.

Terminal source checkpoint:
`build/phase187-round3/checkpoints/CHECKPOINT_20260828_PILOT_B_TERMINAL.md` / SHA-256
`4a073f93ab2d9b04db3d0de7b105789225cba2be9eb4f3f28604ea4ab1a3832d`.

Docs-only correction: `AGENTS.md` additive prepend only; all prior `AGENTS.md` bytes remain an exact
suffix. No product/test, ledger/state, or frozen artifact bytes changed.

## LIVE CHECKPOINT (2026-08-27) — B0 IN PROGRESS / ROOT-I02_014 MERGED

Status: `PLAN_SEALED / RED_FIRST24_RECONCILED / B0_IN_PROGRESS`, `red_executed_count=24/24`.
This block supersedes the ROOT-I02_014 re-derivation checkpoint below it for current state; that
block and every lower section remain evidence.

Current HEAD `db188899dfb774a5b083259a2f2dedbafc9ddad8`, tree
`7add958cc4eda1c62d217d44db1a39a7bc9c31c0`, branch `main`, origin/main still frozen at
`8d1223fc4cacf988da419b97c51a0f87bd965443` (nothing pushed, no ref rewritten), tracked/staged diff
`0`, `git diff --check` exit `0`, protected `.meta`
`a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.

### ROOT-I02_014 — MERGED_AND_VERIFIED

Serial commit `2b727f425`, no-ff merge `db188899d`, status doc
`ROOT-I02_014_EXECUTION_STATUS.md`. Bound to the **residual oracle**
(`ce30c415d4706fa9279e4c4ceef8d85c68d00cf9a1689aadbd24005f6c7de96d`) and the AST function body —
the frozen `174-206` anchor was deliberately not reused, being content-overlapping with
`ROOT-I02_003` and short of the function end.

`m_EditorVersion` is untrusted project metadata joined straight into a filesystem path. An absolute
value discarded the Hub prefix; leading parent segments walked out of it; appending
`Editor/Unity.exe` neutralised neither. `ROOT-I02_003` already required a resolved regular
executable, but resolving canonicalises a path without constraining where it may point. The fix
validates the pinned value as a version component before any join, and accepts a candidate only
when it resolves beneath its own Hub version root, tested **after** resolution so a reparse point
inside the version root cannot redirect the launch. Applied to all three platform branches.

**Scope discipline.** C1 containment, C2 version-component validation and C5 absolute/parent escape
are closed here. **C3 stays closed by `ROOT-I02_003` and is reused, not re-claimed** —
`contained_unity_candidate` calls `accepted_unity_candidate` and that gate's body is unmodified.
**C4 Editor identity is out of scope and is NOT claimed as fixed**; the independent review asserts
no product-identity, signature or version-resource check was introduced.

RED baseline on unmodified current source: `Ran 37 tests; FAILED (failures=4); exit 1` — the four
rejection tests failed and the acceptance test already passed, with no skips, so the symlink case
really executed. After the fix: focused 37/37, full release-tooling 117/117, `--phase134-31` 23/23,
`--phase163-24` 12/12, console runner `All checks passed.` with FAIL count 0, all four bound
controls, independent review 26/26 on both worktree and committed tree, rollback
`ROLLBACK_OK` → `ROLLBACK_NOOP` on a separate copy with the live tree untouched, and
`POST_MERGE_VALIDATION_PASS` with the full suite and console runner re-run green. No non-pass
occurred in this transaction. Both tracked Python files remain LF.

Execution overlay: `fix_status_execution=MERGED_AND_VERIFIED`,
`verification_status_execution=GREEN_VERIFIED`,
`merge_status_execution=MERGED:db188899dfb774a5b083259a2f2dedbafc9ddad8`; `red_result` stays
`CONFIRMED` and `red_status` stays literally `NOT RUN` per the sealed plan. Only the `ROOT-I02_014`
row was written; the mutation set was asserted against the ORIGINAL file on disk.
Execution ledger SHA-256 `2c48099b09f01e98f8110c7d79d1451d6057d7c428cb34a08d0b18605af7c111`;
global state SHA-256 `35c068ebd51e53fbb68229093fff2d49963ed0e209271cbc029e6a0927496c46`.

Scope note carried in the status doc: containment covers the project-pinned tier that this root
names; the generic Hub fallback globs real directories rather than joining untrusted metadata. A
genuine executable placed inside the correct Hub version root is still accepted with no product
check — that is C4 and remains open.

### Remaining B0 queue

`ROOT-I03_002`, `ROOT-I03_006`, `ROOT-I03_011` remain confirmed and not yet remediated. **No I03
root was started in this window.** The carried-forward `ROOT-I02_010` evidence-harness item
(`BLOCKED_EVIDENCE_CORRECTION_SNAPSHOT_DRIFT.md`: `status=PASS` while
`seeded_equals_modified=false`) is still unresolved and `ROOT-I02_010` was not reopened.

## LIVE CHECKPOINT (2026-08-27) — PHASE187_ROUND3_ACCELERATED_REMEDIATION_PLAN

Status: `ROUND3_PROTOCOL_ADOPTED / B0_IN_PROGRESS / I02_014_MERGED`.
This is an execution-cadence amendment after the sealed Round 2 37/37 closure
review. It supplements, and does not rewrite, the frozen B0 register, order, root
identities, or historical evidence.

Plan: `Plan/187/187_ROUND3_ACCELERATED_REMEDIATION_PLAN.md`
Plan SHA-256: `de765649ee98cdc92f30f0c1e954a35c2677c321565e4e793141510e57eae62f`
Frozen register SHA-256: `ff4eaf066f5356f0ae12667947d2180397267713939729496e87e98fafe29329`
Remediation order SHA-256: `3aa265693fa6260e1bbda2cf70128552d5a04be98285da525241d74b2700f58e`
Execution ledger SHA-256: `2c48099b09f01e98f8110c7d79d1451d6057d7c428cb34a08d0b18605af7c111`
Global state SHA-256: `35c068ebd51e53fbb68229093fff2d49963ed0e209271cbc029e6a0927496c46`

Round 3 rules: confirmed current roots use RED → minimal fix → GREEN; uncertain
roots receive bounded triage; blocked/refuted roots receive disposition only. Root
commits and ledger rows remain separate, while expensive validation may be shared
within a demonstrably common code cluster. DeepWiki and repeated closure-review
cycles are not part of this execution stage.

Pilot A is terminal: `ROOT-I02_014` merged as `db188899dfb774a5b083259a2f2dedbafc9ddad8`
with `MERGED_AND_VERIFIED`. Pilot B is the package cluster
`ROOT-I03_002`, `ROOT-I03_006`, `ROOT-I03_011`.

Current boundary: `HEAD=db188899dfb774a5b083259a2f2dedbafc9ddad8`,
tree=`7add958cc4eda1c62d217d44db1a39a7bc9c31c0`, branch `main`, origin/main
remains frozen at `8d1223fc4cacf988da419b97c51a0f87bd965443`; tracked/staged diff is
`0`. Protected `.meta` remains unchanged.

Round3 plan adoption did not touch the I02_014 product diff. The next code action is
the package-cluster pilot; no new DeepWiki or closure-review cycle is required.
## LIVE CHECKPOINT (2026-08-27) — B0 / ROOT-I02_014 RE-DERIVATION TERMINAL (PARTIAL)

Status: `PLAN_SEALED / RED_FIRST24_RECONCILED / B0_IN_PROGRESS`, `red_executed_count=24/24`.
This block supersedes the ROOT-I02_010 checkpoint immediately below it for current state; that
block and every lower section remain evidence.

### ROOT-I02_014 re-derivation — terminal, disposition PARTIAL

Anchor `Scripts/unity_build/unity_il2cpp.py@e70f26f6136ecc5bdd5c96e0b0c99503730efca3:174-206` is
`find_unity_from_project_version`, whose three platform branches were already rewritten by the
merged `ROOT-I02_003`. The anchored range is **content-overlapping**, so the OID-drift rule does
not clear it and the old line-number binding is not reused. Bound by AST body instead: frozen
174-213 (`47c57b0c…`), current 209-251 (`a14130d5…`). The frozen anchor `174-206` has
`covers_whole_function=false` — it truncates seven lines including the fallback `print` and
`return None`.

Four replays in separate sandboxes: frozen verbatim **confirmed**; current verbatim **refuted**
*for a fixture reason only* — the frozen probe plants the escape target as text and cannot pass the
post-`ROOT-I02_003` `MZ` gate; current residual with a real `MZ` executable **confirmed**; frozen
residual **confirmed**. `residual_oracle_agreement=true`. Replay B must never be read as closure.

Claim adjudication (un-stubbed): **C3 regular executable CLOSED** by `accepted_unity_candidate`;
**C1 canonical containment, C2 Unity version component validation, C5 absolute-reset / leading `..`
neutralisation remain OPEN**; **C4 Editor identity** remains open and out of scope per the
`ROOT-I02_003` scope note. `ROOT-I02_003` canonicalises the returned path (`resolve(strict=True)`)
without constraining where it may point — canonicalising is not containment.

- `red_result` remains **`CONFIRMED`**.
- `fix_status_execution` / `verification_status_execution` / `merge_status_execution` remain
  **`NOT_STARTED` / `NOT_RUN` / `NOT_AUTHORIZED`**.
- **Product code and tests unchanged.** Source SHA-256
  `46ddd29370114c862763758536f76a9ff286655231eb71ad27e7f1800f415768` identical before and after the
  window; HEAD `c9bf70eb7a73df9c5a0c6ba79771a53b1c184488`, tree
  `a8a44d3593928a5e3c8ed2a61fc4f20a2c7c1782`, tracked/staged diff `0`, `git diff --check` exit `0`,
  protected `.meta` `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
- Execution ledger SHA-256 `823a1db02478ddff2b83f724f2fc6327fee4815215c82a8a7325638c3a59081c`
  (only the `ROOT-I02_014` `notes` cell changed; mutation set asserted against the ORIGINAL file on
  disk as exactly `[(ROOT-I02_014, notes)]`).
- Global state SHA-256 `3d40ba4eb0e065047902ae7a34ca0d785be1a933bb204f594cc55edfa5b7c0bb`.
- Residual oracle SHA-256 `ce30c415d4706fa9279e4c4ceef8d85c68d00cf9a1689aadbd24005f6c7de96d`
  (`build/phase187-global-remediation/rederivation-root-i02_014/RESIDUAL_ORACLE.json`), bound to the
  current source and function-body hashes. The frozen fixture `effbd5ab…` and typed oracle
  `3a0eb1db…` are insufficient against current bytes; they stay unedited as historical evidence and
  are **not** the execution binding.

Ledger and state rollbacks were both rehearsed on separate copies: literal `ROLLBACK_OK` then
`ROLLBACK_NOOP`, exit `0` each, restored bytes equal `ORIGINAL_FILE`, live files untouched. The
rehearsal predicate explicitly includes `seeded_equals_modified`, the field whose omission caused
the false green recorded in `BLOCKED_EVIDENCE_CORRECTION_SNAPSHOT_DRIFT.md`.

**Carried forward, not actioned here:** that same blocker records
`remediation-root-i02_010/run_evidence_correction_rehearsal.py` reporting `status=PASS` while
`seeded_equals_modified=false`. It remains an unresolved false green in an `ROOT-I02_010` evidence
harness. `ROOT-I02_010` is terminal and was not reopened in this window.

**Next action:** the formal `ROOT-I02_014` RED-before-fix transaction on
`feature/global-remediation/root-i02_014`, against `RESIDUAL_ORACLE.json`, covering C1, C2 and C5.
C4 must not be claimed as fixed. No I03 root starts until `ROOT-I02_014` is terminally resolved.

## LIVE CHECKPOINT (2026-08-27) — B0 IN PROGRESS / ROOT-I02_010 MERGED

Status: `PLAN_SEALED / RED_FIRST24_RECONCILED / B0_IN_PROGRESS`, `red_executed_count=24/24`.
Scope: global remediation execution after the 37-closure second-round consolidation.

### B0 current state

Current HEAD `c9bf70eb7a73df9c5a0c6ba79771a53b1c184488`, tree `a8a44d3593928a5e3c8ed2a61fc4f20a2c7c1782`,
branch `main`, origin/main remains frozen at `8d1223fc4cacf988da419b97c51a0f87bd965443`,
tracked/staged diff `0`, protected `.meta` unchanged at
`a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.

| root | state | serial commit | merge commit | status doc |
|---|---|---|---|---|
| GR-02-RUN-CI-SKIP-EMPTY-RESULT | MERGED_AND_VERIFIED (pre-RED) | `859293320` | `6bd1cf946` | existing |
| ROOT-I01_001 | MERGED_AND_VERIFIED (pre-RED) | `b5f9b3929` | `736823b00` | existing |
| ROOT-I01_002 | MERGED_AND_VERIFIED (pre-RED) | `6c953a46c` | `a0eb409de` | existing |
| ROOT-I02_002 | MERGED_AND_VERIFIED (RED-confirmed) | `996153a59` | `15066e75f` | existing |
| ROOT-I02_003 | MERGED_AND_VERIFIED (RED-confirmed) | `cb9416c93` | `d72e03693` | existing |
| ROOT-I02_004 | MERGED_AND_VERIFIED (RED-confirmed, re-derived) | `2f7b13ca5` | `53668d615` | existing |
| ROOT-I02_009 | MERGED_AND_VERIFIED (RED-confirmed) | `2aee9cb95` | `1c8194f9a` | existing |
| ROOT-I02_010 | MERGED_AND_VERIFIED (RED-confirmed) | `fb85492ef` | `c9bf70eb7` | `ROOT-I02_010_EXECUTION_STATUS.md` |

ROOT-I02_010 bounded the POSIX `ps` residual diagnostic with `PROCESS_DIAGNOSTIC_TIMEOUT_SECONDS = 5`
and catches `subprocess.TimeoutExpired` with `OSError` in the fail-closed fallback. The serial
commit contains only the source and regression test files; the no-ff merge is verified.
Focused 32/32, full release-tooling 112/112, Phase134-31 23/23, Phase163-24 12/12,
console, four bound controls, independent worktree/committed review (23/23), and post-merge
suite all passed. Product transaction rollback and ledger/state rollback both returned
`ROLLBACK_OK` then `ROLLBACK_NOOP` on separate copies with live state untouched.

Execution overlay row ROOT-I02_010 is `fix_status_execution=MERGED_AND_VERIFIED`,
`verification_status_execution=GREEN_VERIFIED`, `merge_status_execution=MERGED:c9bf70eb7a73df9c5a0c6ba79771a53b1c184488`;
`red_status` remains literally `NOT RUN` per the sealed plan. Global state is bound to the
merged HEAD/tree and execution ledger SHA `c5a2eb734cf14a894de5335e04d2e15df51e0fbf5cb22e153d210fde7f5c047a`.
The sealed official validator reports `PHASE187_ROUND2_EXECUTION_VALIDATION_FAIL` only because
its frozen-ref gate still requires origin/main=`8d1223fc`; evidence is recorded, and no push or ref
rewrite is performed.

### Remaining B0 queue

`ROOT-I02_014`, `ROOT-I03_002`, `ROOT-I03_006`, `ROOT-I03_011` remain confirmed and not yet remediated.
The next ordered root is `ROOT-I02_014`; do not start it in this window. Revalidate its frozen
anchored-range bytes first; file-OID drift alone is not invalidation, but range drift is.

This checkpoint supersedes the immediately following ROOT-I02_009 B0 checkpoint. All lower
historical Phase187 sections remain evidence and instructions unless a newer top checkpoint says otherwise.

## LIVE CHECKPOINT (2026-08-27) — B0 IN PROGRESS / ROOT-I02_009 MERGED

Status: `PLAN_SEALED / RED_FIRST24_RECONCILED / B0_IN_PROGRESS`, `red_executed_count=24/24`.
Scope: global remediation execution after the 37-closure second-round consolidation.

### B0 progress

Baseline moves with each merge. Current HEAD `1c8194f9a1f3000d522bf73d54daa5f4972b38b9`,
tree `9c658816f77c5cafc799436cd82a66823653c172`, tracked/staged diff `0`, protected `.meta`
unchanged at `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.

| root | state | serial commit | merge commit | status doc |
|---|---|---|---|---|
| GR-02-RUN-CI-SKIP-EMPTY-RESULT | MERGED_AND_VERIFIED (pre-RED) | `859293320` | `6bd1cf946` | `GR-02-RUN-CI-SKIP-EMPTY-RESULT_EXECUTION_STATUS.md` |
| ROOT-I01_001 | MERGED_AND_VERIFIED (pre-RED) | `b5f9b3929` | `736823b00` | `ROOT-I01_001_EXECUTION_STATUS.md` |
| ROOT-I01_002 | MERGED_AND_VERIFIED (pre-RED) | `6c953a46c` | `a0eb409de` | `ROOT-I01_002_EXECUTION_STATUS.md` |
| ROOT-I02_002 | MERGED_AND_VERIFIED (RED-confirmed) | `996153a59` | `15066e75f` | `ROOT-I02_002_EXECUTION_STATUS.md` |
| ROOT-I02_003 | MERGED_AND_VERIFIED (RED-confirmed) | `cb9416c93` | `d72e03693` | `ROOT-I02_003_EXECUTION_STATUS.md` |
| ROOT-I02_004 | MERGED_AND_VERIFIED (RED-confirmed, re-derived) | `2f7b13ca5` | `53668d615` | `ROOT-I02_004_EXECUTION_STATUS.md` |
| ROOT-I02_009 | MERGED_AND_VERIFIED (RED-confirmed) | `2aee9cb95` | `1c8194f9a` | `ROOT-I02_009_EXECUTION_STATUS.md` |

`ROOT-I02_002` closed the `validate_generated_artifacts` path-type hole in
`Scripts/unity_build/unity_il2cpp.py`: `exists()` alone satisfied the preflight, and the
empty-file test was guarded by `is_file()`, so a directory, a directory symlink/reparse point,
or arbitrary sentinel bytes at a required path passed. RED baseline was 2 failing new tests on
the unmodified source with the positive control already passing; after the fix 13/13 focused,
93/93 full release, 12/12 `--phase163-24`, all four bound controls, `POST_MERGE_VALIDATION_PASS`,
and `INDEPENDENT_FIX_REVIEW_PASS` on both the worktree and the committed tree.
Scope note recorded in its status doc: a regular file holding wrong-but-nonempty bytes still
passes; content/provenance authentication is out of scope and is not claimed.

`ROOT-I02_003` gated Unity discovery on executable type and identity. Every tier accepted on
`Path.exists()` alone, so a directory, a reparse point, or an ordinary text file could win a tier;
explicit and environment paths were checked against the caller cwd while Unity starts with cwd
rebased to the repo root; and a failed process start escaped main's exit taxonomy as a raw
traceback. One shared `accepted_unity_candidate` gate now covers all four tiers, because the
register warns that a fix limited to `--unity` or the environment leaves the exact-pin and Hub
tiers false-green. Scope note in its status doc: the gate proves regular-file type and host
executability, not Unity product identity.

**OID drift rule (standing).** Five of the remaining roots anchor into
`Scripts/unity_build/unity_il2cpp.py`, so every merge drifts the file OID for the rest. Compare the
**anchored line range bytes**, not the file OID. Range drift is an invalidation; file-OID drift
alone is not. `ROOT-I02_003` was adjudicated this way (range 135-255 byte-identical,
`bc2218746af741403f053e54209f12779cbc3ac2f1961e7f73bad7a8cb738655`).

One root per window, each with its own transaction quartet, independent fix review, and
post-merge validation.

### ROOT-I02_004 re-derivation — settled

A re-derivation-only window ran on the overlapping anchor `82-97,266-275`. The ROOT-I02_003
file-OID-drift rule was deliberately **not** applied, because there the anchored range itself is
implicated. Regions were located by content, since ROOT-I02_003 shifted every later line number:
`82-97` is `PRESENT_UNCHANGED_MOVED` (identical bytes, now at line 83) and `266-275` is `CHANGED`
by the ROOT-I02_002 fix — a split anchor.

The frozen RED oracle was replayed against the frozen blob and the current bytes in separate
sandboxes: **both `confirmed`, `oracle_agreement=true`**, each announcing
`Build command completed successfully.` at exit `0` while the requested Player does not exist. An
un-stubbed structural check confirmed `output_path` is never examined after `run_with_progress`,
no post-run existence or mtime check exists, and `REQUIRED_GENERATED_ARTIFACTS` (13 entries, all
under `Packages/`) names no Player output — the path sets are disjoint.

**Disposition: OPEN.** ROOT-I02_002 hardened the pre-build input preflight; ROOT-I02_004 is the
absence of post-run authentication that `output_path` is a newly produced, invocation-owned
Player. `red_result` stays `CONFIRMED`, `fix_status_execution` stays `NOT_STARTED`, and only that
root's `notes` column was written. Evidence:
`build/phase187-global-remediation/rederivation-root-i02_004/REDERIVATION.md`
(SHA-256 `28fee7d5b1f82a6a3b7ee835f218b57f274801bc098404219c0eea406d86d793`); checkpoint
`checkpoints/CHECKPOINT_20260827_B0_ROOT-I02_004_REDERIVATION_TERMINAL.md`. No product code was
edited in that window.

`ROOT-I02_004` was then closed by a normal transaction with RED before fix. A zero process code
had been the controller's sole success oracle: nothing after `run_with_progress` examined
`output_path`, so the command announced a completed build with no Player produced, or with only a
stale pre-run artifact occupying the path. The fix fingerprints `output_path` by
`(st_size, st_mtime_ns)` before the launch and again after a zero exit, rejecting a missing Player
and rejecting an unchanged pre-existing artifact. Both rejections reuse `EXIT_PREFLIGHT_FAILURE`
so the declared taxonomy stays closed. A stale artifact is left untouched — the controller does not
delete what it did not produce. RED baseline was 3 failing tests of 6 new ones, with the three
acceptance/nonzero-exit tests already passing, which is what makes the set fix-neutral. Scope note
in its status doc: ownership is a `(size, mtime_ns)` delta across the invocation, not cryptographic
provenance, and Player content is not validated.

`ROOT-I02_009` made ordinary completion prove quiescence. `run_with_progress` returned the moment
the root process exited; the bounded wait and residual-PID reporting lived only on the timeout
branch, so ownership was released — force-killing any survivor via job kill-on-close or SIGKILL to
the process group — and neither that forced termination nor the failure to converge reached the
caller. Quiescence is now awaited inside the same bound and **decided before ownership is
released**, since releasing it would make every tree look quiescent; the review asserts that
ordering. A real nonzero Unity exit still wins so a genuine failure is not masked. Its anchored
range `508-514` was `PRESENT_UNCHANGED_MOVED` (frozen line 508 → current 564), so no re-derivation
window was needed. Scope note in its status doc: quiescence proves no PID remains owned at the
bound, not that file handles were released or that nothing escaped the job/process group.

**A line-ending incident is recorded under `ROOT-I02_009` NON_PASS_1.** An in-session
`pathlib.Path.write_text` converted `test_release_tooling.py` wholly to CRLF against the repo's LF
convention; git normalization masked it in the diff. It was corrected at the byte level and the
suites re-run green. The independent review now carries `source_line_endings_are_lf` and
`test_line_endings_are_lf` as standing checks — **reuse that review scaffold for the remaining
roots.** Note also that `AGENTS.md` itself is now CRLF for the same reason; it is gitignored and
text-consumed, so this is recorded rather than re-flipped, to avoid invalidating hashes already
written into earlier checkpoints.

**Remaining B0 queue (confirmed, not yet remediated), in remediation-order order:**
`ROOT-I02_010`, `ROOT-I02_014`, `ROOT-I03_002`, `ROOT-I03_006`, `ROOT-I03_011`.

Two roots still anchor into `Scripts/unity_build/unity_il2cpp.py` (`I02_010`, `I02_014`); the OID
drift rule above applies to them, and any root whose anchored range is itself implicated gets a
re-derivation window first, as `ROOT-I02_004` did.

### First-RED two-batch reconciliation (supersedes both raw batches)

The two 24-root first-RED evidence batches disagreed on six roots and neither is adoptable whole.
They were reconciled per root against the frozen register claim and the frozen Git blob, never by
directory name or timestamp. `first-red-20260827-063951` reads as the earlier run but its
per-root directories were written after `first-red-20260827`; the probe source was edited at
08:39, between the two runs, so the two batches ran different probe implementations against
byte-identical inputs (`fixture_sha256` and `source_snapshot_count` match on every divergent root).
BATCH_A's probe was recovered from `build/phase187-global-remediation/__pycache__/first_red_probe.cpython-313.pyc`.

- Authoritative result: `build/phase187-global-remediation/first-red-reconciliation-20260827/AUTHORITATIVE_RESULTS.tsv`
  / SHA-256 `593b05d1e2c51d4dd4af106cf3ebcea5e87fea1f34a57cce610317be41b45a7d`;
  report `RECONCILIATION.md` / SHA-256 `60be5a2e8d11f7f3a721692d33f734a3d200dade3c0d920af09bb1c006bee710`;
  verification `VERIFICATION.txt`.
- Counts: `confirmed=10`, `refuted=0`, `blocked=14`; `convergent=18`, `divergent_adjudicated=6`
  (4 resolve to BATCH_B, 2 resolve to neither batch).
- BATCH_A `refuted` on `ROOT-I03_002` came from an inverted oracle: it scored
  `validate_boundaries() == []` as success, but the frozen blob returns the list of descriptors
  successfully checked and raises on every violation. Corrected to `confirmed`.
- BATCH_A's five `blocked/exit=1` rows were harness aborts, not evidence: one Windows `os.killpg`
  absence, four caused by `load_module` omitting `sys.modules[name] = module` before `exec_module`.
- BATCH_B `refuted` on `ROOT-I03_007` and `ROOT-I03_008` is NOT supported: both probes bailed out
  at `[FAIL] Release build did not produce ...FoxgloveLogSourceGenerator.dll` because
  `validate_source_generator_dll.py@66348745e504:747-751` uses the module-global
  `CHECKED_IN_ARTIFACTS` when `target == "core"` and discards the injected `TARGETS` map. Neither
  seam was reached. Both corrected to `blocked`. Reaching them needs a probe fix
  (`mod.CHECKED_IN_ARTIFACTS`, or a non-`core` target), not a product change.
- Execution ledger synchronized: `remediation-execution-ledger.tsv` SHA-256
  `965a832f8bf313a5aa727d495cfa07be527998a39a9d5ab6f638c46a2d8af4d2` ->
  `820afd996feacd9b71e697e3a09db5a10b20dac4c3e034c17bea2fdcad8810c8`; 24 of 450 rows touched,
  120 cells. Only `red_result`, `red_evidence_path`, `red_evidence_sha256`, `red_cleanup_status`,
  `source_baseline_head`, `source_baseline_tree`, `global_red_status` were written.
  `red_status` stays literally `NOT RUN` (it mirrors the sealed plan's proposed status) and every
  fix/verification/merge column is untouched. Reversible via
  `first-red-reconciliation-20260827/ledger-transaction/ROLLBACK.sh` and `state-transaction/`.
- **B0 eligibility is not the 10 confirmed roots.** `GR-02-RUN-CI-SKIP-EMPTY-RESULT`,
  `ROOT-I01_001` and `ROOT-I01_002` were already merged between the sealed baseline
  `8d1223fc4cacf988da419b97c51a0f87bd965443` and the current baseline, each with
  `PROPOSED_RED_STATUS=NOT RUN`. The probes ran against frozen historical blobs, so `GR-02`'s
  `confirmed` describes a blob already superseded at HEAD and is not an open defect. The confirmed
  and not-yet-remediated set is nine roots: `ROOT-I02_002`, `ROOT-I02_003`, `ROOT-I02_004`,
  `ROOT-I02_009`, `ROOT-I02_010`, `ROOT-I02_014`, `ROOT-I03_002`, `ROOT-I03_006`, `ROOT-I03_011`.

- Frozen packet manifest: `build/phase187-global-remediation/PLAN_PACKET_MANIFEST.tsv` / SHA-256 `a4d31d28a01619ce15b9eb5b670c23dfae8c774fd957e43c79111a394675a39d`.
- Frozen register: `Developer/187/findings/round2-global-remediation/round2-adjudicated-risk-register-v1.tsv` / SHA-256 `ff4eaf066f5356f0ae12667947d2180397267713939729496e87e98fafe29329`.
- Frozen remediation order: `Developer/187/findings/round2-global-remediation/remediation-order.tsv` / SHA-256 `3aa265693fa6260e1bbda2cf70128552d5a04be98285da525241d74b2700f58e`; 24 rows have `first_red=YES`.
- Frozen remediation ledger baseline: `Developer/187/findings/round2-global-remediation/remediation-ledger.tsv` / SHA-256 `08fe94da2d35e2fc842f4cb2504533b15e610648ceaa2055158a5bee1b595a95`; execution overlay is `Developer/187/findings/round2-global-remediation/remediation-execution-ledger.tsv`.
- Seal-sync manifest: `build/phase187-global-remediation/seal-sync-20260827/SEAL_SYNC_MANIFEST.tsv`; seal SHA is recorded in `SEAL_SYNC.sha256`.
- Frozen packet baseline at RED time: HEAD `a0eb409ded731d1c65be7765021f27b669b5374b`, tree `21e35214a55b1f5e9e1b5f04494a4355ddafd14e`. **Current baseline after the ROOT-I02_009 merge: HEAD `1c8194f9a1f3000d522bf73d54daa5f4972b38b9`, tree `9c658816f77c5cafc799436cd82a66823653c172`.** Branch `main`, origin/main `8d1223fc4cacf988da419b97c51a0f87bd965443` (local main is ahead and unpushed), one worktree, stash `0`, tracked/staged diff empty; protected `.meta` SHA-256 `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`. The RED evidence is bound to the RED-time OIDs; revalidate each root's frozen OID before its B0 window and treat drift as an invalidation.
- RED gate: SATISFIED for the 24 ordered `first_red=YES` roots. Each root carries
  command/input/output/exit/SHA/cleanup evidence and one of `confirmed`, `refuted`, `blocked`; see
  the reconciliation block above. No product code was edited during RED.
- Repair gate: only `confirmed` roots may enter root-scoped B0 micro-batches; `refuted` roots
  receive disposition only; `blocked` roots retain evidence. There are no `refuted` roots. The
  eligible B0 set is the nine confirmed, not-yet-remediated roots named above; the 14 `blocked`
  roots stay evidence-only (12 need a POSIX host or a Unity installation, 2 need a probe fix).

Durable execution state: `Developer/187/findings/round2-global-remediation/GLOBAL_REMEDIATION_LEDGER_STATE.tsv` and `remediation-execution-ledger.tsv`.

## LIVE EXECUTION CHECKPOINT (2026-08-27) — DEFENSIVE QA OUTPUT CADENCE

Status: `PROGRESS_CADENCE_ENABLED`
Applies to every Claude/Codex continuation window operating on this repository and supplements the top global-remediation checkpoint.

- **Actual task context:** this work is authorized defensive code review, remediation, and local validation over the named checkout, frozen Git blobs, fixtures, and generated evidence. Keep commands and inputs inside that local boundary. Do not address external hosts, live targets, credentials, or secrets, and do not attempt to evade or override safety controls.
- **Meaning and classification:** preserve exact technical meaning and every required literal contract term. When accurate, describe actions with precise QA language such as `negative test`, `malformed-input test`, `fault injection`, `cleanup/reaping verification`, `artifact integrity`, and `validation false-green`. Do not conceal, encode, weaken, or omit evidence. Classify the action from its actual local target and purpose; do not infer offensive intent from a QA term alone.
- **Mandatory progress cadence:**
  1. Before any command or review expected to run longer than 120 seconds, or likely to consume more than 10,000 tokens, write a durable checkpoint first.
  2. Emit one compact `PROGRESS:` line after each root, batch, stage boundary, meaningful state change, or verified blocker. Include `stage`, `completed`, `result`, `command`, `exit`, `sha`, `artifacts`, `blocker`, and `next`.
  3. During a long read or run, emit a checkpoint at least every 10 minutes or 25 roots, whichever comes first; keep the line delta-only and do not repeat unchanged evidence.
  4. When the remaining context/token budget reaches 25 percent, write/reopen the checkpoint and emit `CHECKPOINT_CONTEXT_LOW`; continue only from the durable checkpoint rather than waiting for exhaustion.
  5. On interruption, tool failure, or a safety-classification boundary, immediately write `BLOCKED_<stage>` with the unchanged HEAD/tree/status, exact last command/input/output/exit, artifact hashes, and one resumable next action. Do not hide or reword the blocked operation.
  6. At a terminal stage, emit the terminal marker only after the required evidence is reopened and hash-verified; never infer completion from process liveness or a partial output.
- **Durable checkpoint location/schema:** use `build/phase187-global-remediation/checkpoints/` (or the active stage checkpoint directory) and record `timestamp`, `stage`, `HEAD`, `tree`, `tracked_diff`, `staged_diff`, `last_command`, literal output, exit status, artifact paths/SHA-256, completed units, unresolved units, blocker, and next action.
- **Continuation rule:** the next window reads `AGENTS.md` through EOF and the newest checkpoint, revalidates the exact boundary, then resumes at the first unresolved unit. Preserve prior evidence and do not rerun completed units unless a bound SHA or input changed.
- **Safety-boundary reporting rule:** if an otherwise authorized local QA operation is stopped by a classifier, write `BLOCKED_SAFETY_CLASSIFICATION` with the factual local context and exact evidence, then select a bounded local verification step that remains authorized. Never disguise the operation or claim a result that was not observed.

## LIVE TERMINAL CHECKPOINT (2026-08-26) — 187-R2-I11

Status: `I11_COMPLETE_TERMINAL_SEAL`
Closure: `187-R2-I11`

This block supersedes every lower checkpoint in this file. The cumulative I11 acceptance and review closure is terminal; no later Phase187 closure has started.

- Live report: `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I11.md` / SHA-256 `96772a426efe9d27d5f90ad9a1053f0b84a3aa54cee6a22912574c6a125d65de`; execution state `COMPLETE`; verdict `FINDINGS_REPORTED`; findings `17 High` (`P1 x4`, `P2 x13`, `P3 x0`); all proposed RED strategies remain literally `NOT RUN`.
- Formal ledger: `Developer/187/round2/local-review/execution-ledger.tsv` / SHA-256 `80158c07965d39aeb775ea97407521f6451d6d6a57b2b1659655115099cad515`; `37/37` complete; only the `187-R2-I11` row changed from `NOT_RUN` to `COMPLETE/FINDINGS_REPORTED` and binds the report SHA above.
- Current matrix binding: `4af8d90e191f045b4e81b98671f6ab44ab142a149d36eff025dfca7881da6992`; runner `bf42419edbf6b8eb369a363e2b2169aabbdf15a6d733afbb5e4d71d0f1d15f1b`; full result `bdd76ceaba6d7f301c1d20940e8a4e6030224efb669b779c98cb363d487b86ee`; predicates `62/62`, probes `28/28`, `SAFE68 PASS`, `OFFICIAL36 PASS`, `HYPOTHETICAL37 PASS`, dependency `70`, residual `0`, RED `0`, exit `0`.
- Final10 independent EOF reviews on the unchanged report/pair are terminal `SPEC COMPLIANT` and `QUALITY APPROVED`; artifacts: `build/i11-final10-spec-review-20260826-last/FINAL_SPEC_REVIEW.md` / SHA-256 `58d64840b5539a9ba982449b68bb83dd2e94ef0b9c6ec0ba6ce3c38de7de8780`; `build/i11-final10-quality-review-20260826-last/FINAL10_QUALITY_REVIEW.md` / SHA-256 `81e534965764f5ca19439961ecba67efc0161f520bad621f11efe864aff1155e`.
- Real post-ledger command `python -B Plan/187/tools/validate_phase187_round2_plans.py --mode execution --closure-id 187-R2-I11` returned `PHASE187_ROUND2_EXECUTION_VALIDATION_PASS`, `completedClosureCount=37`, `dependencyCount=70`, `residualCount=0`, exit `0`.
- Frozen Git boundary remains HEAD/local `main`/`origin/main` `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; one worktree; stash `0`; tracked/staged diff `0`; protected `.meta` SHA-256 `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
- Shutdown is the next and final physical action after this seal is reopened; do not reopen I11 or start a later phase.

# AGENTS.md

Private local bootstrap for Unity2Foxglove agent sessions.

This file replaces the old `SESSION_RECOVERY.local.md`. It is intentionally
ignored by git and should be read at the start of every new or resumed session.

## Current Local State

Date: 2026-08-23

- Primary repo: `D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox`.
- **Phase187 false-positive-resistant communication and token-discipline rule
  (2026-08-16, explicit user instruction):** preserve exact technical meaning
  while minimizing accidental context clustering and unnecessary token use.
  This is a communication and recovery discipline, not a change to review
  scope, evidence quality, or any safeguard.
  - Preserve required literal contract terms such as `RED`, `NOT RUN`,
    `hostile/stress`, exact hashes, commands, and evidence. Never conceal,
    encode, euphemize, weaken, or omit a finding to influence classification.
  - In controller, progress, and handoff prose, prefer precise QA terms such as
    negative test, malformed-input test, fault injection, cleanup/reaping
    verification, artifact integrity/provenance, and validation false-green.
    Use broader terms such as attack, exploit, bypass, or payload only when the
    exact source, frozen plan, or finding requires that meaning.
  - Emit one compact progress update only at a meaningful state change:
    blocker/failure, frozen author SHA, reviewer verdict, ledger seal, or an
    explicit user-requested checkpoint. Do not narrate waiting, repeat
    unchanged state, or restate completed coverage and gate output.
  - Read large reports, source files, and issue packets locally by exact path
    and hash instead of pasting them into chat. Keep exhaustive detail in the
    required local artifact; normally report only the path, SHA, counts,
    changed delta, terminal verdict, and next action. Include full commands and
    literal output only in terminal verification/handoff evidence or when the
    user explicitly requests them.
  - Keep delegation and continuation prompts delta-focused: point to this file
    and exact local artifacts, then state only the active scope, unresolved
    packet, required gates, and prohibited changes. Do not duplicate frozen
    evidence that the next agent must independently read from disk.
  - Treat `187-R2-I04`, `I08`, `I09`, and `I10` as elevated false-positive
    risk: use one closure per window and checkpoint after author validation,
    final review verdicts, and ledger seal so an interrupted window loses no
    approved state. Sandboxing alone does not change content classification.
- **Mandatory Phase187 exact-SHA review-batching rule (2026-08-15, explicit
  user correction):** never implement a "complete review" as "stop at the
  first issue and return to the author." Freeze one diagnostic report SHA,
  launch the requested fresh independent specification, quality, and
  adversarial/RED-feasibility reviewers in parallel on that same SHA, and require every
  reviewer to finish its entire authorized scope through EOF, continue
  searching after each issue, and return one exhaustive issue list. Do not
  edit the report while that audit batch is running. Only after every reviewer
  finishes may the controller reconcile and deduplicate all findings and give
  one consolidated report-only repair packet to one fresh independent author.
  The author then makes one batch repair and completes the full author
  validation chain. Only after that repair is frozen at one unchanged SHA run
  the final full specification review and separate independent quality review,
  including their required fresh gates. If a final reviewer finds an issue, it
  must still finish the complete review; allow every already commissioned
  reviewer on that SHA to finish, aggregate all issues, and repeat one
  consolidated repair rather than cycling per issue. Pre-repair diagnostic
  audit passes may omit redundant `68` / official / hypothetical validator
  runs; author validation and final approval reviews may not.
- **I10 collective-review hard boundary refresh (2026-08-21, explicit user
  instruction):**
  - A commissioned reviewer may not stop, emit a terminal marker, or return work
    to the author after its first issue. It must continue across its entire
    authorized report/source scope through EOF and produce one exhaustive issue
    list.
  - The controller must freeze one report SHA and wait for every commissioned
    specification, quality, and adversarial/RED-feasibility reviewer on that SHA
    to finish before any report edit. It then reconciles and deduplicates all
    complete issue sets into one repair packet and routes that packet once to one
    fresh report-only repair author.
  - The same barrier applies to final specification and quality review. Both must
    finish on the unchanged report SHA. If either finds issues, wait for both,
    aggregate all complete issues once, make one collective repair, and rerun both
    fresh final reviews; never repair or re-review one issue at a time.
  - A partial review, an incomplete batch, report-SHA drift, or one missing
    terminal artifact is a hard stop: checkpoint the unchanged boundary, do not
    edit the report or ledger, and resume the incomplete collective batch.

- **I10 convergence and frozen-total-matrix hard boundary (2026-08-22, explicit user correction):**
  - Final1-Final11 is an audit-convergence failure, not permission for another
    open-ended fresh-review model. The controller has frozen every issue
    predicate from all 21 terminal issue-bearing review artifacts into
    `build/i10-total-acceptance-matrix-20260822/TOTAL_ACCEPTANCE_MATRIX.tsv` at
    SHA-256
    `001c24d5e4ae12b0097e19e8369c9b465e9345c9f58f949e59431c0cc2a1b1c1`:
    exactly 57 historical predicates across Final1-Final11, backed by 13 direct
    probes and 21 source-review identities. `PROBE_DEFINITIONS.tsv` and
    `SOURCE_REVIEW_MANIFEST.tsv` are respectively SHA-256
    `e526995a5a40b0ec6d8825696463caf747a93f8de4b035ba96c6dd516c0bb2b2`
    and `af70293ebe11dc599b0e171702496a70e417cef0df81feddd359dc6e0eed96c3`.
  - Before freezing or installing any Final11 repaired candidate, the one fresh
    Final11 collective repair author must run
    `build/i10-total-acceptance-matrix-20260822/run_total_acceptance_matrix.py`
    at exact SHA-256
    `3ace55737e5dfd031d426ed044e7eb1ae78db11d460498c1c3256a29815194c6`
    with `--run-gates` against one exact report/audit pair and obtain all
    `57/57` predicates PASS, all `13/13` probes PASS,
    `failed_predicates=0`, `failed_probes=0`, and exit `0`. Any nonzero result
    leaves the candidate explicitly unfrozen and prohibits live report edit.
  - A newly added count table, stored zero, or generator-relative completeness
    assertion cannot establish closure. Every zero must come from the frozen
    direct probe/set/source replay, and the candidate transaction must bind the
    exact total-matrix result SHA.
  - Final12 may report a new issue only when its artifact maps it either to an
    exact clause of the original frozen I10 contract or to a direct contradiction
    between a candidate claim and authorized frozen-source bytes, with exact
    OID/range and claim citation. A new metric, threshold, count table, or
    self-created audit model alone is inadmissible and cannot trigger another
    repair cycle. Both Final12 reviewers must still complete EOF on one unchanged
    pair and run the frozen total matrix plus required gates before disposition.

- **Live superseding Phase187 Round 2 checkpoint (2026-08-23, user-requested
  Claude transfer; `PAUSED_V31_UNFROZEN_HANDOFF`; I11 remains `NOT_RUN`):**
  this block supersedes every lower live checkpoint and continuation prompt.
  The three Codex workers were interrupted at the user's requested pause; no
  I11 matrix process remained active. Durable full state and the exact Claude
  prompt are in `build/i11-claude-checkpoints-20260822/HANDOFF.md` at SHA-256
  `6f844f15e9a8a54a69fa698fa9f097db8b0139b3dc5ac39ed7732ca09ecfb2d5`,
  terminal `PAUSED_V31_UNFROZEN_HANDOFF`. Its transaction verification is
  `build/i11-claude-checkpoints-20260822/stage10-v31-unfrozen-paused-handoff-transaction/VERIFICATION.txt`
  at SHA-256
  `a2fa2df25411b3a22e32838229057068b275d888b87d5c7e9f0a9bd44d2e796d`.
- The live I11 report remains the rejected Final3 input at SHA-256
  `20eac900c23dcca64c3f616426f65715bc458b7e96ce14f222469d306472717e`.
  The formal ledger remains SHA-256
  `b1e9f96d74a28d944701875de2de7166d21523313d611e4b313d7bff777e9ef0`,
  `36/37`, with I11 literally `NOT_RUN`. Fresh official validation returned
  `PHASE187_ROUND2_EXECUTION_VALIDATION_PASS`, completed `36/37`, dependency
  `70`, residual `0`, exit `0`. HEAD/tree/main/origin/worktree/stash/diff and
  the protected `.meta` remain at the exact prior boundary; proposed RED
  execution remains `0`.
- Stage9 v3 (`62` historical predicates / `28` probe IDs) is rejected for
  author authorization because the later pre-install audit at SHA-256
  `59a2fe055f2ae840bde21a0106bfe061c20b405e5a138ccc5fe2b60f7c52a347`
  proved false-greens and internal UNSAT (`MATRIX_V3_UNSAT.json` SHA-256
  `017fb04c8519871fd2f545f04d558474a49bf57485a187224456062714cc2763`).
  Preserve all 62 identities and 28 IDs while strengthening/remapping them;
  never use v3 to install or validate a candidate.
- The interrupted v3.1 files under
  `build/i11-total-acceptance-matrix-20260822/v3-to-v3_1-transaction/WORK_ASSETS/`
  are explicitly `UNFROZEN`. At pause, runner/semantics/source replay/protocol
  analyzer/job analyzer/proposal analyzer SHAs were respectively
  `c3d478feb633d7618dcea57e147e9bd9c8393c33f52dc3f1069c6c3fc9d875b8`,
  `a3bf16a71fb1c43f4e1fb39f6997dd20a0724141577ae6fdbf002771d7bbdc4c`,
  `500660d171b42467598578b0db9e9796dba8ada9c1d26d331d39a87097684320`,
  `37afceb6556b8e2038b61f40572fa4936474130f27bb1ee5e6ce8e04a2d28aaa`,
  `105a3abcce7eb6599f6fc8831d5b816d3018e38b0559f30df5a38ed024e6d3a9`,
  and `eb9190378f7b9c1d57cffcad79ad5270ff60c8b3eeed41143fddc8efefbb7261`.
  The proposal analyzer project file was absent. The matrix/definitions/review
  manifest/binding copies remain old v3 bytes, not v3.1. Rehash before use.
- The interrupted author candidate at
  `build/i11-final3-collective-repair-author-20260823/MODIFIED_FILE` is SHA-256
  `61fa913b3ead692bfaad06696741eedb9f5a5221ff9a0063baae3bf9c2404043`;
  generator/evidence-manifest SHAs are
  `81fd0472518eb9d58a63b6057ac6d8eefd7d0cf49891fe815793d4817d6132c6`
  and `d44fa3dd1f5f4cd2ae37dfe9b622eab2eed14042d3bc0ef94272051c20030911`.
  It has no terminal `VERIFICATION.txt`, no `AUTHOR_VALIDATION.md`, no v3.1
  binding/result, and no installation authority. Do not preserve its
  developmental semantic counts; regenerate once after exact v3.1 freeze.
- Next action is exclusively to resume the ten-item cumulative v3.1 closure in
  the Stage10 HANDOFF: exact authorized-scope CFG/call/state/output; complete
  resource lifecycle; extraction and external-boundary semantics; exact
  authorized 487 locator/protocol rows; source-derived IndexOf truth; directed
  causal routes; finding-to-proposal mutation/oracle bindings; eight-action
  AST/CFG/dataflow/deadline/write/Job/PID proof; all-history remapping and 28
  targeted mutants; complete asset binding, positive satisfiability, independent
  exact-SHA preflight, and a rollback-tested freeze. Do this as one batch. Do
  not install the candidate or commission Final4 before v3.1 is terminal.
- After v3.1 freezes, the one collective author repairs all seven Final3 roots
  once and must pass `62/62`, `28/28`, failures `0/0`, safe68, official
  `36/37`, hypothetical `37/37`, dependency `70`, residual `0`, RED `0`, exit
  `0` on the uninstalled pair. Only then install and run fresh same-SHA Final4
  specification and quality EOF reviews. If either finds issues, wait for both
  and aggregate once. Only unchanged-SHA `AUTHOR VALIDATED + SPEC COMPLIANT +
  QUALITY APPROVED` permits sealing solely the I11 ledger row and running the
  real `37/37` validator. Stop before later phases.

- **Copy-pastable Claude resume prompt (2026-08-23 pause):**

```text
Work only in the primary checkout: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox.

Hash and read AGENTS.md and build/i11-claude-checkpoints-20260822/HANDOFF.md through EOF. Treat only the top live checkpoint and terminal PAUSED_V31_UNFROZEN_HANDOFF as current authority. Resume and complete Phase187 Round 2 closure 187-R2-I11 only. Use English for durable artifacts. Do not revisit I10; do not edit product/tests/frozen authority/refs/worktrees/the protected .meta; execute no proposed RED.

Revalidate the exact unchanged Git/report/ledger/frozen-scope boundary and fresh official 36/37, dependency70, residual0, exit0 result. Rehash every interrupted v3.1 work asset and author candidate named in HANDOFF. Stage9 v3 is rejected as false-green/UNSAT, and v3.1 is an interrupted mutable draft: do not install the author candidate and do not start Final4.

Finish all ten unresolved v3.1 closure items in HANDOFF as one cumulative batch. Preserve exactly 62 historical predicate identities and 28 probe IDs; remap every historical equivalent root to strengthened direct probes; derive truth only from exact authorized frozen path/OID/range; add a targeted matrix-owned negative mutant for every probe plus old-live/current-candidate negatives and one source-derived satisfiable positive fixture; bind every executed analyzer/input; obtain one stable exact-SHA independent full preflight with zero blockers. Freeze v3.1 only through ORIGINAL_FILE/MODIFIED_FILE/DIFF_FILE/VERIFICATION.txt/executable ROLLBACK.sh, baseline/modified tests, and separate-copy ROLLBACK_OK then ROLLBACK_NOOP.

Then give the one collective author the complete frozen result once, regenerate all seven Final3 roots/evidence together, and run v3.1 --run-gates on the uninstalled exact pair. Require 62/62, 28/28, failures0/0, safe68 PASS, official36/37 PASS, hypothetical37/37 PASS, dependency70, residual0, RED0, exit0 before AUTHOR VALIDATED or installation. After installation run fresh Final4 specification and separate quality reviews on one unchanged pair; both finish full EOF and all gates before disposition. Aggregate both complete issue lists once if needed. Only same-SHA AUTHOR VALIDATED + SPEC COMPLIANT + QUALITY APPROVED authorizes changing solely I11's ledger row, real post-ledger 37/37, and terminal AGENTS/HANDOFF seal. Stop before any later phase.
```

- **Live superseding Phase187 Round 2 checkpoint (2026-08-22, I11
  new-window launch refreshed; `READY_STAGE0`; I11 not started):** this block
  supersedes every lower live checkpoint and continuation prompt. The handoff
  boundary was independently revalidated without starting I11. The progressive
  execution ledger remains `36/37`, sealed exactly through `187-R2-I10`, at
  SHA-256
  `b1e9f96d74a28d944701875de2de7166d21523313d611e4b313d7bff777e9ef0`;
  the sealed I10 report remains SHA-256
  `3134faee8fe6e2aed4f505b2e9fe6c1d61bb58cd44f28c046c78293831204650`.
  `187-R2-I11` remains literally `NOT_RUN`, its report is absent, and no I11
  checkpoint directory has been created by this handoff. The fresh official I11
  pre-ledger validator returned `36/37`, dependency `70`, residual `0`, exit `0`.
- The unchanged Git boundary is HEAD/local `main`/`origin/main`
  `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`, one worktree, stash `0`,
  tracked/staged diff `0`, and only the protected 59-byte Phase186 `.meta` at
  SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
- I11 authority remains the frozen master SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  and child SHA-256
  `0abbcb587c28510168ddcf01a2b4c3c3d907e28ab29ab752ceacb16aad1af1fd`.
  Primary/dependency/exclusion/residual-ledger/source-universe identities remain
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`,
  and `48f47e1a053ceafaef97c917a4a21f0475784bdf49c195abf6e650a64dbede2a`.
  The exact scope is `57/57` ordered and unique frozen primary paths/OIDs
  (780,691 bytes / 14,538 LF), `2/2` anchors, the exact `1/1` bounded
  `TestSources` dependency at lines 317-583, and `0/0` exclusions. Independent
  scope evidence remains in `build/i10-agents-i11-handoff-20260822/`.
- Next action is exclusively I11 Stage 0 in a new window: hash/read this file
  through EOF, create `build/i11-claude-checkpoints-20260822/HANDOFF.md`,
  revalidate the unchanged boundary, checkpoint it, and emit
  `STAGE0_BOUNDARY_COMPLETE`. Do not revisit I10.

- **Copy-pastable I11 new-window startup prompt (2026-08-22 refresh):**

```text
Work only in the primary checkout: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox.

Hash and read AGENTS.md through EOF. Treat only its top live checkpoint as current authority. Take over and complete Phase187 Round 2 closure 187-R2-I11 only, starting Stage 0 now. Use English for durable review artifacts and checkpoint prose. Do not revisit I10; do not edit product/tests/frozen authority/refs/worktrees/the protected .meta; execute no proposed RED.

Before any I11 authoring, create durable build/i11-claude-checkpoints-20260822/HANDOFF.md. Revalidate ledger SHA b1e9f96d74a28d944701875de2de7166d21523313d611e4b313d7bff777e9ef0 at 36/37, sealed I10 report SHA 3134faee8fe6e2aed4f505b2e9fe6c1d61bb58cd44f28c046c78293831204650, I11 literally NOT_RUN/report absent, master SHA c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057, I11 child SHA 0abbcb587c28510168ddcf01a2b4c3c3d907e28ab29ab752ceacb16aad1af1fd, all five frozen identities, HEAD/tree/worktree/stash/diff/protected-meta boundary, and `python -B Plan/187/tools/validate_phase187_round2_plans.py --mode execution --closure-id 187-R2-I11` at 36/37, dependency 70, residual 0, exit 0. Checkpoint and emit STAGE0_BOUNDARY_COMPLETE.

Use frozen Git objects and review from zero in manifest order over 57/57 ordered and unique primaries (780,691 bytes / 14,538 LF), both exact anchors, the exact bounded TestSources dependency lines 317-583, and zero exclusions. Read every authorized body through EOF. Record entry -> call -> state -> output, exceptional branches, resource ownership/release, paired variants, aggregate-gate behavior, and deterministic negative-test proposals. Keep every proposal literally NOT RUN and run none.

Follow the exact-SHA collective sequence: one fresh report-only author; freeze one diagnostic report SHA; three fresh exhaustive same-SHA diagnostic reviewers (specification, quality, adversarial/RED-feasibility) all finish their full authorized scope and EOF before any disposition; aggregate and deduplicate all complete issue lists once; route one consolidated report-only repair packet to one fresh collective repair author; run full author gates; then commission one fresh exhaustive final specification reviewer and one separate fresh exhaustive final quality reviewer on one unchanged SHA. Neither reviewer may stop after the first issue. Wait for every commissioned reviewer, aggregate once, and repair once; a partial reviewer, missing terminal artifact, or SHA drift is a hard stop.

Author and final gates must freshly pass safe68, official pre-ledger 36/37, and reader-only hypothetical exact-report-SHA 37/37, dependency70, residual0. Preserve ORIGINAL_FILE, MODIFIED_FILE, DIFF_FILE, VERIFICATION.txt, and executable ROLLBACK.sh for every modification, and rehearse ROLLBACK_OK then ROLLBACK_NOOP on a separate copy. Only unchanged-SHA AUTHOR VALIDATED + SPEC COMPLIANT + QUALITY APPROVED authorizes changing only the I11 ledger row. Then run the real post-ledger validator at 37/37, update AGENTS.md with the terminal all-I-series seal, and stop. Do not start any later phase.
```

- **Live superseding Phase187 Round 2 checkpoint (2026-08-22, I10 sealed;
  staged I11 handoff; I11 not started):** the progressive execution ledger is
  now `36/37`, sealed exactly through `187-R2-I10`; I11 remains pristine
  `NOT_RUN` and its report is absent. The formal ledger is SHA-256
  `b1e9f96d74a28d944701875de2de7166d21523313d611e4b313d7bff777e9ef0`
  (10,942 bytes / 38 strict UTF-8 LF-only lines). The sealed I10 report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I10.md` at exact
  SHA-256
  `3134faee8fe6e2aed4f505b2e9fe6c1d61bb58cd44f28c046c78293831204650`
  (129,278 bytes). It records 12 High findings (`P1 x3`, `P2 x9`, `P3 x0`),
  11 future proposals plus unavailable I10-011, and every proposed result remains
  literally `NOT RUN` / `NOT OBSERVED`; proposed RED execution count is `0`.
- One unchanged I10 report/audit pair received terminal `AUTHOR VALIDATED`,
  constrained exhaustive `SPEC COMPLIANT`, and separate constrained exhaustive
  independent `QUALITY APPROVED`. Final12 reviewers both completed EOF before
  collective disposition and returned admissible issue count `0`. The report-bound
  audit is SHA-256
  `8c9077ba69b15cbeb2f9c3f168a32dcd3846c6b9f720ac938aafb7f6ab8c1238`;
  frozen Final1-Final11 matrix/result/binding SHAs are respectively
  `001c24d5e4ae12b0097e19e8369c9b465e9345c9f58f949e59431c0cc2a1b1c1`,
  `ebb1c0bee256051a1aaa2c902ce89837695b597b25684784669d98c4354053c5`,
  and `82c74a4d43ad82e464176b910335764e11c20f4ccb2b1a90c789a801d24d42f1`;
  all 57/57 historical predicates and 13/13 direct probes passed with zero
  failures. Terminal specification and quality artifacts are SHA-256
  `b0e0d4806af8d2a66ad5a92ac8c3acae8d1c01e8854529a9fd61f3d1906cc415`
  and `e478ee2f1e100706d4e748ab303f8928556be7dcfada26dd7a02b6c2a0babc03`.
  The real post-ledger validator passed `36/37`, dependency `70`, residual `0`,
  exit `0`.
- Final I10 author evidence is in
  `build/i10-stage5-final11-matrix-repair-author-20260822/`; its
  `MODIFIED_FILE`, `DIFF_FILE`, `VERIFICATION.txt`, and executable `ROLLBACK.sh`
  are respectively SHA-256
  `3134faee8fe6e2aed4f505b2e9fe6c1d61bb58cd44f28c046c78293831204650`,
  `e8a56f0df0e04aa57f737c5e5f2a1a57715f7811056fd117cdcf61f1ac268ed2`,
  `3997b58d13d4c7387f821c9b0d044f5116bf5f1d0ce19e35bcf972ea4af51f9c`,
  and `b6312685beb080750d5db33cdbf6ed5cc9715c0c8a4c2715345104cf348c7039`.
  Final12 spec/quality transactions are
  `build/i10-final12-spec-review-20260822/` and
  `build/i10-final12-quality-review-20260822/`. The ledger seal transaction is
  `build/i10-ledger-seal-20260822/`; its `MODIFIED_FILE`, `DIFF_FILE`,
  `VERIFICATION.txt`, and executable `ROLLBACK.sh` are respectively SHA-256
  `b1e9f96d74a28d944701875de2de7166d21523313d611e4b313d7bff777e9ef0`,
  `4f4bbbd7e580e5cf4660f08574a0b6e110f477a3ae83bc4d3613c59e22d44522`,
  `cdd39ab961821c9ad1c35e929e75d5c576ae6f2f032010754444582496bb7b8a`,
  and `e367fc549ffcd39477cce0e189b69bba515fdc8ba63cb11ca34dd138fe8439a0`.
- The I11 authority is the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I11_CROSS_MODULE_REVIEW_GATE_AGGREGATES_REVIEW_PLAN.md`
  at SHA-256
  `0abbcb587c28510168ddcf01a2b4c3c3d907e28ab29ab752ceacb16aad1af1fd`
  (16,822 bytes / 225 CRLF lines). Its frozen primary, dependency, exclusion,
  residual-ledger, and source-universe identities are respectively
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`,
  and `48f47e1a053ceafaef97c917a4a21f0475784bdf49c195abf6e650a64dbede2a`.
  Its exact frozen scope is `57/57` ordered and unique primary paths/OIDs,
  totaling 780,691 bytes / 14,538 LF, plus `2/2` anchors, `1/1` bounded
  dependency, and `0/0` exclusions. The anchors are
  `GenerationEditorOptimizationTests.cs` blob
  `a34089ebe746767f798b4a3235ceeae88718dd58` ::
  `class FoxRunSharedEmitterOptimizationTests` line 17 and
  `Phase173109ReviewTests.cs` blob
  `96d91a275a39f51d9c04ee5b72ad421eebd54e3c` ::
  `class Phase173109ReviewTests` line 12. The bounded I06 dependency is
  `RuntimeValidationOptimizationTests.cs` blob
  `54a3043fa24de65787ea19a9bbeae4793ba766da` :: `class TestSources`
  lines 317-583 because aggregate gates depend on shared fail-closed source
  lookup. Independent authority evidence is in
  `build/i10-agents-i11-handoff-20260822/`: `I11_AUTHORITY.json`,
  `I11_PRIMARY_SCOPE.tsv`, `I11_DEPENDENCY_SCOPE.tsv`, and
  `I11_EXCLUSION_ROWS.tsv` are respectively SHA-256
  `5af1be622a616be4b15cb7fa31cf524771f3997fd0e56419800d1044348d0dfb`,
  `5fcf995a4da16ea9da8b0f71bf52ccdd2761c0c17f420a4330786684700cc583`,
  `593abfd9bc1c7b2d2001d8ae95430797079219763191a713f78fc6c5f46ec714`,
  and `98d5be7e716d07f0667937e960911c67063d318ecb1d8aad3f49bdcce8795a5e`.
- The seal boundary remains HEAD/local main/origin main
  `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`, one worktree, stash `0`,
  tracked/staged diff `0`, and only the protected 59-byte Phase186 `.meta` at
  SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.

- **Copy-pastable staged I11 continuation prompt:**

```text
Work only in the primary checkout: D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox.

Hash and read AGENTS.md through EOF. Treat only its top live checkpoint as current authority, then complete Phase187 Round 2 closure 187-R2-I11 only. Do not revisit I10 or edit product/tests/frozen authority/refs/worktrees/protected .meta, and execute no proposed RED.

Stage 0: revalidate ledger b1e9f96d74a28d944701875de2de7166d21523313d611e4b313d7bff777e9ef0 at 36/37, sealed I10 report 3134faee8fe6e2aed4f505b2e9fe6c1d61bb58cd44f28c046c78293831204650, master c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057, I11 child 0abbcb587c28510168ddcf01a2b4c3c3d907e28ab29ab752ceacb16aad1af1fd, five frozen identities, I11 NOT_RUN/report absent, Git/meta boundary, and official validator 36/37 dependency70 residual0. Create durable build/i11-claude-checkpoints-20260822/HANDOFF.md and checkpoint every stage.

Use frozen Git objects and review from zero in manifest order over 57/57 unique primaries (780,691 bytes/14,538 LF), both exact anchors, the exact TestSources dependency lines 317-583, and zero exclusions. Read every authorized body through EOF. Record entry -> call -> state -> output, exceptional branches, resource ownership/release, paired variants, and deterministic negative-test proposals; keep every proposal literally NOT RUN and execute none.

Follow the exact-SHA collective sequence: one fresh report-only author; freeze one diagnostic report SHA; three fresh exhaustive same-SHA diagnostic reviewers (specification, quality, adversarial/RED-feasibility) all finish EOF before disposition; aggregate/deduplicate once; one fresh report-only collective repair author with full author gates; then one fresh exhaustive final specification reviewer and one separate fresh exhaustive final quality reviewer on one unchanged SHA. Never stop after the first issue or repair one issue at a time. A partial reviewer, missing artifact, or SHA drift is a hard stop.

Author and final gates must freshly pass safe68, official pre-ledger 36/37, and reader-only hypothetical exact-report-SHA 37/37, dependency70, residual0. Preserve original/modified/diff/verification/executable-rollback artifacts for every modification and rehearse ROLLBACK_OK/ROLLBACK_NOOP on another copy. Only same-SHA AUTHOR VALIDATED + SPEC COMPLIANT + QUALITY APPROVED authorizes changing only the I11 ledger row, followed by the real 37/37 validator. Update AGENTS.md with the terminal all-I-series seal and stop. Start Stage 0 now.
```

- **Live superseding Phase187 Round 2 checkpoint (2026-08-22, I10 Final11
  total-matrix author validated; Final12 not commissioned):** the live I10 report
  is now SHA-256
  `3134faee8fe6e2aed4f505b2e9fe6c1d61bb58cd44f28c046c78293831204650`,
  bound to audit SHA-256
  `8c9077ba69b15cbeb2f9c3f168a32dcd3846c6b9f720ac938aafb7f6ab8c1238`.
  The fresh Final11 repair author applied `F11RP-001..004` together once and,
  before installing the live report, passed the frozen total acceptance matrix
  at `57/57`, all `13/13` direct probes, `failed_predicates=0`,
  `failed_probes=0`, exit `0`; result SHA-256 is
  `ebb1c0bee256051a1aaa2c902ce89837695b597b25684784669d98c4354053c5`
  and exact-pair binding SHA-256 is
  `82c74a4d43ad82e464176b910335764e11c20f4ccb2b1a90c789a801d24d42f1`.
  The controller independently reproduced the same matrix result and fresh safe
  `68`, official `35/37`, hypothetical `36/37`, dependency `70`, residual `0`.
  Terminal status is `AUTHOR VALIDATED`; formal ledger remains
  `b3171ccf1e53dab1af25a4f0588f5c9f179c7fae18509378f94c8fd4e72cc0c5`
  at `35/37`, with I10/I11 `NOT_RUN`; no proposed RED ran. No Final12 reviewer
  is yet commissioned. Next: both fresh Final12 reviewers must use this unchanged
  report/audit/result/binding quartet, finish EOF, rerun the frozen total matrix,
  and admit a new issue only by an exact original-I10-contract clause or direct
  candidate-claim versus frozen-source contradiction. A new count table or
  self-created audit model is inadmissible. Do not start I11. The immediately
  following older convergence-reset block is historical only.

- **Live superseding Phase187 Round 2 checkpoint (2026-08-22, I10 Stage 5
  convergence reset; Final11 candidate not frozen):** the formal ledger remains
  SHA-256 `b3171ccf1e53dab1af25a4f0588f5c9f179c7fae18509378f94c8fd4e72cc0c5`
  at `35/37`; I10 and I11 remain literally `NOT_RUN`. The unchanged live I10
  report is SHA-256
  `049ac0511c7f960caf03e51d6e9c6c57a5e33c9553371c8bc49802f1dfebffee`,
  bound to audit SHA-256
  `2141bcc898a142153ddda232c789408991f6f59e428dd28757569fc6365d7ceb`.
  Both complete Final11 reviewers finished through EOF on that pair: specification
  SHA-256 `04c39d3d2967fb51bd5957b3387b5e10b9ed7bbf7dad0549db9b683dc0b486de`
  returned 3 issues and quality SHA-256
  `2d13c2d1a8eb839206cb65c0cb155ff924700b567501084244412b161a4e9744`
  returned 4 issues. Their one consolidated four-root packet is
  `build/i10-stage5-final11-collective-repair-packet-20260822/CONSOLIDATED_REPAIR_PACKET.md`
  at SHA-256
  `e3a8af98de91282ce0ffd9247496616708d6dd6c92ae3cb3b42c8cd92cb87b54`.
  The interrupted build candidate report/audit SHAs
  `0e78a52196eed5298885640aba05cf7725910b5f91c6107ca0a3568ee3f5df34` /
  `d33d9bf590798fdbfa46269c8d7b7ffa956e17132809fda8790cd0d13cd7b0b6`
  have no terminal `AUTHOR VALIDATED`, are explicitly `UNFROZEN`, and have not
  replaced the live pair. The matrix pre-freeze baseline correctly returned
  `failed_predicates=57`, `failed_probes=1`, exit `1` without `--run-gates`.
  No Final12 reviewer is commissioned. Next: one fresh Final11 collective repair
  author consumes the frozen packet and total matrix, reaches exact zero once,
  freezes one candidate, then the controller launches the constrained Final12
  same-SHA pair. Do not start I11.

- **Live superseding Phase187 Round 2 checkpoint (2026-08-21, I09 seal
  revalidated and I10 collective-review hard boundary refreshed; I10 not
  started):** the
  progressive execution ledger is now `35/37`, sealed exactly through
  `187-R2-I09`. `187-R2-I10` and `187-R2-I11` remain pristine `NOT_RUN`; the
  I10 report is absent. The formal ledger is SHA-256
  `b3171ccf1e53dab1af25a4f0588f5c9f179c7fae18509378f94c8fd4e72cc0c5`
  (10,785 bytes / 38 strict UTF-8 LF-only lines; no CR or NUL; terminal LF).
  The sealed I09 report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I09.md` at exact
  SHA-256
  `fc4bae6c17dc3f80b806f9a6b271df6bda4976724b55d41ba4705bc66d49f713`
  (139,820 bytes / 1,038 strict UTF-8 LF-only lines). It records 37 High
  findings (`P1 x2`, `P2 x35`, `P3 x0`), 39/39 distinct case-bound proposal
  roots, and every proposed deterministic RED remains literally `NOT RUN`.
- One unchanged final I09 report SHA received terminal **`AUTHOR VALIDATED`**,
  fresh exhaustive **`SPEC COMPLIANT`**, and separate fresh exhaustive
  independent **`QUALITY APPROVED`** verdicts. Both final reviewers completed
  their entire same-SHA scope through EOF before collective disposition and
  returned exhaustive issue count `0`; no partial issue was returned to the
  author. Their full coverage is `127/127` ordered and unique primaries,
  `120/120` unique primary OIDs, `2/2` anchors, `5/5` bounded dependencies,
  and `12/12` exclusions. All author and final-review gates freshly passed safe
  `68`, official pre-ledger `34/37`, and reader-only hypothetical exact-SHA
  `35/37`; the real post-ledger validator then passed at `35/37`, dependency
  `70`, residual `0`, exit `0`. No proposed RED ran.
- Final I09 author evidence is in
  `build/i09-final2-repair-author-20260820/`: `MODIFIED_FILE`/report SHA-256
  `fc4bae6c17dc3f80b806f9a6b271df6bda4976724b55d41ba4705bc66d49f713`,
  `DIFF_FILE` SHA-256
  `69b879aaa76c29cf825e1760b76a98987f6fa62965290ef0bf911e48a65632be`,
  `VERIFICATION.txt` SHA-256
  `253a4cae67509688363667dad853fa0b7702d88aaf0a947e00349702f78e8828`,
  executable `ROLLBACK.sh` SHA-256
  `987c863ac68dc23269e80b3bce9209d5b78d19e412aabfa3c459054b09b04861`,
  and author reconciliation SHA-256
  `bb3a691682e5b7dc30d3eb9f7ff7aa7077a3bc44122dec27906567bb528d4e90`.
  The collective final2 repair packet is SHA-256
  `23f98b982df8907c962ae1b248ebcb2c629c87b4c8f62685cb0979dcb552cce9`.
- The terminal specification artifact is
  `build/i09-final2-spec-review-20260820/FINAL_SPEC_REVIEW.md` at SHA-256
  `e09efa2306c4329764750fe9ac74fabcb270a596c5c1c5e078aaf52dcb61f6f6`;
  its transaction `DIFF_FILE`, `VERIFICATION.txt`, and executable
  `ROLLBACK.sh` are respectively SHA-256
  `a22b69e127cc9c547fb775baf9f4b4c025c0f2a1c1d5d4979fcfea5ab1235a65`,
  `b8c0ebd1e6196d1e5f22b4b1f6ca9a853f46590a3f8b1fe9e0d78d94e0072647`,
  and `689eac3227706c97434b6fed79d3dbc92e221f57647b8662ca924ce83937fe6e`.
  The separate terminal quality artifact is
  `build/i09-final2-quality-review-20260820/FINAL_QUALITY_REVIEW.md` at SHA-256
  `9de37c190fc8c2ca6feaaf50bc5b290f4dac17dcf4879e6ac0e6eb43e675656f`;
  its transaction `DIFF_FILE`, `VERIFICATION.txt`, and executable
  `ROLLBACK.sh` are respectively SHA-256
  `96592813da3034d4096932b63c441c713050330b1f383184d7de71ff89eb8b37`,
  `541ebbad282854101674c6bf950201a2db862ffa3ee66fb0c151266355c3b5d2`,
  and `8f60324db85293d9066447e979265513efc1596529e546079a6598c42e17ddd9`.
- The collective Stage 5 checkpoint transaction is
  `build/i09-claude-checkpoints-20260820/final2-stage5-approved-transaction/`:
  `MODIFIED_FILE`, `DIFF_FILE`, `VERIFICATION.txt`, and executable
  `ROLLBACK.sh` are respectively SHA-256
  `c3512da6ed564b8ce9bf48b698b3bd6931672a26a8d3f89d0f048fd0f7110727`,
  `9e858d6eb08bf1dcdcddb34290c5ef52b61957d66d00247d77a61c200147d9aa`,
  `546fd9a888b94a4fbf25272311a29ff85dfd242ae0b54190e857b081e5ad1463`,
  and `6085f142ba57e08e5c5498ae2f13e6aed880781984f34887e41cc20db471a4ea`.
  The ledger seal transaction is `build/i09-ledger-seal-20260820/`:
  `MODIFIED_FILE`, `DIFF_FILE`, `VERIFICATION.txt`, and executable
  `ROLLBACK.sh` are respectively SHA-256
  `b3171ccf1e53dab1af25a4f0588f5c9f179c7fae18509378f94c8fd4e72cc0c5`,
  `1dee30de909f583889eab2fd37f00f4850d14cdf086c458a9ec1402b6a598e5f`,
  `28f6c2e84f0806b8857bd5326865b5c0f71ce4edc7b0ee98f5151b8928da93eb`,
  and `d615805ab321d1a491fa9d5520e27a89fda4c9c7234e035d463ce749791588ad`.
  Every modifying transaction preserves original, modified, diff,
  verification, executable rollback, and a successful separate-copy
  `ROLLBACK_OK` / `ROLLBACK_NOOP` rehearsal.
- The I10 authority is the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I10_ROS_BRIDGE_MANUAL_ACCEPTANCE_REVIEW_PLAN.md`
  at SHA-256
  `700222790c1157859ff39ec3dd703e262e90fea399838265d75ed66ab63aef44`
  (35,069 bytes / 326 CRLF lines). Its frozen primary, dependency, exclusion,
  residual-ledger, and source-universe identities are respectively
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`,
  and `48f47e1a053ceafaef97c917a4a21f0475784bdf49c195abf6e650a64dbede2a`.
  Its exact frozen-object scope is `157/157` ordered and `157/157` unique
  primary paths, totaling 2,110,333 bytes / 52,868 LF across 127 unique blob
  OIDs (1,917,049 bytes / 47,003 LF), plus `2/2` anchors, `4/4` bounded
  dependencies, and `0/0` exclusion rows.
- The two exact I10 anchors are
  `Scripts/smoke/ros2/phase181_custom_ros2_peer.py` at blob
  `cf2b503be24d91d9b875d56c7d962a0c631c2c56` :: `def main` (line 2838),
  and
  `Unity2Foxglove/Assets/Editor/ManualAcceptance/Phase186Ros2BridgeAcceptanceBuilder.cs`
  at blob `b0fec479bb51036bdaa8d7e73904c8f272ef69b3` :: `class
  Phase186Ros2BridgeAcceptanceBuilder` (line 20). The four bounded dependency
  symbols/reasons resolve exactly in their frozen blobs: owner `187-R2-H08`,
  `Tools/ros2_bridge/unity2foxglove_ros2_bridge/test/test_full_duplex_origin.cpp`
  at `7e7ad442ca4b21afcffe99f38e5afc6ff813b1f5` ::
  `ConsecutiveBridgeNodesRetirePublisherBeforeOriginRegistry` (line 108),
  because manual rows must agree with native duplex behavior; owner
  `187-R2-H04`, `Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py`
  at `4c18eec2331d45eb0a890988a85489afa0ecdb85` :: `def main` (line 1091),
  because the manual matrix depends on exact distro/package selection; owner
  `187-R2-H03`,
  `Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2TransportProvider.cs`
  at `bbadedf0893bf15f767324035aedcf02e7ccdf09` :: `class
  FoxRunRos2TransportProvider` (line 22), because R2FU manual rows must reach
  the real Provider; and owner `187-R2-H07`,
  `Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeConnection.cs`
  at `294c100f25850ce6023b1ef3fbe2f1a9090ef6fd` :: `class
  Ros2BridgeConnection` (line 18), because Bridge manual rows must reach the
  real Unity connection. The exclusion-manifest filter is
  `consumer_closure=187-R2-I10` and has exactly zero rows.
- Independent frozen-object authority evidence is retained in
  `build/i09-agents-i10-handoff-20260820/`: `I10_AUTHORITY.json`,
  `I10_PRIMARY_SCOPE.tsv`, `I10_DEPENDENCY_SCOPE.tsv`,
  `I10_EXCLUSION_ROWS.tsv`, and `I10_AUTHORITY_AUDIT.log` are respectively
  SHA-256
  `ddb6baac6ba6712cfeb80afb81568fd90ff5312cb5687f63be9b7bf63b4012f4`,
  `ce9541db3c031ddbd039024d7af1bedfeb60d749a3e8adc37052e4b51a547a56`,
  `5a73c67208f0f7a2485f58104913c59e4f261bffc28c83eb44047cf633757ae0`,
  `2fab1703b4c2cd78c3813604dc1a1d5315ef8e9d617ba1e52f73d42609d5d3b4`,
  and `0358b790ec1d8e6c6d3fc46afa6ff75193d7cd2f100c31f4221f94992d6f6d2e`.
- The handoff boundary was freshly rechecked: HEAD/local `main`/`origin/main`
  are `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; one worktree, no stash, no
  tracked or staged changes, and only the protected untracked 59-byte Phase186
  `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  The official post-I09 validator passed at `35/37`, dependency `70`, residual
  `0`; I10 remains unstarted and its report remains absent. This checkpoint
  replaces only the previous top live block over pre-handoff AGENTS SHA-256
  `369f51ea54802dd9e7d946b03e27fe498cc02c1b02d20df442dde348e984d6a1`;
  every historical block below remains byte-identical. After terminal I09
  verification and the `STAGE6_I09_SEALED` marker, the controller schedules
  shutdown as its sole final action. I10 resumes in a fresh window after the
  next startup.
- **Copy-pastable staged I10 continuation prompt:**

```text
Work only in the primary checkout: `D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox`.

This continuation prompt is for the fresh window after the next startup. Take over and complete Phase187 Round 2 closure `187-R2-I10` only. Treat only the top live checkpoint in `AGENTS.md` as current authority. Use English for all durable review artifacts and takeover/checkpoint prose. Do not start I11.

Collective-review hard boundary (explicit and mandatory): no reviewer may stop at
the first issue or return a partial terminal verdict. Each commissioned reviewer
must continue through its entire authorized report/source scope and EOF, then
return one exhaustive list. The controller must wait for every reviewer in the
same-SHA batch, aggregate and deduplicate all lists once, issue one report-only
repair packet, and permit one collective repair. The same barrier applies to both
final reviewers: finish both, aggregate once, repair once, then rerun both. Any
incomplete reviewer or report-SHA drift freezes report and ledger mutation.

Durable-output rule (mandatory): at the start create `build/i10-claude-checkpoints-20260820/HANDOFF.md`. At every stage boundary below, append exact live hashes/counts, commands with literal output and exit status, completed work, unresolved work, and next stage; reopen/hash the checkpoint and immediately emit the named compact stage marker before continuing automatically. During the long Stage 1 frozen-source read, append and immediately report `STAGE1_READ_BATCH_<n>` after each 25 primary paths or 20 minutes, whichever comes first. Before remaining context falls below one quarter, write/reopen a resumable checkpoint and emit it. On interruption or tool/model outage, write `BLOCKED_<stage>` with the unchanged boundary before any retry. Never claim a partial reviewer as terminal.

Stage 0 — boundary verification. Independently hash and read `AGENTS.md` through EOF. Verify the ledger, sealed I09 report and final2 author/specification/quality/ledger artifacts, master/I10 child, five frozen identities, refs/tree, I09/I10/I11 rows, protected `.meta`, process state, and one-worktree/no-stash boundary. Expected: ledger `b3171ccf1e53dab1af25a4f0588f5c9f179c7fae18509378f94c8fd4e72cc0c5` at `35/37`; I09 sealed to report `fc4bae6c17dc3f80b806f9a6b271df6bda4976724b55d41ba4705bc66d49f713`; I10/I11 pristine `NOT_RUN`; I10 report absent; HEAD/main/origin `8d1223fc4cacf988da419b97c51a0f87bd965443`; tree `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`. Run `python -B Plan/187/tools/validate_phase187_round2_plans.py --mode execution --closure-id 187-R2-I10`; require `35/37`, dependency `70`, residual `0`. Checkpoint and emit `STAGE0_BOUNDARY_COMPLETE`.

Use frozen master SHA `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057` and I10 child SHA `700222790c1157859ff39ec3dd703e262e90fea399838265d75ed66ab63aef44`. Review from zero in manifest order over `157/157` ordered and unique primary paths (2,110,333 bytes / 52,868 LF; 127 unique OIDs totaling 1,917,049 bytes / 47,003 LF), `2/2` anchors, `4/4` bounded dependencies, and `0/0` exclusions. Read frozen Git objects, not mutable working-tree files.

Stage 1 — one fresh independent report-only author. Read every authorized primary blob through EOF in manifest order, both exact anchors, all four exact dependency symbols/reasons, and the exact zero-row exclusion filter. Record entry -> call -> state -> output, every exceptional branch, resource ownership/release, paired variants, and required deterministic negative-test proposals. Keep every proposed RED literally `NOT RUN` and execute none. Write the complete diagnostic report only after full source coverage; create/reopen the original/modified/diff/verification/executable-rollback transaction and rehearse rollback on another copy; run author mechanics/source/citation/EOF gates; freeze one diagnostic report SHA; checkpoint and emit `STAGE1_DIAGNOSTIC_AUTHOR_FROZEN <sha> <counts>`. Do not review changing bytes.

Stage 2 — one exact-SHA collective diagnostic review batch. Launch fresh independent specification, quality, and adversarial/RED-feasibility reviewers in parallel on the same frozen diagnostic SHA. Every reviewer must read its complete authorized scope and report through EOF, continue after every issue, and return one exhaustive issue list/artifact. Do not edit the report while any commissioned reviewer is unfinished. Never stop after one issue or return one issue at a time. Pre-repair diagnostic reviews may omit redundant safe/official/hypothetical gates; no proposed RED may run. Emit `STAGE2_SPEC_COMPLETE`, `STAGE2_QUALITY_COMPLETE`, and `STAGE2_ADVERSARIAL_COMPLETE` only for complete terminal artifacts.

Stage 3 — aggregate once. After all three reviewers finish, reconcile and deduplicate every complete issue set into one consolidated report-only repair packet. Write/reopen/hash the packet, checkpoint, and emit `STAGE3_REPAIR_PACKET_FROZEN <sha> <issue-count>`. Do not repair issue-by-issue.

Stage 4 — one fresh collective repair author. Apply the entire consolidated packet once to the report only; do not edit product code/tests. Preserve every `NOT RUN`. Preserve/reopen original/modified/diff/verification/executable-rollback artifacts and rehearse rollback. Run the full author validation chain including safe `68`, official pre-ledger `35/37`, and reader-only hypothetical exact-report-SHA `36/37`, dependency `70`, residual `0`, with no formal-ledger mutation. Freeze the repaired SHA and emit `STAGE4_AUTHOR_VALIDATED <sha> <counts>` only after terminal `AUTHOR VALIDATED`.

Stage 5 — repeated exhaustive same-SHA final batch. On the unchanged repaired SHA run one fresh exhaustive specification review and one separate fresh independent exhaustive quality review. Both must continue through EOF after every issue and freshly run safe `68`, official `35/37`, and reader-only hypothetical exact-SHA `36/37`, dependency `70`, residual `0`. If either finds anything, let both finish, aggregate all complete issues once, run one collective repair, and repeat both complete final reviews; never cycle one issue at a time. Only same-SHA `AUTHOR VALIDATED` + `SPEC COMPLIANT` + `QUALITY APPROVED` authorizes Stage 6.

Stage 6 — ledger seal and stop. Change only the I10 ledger row with a complete transaction and separate-copy rollback rehearsal. Run the real post-ledger validator and require `36/37`, dependency `70`, residual `0`. Reopen every artifact; prove I11 remains pristine, I10 report SHA is ledger-bound, refs/tree/worktree/stash/protected `.meta` are unchanged, and no proposed RED ran. Update `AGENTS.md` transactionally with a staged I11 new-window handoff, checkpoint, emit `STAGE6_I10_SEALED <report-sha> <ledger-sha>`, and stop without starting I11. Follow the terminal power instruction that is current in that future window.

Prohibitions throughout: do not edit product code, tests, frozen authority/manifests, I09, protected `.meta`, Git refs, or worktrees; do not execute proposed REDs; do not expose a partial issue as a terminal verdict; do not discard completed same-SHA reviewers; do not spin on repeated outages without a durable blocker checkpoint.
```
- **Live superseding Phase187 Round 2 checkpoint (2026-08-18, fresh
  I08 takeover refresh; I08 still not started):** the full boundary was
  independently revalidated before this refresh. The ledger remains SHA-256
  `b295e6d58182643792ab26b847a893131ca55cc132462d8f6828c98515f3763f`
  (`33/37`), sealed exactly through I07; I08 and I09 remain pristine
  `NOT_RUN`, and the I08 report remains absent. The sealed I07 report remains
  SHA-256
  `eda8e3097740f1eff737b6985c5cab6cf42183ee038c55d58d59418a561c14f2`.
  The official pre-I08 validator freshly passed at `33/37`, dependency `70`,
  residual `0`.
- This refresh corrects one encoding description in the immediately following
  historical I08 handoff: the I08 child remains exact SHA-256
  `f53902cce31fdf13a40c9ea8a3b8192a9d1c6e18d6cd2b6b0b7e77932cef3f1b`
  and 7,620 bytes, but its 169 lines are CRLF (`169` CR and `169` LF), not
  LF-only. Its scope remains `3/3 + 2/2 + 1/1 + 0/0`, including the absent
  requested `class ValidationEvidence` and the actual enum/formatter/text-
  writer symbols recorded below.
- HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443`; tree remains
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; one worktree, no stash, empty
  tracked/staged diffs, and only the protected 59-byte Phase186 `.meta` at
  SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  No I08 author/reviewer/validator process remains. This block was inserted
  over pre-refresh AGENTS SHA-256
  `df57c52a64f4d1ba7ba40e2555045b5dc416fcf1f44e62ad0d5430244f067a97`;
  the next window must hash and read the newly modified file through EOF.
- **Copy-pastable I08 continuation prompt:**

```text
Work only in the primary checkout: `D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox`.

Take over and complete Phase187 Round 2 closure `187-R2-I08` only. Treat only the top live checkpoint in `AGENTS.md` as current authority. Before authoring, independently hash and read `AGENTS.md` through EOF, then revalidate the ledger, sealed I07 report, master/I08 child, five frozen identities, refs/tree, I07/I08/I09 rows, protected `.meta`, process state, and the single-worktree boundary. Expected current boundary: ledger `b295e6d58182643792ab26b847a893131ca55cc132462d8f6828c98515f3763f` at `33/37`; I08 pristine `NOT_RUN` and report absent; HEAD/main/origin `8d1223fc4cacf988da419b97c51a0f87bd965443`; tree `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`.

Use frozen master SHA `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057` and I08 child SHA `f53902cce31fdf13a40c9ea8a3b8192a9d1c6e18d6cd2b6b0b7e77932cef3f1b`. Review from zero in manifest order over `3/3` ordered and unique primaries (`48,969` bytes / `1,077` LF), `2/2` anchors, the `1/1` bounded I06 dependency specification, and `0/0` exclusions. The dependency requests `ValidationEvidence.cs :: class ValidationEvidence`, but that exact class is absent; the frozen blob instead contains `enum ValidationEvidence`, `ValidationEvidenceFormatter`, and `ValidationEvidenceTextWriter`. Record this mismatch truthfully and do not substitute a nonexistent class.

Follow the mandatory exact-SHA batching sequence: one fresh independent report-only author; freeze the diagnostic report SHA; run fresh exhaustive specification, quality, and adversarial/RED-feasibility reviews in parallel on those unchanged bytes, with every reviewer continuing through EOF; aggregate all complete findings once; route one consolidated report-only repair to one fresh author with the full author-validation chain; then run a fresh final specification review and a separate fresh independent quality review on one unchanged repaired SHA. Keep every proposed RED literally `NOT RUN` and execute none.

Author and final-review gates must freshly pass safe `68`, official pre-ledger `33/37`, and reader-only hypothetical exact-SHA `34/37`, with dependency `70` and residual `0`. Only same-SHA terminal `AUTHOR VALIDATED` + `SPEC COMPLIANT` + `QUALITY APPROVED` permits changing only the I08 ledger row, followed by the real `34/37` validator. Preserve verified original/modified/diff/verification/executable-rollback artifacts for every report, ledger, or AGENTS modification and rehearse rollback on another copy.

Do not edit product code, tests, frozen authority/manifests, I07, the protected `.meta`, Git refs, or worktrees. Do not start I09. Because I08 is elevated false-positive risk, use this window for I08 only; after a genuine I08 seal and real `34/37` pass, update `AGENTS.md` with the I09 new-window handoff and stop. Keep progress delta-only at the frozen diagnostic SHA, frozen repaired author SHA, final review verdicts, ledger seal, or failure.
```

- **Live superseding Phase187 Round 2 checkpoint (2026-08-17, successful
  I07 seal and explicit user-requested I08 new-window handoff; I08 not
  started):** the progressive execution ledger is `33/37`, sealed exactly
  through `187-R2-I07`; `187-R2-I08` remains pristine `NOT_RUN` with empty
  verdict, report SHA, and notes, and its report is absent. The formal ledger
  is SHA-256
  `b295e6d58182643792ab26b847a893131ca55cc132462d8f6828c98515f3763f`
  (10,469 bytes / 38 strict UTF-8 LF-only lines; no CR or NUL; terminal LF).
  The sealed I07 report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I07.md` at exact
  SHA-256
  `eda8e3097740f1eff737b6985c5cab6cf42183ee038c55d58d59418a561c14f2`
  (196,988 bytes / 763 strict UTF-8 LF-only lines). It records 30 High
  findings (`P1 x19`, `P2 x11`, `P3 x0`), and all 30 proposed deterministic
  RED strategies remain honestly `NOT RUN`.
- One unchanged final I07 report SHA received terminal **`AUTHOR VALIDATED`**,
  fresh full **`SPEC COMPLIANT`**, and separate fresh independent **`QUALITY
  APPROVED`** verdicts. The final report covers `19/19` ordered and `19/19`
  unique primaries (165,166 bytes / 3,277 LF), `2/2` anchors, the `1/1`
  bounded I06 dependency, and `0/0` exclusions. Its final audit reproduced 30
  sequential IDs / 270 required fields / 19 path-OID-rule triples / 267
  citation sites / 358 citation ranges over all 20 authorized OIDs, 144
  loop-expanded check-map rows, and 38 finite negative predicates, with zero
  unauthorized, out-of-range, or stale items.
- The terminal author and both successful final reviewers freshly passed safe
  `68`, official pre-ledger `32/37`, and reader-only hypothetical exact-SHA
  `33/37`; the real post-ledger validator passed at `33/37`, dependency `70`,
  residual `0`. No proposed RED ran. Report evidence is retained in
  `build/i07-initial-author-20260817/`,
  `build/i07-diagnostic-consolidated-repair-20260817/`, and
  `build/i07-final-consolidated-repair-20260817/`; diagnostic/final review
  packets are in `build/i07-diagnostic-review-batch-20260817/` and
  `build/i07-final-review-batch-20260817/`; the ledger-seal transaction is
  `build/i07-ledger-seal-20260817/`. Every modifying transaction preserves
  original, modified, diff, verification, executable rollback, and successful
  rollback-rehearsal artifacts. This I08 handoff checkpoint was inserted over
  the verified pre-handoff AGENTS SHA-256
  `ce4a8fe577fdd9ae125c9c81753a52037893c7eb62c81b941c3c3762b2c65130`;
  the next window must independently hash and read the current file through
  EOF.
- The I08 authority is the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I08_EXECUTABLE_ARTIFACT_VALIDATION_GATES_REVIEW_PLAN.md`
  at SHA-256
  `f53902cce31fdf13a40c9ea8a3b8192a9d1c6e18d6cd2b6b0b7e77932cef3f1b`
  (7,620 bytes / 169 strict LF-only lines). Its frozen
  primary/dependency/exclusion/residual/source-universe identities are
  respectively
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`,
  and `48f47e1a053ceafaef97c917a4a21f0475784bdf49c195abf6e650a64dbede2a`.
  Its exact frozen-object scope is `3/3` ordered and `3/3` unique primaries
  (48,969 bytes / 1,077 LF), `2/2` requested anchors, `1/1` requested bounded
  I06 dependency specification, and `0/0` exclusions. The anchors are
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase10Validation.cs` ::
  `class Phase10Validation` (lines 18-354) and
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase134_10Validation.cs` ::
  `class Phase134_10Validation` (lines 20-435). The dependency row requests
  blob `1c3d30e2b631562473148fcf4f97317babfdb3d0` at
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/ValidationEvidence.cs` ::
  `class ValidationEvidence`, owned by I06; exact resolution is absent. The
  frozen blob instead exposes `enum ValidationEvidence` (lines 14-23),
  `class ValidationEvidenceFormatter` (25-68), and
  `class ValidationEvidenceTextWriter` (70-150). The I08 report must record
  this mismatch truthfully and must not substitute a nonexistent class.
- The handoff boundary was freshly rechecked: HEAD/local `main`/`origin/main`
  are `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`, one worktree, no stash, no
  tracked or staged changes, and only the protected untracked 59-byte
  Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  The official pre-I08 validator passed at `33/37`, dependency `70`, residual
  `0`; no relevant I08 author, reviewer, validator, Python, Unity, ROS,
  native, or build process remains.
- Because I08 is an elevated false-positive-risk closure, the next fresh
  window must use one closure only and the mandatory exact-SHA batching
  sequence: (1) independently rehash AGENTS, ledger, sealed I07 report,
  master/I08 child, five frozen identities, refs/tree, I07/I08/I09 rows,
  protected `.meta`, process state, and the single-worktree boundary; (2)
  start one fresh independent report-only author from zero over
  `3/3 + 2/2 + 1/1 + 0/0`, reading frozen Git objects in manifest order and
  keeping every proposed RED literally `NOT RUN`; (3) freeze one diagnostic
  report SHA and run three parallel exhaustive specification, quality, and
  adversarial/RED-feasibility reviews on those same unchanged bytes, with
  every reviewer continuing through EOF after every issue; (4) aggregate and
  deduplicate all findings once, route one consolidated report-only repair
  packet to one fresh independent author, and require the complete author-
  validation chain; and (5) freeze the repaired SHA and run a fresh final full
  specification review plus a separate fresh independent quality review on
  one unchanged SHA. Author and final-review gates include safe `68`, official
  pre-ledger `33/37`, and reader-only hypothetical exact-SHA `34/37`. Only
  same-SHA terminal `AUTHOR VALIDATED` + `SPEC COMPLIANT` + `QUALITY APPROVED`
  permits changing only the I08 ledger row, followed by the real `34/37`,
  dependency `70`, residual `0` validator.
- Do not edit product code, tests, frozen authority/manifests, I07, the
  protected `.meta`, Git refs, or worktrees. Do not execute any proposed RED.
  Keep progress delta-only and checkpoint at the frozen diagnostic SHA,
  frozen repaired author SHA, final review verdicts, and ledger seal. Stop
  after the I08 seal and prepare the next-window handoff; do not start I09 in
  the I08 window.

- **Live superseding Phase187 Round 2 checkpoint (2026-08-17, successful
  I06 seal and explicit user-requested I07 new-window handoff; I07 not
  started):** the progressive execution ledger is `32/37`, sealed exactly
  through `187-R2-I06`; `187-R2-I07` remains pristine `NOT_RUN` with empty
  verdict, report SHA, and notes, and its report is absent. The formal ledger
  is SHA-256
  `250c8214233932c85be07744a5ad4e3f706a8aa9833b2a33c4361c72f1b60333`
  (10,310 bytes / 38 strict UTF-8 LF-only lines; no CR or NUL; terminal LF).
  The sealed I06 report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I06.md` at exact
  SHA-256
  `9e63dace058d269fb41dcc6e349aaf0948e325e8467c138e966a377c60c7d2fd`
  (105,773 bytes / 574 strict UTF-8 LF-only lines). It records 35 High
  findings (`P1 x28`, `P2 x7`, `P3 x0`), and all 35 proposed deterministic
  RED strategies remain honestly `NOT RUN`.
- One unchanged final I06 report SHA received terminal **`AUTHOR VALIDATED`**,
  fresh full **`SPEC COMPLIANT`**, and separate fresh independent **`QUALITY
  APPROVED`** verdicts. The final report covers `20/20` ordered and `20/20`
  unique primaries (263,673 bytes / 5,133 LF), `2/2` requested anchor
  specifications with exact symbol resolution `1/2`, the `1/1` bounded I05
  dependency, and `0/0` exclusions. Finding 032 truthfully records that the
  child requests `ValidationEvidence.cs :: class ValidationEvidence` while
  the frozen blob exposes `enum ValidationEvidence` plus the formatter and
  text-writer classes; no nonexistent class was substituted. The final audit
  reproduced 35 sequential IDs / 315 required fields / 20 path-OID-rule
  triples / 144 citation sites / 184 citation ranges over all 21 authorized
  OIDs, with zero unauthorized, out-of-range, or stale items.
- The terminal author and both successful final reviewers freshly passed safe
  `68`, official pre-ledger `31/37`, and reader-only hypothetical exact-SHA
  `32/37`; the real post-ledger validator passed at `32/37`, dependency `70`,
  residual `0`. No proposed RED ran. Report transaction evidence is retained
  in `build/i06-initial-author-20260817/`,
  `build/i06-diagnostic-consolidated-repair-20260817/`,
  `build/i06-final-quality-consolidated-repair-20260817/`, and
  `build/i06-final2-consolidated-repair-20260817/`; the ledger-seal transaction
  is `build/i06-ledger-seal-20260817/`. Every transaction preserves original,
  modified, diff, verification, executable rollback, and successful rollback
  rehearsal artifacts. This I07 handoff checkpoint was inserted over the
  verified pre-handoff AGENTS SHA-256
  `ef30f25d3c68917b3b96bc3382dfa51a0c2b291d11a16f87e15bf903dddaef9f`;
  the next window must independently hash and read the current file through
  EOF.
- The I07 authority is the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I07_SOURCE_INSPECTION_VALIDATION_GATES_REVIEW_PLAN.md`
  at SHA-256
  `d42e4badb2292d2487b53f63c952e22d1d1cd3bb2ac2a07206a723e8ce7beb2a`.
  Its frozen primary/dependency/exclusion/residual/source-universe identities
  are respectively
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`,
  and `48f47e1a053ceafaef97c917a4a21f0475784bdf49c195abf6e650a64dbede2a`.
  Its exact frozen-object scope is `19/19` ordered and `19/19` unique
  primaries (165,166 bytes / 3,277 LF), `2/2` anchors, `1/1` bounded I06
  dependency, and `0/0` exclusions. The anchors are
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationSourceHelpers.cs`
  :: `SourceMethod` and
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase164_46Validation.cs` ::
  `class Phase164_46Validation`; the dependency is
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs` ::
  `class PhaseValidationRegistry`, owned by I06.
- The handoff boundary was freshly rechecked: HEAD/local `main`/`origin/main`
  are `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`, one worktree, no stash, no
  tracked or staged changes, and only the protected untracked 59-byte
  Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  The official pre-I07 validator passed at `32/37`, dependency `70`, residual
  `0`; no relevant I07 author, reviewer, validator, Python, Unity, ROS,
  native, or build process remains.
- The next fresh window must use the mandatory exact-SHA batching sequence:
  (1) independently rehash AGENTS, ledger, sealed I06 report, master/I07
  child, five frozen identities, refs/tree, I06/I07/I08 rows, protected
  `.meta`, process state, and the single-worktree boundary; (2) start one
  fresh independent report-only author from zero over
  `19/19 + 2/2 + 1/1 + 0/0`, reading frozen Git objects in manifest order and
  keeping every proposed RED literally `NOT RUN`; (3) freeze one diagnostic
  report SHA and run three parallel exhaustive specification, quality, and
  adversarial/RED-feasibility reviews on those same unchanged bytes, with
  every reviewer continuing through EOF after every issue; (4) aggregate and
  deduplicate all findings once, route one consolidated report-only repair
  packet to one fresh independent author, and require the complete author-
  validation chain; and (5) freeze the repaired SHA and run a fresh final full
  specification review plus a separate fresh independent quality review on
  one unchanged SHA. Author and final-review gates include safe `68`, official
  pre-ledger `32/37`, and reader-only hypothetical exact-SHA `33/37`. Only
  same-SHA terminal `AUTHOR VALIDATED` + `SPEC COMPLIANT` + `QUALITY APPROVED`
  permits changing only the I07 ledger row, followed by the real `33/37`,
  dependency `70`, residual `0` validator.
- Do not edit product code, tests, frozen authority/manifests, I06, the
  protected `.meta`, Git refs, or worktrees. Do not execute any proposed RED.
  Keep progress delta-only and checkpoint at the frozen diagnostic SHA,
  frozen repaired author SHA, final review verdicts, and ledger seal. Do not
  start I08 until I07 is genuinely sealed and the real post-ledger `33/37`
  validator passes.

- **Live superseding Phase187 Round 2 checkpoint (2026-08-17, successful
  I05 seal and explicit user-requested I06 new-window handoff; I06 not
  started):** the progressive execution ledger is `31/37`, sealed exactly
  through `187-R2-I05`; `187-R2-I06` remains pristine `NOT_RUN` with empty
  verdict, report SHA, and notes, and its report is absent. The formal ledger
  is SHA-256
  `8f422892b19eba74740f48ffe8b40f077e590107b10f7b88b2575520f152d74a`
  (10,152 bytes / 38 strict UTF-8 LF-only lines; no CR or NUL; terminal LF).
  The sealed I05 report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I05.md` at exact
  SHA-256
  `6dc24bde25a75031082fee6739e960020b24ad224a0e50d07f6c4ad8f95f3ca4`
  (88,869 bytes / 476 strict UTF-8 LF-only lines). It records 26 High
  findings (`P1 x15`, `P2 x11`, `P3 x0`), and all 26 proposed deterministic
  RED strategies remain honestly `NOT RUN`.
- One unchanged I05 report SHA received terminal **`AUTHOR VALIDATED`**,
  fresh full **`SPEC COMPLIANT`**, and separate fresh independent **`QUALITY
  APPROVED`** verdicts. The final report covers `8/8` ordered and `8/8`
  unique primaries (79,913 bytes / 1,981 LF), `2/2` anchors, the `1/1`
  bounded I06 dependency, and `0/0` exclusions. Its final audit reproduced
  26 sequential IDs / 234 required fields / 73 citation sites / 231 citation
  ranges over 9 authorized OIDs, with zero unauthorized, out-of-range, or
  stale items. Author and final-review gates passed safe `68`, official
  pre-ledger `30/37`, and reader-only hypothetical exact-SHA `31/37`; the
  real post-ledger validator passed at `31/37`, dependency `70`, residual
  `0`. No proposed RED ran.
- I05 report creation and consolidated repair evidence is retained in
  `build/i05-initial-author-20260817/` and
  `build/i05-diagnostic-consolidated-repair-20260817/`; its ledger-seal
  transaction is `build/i05-ledger-seal-20260817/`. The live report and
  ledger remain at the sealed SHAs above. This I06 handoff checkpoint was
  inserted over the verified pre-handoff AGENTS SHA-256
  `b2c57c5ff55925b199d93bea4446785e2686e48a9d6ddfba4a326e0eb695a264`;
  the new window must independently hash and read the current file through
  EOF.
- The I06 authority is the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I06_VALIDATION_FRAMEWORK_EVIDENCE_MODEL_REVIEW_PLAN.md`
  at SHA-256
  `4770eb9642221ff1ae44827deb98b421c07b5a29bb296c6a7075ef1a83ea9290`.
  Its frozen primary/dependency/exclusion/residual/source-universe identities
  are respectively
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`,
  and `48f47e1a053ceafaef97c917a4a21f0475784bdf49c195abf6e650a64dbede2a`.
  Its exact frozen-object scope is `20/20` ordered and `20/20` unique
  primaries (263,673 bytes / 5,133 LF), `2/2` anchors, `1/1` bounded I05
  dependency, and `0/0` exclusions. The anchors are
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs`
  :: `class PhaseValidationRegistry` and
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/ValidationEvidence.cs` ::
  `class ValidationEvidence`; the dependency is `Scripts/release/run_ci.py`
  :: `def main`, owned by I05.
- The handoff boundary was freshly rechecked: HEAD/local `main`/`origin/main`
  are `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`, one worktree, no stash, no
  tracked or staged changes, and only the protected untracked 59-byte
  Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  The official pre-I06 validator passed at `31/37`, dependency `70`, residual
  `0`; no relevant I06 author, reviewer, validator, Python, Unity, ROS,
  native, or build process remains.
- The new window must use the mandatory exact-SHA batching sequence: (1)
  independently rehash AGENTS, ledger, sealed I05 report, master/I06 child,
  five frozen identities, refs/tree, I05/I06/I07 rows, protected `.meta`,
  process state, and the single-worktree boundary; (2) start one fresh
  independent report-only author from zero over `20/20 + 2/2 + 1/1 + 0/0`,
  reading frozen Git objects in manifest order and keeping every proposed RED
  literally `NOT RUN`; (3) freeze one diagnostic report SHA and run three
  parallel exhaustive specification, quality, and adversarial/RED-feasibility
  reviews on those same unchanged bytes, with every reviewer continuing
  through EOF after every issue; (4) aggregate and deduplicate all findings
  once, route one consolidated report-only repair packet to one fresh
  independent author, and require the complete author-validation chain; and
  (5) freeze the repaired SHA and run a fresh final full specification review
  plus a separate fresh independent quality review on one unchanged SHA.
  Author and final-review gates include safe `68`, official pre-ledger
  `31/37`, and reader-only hypothetical exact-SHA `32/37`. Only same-SHA
  terminal `AUTHOR VALIDATED` + `SPEC COMPLIANT` + `QUALITY APPROVED` permits
  changing only the I06 ledger row, followed by the real `32/37`, dependency
  `70`, residual `0` validator.
- Do not edit product code, tests, frozen authority/manifests, I05, the
  protected `.meta`, Git refs, or worktrees. Do not execute any proposed RED.
  Keep progress delta-only and checkpoint at the frozen diagnostic SHA,
  frozen repaired author SHA, final review verdicts, and ledger seal. Do not
  start I07 until I06 is genuinely sealed and the real post-ledger `32/37`
  validator passes; stop after the I06 seal and prepare the next handoff.

- **Live superseding Phase187 Round 2 checkpoint (2026-08-17, explicit
  user-requested I05 new-window handoff; I05 not started):** this window is
  paused before I05 authoring. The progressive execution ledger is `30/37`,
  sealed exactly through `187-R2-I04`; `187-R2-I05` remains pristine `NOT_RUN`
  with empty verdict, report SHA, and notes, and its report is absent. The
  formal ledger remains SHA-256
  `8534392d4ed0e0ff359434a9c4f2a9b2457de14751925311cbedf1bb36988525`
  (9,993 bytes / 38 strict UTF-8 LF-only lines; no CR or NUL; terminal LF).
  The sealed I04 report remains SHA-256
  `43f8b1558f99addf8c3f25f33f0a32c9a0a7b5c74d691748f989524430e7c470`
  (96,253 bytes / 503 strict UTF-8 LF-only lines). This checkpoint was inserted
  over the verified pre-handoff AGENTS SHA-256
  `3c1e5bf9070f0bff85563191f52b2a87deac5d9dfe52a6ada817c063879ae819`;
  the new window must independently hash and read the current file through EOF.
- The I05 authority is the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I05_CI_WORKFLOW_ORCHESTRATION_REVIEW_PLAN.md`
  at SHA-256
  `ab3863bfd8b046019a1ad6aed481041dccbc664f5047e402bd243f1e115a64ca`.
  Its frozen primary/dependency/exclusion/residual/source-universe identities
  are respectively
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`,
  and `48f47e1a053ceafaef97c917a4a21f0475784bdf49c195abf6e650a64dbede2a`.
  Its exact frozen-object scope is `8/8` ordered and `8/8` unique primaries
  (79,913 bytes / 1,981 LF), `2/2` anchors, `1/1` bounded I06 dependency, and
  `0/0` exclusions. The anchors are `Scripts/release/run_ci.py :: def main`
  and `.github/workflows/dotnet-tests.yml :: run_ci.py`; the dependency is
  `Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs` ::
  `class PhaseValidationRegistry`, owned by I06.
- The handoff boundary was freshly rechecked: HEAD/local `main`/`origin/main`
  are `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`, one worktree, no stash, no
  tracked or staged changes, and only the protected untracked 59-byte Phase186
  `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  The official pre-I05 validator passed at `30/37`, dependency `70`, residual
  `0`; no relevant I05 author, reviewer, validator, Python, Unity, ROS, native,
  or build process remains.
- The new window must use the mandatory exact-SHA batching sequence: (1)
  independently rehash AGENTS, ledger, sealed I04 report, master/I05 child/five
  manifests, refs/tree, I04/I05/I06 rows, protected `.meta`, process state, and
  the single-worktree boundary; (2) start one fresh independent report-only
  author from zero over `8/8 + 2/2 + 1/1 + 0/0`, reading frozen Git objects in
  manifest order and keeping every proposed RED literally `NOT RUN`; (3)
  freeze one diagnostic report SHA and run three parallel exhaustive
  specification, quality, and adversarial/RED-feasibility reviews on those same
  unchanged bytes, with every reviewer continuing through EOF after every
  issue; (4) aggregate and deduplicate all findings once, route one
  consolidated report-only repair packet to one fresh independent author, and
  require the complete author-validation chain; and (5) freeze the repaired
  SHA and run a fresh final full specification review plus a separate fresh
  independent quality review on one unchanged SHA. Author and final-review
  gates include safe `68`, official pre-ledger `30/37`, and reader-only
  hypothetical exact-SHA `31/37`. Only same-SHA terminal `AUTHOR VALIDATED` +
  `SPEC COMPLIANT` + `QUALITY APPROVED` permits changing only the I05 ledger
  row, followed by the real `31/37`, dependency `70`, residual `0` validator.
- Do not edit product code, tests, frozen authority/manifests, I04, the
  protected `.meta`, Git refs, or worktrees. Do not execute any proposed RED.
  Keep progress delta-only and checkpoint at the frozen diagnostic SHA, frozen
  repaired author SHA, final review verdicts, and ledger seal. Do not start I06
  until I05 is genuinely sealed and the real post-ledger validator passes.

- **Live superseding Phase187 Round 2 checkpoint (2026-08-17, successful I04 seal and elevated-risk window boundary):** the progressive execution ledger is now `30/37`, sealed exactly through `187-R2-I04`. `187-R2-I05` remains pristine `NOT_RUN` with empty verdict, report SHA, and notes. The formal ledger is SHA-256 `8534392d4ed0e0ff359434a9c4f2a9b2457de14751925311cbedf1bb36988525` (9,993 bytes / 38 strict LF-only lines). Because I04 is an elevated false-positive-risk closure, stop here and do not start I05 in this window.
- The sealed I04 report is `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I04.md` at exact SHA-256 `43f8b1558f99addf8c3f25f33f0a32c9a0a7b5c74d691748f989524430e7c470` (96,253 bytes / 503 strict UTF-8 LF-only lines). It records 30 High findings (`P1 x15`, `P2 x15`, `P3 x0`), and all 30 proposed deterministic RED strategies remain honestly `NOT RUN`. One unchanged SHA received terminal `AUTHOR VALIDATED`, fresh full `SPEC COMPLIANT`, and separate fresh independent `QUALITY APPROVED`. The real post-ledger validator passed at `30/37`, dependency `70`, residual `0`.
- I04 report creation/repair evidence is `build/i04-initial-author-20260817/` and `build/i04-diagnostic-consolidated-repair-20260817/`; its ledger-seal transaction is `build/i04-ledger-seal-20260817/`. Each retained original/modified/diff/verification/rollback artifacts and successful rollback rehearsal on another copy; the live report and ledger remain sealed at the SHAs above. No proposed RED, product/test edit, frozen authority/manifest edit, I03 edit, protected-artifact edit, Git-ref change, or worktree change occurred.
- Next fresh window must independently read this file through EOF and rehash the full boundary before starting I05. At this seal HEAD/local `main`/`origin/main` remain `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`, one worktree, no stash, empty tracked/staged diffs, and only the protected untracked 59-byte Phase186 `.meta` at SHA-256 `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-17, I04 final dual review approved; ledger seal authorized):** the unchanged I04 report SHA-256 `43f8b1558f99addf8c3f25f33f0a32c9a0a7b5c74d691748f989524430e7c470` (96,253 bytes / 503 LF) now holds terminal `AUTHOR VALIDATED`, fresh full `SPEC COMPLIANT`, and separate fresh independent `QUALITY APPROVED`. It records 30 High findings (`P1 x15`, `P2 x15`, `P3 x0`), and all 30 proposed RED strategies remain `NOT RUN`.
- Both final reviewers independently exhausted `12/12` ordered/unique primaries (228,672 bytes / 4,673 LF), `2/2` anchors, the `1/1` bounded dependency, and `0/0` exclusions; each reproduced 30 IDs / 270 fields / 76 citation sites / 191 ranges over 13 authorized OIDs with zero unauthorized/out-of-range items, approved R1-R12, and freshly passed safe `68`, official `29/37`, and reader-only hypothetical exact-SHA `30/37` with the formal ledger unchanged.
- The formal ledger remains SHA-256 `0f10323270ec506b7d4a8f716f2cb1b41979f67b3a153058b8509f2bec80afdb` and I04 remains pristine `NOT_RUN` at this checkpoint. The only authorized next mutation is a verified one-row I04 ledger transaction to `COMPLETE` / `FINDINGS_REPORTED` with exact report SHA and 30-finding notes, followed by the real `30/37`, dependency `70`, residual `0` validator; restore I04 to pristine `NOT_RUN` on any failure and stop without starting I05.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-17, I04 consolidated diagnostic repair AUTHOR VALIDATED; final dual review next):** the formal ledger remains `29/37` at SHA-256 `0f10323270ec506b7d4a8f716f2cb1b41979f67b3a153058b8509f2bec80afdb`, with I04 pristine `NOT_RUN`. The repaired report `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I04.md` is frozen at SHA-256 `43f8b1558f99addf8c3f25f33f0a32c9a0a7b5c74d691748f989524430e7c470` (96,253 bytes / 503 strict LF-only lines) and holds terminal `AUTHOR VALIDATED`: 30 High findings (`P1 x15`, `P2 x15`, `P3 x0`), 30/30 proposed RED strategies literally `NOT RUN`.
- One fresh author source-checked and applied all R1-R12 corrections in one batch; all original 24 findings remain and six distinct findings were added. Exact disposition is R1->006, R2->005, R3->001-004, R4->013, R5->009, R6->014, R7->024 upgraded to P1, R8->028, R9->020, R10->022, R11->029, R12->018/023/027. Transaction evidence is `build/i04-diagnostic-consolidated-repair-20260817/`. Full `12/12 + 2/2 + 1/1 + 0/0` coverage, two EOF reads, 30 IDs/270 fields/191 citation ranges/13 authorized OIDs, safe `68`, official `29/37`, and reader-only hypothetical exact-SHA `30/37` all passed; no proposed RED ran.
- Freeze these bytes and run one fresh final exhaustive specification reviewer plus one separate fresh independent quality reviewer in parallel. Each must redo the entire authorized scope through EOF after every issue and independently run safe `68`, official `29/37`, and reader-only hypothetical exact-SHA `30/37`. Do not edit the report while either review runs. Only same-SHA terminal `SPEC COMPLIANT` and `QUALITY APPROVED` permit the I04 ledger transaction and real `30/37` validator.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-17, I04 three-reviewer diagnostic batch complete and rejected; one consolidated repair pending):** the formal ledger remains `29/37` at SHA-256 `0f10323270ec506b7d4a8f716f2cb1b41979f67b3a153058b8509f2bec80afdb`, and I04 remains pristine `NOT_RUN`. The frozen diagnostic report remains SHA-256 `38a6b783d6817ad4060796ea961d1bb31ffcf3ca46361940886ffc406049f598` (71,342 bytes / 415 LF), 24 High findings (`P1 x10`, `P2 x14`), 24/24 proposed RED strategies `NOT RUN`. It holds `AUTHOR VALIDATED` but has complete verdicts `DIAGNOSTIC SPEC ISSUES FOUND`, `DIAGNOSTIC QUALITY ISSUES FOUND`, and `DIAGNOSTIC RED-FEASIBILITY ISSUES FOUND`; it is unapproved and must not enter the ledger.
- All three fresh reviewers independently exhausted the same unchanged `12/12 + 2/2 + 1/1 + 0/0` scope through EOF and reproduced 24 IDs / 216 fields / 151 citation ranges over 13 authorized OIDs with zero unauthorized/out-of-range items. The controller deduplicated their complete lists into this binding twelve-item report-only packet: (R1) replace the stale-PID non-defect with authenticated numeric-PID cleanup coverage; (R2) add unbounded redirected-output accumulation; (R3) close overload-present and normal-success descendant ownership and make REDs 001/002/004 reach deterministic existing seams; (R4) add full ASCII version-grammar validation for Unicode-digit/terminal-LF inputs; (R5) broaden finding 007 to all optional-package dependency properties and semantic JSON location; (R6) cover concurrent `VersionBump.run` coherence; (R7) bind/verify native Windows x64 architecture; (R8) cover artifact/staging/package source and destination symlink/junction redirection; (R9) cover direct staging-manifest partial-write atomicity; (R10) add the cleanup-induced false-green oracle in `test_remote_gateway_tooling.py:46-65`; (R11) correct finding 023's default-CI citation and account for the three other explicit no-parent-deadline I05 paths; and (R12) bind findings 014/018/022 REDs to deterministic stand-ins, barriers, controls, restoration, deadlines, and teardown. All existing 24 causal findings remain source-supported; each packet item must be source-checked, not silently dropped, and any disproved item must retain exact counterevidence.
- Next: one fresh independent report-only repair author must redo complete frozen coverage from zero, apply all surviving corrections in one batch with a verified rollback transaction, update every affected graph/branch/finding/count/reconciliation surface, keep every proposed RED literally `NOT RUN`, and complete two EOF reads, full audit, safe `68`, official `29/37`, and reader-only hypothetical exact-SHA `30/37`. Freeze that `AUTHOR VALIDATED` SHA, then run fresh final specification and separate quality reviews in parallel on unchanged bytes; no report edit, ledger mutation, or final approval exists yet.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-17, I04 initial diagnostic author validated; three-reviewer batch next):** the formal ledger remains `29/37` at SHA-256 `0f10323270ec506b7d4a8f716f2cb1b41979f67b3a153058b8509f2bec80afdb`; I04 remains pristine `NOT_RUN`. The new report `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I04.md` is frozen at SHA-256 `38a6b783d6817ad4060796ea961d1bb31ffcf3ca46361940886ffc406049f598` (71,342 bytes / 415 strict LF-only lines) with terminal `AUTHOR VALIDATED`, 24 High-confidence findings (`P1 x10`, `P2 x14`, `P3 x0`), and 24/24 proposed RED strategies literally `NOT RUN`.
- The author independently completed `12/12` ordered/unique primaries (228,672 bytes / 4,673 LF), `2/2` anchors, the `1/1` bounded I05 dependency, `0/0` exclusions, two EOF reads, full mechanics/object/triple/citation/stale audit, safe `68`, official `29/37`, and reader-only hypothetical exact-SHA `30/37`; transaction evidence is `build/i04-initial-author-20260817/`. No product/test/frozen authority/I03/ledger/protected artifact/ref/worktree changed.
- Freeze these report bytes and run one fresh exhaustive specification reviewer, one fresh exhaustive quality reviewer, and one fresh adversarial/RED-feasibility reviewer in parallel on this exact SHA. Every reviewer must independently cover the full authorized scope through EOF after every issue and return one complete list. Do not edit the report until all three finish. Then aggregate once and route one consolidated report-only repair to one new author; full author gates and final fresh specification plus separate quality reviews remain mandatory before any I04 ledger seal.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-17, explicit
  user-requested I04 new-window handoff; I04 not started):** this window is
  paused before I04 authoring. The progressive execution ledger remains
  `29/37`, sealed exactly through `187-R2-I03`; `187-R2-I04` remains pristine
  `NOT_RUN` with empty verdict, report SHA, and notes, and its report is absent.
  The ledger remains SHA-256
  `0f10323270ec506b7d4a8f716f2cb1b41979f67b3a153058b8509f2bec80afdb`.
  The sealed I03 report remains SHA-256
  `43e2e0dc57503f4fb836fc38fc55abfb48397ef39547c6fa541da3da1bad7be5`.
  This checkpoint was inserted over the verified pre-handoff AGENTS SHA-256
  `956ca87b75e3dc9c7df7519847b7e85fb0e68a12bd6e5b680e97ed8323ab6406`;
  the new window must independently hash and read the current file through EOF.
- The I04 authority is unchanged: master SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`,
  child SHA-256
  `ef56f18e54d5f9344425bd9e4a37097c6101d72528751d1bf4809b24c0a9f1ab`,
  and frozen primary/dependency/exclusion/residual/source-universe identities
  `e2d559a3...e453`, `ceb8c6bf...c1c2d`, `3e89ba11...76c`,
  `cf79de26...2887`, and `48f47e1a...de2a`. Its exact scope is
  `12/12 + 2/2 + 1/1 + 0/0`, read from frozen Git objects in manifest order.
- The handoff boundary was freshly rechecked: HEAD/local `main`/`origin/main`
  are `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`, one worktree, no stash, no
  tracked/staged changes, only the protected untracked 59-byte Phase186
  `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`,
  and no relevant I04 author/reviewer/validator process.
- The new window must use the mandatory exact-SHA batching sequence: one fresh
  independent report-only author; freeze the diagnostic SHA; three parallel
  exhaustive specification, quality, and adversarial/RED-feasibility reviews;
  aggregate once; one fresh consolidated-repair author; full author gates;
  then final fresh specification and separate quality reviews on one unchanged
  SHA. Author and final-review gates include safe `68`, official pre-ledger
  `29/37`, and reader-only hypothetical `30/37`. Only same-SHA `AUTHOR
  VALIDATED` + `SPEC COMPLIANT` + `QUALITY APPROVED` permits changing the I04
  ledger row, followed by the real `30/37`, dependency `70`, residual `0`
  validator. Every proposed RED remains `NOT RUN`.
- Do not edit product code, tests, frozen authority/manifests, I03, protected
  `.meta`, refs, or worktrees. Keep progress delta-only and checkpoint at the
  frozen author SHA, final verdicts, and ledger seal. Because I04 is elevated
  false-positive risk, stop after the I04 ledger seal and real `30/37` validator;
  do not start I05 in that window.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-17, successful I03 seal
  and explicit I04 high-risk window boundary):** the progressive execution
  ledger is now `29/37`, sealed exactly through `187-R2-I03`. `187-R2-I04` is
  next and remains pristine `NOT_RUN` with empty verdict, report SHA, and notes.
  The formal ledger is SHA-256
  `0f10323270ec506b7d4a8f716f2cb1b41979f67b3a153058b8509f2bec80afdb`
  (9,834 bytes / 38 strict LF lines; no CR or NUL; terminal LF). Every lower
  live-checkpoint paragraph is historical evidence and is superseded by this
  checkpoint.
- The sealed I03 report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I03.md` at exact
  SHA-256
  `43e2e0dc57503f4fb836fc38fc55abfb48397ef39547c6fa541da3da1bad7be5`
  (141,133 bytes / 541 strict UTF-8 LF-only lines). It records 28
  High-confidence findings (`P1 x11`, `P2 x17`, `P3 x0`), and all 28 proposed
  deterministic RED strategies remain honestly `NOT RUN`.
- One unchanged I03 report SHA received terminal **`AUTHOR VALIDATED`**, fresh
  full **`SPEC COMPLIANT`**, and separate fresh independent **`QUALITY
  APPROVED`** verdicts. Author and both final reviewers independently covered
  `80/80` ordered primaries / `75/75` unique blobs, `2/2` anchors, `1/1`
  bounded dependency, and `2985/2985` exclusions with zero excluded-body
  reads. The report has 9 sections, 28 sequential IDs, 252 required fields,
  80/80 path/OID/rule triples, 318 citation sites / 362 ranges over 36
  authorized OIDs, and zero unauthorized, out-of-range, or stale items. Each
  final reviewer freshly passed safe `68`, official pre-ledger `28/37`, and
  reader-only hypothetical exact-SHA `29/37`; the real post-ledger validator
  then passed at `29/37`, dependency `70`, residual `0`.
- The final I03 repair corrected R1-R5 together. Finding 017 now quotes the
  exact `[\\/]` source regex, uses a valid two-leading-backslash UNC example,
  and treats public drive-letter paths as existing controls. Finding 022 uses
  a genuinely matching `?` glob. Finding 012 covers the fourth analyzer-meta
  `exists()` path-type consumer. New finding 028 is `P2`/High for the distinct
  case-sensitive `.DLL`/asmdef collision-discovery gap. All graph, hostile,
  paired, distinctness, reconciliation, and count surfaces were approved.
- I03 report-repair transaction evidence is
  `build/i03-r1-r5-consolidated-repair-20260817/`; its ledger-seal transaction
  is `build/i03-ledger-seal-20260817/`. Both contain preserved originals,
  modified copies, diffs, verification records, executable rollback scripts,
  and successful rollback rehearsals on separate copies; live report and
  ledger remain at the sealed SHAs above.
- I04 authority remains the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I04_DOWNLOAD_PROCESS_BUILD_RELEASE_SECURITY_REVIEW_PLAN.md`
  at SHA-256
  `ef56f18e54d5f9344425bd9e4a37097c6101d72528751d1bf4809b24c0a9f1ab`
  (8,909 bytes / 179 CRLF physical lines). Its frozen dossier is `12/12`
  ordered primaries / `12/12` unique blobs (228,672 bytes / 4,673 LF), `2/2`
  anchors, `1/1` bounded I05 dependency, and `0/0` exclusions. The I04 report
  is absent.
- Because I04 is an elevated false-positive-risk closure and this window
  completed I03, do not start I04 report authoring in this window. The next
  fresh window must: (1) read this file through EOF and independently rehash
  AGENTS, ledger, sealed I03 report, master/I04 child/five manifests,
  refs/tree, I03/I04 rows, protected `.meta`, process state, and the
  single-worktree boundary; (2) start one fresh I04 report-only author from
  zero over `12/12 + 2/2 + 1/1 + 0/0`, preserving frozen-index order and
  leaving every proposed RED `NOT RUN`; (3) freeze one diagnostic SHA and run
  the mandatory parallel exhaustive specification, quality, and
  adversarial/RED-feasibility batch, aggregate all issues once, and route one
  consolidated repair to one new author; (4) require the repaired SHA to pass
  the full author chain, safe `68`, official `29`, and reader-only hypothetical
  `30`, followed by fresh final specification and separate quality reviews on
  one unchanged SHA; and (5) only then seal I04 and require the real `30/37`
  validator before continuing I05-I11.
- At this checkpoint HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`, one worktree, no stash, empty
  tracked/staged diffs, and only the protected untracked 59-byte Phase186
  `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`
  in status. No relevant author/reviewer/validator/Python/Unity/ROS/native/build
  process is running. No product, test, proposed RED, frozen authority/
  manifest, protected artifact, Git ref, or worktree changed.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-16, I03 final dual
  review complete and rejected; one consolidated repair pending):** the
  progressive execution ledger remains `28/37`, sealed exactly through
  `187-R2-I02`. `187-R2-I03` is active but unsealed; its formal row is still
  pristine `NOT_RUN` with empty verdict, report SHA, and notes. The formal
  ledger remains SHA-256
  `65abc09adc7577fc62b6b1dcc964189a4d8024a014ceb891c052edab1c3885ac`,
  byte- and mtime-unchanged. Every lower live-checkpoint paragraph is historical
  evidence and is superseded by this checkpoint.
- The live I03 report remains byte-frozen at exact SHA-256
  `ecf7b66079cbec807b1efdcaa9726ba4eea3e0e93054436839a9e26140977571`
  (133,387 bytes / 526 strict UTF-8 LF-only lines), 27 High findings
  (`P1 x11`, `P2 x16`, `P3 x0`), 27/27 literal `The strategy is NOT RUN.`
  It holds terminal `AUTHOR VALIDATED` but final verdicts **`SPEC ISSUES
  FOUND`** and **`QUALITY ISSUES FOUND`**; it is therefore rejected, has no
  approval, and must not enter the ledger.
- Both final reviewers exhausted the same unchanged bytes through EOF,
  independently covering `80/80` ordered primaries / `75/75` unique blobs,
  `2/2` anchors, `1/1` bounded dependency, and `2985/2985` exclusion rows with
  zero excluded-body reads and without opening the protected `.meta`. Both
  independently reproduced the report's own coverage arithmetic (4 shared blob
  identities / 5 duplicate instances; 280,560 B / 6,238 LF path-counted;
  278,636 B / 6,164 LF unique-counted). Both reran all three gates: safe `68`
  (`Ran 68 tests`, `OK`, exit `0`), official `28/37` dependency `70` residual
  `0`, and reader-only hypothetical exact-SHA `29/37` dependency `70` residual
  `0` `errors=[]` with the real ledger never written. Specification measured
  297 citation sites / 337 ranges, 0 out-of-bounds, 36 authorized reachable
  OIDs, 80/80 triples, 12/12 exact-case catalog tokens, 0 stale residue.
  Quality independently confirmed all 27 findings real, correctly
  severity-rated, mutually distinct, with no false trigger and no overclaim,
  and confirmed C1-C6 and N24-N27 all correctly applied.
- The controller deduplicated both complete lists into **five** binding
  report-only corrections. Both reviewers independently raised R1, so it is
  the highest-confidence item. One fresh independent author must apply all five
  together and may not silently drop one:
  - **R1 (both reviewers) — line 387 misquoted regex.** The report renders
    `` `\b[A-Za-z]:[\/]` `` but frozen source
    `c8c0b57ba6ad785ae9f5f90f75efcaecbbe25363:97` is
    `re.compile(r"\b[A-Za-z]:[\\/]")`. The classes are not equivalent: `[\/]`
    matches only `/`, `[\\/]` matches `\` or `/`. This is a misquotation at a
    cited evidence range, and it would mislead a reader into inferring a
    non-existent backslash-drive-path gap in the one finding that enumerates
    unscanned host-path classes. Both reviewers rejected the author's
    "convention" defense: the report elsewhere quotes source strings exactly
    (finding 005's five Python substrings; finding 025's `"FOXBRG" + "042"`),
    and backslashes inside backticks are literal in Markdown. Change `[\/]` to
    `[\\/]`. Optionally note in finding 017's RED that the backslash-separated
    drive-letter case in public content is an **existing negative control**,
    not a target.
  - **R2 (specification) — line 387 malformed UNC example.** `\host\users\alice\...`
    is not a UNC path; UNC requires two leading backslashes. Finding 017's
    stated gap is that no scanner models UNC roots, so illustrating it with a
    non-UNC string weakens the finding's own class definition. Change to
    `\\host\users\alice\...`.
  - **R3 (quality) — line 446 finding 022's example inverts its own outcome.**
    The trigger cites a "matching" `?` wildcard `src\FoxRun?iagnostics.cs`, but
    the only frozen provider sources are `src/FoxRunR2fuDiagnostics.cs`
    (`746a202bbbbad721fcafe902b4d7c8305f57b661:19`) and
    `src/FoxRunBridgeDiagnostics.cs`
    (`160c53040f1fa7ce51ebd641ec7b6ad178c4248e:22`); `?` matches exactly one
    character, so that glob matches nothing and
    `66348745e50432de3eda1ad6b5e34e55dcb11fcd:307-319` raises
    `ValueError("Compile glob matched no files")`, caught at `:535-536` and
    recorded as a failure — the opposite of the claimed bypass. Replace with a
    genuinely matching example such as `src\FoxRunR2fu?iagnostics.cs` or
    `src\FoxRunBridge?iagnostics.cs`. Finding, severity, and counts unchanged.
  - **R4 (quality) — the mandated `case-sensitive DLL` hostile attempt is
    labelled but never attempted.** Child plan `a7cddab7...:68` lists it and
    `:211` requires every hostile scenario to be attempted; the token appears
    only once, in the line-175 label list. No hostile paragraph concerns a DLL.
    The reviewer's own trace found DLL-identity predicates fail closed at
    `b628c3f8...:200-203`, `66348745...:759-765`, `:778-781`,
    `c8c0b57...:455-458`, and `c8c0b57...:123-157`/`:685`, with one real
    escape: `c8c0b57ba6ad785ae9f5f90f75efcaecbbe25363:655-672`, where
    `check_google_protobuf_collision` builds `dll_stems` from
    `plugin_dir.glob("*.dll")`, which on a case-sensitive host misses a `.DLL`
    suffix, so a case-varied plug-in DLL sharing a stem with a sibling asmdef
    escapes both `filename_collisions` and `name_collisions`. Minimum
    correction, counts unchanged: add one hostile paragraph recording the
    attempt and this traced outcome, in the restrained style already used for
    the non-atomic `report.json` at line 159. The author may instead promote
    the escape to finding 028 if the frozen evidence warrants; note its
    provenance consequence overlaps finding 011 and only the collision
    consequence is new. If promoted at P3 the exact new counts are `28` total,
    `P1 x11`, `P2 x16`, `P3 x1`, High x28, 28/28 NOT RUN; if at P2, `28` total,
    `P1 x11`, `P2 x17`, `P3 x0`, High x28.
  - **R5 (quality) — finding 012 omits a fourth `exists()` site it already
    reads.** Finding 012 says "three independent consumers" share the
    path-shape root, but `66348745e50432de3eda1ad6b5e34e55dcb11fcd:526-527`
    (`meta = Path(str(artifact) + ".meta"); if not meta.exists():`) is a
    fourth — a directory named `<analyzer>.dll.meta` satisfies it. The report
    cites that exact range in finding 013 (line 340) but draws only the
    YAML/identity conclusion, and finding 013's trigger presupposes a `.meta`
    **regular file**, so the type case falls between 012 and 013. A
    compensating `is_file()` exists at `b628c3f83ed617630846dd6310362105e523a0f0:202`
    but in a different tool, and `package-check.yml:27-50` runs neither.
    Either extend finding 012 to that fourth consumer with a matching RED case
    (replace one analyzer `.dll.meta` with a directory), or add one sentence to
    finding 013 recording that the type gap is covered by the matrix
    `is_file()` and therefore not assigned. Counts unchanged either way; if
    instead broken out as its own finding, `28` total, `P1 x11`, `P2 x17`,
    `P3 x0`, High x28.
- Under the recommended minimum corrections the repaired report stays at `27`
  High findings, `P1 x11`, `P2 x16`, `P3 x0`, and 27/27 honest `NOT RUN`. If
  the author's independent source-check promotes R4 or R5 to a new finding, it
  must update every count, graph, branch, distinctness, reconciliation, and
  summary surface accordingly and state the exact reconciled counts. If frozen
  evidence disproves any item, record the exact counter-evidence rather than
  omitting it.
- One recorded non-defect: the prior author self-reported 331 citation ranges
  while specification measured 337. That number appears nowhere in the report,
  so it is a handoff-note discrepancy only and requires no report change.
- Exact continuation order: (1) rehash AGENTS, live report, ledger,
  master/child/five manifests, refs/tree, I02/I03 rows, protected `.meta`, and
  the single-worktree boundary; (2) give R1-R5 to one fresh independent
  report-only author, which must redo `80/80 + 75/75 + 2/2 + 1/1 + 2985/2985`
  from zero, source-check every item, and apply one consolidated repair; (3)
  require the full author chain — two post-edit EOF reads, mechanics/object/
  triple/citation/stale audits, safe `68`, official `28`, and reader-only
  hypothetical `29` with the ledger untouched; (4) freeze the new
  `AUTHOR VALIDATED` SHA and run fresh final specification plus separate
  independent quality reviews in parallel on unchanged bytes, letting both
  finish after every issue; (5) only when one unchanged SHA holds both `SPEC
  COMPLIANT` and `QUALITY APPROVED`, update only the I03 ledger row to
  `COMPLETE` / `FINDINGS_REPORTED` with that exact SHA and require the real
  post-ledger validator at `29/37`, dependency `70`, residual `0`, restoring
  I03 to pristine `NOT_RUN` on any failure; and (6) continue I04-I11 in frozen
  order through `37/37`. I03 is not the terminal endpoint.
- Harness note for future windows: this environment has no Codex
  `tools.apply_patch`. Its surgical patch primitive is `Edit` (unique-context
  replacement, equivalent to a bare `@@` hunk). The binding properties are
  Edit-only on the live report, one patching operation, and a byte-exact final
  SHA or full restore. The successful repair to `ecf7b660...7571` used exactly
  seven `Edit` hunks with zero corrections, each `old_string`/`new_string`
  extracted programmatically and each post-hunk state hashed against a
  precomputed expectation. An earlier attempt that hand-transcribed ~52.8 KB
  failed the exact-SHA gate and correctly restored `f58da243...08ca`.
- At this checkpoint HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443` with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; there is one worktree, no stash,
  tracked and staged diffs are empty, and the sole Git-status path remains the
  protected untracked 59-byte Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  No report edit occurred during either final review. No proposed RED ran. No
  product code, test, frozen authority/manifest, formal ledger, protected
  artifact, Git ref, or worktree changed.
- **Historical Phase187 Round 2 checkpoint (2026-08-16, I03 consolidated
  final-review repair applied and AUTHOR VALIDATED; final dual review not yet
  started):** the progressive execution ledger remains `28/37`, sealed exactly
  through `187-R2-I02`. `187-R2-I03` is active but unsealed; its formal row is
  still pristine `NOT_RUN` with empty verdict, report SHA, and notes. The formal
  ledger remains SHA-256
  `65abc09adc7577fc62b6b1dcc964189a4d8024a014ceb891c052edab1c3885ac`
  (9,675 bytes / 38 strict LF lines), mtime unchanged. Every lower
  live-checkpoint paragraph is historical evidence and is superseded by this
  checkpoint.
- The live I03 report is now
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I03.md` at exact
  SHA-256
  `ecf7b66079cbec807b1efdcaa9726ba4eea3e0e93054436839a9e26140977571`
  (133,387 bytes / 526 strict UTF-8 LF-only lines; no BOM, CR, or NUL; terminal
  LF). It contains 27 High-confidence findings (`P1 x11`, `P2 x16`, `P3 x0`)
  and 27/27 literal `The strategy is NOT RUN.` declarations. The prior rejected
  SHA `f58da243...08ca` is superseded. This SHA has terminal author validation
  only; it has **no** specification or quality verdict and must not enter the
  ledger yet.
- The consolidated C1-C6 + N24-N27 packet from the prior final dual review was
  applied in full. One fresh independent author re-derived `80/80` ordered
  primaries / `75/75` unique blobs, `2/2` anchors, `1/1` bounded dependency, and
  `2985/2985` exclusion accounting with zero excluded-body reads, and
  source-confirmed all ten packet items with none overturned. The controller
  independently re-verified the exact SHA, byte shape, 9 sections, 27 sequential
  IDs, 27 field sets, `P1 x11` / `P2 x16` / `P3 x0`, High x27, 27 `NOT RUN`,
  zero stale 23-finding residue, and the unchanged ledger/`.meta`/refs boundary.
- Author gates all passed on this exact SHA: two post-edit EOF reads; mechanics
  with all 12 catalog tokens exact-case; 36 cited OIDs authorized and reachable;
  `80/80` ordered path/OID/rule triples; 297 citation sites / 331 ranges with
  zero out-of-bounds and zero unauthorized; clean stale scan; safe `68`
  (`Ran 68 tests`, `OK`, exit `0`); official execution validation at `28/37`,
  dependency `70`, residual `0`; and formal-ledger-reader-only in-memory
  hypothetical exact-SHA validation at `29/37`, dependency `70`, residual `0`,
  `errors=[]`, with the real ledger SHA and mtime unchanged.
- The live write used exactly seven `Edit` hunks with zero corrections, each
  `old_string`/`new_string` extracted programmatically and each post-hunk state
  hashed against a precomputed expectation. No wholesale write ever touched the
  live report. Note for future windows: this harness has no Codex
  `tools.apply_patch`; its surgical patch primitive is `Edit` (unique-context
  replacement, equivalent to a bare `@@` hunk). The binding properties are
  Edit-only on the live report, one patching operation, and a byte-exact final
  SHA or full restore. An earlier retry failed this gate purely through manual
  transcription of ~52.8 KB and correctly restored `f58da243...08ca`.
- Transaction evidence is `build/i03-final-review-repair-retry-a1f3c7/` with
  `187-R2-I03.ORIGINAL.md`, `187-R2-I03.MODIFIED.md`, `187-R2-I03.DIFF.patch`,
  `VERIFICATION.txt`, and an executable `ROLLBACK.sh`. Rollback was rehearsed on
  a separate copy (`ROLLBACK_OK`, exit `0`, restored to `f58da243...08ca`) while
  the live report retained the new SHA.
- One open item the author deliberately did not overturn, flagged for the final
  reviewers rather than decided by the author: finding 017 renders the scanner
  regex as `[\/]` while the Python source is `[\\/]`. The author judged the
  supported claim true regardless, the citation in-bounds, and the convention
  internally consistent on that line. Both final reviewers must rule on it
  explicitly instead of inheriting that judgment.
- Exact continuation order: (1) rehash AGENTS, live report, ledger,
  master/child/five manifests, refs/tree, I02/I03 rows, protected `.meta`, and
  the single-worktree boundary; (2) on the unchanged SHA
  `ecf7b660...7571` start one fresh full specification reviewer and one separate
  fresh independent quality reviewer in parallel, each covering
  `80/80 + 75/75 + 2/2 + 1/1 + 2985/2985` from zero, continuing through EOF after
  every issue, returning one exhaustive list, and running its own fresh safe
  `68`, official `28`, and reader-only hypothetical `29`; do not edit the report
  while either runs; (3) if either finds issues, let both finish, aggregate once,
  and route one consolidated packet to one new author, then repeat the full
  author chain and both final reviews; (4) only when one unchanged SHA holds both
  `SPEC COMPLIANT` and `QUALITY APPROVED`, update only the I03 ledger row to
  `COMPLETE` / `FINDINGS_REPORTED` with that exact SHA and require the real
  post-ledger validator at `29/37`, dependency `70`, residual `0`, restoring I03
  to pristine `NOT_RUN` on any failure; and (5) continue I04-I11 in frozen order
  through `37/37`. I03 is not the terminal endpoint.
- At this checkpoint HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443` with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; there is one worktree, no stash,
  tracked and staged diffs are empty, and the sole Git-status path remains the
  protected untracked 59-byte Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  No proposed RED ran. No product code, test, frozen authority/manifest, formal
  ledger, protected artifact, Git ref, or worktree changed. Only the ignored I03
  report, ignored `build/` transaction evidence, and this ignored bootstrap
  changed.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-16, explicit
  user-requested pause during the consolidated I03 final-review repair):** the
  progressive execution ledger remains `28/37`, sealed exactly through
  `187-R2-I02`. `187-R2-I03` remains unsealed and its formal row is pristine
  `NOT_RUN` with empty verdict, report SHA, and notes. The formal ledger remains
  SHA-256
  `65abc09adc7577fc62b6b1dcc964189a4d8024a014ceb891c052edab1c3885ac`
  (9,675 bytes / 38 strict LF lines). The user explicitly requested this
  window stop after refreshing this checkpoint and producing a Claude Code
  continuation prompt. Every lower live-checkpoint is historical evidence and
  is superseded by this pause checkpoint.
- The live I03 report remains the rejected exact SHA-256
  `f58da243d8b7d665535a685af6f912732fa332abe237d0260e87cdcf58ca08ca`
  (105,908 bytes / 468 strict UTF-8 LF-only lines; no BOM, CR, or NUL;
  terminal LF). It still contains 23 High findings (`P1 x8`, `P2 x15`) and
  23 honest `NOT RUN` strategies. It has terminal author validation from the
  prior cycle but final verdicts **`SPEC ISSUES FOUND`** and **`QUALITY ISSUES
  FOUND`**; therefore it is rejected, has no approval, and must not enter the
  ledger.
- The complete final-review packet remains the six existing-report corrections
  plus four new candidates in the immediately lower `I03 final dual review
  rejected and one consolidated repair pending` checkpoint. Their deduplicated
  expected outcome, if all four candidates survive, is 27 High findings,
  `P1 x11`, `P2 x16`, `P3 x0`, and `27/27` honest `NOT RUN` strategies. That
  lower packet is binding detail for the next author; this pause does not
  supersede its C1-C6 and N24-N27 content, only its active-work status.
- A fresh independent repair author completed from-zero source coverage before
  the pause: `80/80` ordered primaries / `75/75` unique blobs, `2/2` anchors,
  `1/1` bounded dependency, and `2985/2985` exclusion accounting with zero
  excluded-body reads. It source-confirmed C1-C6 and N24-N27 and prepared a
  scratch desired report at
  `build/i03-final-review-consolidated-repair-from-f58da243/187-R2-I03.desired.md`
  with SHA-256
  `ecf7b66079cbec807b1efdcaa9726ba4eea3e0e93054436839a9e26140977571`
  (133,387 bytes / 526 LF). Its scratch audit passed 9 sections, 27 findings,
  243 fields, `P1 x11`, `P2 x16`, High x27, 27 NOT RUN, 80 triples, and 297
  citations over 36 authorized OIDs with zero unauthorized/out-of-range/stale
  items. This is progress evidence only and is not reusable author coverage or
  approval.
- That repair author's sole live-report `tools.apply_patch` attempt used a
  generated patch with incompatible ranged hunk headers and failed before any
  write (`Failed to find context '-124,63 +124,71 @@'`). It made no second live
  attempt, signed no `AUTHOR VALIDATED`, and the controller explicitly
  interrupted it at the user's pause. The live report remained byte-identical
  to `f58da243...08ca`.
- Before interruption, the author prepared a corrected **untrusted** Codex
  custom patch at
  `build/i03-final-review-consolidated-repair-from-f58da243/next-author-custom-bare-at-at.patch`,
  SHA-256
  `a706cfb5739c126f4c2b391c75f5b11651af3d9f9f4a2bd5f2abbcc08262f7af`
  (81,034 bytes / 268 LF), containing one target update, seven bare `@@` hunks,
  and zero ranged hunk headers. A scratch simulator found one ordered match for
  every hunk and reproduced exact desired SHA `ecf7b660...7571`, 133,387 bytes /
  526 LF. The next author may inspect this staged patch only after independently
  redoing boundary, source coverage, and desired-report audits; it must not
  treat the staged content or simulation as a gate.
- Exact continuation order for the next Claude Code window: (1) read this file
  through EOF and independently rehash AGENTS, live report, formal ledger,
  master/child/five manifests, refs/tree, I02/I03 rows, protected `.meta`,
  process state, and single-worktree boundary; (2) start one fresh independent
  report-only author from live SHA `f58da243...08ca`, redo complete
  `80/80 + 75/75 + 2/2 + 1/1 + 2985/2985` coverage and C1-C6/N24-N27
  source-check, and validate the staged desired/patch only as untrusted draft;
  (3) after scratch preflight, apply the full report repair through exactly one
  successful live-report `apply_patch` using only custom bare hunks, never the
  failed ranged patch, then perform two EOF reads, full mechanics/object/
  triple/citation/stale audit, fresh safe `68`, official `28`, and
  formal-ledger-reader-only hypothetical exact-SHA `29`; (4) freeze a terminal
  `AUTHOR VALIDATED` SHA and run fresh final full specification plus separate
  independent quality reviews in parallel on unchanged bytes, letting both
  finish after every issue; (5) aggregate any further issues once through one
  new author, or, only if the same SHA receives `SPEC COMPLIANT` and `QUALITY
  APPROVED`, update only I03 and require the real post-ledger validator at
  `29/37`, dependency `70`, residual `0`; and (6) continue I04-I11 in frozen
  order through `37/37`.
- At this pause HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`, one worktree, no stash, empty
  tracked/staged diffs, and only the protected 59-byte Phase186 `.meta` at SHA
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`
  in status. No relevant author/reviewer/validator/Python/Unity/ROS/native/build
  process is running. No product, test, proposed RED, frozen authority/manifest,
  ledger, protected artifact, Git ref, or worktree changed.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-16, I03 final dual
  review rejected and one consolidated repair pending):** the progressive
  execution ledger remains `28/37`, sealed exactly through `187-R2-I02`.
  `187-R2-I03` remains unsealed and formally pristine `NOT_RUN`; the ledger is
  unchanged at SHA-256
  `65abc09adc7577fc62b6b1dcc964189a4d8024a014ceb891c052edab1c3885ac`.
  The rejected but byte-frozen I03 report remains SHA-256
  `f58da243d8b7d665535a685af6f912732fa332abe237d0260e87cdcf58ca08ca`
  (105,908 bytes / 468 strict LF lines). Every lower live-checkpoint is
  historical evidence and is superseded by this checkpoint.
- Fresh final specification and separate fresh independent quality reviewers
  both exhausted the same unchanged SHA through EOF, independently covering
  `80/80` ordered primaries / `75/75` unique blobs, `2/2` anchors, `1/1`
  bounded dependency, and `2985/2985` exclusion rows with zero excluded-body
  reads. Both reran safe `68`, official `28/37`, and reader-only hypothetical
  exact-SHA `29/37`; every gate exited `0`, dependency stayed `70`, residual
  stayed `0`, and report/ledger hashes and mtimes stayed unchanged. Their
  terminal verdicts were **`SPEC ISSUES FOUND`** and **`QUALITY ISSUES FOUND`**.
  These verdicts grant no approval, but their complete issue lists are the
  binding input to the next one-author repair.
- The controller reconciled and deduplicated the two final-review lists into
  six existing-report corrections. One fresh independent report-only author
  must apply all six together and may not silently drop one: (C1) finding 005
  must say only three of its first five required strings remain, identify both
  missing first-predicate strings, and separately explain the second-predicate
  fallback failure; (C2) complete the graph/mapping by adding finding 020 to
  Entry A, 013 to Entry B, explicit entry→call→state→output paths for 003 and
  004, and 003 to the hostile case discussion; (C3) repair finding 006 by
  acknowledging the compiled Phase16 raw-string oracle at
  `729f6d428f274b5555f2c1ce075ab6c52a2fb7a6:538-543`, removing the false
  unconditional R2FU no-oracle claim, and authenticating the actual frozen SDK/
  Bridge/R2FU/RemoteGateway identity, version, exact dependency-key, and value
  matrix with removed-key, added-key, and changed-value controls; (C4) extend
  finding 012's same `exists()` path-shape root to non-required notice-inventory
  DLLs at `c8c0b57...:675-704` and sample `.meta` sidecars at
  `c8c0b57...:468-484`; (C5) extend finding 017 beyond other drive letters to
  same-drive user paths, UNC paths, POSIX home paths, and the paired public-
  content scan while preserving repository-relative/allowed controls; and
  (C6) scope finding 020 strictly to the reviewed `validate_unity_package.py`
  gap, explicitly reserving whether the separately invoked out-of-universe
  typesupport-add-on validator named at `80965f2f...:45-50` compensates, and
  narrow consequence/confidence/severity if the authorized evidence requires.
- Add four independent High-confidence findings after source-checking their
  full frozen causal paths: (N24, P1) the matrix lacks the SDK→R2FU forbidden
  asmdef edge because `b628c3f8...:160-179` registers only SDK→Bridge,
  R2FU→Bridge, and Bridge→R2FU, while the other reviewed guards do not inspect
  SDK asmdef references; the RED must use public `validate_boundaries`, a
  complete temporary topology, both name/GUID cases, clean/allowed/existing-
  edge controls, restored globals, deadline, descendant reap, and full cleanup;
  (N25, P2) `_provider_descriptor_ids` at `66348745...:480-488` sees only
  contiguous `PREFIXddd`, so a compile-valid constructed correct-prefix ID can
  escape source/ledger reconciliation while RS2008 is disabled; use real
  validator plus provider build and contiguous/constructed declared/undeclared
  controls; (N26, P1) the six real local UPM bindings at
  `bd240815037f40590c56102672581e7009990d70:14-19` have no reviewed oracle, so
  missing, decoy, out-of-root, stale, or identity-mismatched `file:` targets can
  pass canonical package gates; authenticate resolved target package identity
  through a real public entry path and complete temporary consumer topology;
  and (N27, P1) ordinary SDK sample, Bridge required-asset, scene/script/asmdef,
  and folder `.meta` identity is not authenticated by the existence/file/
  `folderAsset` checks, independently of analyzer meta finding 013 and generated
  meta finding 014; use invalid/duplicate/wrong-importer/valid-unique temporary
  controls without reading frozen excluded `.meta` bodies.
- If all four additions survive the new author's independent source-check and
  C6 remains P2/High, the repaired report has `27` High findings, `P1 x11`,
  `P2 x16`, `P3 x0`, and `27/27` honest `NOT RUN` strategies. The author must
  update all nine sections, graph, exceptional/resource/concurrency, paired,
  hostile/stress, distinctness, reconciliation, blocked boundaries, citations,
  summaries, counts, and stale-token surfaces; it must not merely append four
  findings. If authorized frozen evidence disproves or changes an item, record
  the exact counter-evidence and reconciled count rather than omitting it.
- Next sequence: start one fresh independent report-only author from the
  current rejected SHA; permit one consolidated report repair via `apply_patch`
  and require two post-edit EOF reads, full mechanics/object/triple/citation/
  stale audit, fresh safe `68`, official `28`, and reader-only hypothetical
  exact-SHA `29`, leaving the formal ledger unchanged. Freeze the new terminal
  `AUTHOR VALIDATED` SHA and repeat fresh final full specification plus separate
  independent quality reviews in parallel. Any further issue again waits for
  both complete lists and one consolidated repair. Only same-SHA `SPEC
  COMPLIANT` plus `QUALITY APPROVED` permits I03 seal and real `29/37`.
- At this checkpoint HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`, one worktree, no stash, empty
  tracked/staged diffs, and only the protected 59-byte Phase186 `.meta` at SHA
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`
  in status. No report edit, proposed RED, product/test/frozen/ledger/protected
  artifact change, Git ref change, or new worktree occurred during final review.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-16, I03 consolidated
  author repair validated and final dual review pending):** the progressive
  execution ledger remains `28/37`, sealed exactly through `187-R2-I02`.
  `187-R2-I03` is active but unsealed; its formal row remains pristine
  `NOT_RUN` with empty verdict, report SHA, and notes. The ledger remains
  SHA-256
  `65abc09adc7577fc62b6b1dcc964189a4d8024a014ceb891c052edab1c3885ac`.
  Every lower live-checkpoint paragraph is historical evidence and is
  superseded by this checkpoint.
- The current author-validated but unapproved I03 report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I03.md` at exact
  SHA-256
  `f58da243d8b7d665535a685af6f912732fa332abe237d0260e87cdcf58ca08ca`
  (105,908 bytes / 468 strict UTF-8 LF-only lines; no BOM, CR, or NUL;
  terminal LF). It has 23 consecutive High-confidence findings, `P1 x8`,
  `P2 x15`, `P3 x0`, exactly nine required fields per finding, and 23/23
  literal `The strategy is NOT RUN.` declarations.
- One fresh independent report-only author returned terminal **`AUTHOR
  VALIDATED`** after independently covering `80/80` ordered primaries / `75/75`
  unique blobs, `2/2` anchors, `1/1` bounded dependency, and `2985/2985`
  exclusion rows without reading an excluded body. It source-confirmed all
  `M1-M3`, `R1-R2`, and `N01-N19`, reconciled all nine report sections, and
  used exactly one successful `apply_patch` invocation against the live report
  with no second live-report write. Two fresh post-edit EOF reads, `80/80`
  ordered path/OID/rule triples, `237` authorized citations over `32` OIDs,
  zero unauthorized/out-of-range citations, and zero stale-token residue all
  passed.
- Author gates passed on the current exact SHA: safe `68` (`Ran 68 tests in
  23.668s`, `OK`, exit `0`); official execution validation at `28/37`,
  dependency `70`, residual `0`; and formal-ledger-reader-only in-memory
  hypothetical exact-SHA validation at `29/37`, dependency `70`, residual `0`,
  with one ledger-reader interception and unchanged formal ledger SHA/mtime.
  The controller independently reread the complete 468-line report, repeated
  the mechanics/object/triple/citation/stale audit, tested rollback on another
  copy, repeated safe `68` (`Ran 68 tests in 36.998s`, `OK`, exit `0`),
  official `28/37`, and reader-only hypothetical `29/37`; every gate exited
  `0`, and the live report and ledger hashes remained unchanged.
- Process history does not grant approval: one earlier content-valid author
  withheld its verdict after making two live report patch calls and the
  controller restored the original diagnostic SHA; a later replay's sole live
  patch attempt failed before any write. The successful fresh author then
  started from the exact original diagnostic SHA and performed the single
  successful live report patch described above. Retained ignored transaction
  evidence is historical only.
- Freeze report SHA `f58da243...08ca` now. Start a fresh full specification
  reviewer and a separate fresh independent quality reviewer in parallel on
  these same unchanged bytes. Each must independently read AGENTS/master/child/
  report through EOF, cover all `80/80 + 75/75 + 2/2 + 1/1 + 2985/2985`,
  validate all 23 findings and every report surface, continue after every
  issue, return one exhaustive issue list, and run its own fresh safe `68`,
  official `28`, and ledger-reader-only hypothetical exact-SHA `29`. Do not
  edit the report while either review is running. If any issue exists, let both
  finish, aggregate once, and route one consolidated repair packet to one new
  author; every report edit invalidates all verdicts.
- Only if the same unchanged report SHA receives both terminal **`SPEC
  COMPLIANT`** and separate **`QUALITY APPROVED`** may the controller update
  only I03 to `COMPLETE` / `FINDINGS_REPORTED` with that exact SHA. Then require
  the real post-ledger validator at `29/37`, dependency `70`, residual `0`,
  restoring I03 to pristine `NOT_RUN` on failure. Continue I04-I11 in frozen
  order through `37/37`; I03 is not the terminal endpoint.
- At this checkpoint HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`, one worktree, no stash, empty
  tracked/staged diffs, and only the protected 59-byte Phase186 `.meta` at SHA
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`
  in status. No proposed RED ran; no product, test, frozen authority/manifest,
  ledger, protected artifact, Git ref, or additional worktree changed.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-16, I03 diagnostic
  batch complete and consolidated repair pending):** the progressive ledger
  remains `28/37`, sealed through `187-R2-I02`; `187-R2-I03` remains unsealed
  and formally pristine `NOT_RUN`. The ledger remains SHA-256
  `65abc09adc7577fc62b6b1dcc964189a4d8024a014ceb891c052edab1c3885ac`.
  The frozen unapproved diagnostic I03 report remains exact SHA-256
  `4d11897666b6c3c0f1fc070ed13ca1d733400525e361ce0f90af15ad81fdf453`
  (72,281 bytes / 257 strict LF lines), with four High findings (`P1 x1`,
  `P2 x3`) and four honest `NOT RUN` REDs. Every lower live-checkpoint is
  historical evidence and is superseded by this checkpoint.
- Three fresh independent diagnostic reviewers exhausted the complete same-SHA
  scope in parallel: each read master/child/report through EOF, `80/80`
  ordered primaries / `75/75` unique blobs, `2/2` anchors, `1/1` dependency,
  and `2985/2985` exclusions; each continued after every issue; none edited a
  file or ran a proposed RED. They returned `SPEC DIAGNOSTIC ISSUES FOUND`,
  `QUALITY DIAGNOSTIC ISSUES FOUND`, and `RED-FEASIBILITY DIAGNOSTIC ISSUES
  FOUND`. Their start/end report, ledger, AGENTS, and protected `.meta` hashes
  were identical. No diagnostic verdict is an approval.
- The controller reconciled and deduplicated the full three-way packet. One
  fresh independent report-only author must source-check and apply the entire
  packet in one batch; it must not silently drop an item. Three report-level
  corrections do not change counts: (M1) list the frozen source-universe path
  and SHA rather than claiming unnamed “eight artifacts”; (M2) correct binary
  exclusion semantics to `prior_disposition=BINARY_ARTIFACT` and
  `disposition=EXCLUDED_BINARY_ARTIFACT`, reserving
  `EXCLUDED_WITH_REASON` for the package-lock prior disposition; (M3) remove
  the false claim that every check shares one filesystem snapshot, scope the
  initial package enumeration precisely, and describe later independent
  traversals plus concurrent-mutation limits.
- Repair existing findings without changing their identity: (R1) rewrite
  I03-002's RED to drive real `validate_boundaries` on a complete temporary
  sibling-package topology, restore `ROOT`, make the unrelated GUID resolve to
  a non-target asmdef/meta, and retain target sibling GUID, name-form, and
  co-located controls without locking a private helper; (R2) rewrite I03-004's
  oracle so the Performance default contains zero Bridge Compile items with
  the sibling present or absent, rejects explicit true and an `Exists`-only
  guard, and uses intentional true importers only as separate controls.
- Add these nineteen deduplicated High-confidence candidate findings after
  independently confirming each frozen causal path and keeping every RED
  `NOT RUN`: (N01, P2) the compiled Phase164-1 exact-owner currently asserts
  stale validator source strings and fails the frozen baseline; (N02, P1) the
  package dependency key/value/version matrix leaves Bridge/R2FU SDK pins and
  SDK dependency values unchecked; (N03, P1) multi-artifact and multi-target
  `--update` is non-transactional across second-copy, later-target, and final
  composition failures; (N04, P1) persistent output directories let stale
  analyzer artifacts masquerade as output of the current successful build;
  (N05, P2) package-tool external processes lack effective deadline/reap and
  Phase16 performs blocking pipe reads before its nominal timeout; (N06, P2)
  third-party notice token containment does not authenticate artifact/license
  section attribution; (N07, P1) the closed eight-entry binary inventory never
  discovers a newly bundled DLL; (N08, P2) required release files and sidecars
  checked only with `exists()` can be directories; (N09, P1) analyzer `.meta`
  checks do not authenticate GUID/importer content or GUID uniqueness; (N10,
  P1) GeneratedProto validation lacks `.cs`/`.meta` bijection and global GUID
  uniqueness; (N11, P2) lowercased output-root prefix comparison escapes on a
  case-sensitive host; (N12, P2) package-matrix filename markers permit a
  same-name semantic decoy/vacuous provider surface; (N13, P2) fixed `C:`/`D:`
  patterns miss forbidden absolute paths on other drives independently of
  I03-003's lost IGNORECASE flag; (N14, P2) exact-lowercase `bin`/`obj` scans
  miss tracked `Bin`/`Obj` on the case-sensitive runner; (N15, P2) the console
  adapter lane includes the complete Native tree when adapter=true and
  native=false; (N16, P2) typesupport add-on sibling-conflict metadata is not
  validated, and runtime dependency pairing does not compensate; (N17, P1)
  analyzer ownership parses only direct csproj XML and ignores effective
  imported MSBuild Compile/reference items; (N18, P2) provider explicit-source
  policy rejects `*` but permits a matching `?` wildcard; and (N19, P2) the
  expected-prefix-only descriptor scan misses a provider diagnostic emitted
  under the other provider's namespace while RS2008 is disabled.
- Reconcile severity conflicts in favor of the demonstrated release/provenance
  consequences above: N03, N04, and N10 are `P1`; N05 is one comprehensive
  resource-lifetime finding across its paired call sites. If frozen evidence
  disproves any candidate, the author must document the exact counter-evidence
  in reconciliation rather than omit it. If all nineteen survive, the repaired
  report has `23` High findings, `P1 x8`, `P2 x15`, `P3 x0`, and `23/23` honest
  `NOT RUN` REDs. Update every graph, exceptional/resource/paired/hostile,
  findings, distinctness, blocked/rejected, summary, count, citation, and stale
  surface; do not merely append the Findings section.
- The new author must edit only I03 report via `apply_patch`, then perform two
  complete post-edit EOF reads, the full mechanics/object/triple/citation/
  stale audit, fresh safe `68`, official `28`, and ledger-reader-only
  hypothetical exact-SHA `29`; the formal ledger must remain unchanged. After
  terminal author validation, freeze that exact SHA and commission fresh final
  full specification and separate independent quality reviewers on the same
  bytes. Only their same-SHA `SPEC COMPLIANT` and `QUALITY APPROVED` allow the
  I03 ledger seal and real `29/37` validator. Continue I04-I11 through `37/37`.
- At this checkpoint HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443`, tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`, one worktree, no stash, empty
  tracked/staged diffs, and only the protected 59-byte Phase186 `.meta` at SHA
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`
  in status. No proposed RED ran; no product/test/frozen authority/manifest/
  ledger/protected artifact/ref changed.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-16, I03 diagnostic
  author validated and exact-SHA audit batch pending):** the progressive
  execution ledger remains `28/37`, sealed exactly through `187-R2-I02`.
  `187-R2-I03` is active but unsealed; its formal ledger row remains pristine
  `NOT_RUN` with empty verdict, report SHA, and notes. The formal ledger is
  `Developer/187/round2/local-review/execution-ledger.tsv` at unchanged
  SHA-256
  `65abc09adc7577fc62b6b1dcc964189a4d8024a014ceb891c052edab1c3885ac`
  (9,675 bytes, 38 strict LF-only lines; no CR or NUL; terminal LF). Every
  lower live-checkpoint paragraph is historical evidence and is superseded by
  this checkpoint.
- The current **unapproved diagnostic I03 report** is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I03.md` at exact
  SHA-256
  `4d11897666b6c3c0f1fc070ed13ca1d733400525e361ce0f90af15ad81fdf453`
  (72,281 bytes, 257 strict UTF-8 LF-only lines; no BOM, CR, or NUL; terminal
  LF). It records four High-confidence findings (`P1 x1`, `P2 x3`, `P3 x0`),
  and all four proposed deterministic REDs remain honestly `NOT RUN`. The
  report has nine required sections, consecutive IDs `187-R2-I03-001` through
  `004`, exactly nine required fields per finding, and no placeholder text.
- A fresh independent author returned terminal **`AUTHOR VALIDATED`** after
  covering `80/80` ordered primary paths / `75/75` unique blobs (280,560
  path-counted bytes / 6,238 LF lines; 278,636 unique bytes / 6,164 unique LF
  lines), `2/2` anchors, `1/1` bounded dependency, and `2985/2985` exclusion
  rows. The controller independently reread the report to EOF twice, matched
  all `80/80` ordered path/OID/rule triples, verified all four finding field
  sets and severity/confidence counts, and resolved `200` citation tokens over
  `42` authorized OIDs with zero unauthorized or out-of-range citations.
  Fresh controller gates passed: real safe `68` (`Ran 68 tests in 14.569s`,
  `OK`, exit `0`), official execution validation at `28/37`, dependency `70`,
  residual `0`, and ledger-reader-only in-memory hypothetical exact-SHA
  validation at `29/37`, dependency `70`, residual `0`. The formal ledger hash
  was identical before and after.
- The prior Claude controller stated that it launched the mandatory three-way
  diagnostic audit but exhausted its token allowance before returning any
  reviewer terminal result. No specification, quality, or RED-feasibility
  issue list or verdict from that batch is present in the handoff or on disk,
  and no relevant process remains. Those incomplete commissions grant no
  reusable coverage or verdict. Keep the exact report SHA frozen and run a
  fresh parallel diagnostic specification, quality, and RED-feasibility batch
  on these same unchanged bytes; every reviewer must exhaust its complete
  authorized scope through EOF and continue after every issue. Do not edit the
  report until all three fresh reviewers return.
- After the three diagnostic reviewers finish, aggregate and deduplicate their
  complete issue lists once. If any issue exists, give one consolidated
  report-only repair packet to one fresh independent author, require one batch
  repair via `apply_patch`, and rerun the complete author EOF/audit/safe-68/
  official-28/hypothetical-29 chain. Then freeze one unchanged repaired SHA and
  run fresh final full specification and separate independent quality reviews,
  including their required gates. If either final reviewer finds issues, let
  every commissioned reviewer finish, aggregate once, and repeat one repair
  cycle rather than cycling per issue. Only when the same unchanged SHA has
  both `SPEC COMPLIANT` and `QUALITY APPROVED` may I03 alone be updated to
  `COMPLETE` / `FINDINGS_REPORTED`; require the real post-ledger validator at
  `29/37`, dependency `70`, residual `0`, restoring I03 to pristine `NOT_RUN`
  on any failure. Continue I04-I11 in frozen order through `37/37`.
- I03 frozen authority remains master SHA
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`,
  child SHA
  `a7cddab7036057777206eca02d8c7bf38817bbf270548e0b97e535c4ab037673`,
  primary/dependency/exclusion/residual/source-universe SHA values
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`,
  and `48f47e1a053ceafaef97c917a4a21f0475784bdf49c195abf6e650a64dbede2a`.
- At this checkpoint HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443` with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; there is one worktree, no stash,
  tracked and staged diffs are empty, and the sole Git-status path remains the
  protected untracked 59-byte Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  No proposed RED ran, and no product, test, frozen authority/manifest, formal
  ledger, protected `.meta`, branch, commit, worktree, or remote ref changed.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-16, explicit
  user-requested pause before I03 report authoring):** the progressive
  execution ledger is `28/37`, sealed exactly through `187-R2-I02`.
  `187-R2-I03` is the next closure and remains pristine `NOT_RUN` with empty
  verdict, report SHA, and notes. The formal ledger remains
  `Developer/187/round2/local-review/execution-ledger.tsv` at SHA-256
  `65abc09adc7577fc62b6b1dcc964189a4d8024a014ceb891c052edab1c3885ac`
  (9,675 bytes, 38 strict LF-only lines; no CR or NUL; terminal LF). The user
  explicitly requested that this window stop after refreshing this checkpoint
  and preparing a Claude continuation prompt. Every lower live-checkpoint
  paragraph is historical evidence and is superseded by this checkpoint.
- The sealed I02 report remains
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I02.md` at exact
  SHA-256
  `74c13706b234f29dae1156b3d8cc237e2aa2592d40a0339429f41a6f381a65d5`
  (99,308 bytes, 406 strict UTF-8 LF-only lines). Its unchanged bytes received
  terminal `AUTHOR VALIDATED`, fresh full `SPEC COMPLIANT`, and separate fresh
  independent `QUALITY APPROVED`; the real post-ledger validator passed at
  `28/37`, dependency `70`, residual `0`. I02 is complete and must not be
  repeated or reopened while starting I03.
- A fresh I03 diagnostic author was started and then explicitly interrupted at
  the user's pause request before any report write. It independently rehashed
  the frozen authority, passed the official baseline validator at `28/37`, and
  reported full read coverage of `80/80` ordered paths / `75/75` unique blobs
  (280,560 bytes / 6,238 LF), `2/2` anchors, `1/1` bounded dependency, and
  `2985/2985` exclusion accounting. It had begun in-memory graph/finding
  reconciliation but created no I03 report, no transaction artifact, and no
  reusable author verdict or approval. This interrupted reading is progress
  evidence only: the next author must independently restart I03 coverage and
  author validation from zero rather than treating it as a completed gate.
- `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I03.md` is absent.
  No proposed RED ran. No product, test, frozen authority/manifest, ledger,
  protected `.meta`, tracked/staged state, branch, commit, worktree, remote ref,
  or I02 report changed during the interrupted I03 attempt. No I03 author,
  reviewer, relevant validator, Python, Unity, ROS, native, or build process is
  running at this pause.
- I03 authority remains the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I03_UPM_ASMDEF_PACKAGE_PROVENANCE_REVIEW_PLAN.md`
  at SHA-256
  `a7cddab7036057777206eca02d8c7bf38817bbf270548e0b97e535c4ab037673`.
  Its exact dossier is `80/80` ordered primary paths / `75/75` unique blobs
  (280,560 bytes / 6,238 LF), `2/2` anchors, `1/1` bounded I05 dependency, and
  `2985/2985` excluded-body rows. Frozen primary/dependency/exclusion/residual/
  source-universe identities remain `e2d559a3...e453`, `ceb8c6bf...c2d`,
  `3e89ba11...76c`, `cf79de26...887`, and `48f47e1a...e2a` respectively.
- Exact continuation order for the next Claude window: (1) read this file to
  EOF and independently rehash AGENTS, ledger, sealed I02 report, master/I03
  child/manifests, refs/tree, I02/I03 rows, protected `.meta`, process state,
  and the single-worktree boundary; (2) start one fresh I03 report-only author
  from zero over `80/80 + 2/2 + 1/1 + 2985/2985`, create only the I03 report
  via `apply_patch`, leave every proposed RED honestly `NOT RUN`, and complete
  the full author EOF/audit/safe-68/official-28/hypothetical-29 chain; (3)
  freeze that diagnostic SHA and run fresh exhaustive specification, quality,
  and adversarial/RED-feasibility reviewers in parallel on the same unchanged
  bytes, with every reviewer continuing through EOF after every issue; (4)
  aggregate all issues once, route one consolidated report-only repair packet
  to one new author, and repeat the full author chain; (5) on one unchanged
  repaired SHA run fresh final full specification and separate independent
  quality reviews, batch any further issues rather than cycling per issue; (6)
  only after the same SHA has `SPEC COMPLIANT` and `QUALITY APPROVED`, update
  only I03 and require the real post-ledger validator at `29/37`, dependency
  `70`, residual `0`, restoring I03 to pristine `NOT_RUN` on failure; and (7)
  continue I04-I11 in frozen order through `37/37`.
- At this pause HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443` with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; there is one worktree, no stash,
  tracked and staged diffs are empty, and the sole Git-status path remains the
  protected untracked 59-byte Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-16, successful I02
  seal and immediate I03 continuation):** the progressive execution ledger is
  now `28/37`, sealed exactly through `187-R2-I02`. `187-R2-I03` is the next
  untouched closure; I03-I11 remain pristine `NOT_RUN` with empty verdict,
  report SHA, and notes. The formal ledger is
  `Developer/187/round2/local-review/execution-ledger.tsv` at SHA-256
  `65abc09adc7577fc62b6b1dcc964189a4d8024a014ceb891c052edab1c3885ac`
  (9,675 bytes, 38 strict LF-only lines; no CR or NUL; terminal LF). Every
  lower live-checkpoint paragraph is historical evidence and is superseded by
  this checkpoint.
- The sealed I02 report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I02.md` at exact
  SHA-256
  `74c13706b234f29dae1156b3d8cc237e2aa2592d40a0339429f41a6f381a65d5`
  (99,308 bytes, 406 strict UTF-8 LF-only lines; no BOM, CR, or NUL; terminal
  LF). It contains 19 High-confidence findings (`P1 x12`, `P2 x4`, `P3 x3`),
  and all 19 proposed deterministic REDs remain honestly `NOT RUN`.
- One unchanged I02 report SHA received terminal **`AUTHOR VALIDATED`**, fresh
  full **`SPEC COMPLIANT`**, and separate fresh independent **`QUALITY
  APPROVED`** verdicts. Author, controller, specification, and quality coverage
  each used the complete `39/39` ordered primaries / `39/39` unique blobs
  (209,710 bytes / 6,416 LF lines), `2/2` anchors, `3/3` complete named
  dependencies, and `0/0` exclusions. Required report EOF rereads,
  mechanics/object/citation/stale audits, real Phase181 interface-tooling safe
  `68`, official pre-ledger `27`, and ledger-reader-only hypothetical exact-SHA
  `28` all passed without changing the candidate or formal ledger. Both final
  reviewers exhausted the entire authorized scope on the same bytes and
  returned empty issue lists. The real post-ledger validator then passed at
  `28/37`, dependency `70`, residual `0`.
- I02's sealed report incorporates the final exact-SHA reconciliation: I02-003
  scopes relative executable/current-directory semantics separately for POSIX
  and Windows; I02-017 is `P2`/High for the unsupported cleanup path without
  the rejected project/Library-lock claim; and I02-019 is `P1`/High for stale
  numeric process-group ownership only after the original group lifetime has
  ended and the number becomes reusable. These corrections and every other
  finding were independently approved. No proposed RED was run.
- I02 authority remains the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I02_EDITOR_MONO_IL2CPP_PLAYER_PARITY_REVIEW_PLAN.md`
  at SHA-256
  `1f44d22236a8454b0a06359663c05e43cf05d1e466c9700b2ae7e932ce925117`.
  Frozen manifest identities remain primary
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  dependency
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  exclusion
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  residual
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`,
  and source universe
  `48f47e1a053ceafaef97c917a4a21f0475784bdf49c195abf6e650a64dbede2a`.
- The next closure is `187-R2-I03`. Its untouched child authority is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I03_UPM_ASMDEF_PACKAGE_PROVENANCE_REVIEW_PLAN.md`
  at SHA-256
  `a7cddab7036057777206eca02d8c7bf38817bbf270548e0b97e535c4ab037673`
  (21,282 bytes, 249 CRLF physical lines). Its frozen dossier is `80/80`
  ordered primary paths / `75/75` unique blobs (280,560 bytes / 6,238 LF
  lines), `2/2` anchors (`Scripts/package/validate_unity_package.py :: def
  main` and `Packages/dev.unity2foxglove.sdk/package.json ::
  dev.unity2foxglove.sdk`), `1/1` bounded dependency (`187-R2-I05` package
  structure in `.github/workflows/package-check.yml`), and `2985/2985`
  excluded-body rows. The expected report
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I03.md` does not
  exist.
- Exact continuation order: (1) independently rehash this file, ledger, sealed
  I02 report, frozen master/I03 child/manifests, refs/tree, I02/I03 rows,
  protected `.meta`, process state, and the single-worktree boundary; (2) start
  I03 from zero with one fresh independent report-only author over all
  `80/80 + 2/2 + 1/1 + 2985/2985`, preserving frozen-index order and leaving
  every proposed RED `NOT RUN`; (3) freeze one diagnostic report SHA and run
  the mandatory parallel exhaustive specification, quality, and
  adversarial/RED-feasibility diagnostic batch on those unchanged bytes;
  aggregate all issues once and send one consolidated repair packet to one new
  author; (4) require the repaired exact SHA to complete the full author chain,
  then fresh final specification and separate independent quality review on
  the same unchanged bytes; (5) only after `SPEC COMPLIANT` and `QUALITY
  APPROVED`, update only I03 and require the real post-ledger validator at
  `29/37`, dependency `70`, residual `0`, restoring I03 to pristine `NOT_RUN`
  on failure; and (6) continue I04-I11 in frozen order through `37/37`. No
  individual I closure is the terminal endpoint.
- At this checkpoint HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443` with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; there is one worktree, no stash,
  tracked and staged diffs are empty, and the sole Git-status path remains the
  protected untracked 59-byte Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  No author/reviewer or relevant Python, Unity, ROS, validator, native, or build
  process is running. No product code, test, proposed RED, frozen authority or
  manifest, tracked/staged state, protected `.meta`, branch, commit, worktree,
  or remote ref was modified. Only the ignored I02 report, formal ignored
  ledger, this ignored bootstrap, and ignored `build/` transaction evidence
  changed.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-16, user-requested
  recovery checkpoint after the consolidated I02 author repair):** the
  progressive execution ledger remains `27/37`, sealed exactly through
  `187-R2-I01`. `187-R2-I02` is active but unsealed; I02-I11 remain formally
  pristine `NOT_RUN` with empty verdict, report SHA, and notes. The formal
  ledger remains
  `Developer/187/round2/local-review/execution-ledger.tsv` at SHA-256
  `57e87260dd7f48c872ce58eb0c587341f576ee73a672a6dc5074977765563f7a`
  (9,406 bytes, 38 strict LF-only lines; no CR or NUL; terminal LF). Every
  lower live-checkpoint paragraph is historical evidence and is superseded by
  this checkpoint.
- The current **unapproved author-validated I02 candidate** is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I02.md` at exact
  SHA-256
  `74c13706b234f29dae1156b3d8cc237e2aa2592d40a0339429f41a6f381a65d5`
  (99,308 bytes, 406 strict UTF-8 LF-only lines; no BOM, CR, or NUL; terminal
  LF). It records 19 findings (`P1 x12`, `P2 x4`, `P3 x3`), all High
  confidence, and all 19 proposed deterministic REDs remain honestly `NOT
  RUN`. This SHA has **no final specification or quality verdict** and must not
  enter the ledger yet.
- One fresh independent report-only author applied exactly one consolidated
  `apply_patch` to superseded SHA `7e92aead...a65d5`, then returned **`AUTHOR
  VALIDATED`** on the exact current bytes. Validation covered `39/39` ordered
  primaries / `39/39` unique blobs (209,710 bytes / 6,416 LF lines), `2/2`
  anchors, `3/3` complete named dependencies, and `0/0` exclusions; two full
  post-edit EOF rereads; complete mechanical, object, citation, and stale-text
  audit; the real Phase181 interface-tooling safe gate (`Ran 68 tests`, `OK`,
  exit `0`); official pre-ledger execution validation at `27/37`, dependency
  `70`, residual `0`; and ledger-reader-only pure-memory hypothetical exact-SHA
  validation at `28/37`, dependency `70`, residual `0`. The controller freshly
  repeated the report mechanics/OID/range audit, safe `68`, official `27`, and
  hypothetical `28`; every command exited `0`, and the formal ledger SHA was
  identical before and after.
- The consolidated repair resolved the complete three-item packet from the
  prior full specification and quality reviews: (1) I02-003 now scopes
  relative executable/current-directory resolution precisely across POSIX and
  Windows while retaining its proved type/identity/error-boundary body; (2)
  I02-017 removes the unsupported project/Library-lock claim and is now
  `P2`/High for the unassigned suspended-child ownership leak; and (3) new
  I02-019 is `P1`/High for reuse of the stale numeric POSIX ownership token only
  after the original process-group lifetime has ended, with the still-live
  original-group case explicitly excluded. All graph, exceptional,
  concurrency, paired, hostile/stress, count, NOT-RUN, and reconciliation
  surfaces were updated.
- Review history is evidence only and grants no reusable approval. Exact SHA
  `7e92aead7dd605704d9485287dff63532e813fcee59be1dda0fb0ebe006ffb6b`
  completed both full final reviews but received **`SPEC ISSUES FOUND`** and
  **`QUALITY ISSUES FOUND`**. Their exhaustive results were reconciled into the
  single repair above. The report edit invalidated all verdicts on that old
  SHA. The still older `8dda04d3...0b2e` and every earlier I02 candidate remain
  superseded; no proposed RED ran in any cycle.
- I02 authority remains the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I02_EDITOR_MONO_IL2CPP_PLAYER_PARITY_REVIEW_PLAN.md`
  at SHA-256
  `1f44d22236a8454b0a06359663c05e43cf05d1e466c9700b2ae7e932ce925117`.
  Frozen identities remain primary
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  dependency
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  exclusion
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  residual
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`,
  and source universe
  `48f47e1a053ceafaef97c917a4a21f0475784bdf49c195abf6e650a64dbede2a`.
- Exact continuation order: (1) independently rehash this checkpoint, the
  current report, ledger, frozen authority/manifests, refs/tree, I01/I02 rows,
  protected `.meta`, process state, and single-worktree boundary; (2) freeze
  exact report SHA `74c13706...a65d5` and commission a fresh full
  specification reviewer and a separate fresh independent quality reviewer in
  parallel on those unchanged bytes; every reviewer must finish the complete
  authorized scope through EOF even after finding an issue; (3) allow both
  reviewers to finish, aggregate every issue once, and route any issue only to
  one new report-only author, then repeat the complete author and dual-review
  chain on the new SHA; (4) only after one unchanged SHA has both `SPEC
  COMPLIANT` and `QUALITY APPROVED`, update only I02 to `COMPLETE` /
  `FINDINGS_REPORTED` with that exact SHA and run the real post-ledger validator
  at `28/37`, dependency `70`, residual `0`, restoring I02 to pristine
  `NOT_RUN` on any failure; and (5) continue I03-I11 in frozen order through
  `37/37`. No individual I closure is the terminal endpoint.
- At this checkpoint HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443` with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; there is one worktree, no stash,
  tracked and staged diffs are empty, and the sole Git-status path remains the
  protected untracked 59-byte Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  No author/reviewer, relevant Python, Unity, ROS, validator, native, or build
  process is running. No product code, test, proposed RED, frozen authority or
  manifest, formal ledger, tracked/staged state, protected `.meta`, branch,
  commit, worktree, or remote ref was modified. Only the ignored I02 report,
  this ignored bootstrap, and ignored `build/` transaction evidence changed.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-16, requested
  window switch after the third I02 exact-SHA final-quality rejection):** the
  progressive execution ledger remains `27/37`, sealed exactly through
  `187-R2-I01`. `187-R2-I02` is active but unsealed; I02-I11 remain pristine
  `NOT_RUN` with empty verdict, report SHA, and notes. The formal ledger is
  `Developer/187/round2/local-review/execution-ledger.tsv` at SHA-256
  `57e87260dd7f48c872ce58eb0c587341f576ee73a672a6dc5074977765563f7a`
  (9,406 bytes, 38 strict LF-only lines; no CR or NUL; terminal LF). Every
  lower live-checkpoint paragraph is historical evidence and is superseded by
  this checkpoint.
- The current **unapproved** I02 report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I02.md` at exact
  SHA-256
  `8dda04d3d5dd13261ba52e642c6c4cba484161882dedd140506ed14f2d3c0b2e`
  (76,052 bytes, 370 strict UTF-8 LF-only lines; no BOM, CR, or NUL; terminal
  LF). It records 16 High-confidence findings (`P1 x10`, `P2 x3`, `P3 x3`),
  and all 16 proposed deterministic REDs remain honestly `NOT RUN`. A fresh
  independent author fully validated this exact SHA over `39/39` primaries,
  `2/2` anchors, `3/3` complete named dependencies, and `0/0` exclusions,
  including two EOF rereads, the full audit, safe `68`, official current
  `27`, and ledger-reader-only hypothetical `28`. A fresh full final
  specification reviewer then returned **`SPEC COMPLIANT`** on the same
  unchanged SHA. A separate fresh independent final quality reviewer
  exhausted the same complete scope and returned **`QUALITY ISSUES FOUND`**.
  Therefore this SHA is not approved and must not enter the ledger. Any report
  edit invalidates its specification verdict.
- The final-quality review returned exactly three issues after completing the
  whole authorized scope:
  1. Add a distinct High-confidence P1 Windows partial-acquisition finding.
     At frozen blob
     `e70f26f6136ecc5bdd5c96e0b0c99503730efca3:678-700`, Windows creates the
     suspended child before `job.assign`, but cleanup catches only
     `Exception`. `KeyboardInterrupt` or another `BaseException` after
     `Popen` returns and before assignment bypasses `process.kill()` and
     `job.close()`; caller ownership/finally does not begin until lines
     727-778. The unassigned suspended child can survive controller exit.
     Current report line 117's claim that every pre-return acquisition failure
     kills the primary and closes the Job must also be corrected.
  2. Add a distinct High-confidence P1 POSIX process-tree escape finding.
     Frozen blob
     `e70f26f6136ecc5bdd5c96e0b0c99503730efca3:575-611,636-675,702-703`
     owns and enumerates only one numeric process group. A non-group-leader
     descendant can call `setsid()` or change process group before root
     completion/timeout; `killpg` and the `ps` PGID filter then neither kill
     nor report it. This is distinct from I02-009 same-group convergence and
     I02-001/I02-015 controller-cleanup gaps. The RED must use an authenticated
     lower process-ownership seam and exact escape/live markers so executable
     validation cannot create a false green.
  3. Correct I02-008's false FIFO trigger and RED branch. With the writer
     already open, `handle.seek(0)` at frozen blob
     `e70f26f6136ecc5bdd5c96e0b0c99503730efca3:354-365` raises an illegal-seek
     / `UnsupportedOperation` caught by `except OSError` before `readlines()`;
     writer-open/no-newline FIFO therefore does not prove the claimed read
     block. Retain the real no-writer FIFO `open()` block, a genuinely
     seekable blocking device/reader seam, and the large regular-file memory
     branch; repair trigger, strategy, controls, graph, and reconciliation.
- I02 review history is evidence only and grants no reusable approval. The
  initial diagnostic SHA `aa6f795a...4721` had eight findings and completed
  the mandatory parallel diagnostic specification/quality/adversarial batch.
  Consolidated repair SHA `15ad3f12...0a8a` had ten findings and passed final
  specification, but final quality found two omissions. Repair SHA
  `0a00b130...6570` had twelve findings, but its complete final specification
  and quality reviews found the next consolidated six issues. Current SHA
  `8dda04d3...0b2e` has sixteen findings and the exact current verdicts above.
  No proposed RED ran in any cycle.
- I02 authority remains the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I02_EDITOR_MONO_IL2CPP_PLAYER_PARITY_REVIEW_PLAN.md`
  at SHA-256
  `1f44d22236a8454b0a06359663c05e43cf05d1e466c9700b2ae7e932ce925117`.
  Its frozen dossier remains `39/39` ordered primary paths / `39/39` unique
  blobs (209,710 bytes / 6,416 LF lines), `2/2` anchors, `3/3` complete named
  dependencies, and `0/0` exclusions. Frozen identities remain primary
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  dependency
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  exclusion
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  residual
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`,
  and source universe
  `48f47e1a053ceafaef97c917a4a21f0475784bdf49c195abf6e650a64dbede2a`.
- Exact continuation order for the next window: (1) read this file through
  EOF and independently rehash AGENTS, ledger, current I02 report, frozen
  master/child/manifests, refs/tree, I01/I02 rows, protected `.meta`, process
  state, and the single-worktree boundary; (2) give the three-item complete
  quality packet above to one fresh independent report-only author, who must
  repair I02 once via `apply_patch`, preserve all current findings, add the two
  distinct P1 findings, correct I02-008, update every graph/branch/count/
  reconciliation surface, and complete full author validation from zero; if
  no additional issue is found, the expected report is 18 High-confidence
  findings (`P1 x12`, `P2 x3`, `P3 x3`), all REDs `NOT RUN`; (3) require that
  new frozen SHA to pass two post-edit EOF rereads, full mechanics/object/
  citation/stale audit, fresh safe `68`, official `27`, and ledger-reader-only
  hypothetical `28`; (4) on that same unchanged SHA commission fresh full
  specification and separate independent quality reviewers, let both finish
  their entire scope even after any issue, and batch any further issues through
  one new author; (5) only after one unchanged SHA has both `SPEC COMPLIANT`
  and `QUALITY APPROVED`, update only I02 to `COMPLETE` /
  `FINDINGS_REPORTED` with its exact SHA and run the real post-ledger validator
  at `28/37`, dependency `70`, residual `0`, restoring I02 to pristine
  `NOT_RUN` on any failure; and (6) continue I03-I11 in frozen order through
  `37/37`. No individual I closure is the terminal endpoint.
- At this handoff HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443` with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; there is one worktree, no stash,
  tracked and staged diffs are empty, and the sole Git-status path is the
  protected untracked 59-byte Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  No author/reviewer subagent or relevant Python, Unity, ROS, validator,
  native, or build process is running. During I02 only the ignored I02 report
  and this ignored bootstrap were edited. No product code, test, proposed RED,
  frozen authority/evidence/manifest, formal ledger, tracked/staged state,
  protected `.meta`, worktree, branch, commit, or remote ref was modified.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-15, successful I01
  seal followed by the user-requested window switch):** the progressive
  execution ledger is now `27/37`, sealed exactly through `187-R2-I01`.
  `187-R2-I02` is the next untouched closure; I02-I11 remain pristine
  `NOT_RUN` with empty verdict, report SHA, and notes. The formal ledger is
  `Developer/187/round2/local-review/execution-ledger.tsv` at SHA-256
  `57e87260dd7f48c872ce58eb0c587341f576ee73a672a6dc5074977765563f7a`
  (9,406 bytes, 38 strict LF-only lines; no CR or NUL; terminal LF). All lower
  live-checkpoint paragraphs are historical evidence and are superseded by
  this checkpoint.
- The sealed I01 report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I01.md` at exact
  SHA-256
  `8c5d0240b8890698bbf2ee82ffba8fbfd874fd77ba979fe37ed215e417018e6d`
  (153,270 bytes, 554 strict UTF-8 LF-only lines; no BOM, CR, or NUL; terminal
  LF). It records 19 High-confidence findings (`P1 x6`, `P2 x9`, `P3 x4`),
  and all 19 proposed deterministic REDs remain honestly `NOT RUN`.
- One unchanged I01 report SHA received terminal **`AUTHOR VALIDATED`**, fresh
  full **`SPEC COMPLIANT`**, and separate fresh independent **`QUALITY
  APPROVED`** verdicts. Author validation covered `25/25` ordered primaries,
  `2/2` anchors, the `1/1` bounded `class FoxgloveManager` dependency at lines
  35-555, `0/0` exclusions, two complete EOF rereads, the full mechanical,
  object, citation, and stale-text audit, safe `68`, official pre-ledger `26`,
  and ledger-reader-only hypothetical exact-SHA `27`. Both final reviewers
  exhausted their complete authorized scope on the same bytes. The real
  post-ledger validator then passed at `27/37`, dependency `70`, residual `0`.
- I01 authority remains the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I01_MANAGER_INSPECTOR_SERIALIZATION_REVIEW_PLAN.md`
  at SHA-256
  `1fe17e220253db45eaa05e69ddadb33f5c506af60f623d60d8b92976f6907ae4`.
  Its final frozen scope was `25/25` paths and unique blobs (283,864 bytes /
  6,353 lines), `2/2` anchors, `1/1` bounded dependency, and `0/0`
  exclusions. The mandatory exact-SHA review-batching rule above was applied
  to the final I01 cycle and is binding for I02 onward.
- I02 authority is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I02_EDITOR_MONO_IL2CPP_PLAYER_PARITY_REVIEW_PLAN.md`
  at SHA-256
  `1f44d22236a8454b0a06359663c05e43cf05d1e466c9700b2ae7e932ce925117`
  (13,278 bytes, 207 CRLF physical lines). Its frozen dossier is `39/39`
  ordered primary paths / `39/39` unique blobs (209,710 bytes / 6,416 LF
  lines), `2/2` anchors (`Scripts/unity_build/unity_il2cpp.py :: def main` and
  `Packages/dev.unity2foxglove.sdk/Runtime/link.xml :: Google.Protobuf`),
  `3/3` bounded dependencies (`FoxgloveLogSourceGenerator` and the R2FU and
  Bridge `FoxRunProviderAnalyzer` classes), and `0/0` exclusions. The expected
  report path is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I02.md`; it does not
  exist. Frozen manifest identities remain primary
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  dependency
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  exclusion
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  residual
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`,
  and source universe
  `48f47e1a053ceafaef97c917a4a21f0475784bdf49c195abf6e650a64dbede2a`.
  Do not start I02 in this window.
- Exact continuation order for the next window: (1) read this file through EOF
  and independently rehash AGENTS, the formal ledger, frozen master/I02 child/
  manifests, refs/tree, I01/I02 rows, sealed I01 report, protected `.meta`,
  process state, and the single-worktree boundary; (2) start I02 from zero and
  enforce the mandatory batching rule above: freeze one diagnostic SHA, let
  every commissioned independent reviewer exhaust its complete scope, combine
  all issues once, and send one consolidated report-only repair packet to one
  fresh independent author; (3) require the repaired exact SHA to pass full
  author validation, then fresh full specification and separate independent
  quality reviews on the same unchanged bytes, including all required fresh
  gates; (4) only after `SPEC COMPLIANT` and `QUALITY APPROVED`, update only
  the I02 ledger row and require the real post-ledger validator at `28/37`,
  dependency `70`, residual `0`, restoring I02 to pristine `NOT_RUN` on any
  failure; and (5) continue I03-I11 in frozen order through `37/37`. No
  individual I closure is the terminal task endpoint.
- At this handoff HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443` with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; there is one worktree, tracked
  and staged diffs are empty, and the only status path is the protected
  untracked 59-byte Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  No relevant author/reviewer, validator, Python, Unity, ROS, native, or build
  process is running. No product code, test, proposed RED, frozen authority or
  manifest, tracked/staged state, protected `.meta`, or additional worktree was
  modified. Do not run proposed REDs, edit protected/frozen artifacts, create
  a Phase187 worktree, or push `main`.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-15, user-requested
  window switch after the exact-SHA I01 quality rejection):** the progressive
  execution ledger remains at `26/37`, sealed exactly through `187-R2-H08`.
  `187-R2-I01` is active but unsealed, and I01/I02 remain pristine `NOT_RUN`
  with empty verdict, report SHA, and notes. The formal ledger is
  `Developer/187/round2/local-review/execution-ledger.tsv` at SHA-256
  `644d3cb4f767bf08b02cd068f779b6ce88c6452e9bb9427a57bced0d64164fec`
  (9,138 bytes, 38 strict LF-only lines). All lower live-checkpoint paragraphs
  are historical evidence and are superseded by this checkpoint.
- The current **unapproved** I01 report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I01.md` at exact
  SHA-256
  `b997182217f22b1497d8e9ef3829979ea4a7844878133d6f3ac6dbabe0266412`
  (66,189 bytes, 280 strict UTF-8 LF-only lines; no BOM, CR, or NUL; terminal
  LF). It records seven High-confidence findings (`P1 x3`, `P2 x4`, `P3 x0`),
  and all seven proposed deterministic REDs remain honestly `NOT RUN`. A fresh
  independent author fully validated this exact SHA, including two EOF reads,
  the full audit, and fresh safe `68`, official `26`, and ledger-reader-only
  hypothetical `27`. A brand-new specification reviewer then returned **`SPEC
  COMPLIANT`** on the same unchanged SHA. A separate brand-new quality reviewer
  returned **`QUALITY ISSUES FOUND`**, so this SHA is not approved and must not
  enter the ledger. Any report edit invalidates the specification verdict.
- The blocking quality issue is one omitted distinct High-confidence P1
  finding, to be added as I01-008. Certificate generation can set path A,
  enable distribution, and start the static distributor with A; the Inspector
  can then select path B and display/copy B's fingerprint without rebinding the
  already-running distributor. The UI can therefore advertise/import A while
  asserting fingerprint B. Source evidence is frozen blob
  `3e8231d73bd7426bee8bf52f69e998360c456910:451-467,528-542,586-600`
  and blob
  `5670fa494b91c9ced3ce677d6d41a623b10d836a:44-65,68-90`. This is distinct
  from I01-006 because a path-string change correctly refreshes I01-006's
  fingerprint cache while the static distributor path remains stale.
- Minimum report-only repair: add I01-008; update the graph, exceptional/
  hostile/paired branches, counts, findings, summary, and reconciliation; and
  add a deterministic child-process RED that starts distribution with A,
  changes the real Inspector path to B, then requires served certificate bytes,
  displayed fingerprint, and copied fingerprint all to identify B. Include
  unchanged-A and play-mode-restart controls, a host-owned hard deadline and
  whole-process-tree teardown, distributor disposal, complete clipboard and
  Inspector/Selection/SessionState/Undo/dirty-state restoration, and disposable
  project/temp-root cleanup. Preserve implementation latitude and `NOT RUN`.
- The report-review chain explains the extended I01 loop and must remain
  historical, not reusable approval: `c1eb1184...e8a76` passed specification
  but quality found a body-count error and unsafe I01-001/002 cleanup;
  `f36e6db3...7a04` repaired those but specification found omitted asset-root
  Browse coverage in I01-007; `b852d794...e848ed` repaired that but
  specification found missing hard process boundaries in I01-003/I01-006; and
  current `b9971822...6412` repaired those, passed author and specification,
  but quality found omitted I01-008. Each changed SHA invalidated earlier
  review verdicts; no proposed RED was run.
- I01 authority remains the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  (11,877 bytes) and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I01_MANAGER_INSPECTOR_SERIALIZATION_REVIEW_PLAN.md`
  at SHA-256
  `1fe17e220253db45eaa05e69ddadb33f5c506af60f623d60d8b92976f6907ae4`
  (11,412 bytes, 191 physical lines). Its exact frozen scope is `25/25`
  ordered primary paths / `25/25` unique blobs (283,864 bytes / 6,353 lines),
  `2/2` anchors, `1/1` bounded `class FoxgloveManager` dependency, and `0/0`
  exclusions. Frozen manifest identities remain primary
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  dependency
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  exclusion
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  and residual
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`.
- Exact continuation order for the next window: (1) read this file through EOF
  and independently rehash AGENTS, ledger, current I01 report, frozen master/
  child/manifests, refs/tree, I01/I02 rows, protected `.meta`, relevant
  processes, and the single-worktree boundary; (2) use a fresh `gpt-5.6-sol` /
  `max` independent author to apply the I01-008 report-only repair and validate
  the resulting eight-finding exact SHA from zero over `25/25 + 2/2 + 1/1 +
  0/0`, two complete post-edit EOF rereads, the full sections/IDs/fields/counts/
  NOT-RUN/tokens/triples/objects/citations/stale-text audit, and fresh safe
  `68`, official `26`, and ledger-reader-only hypothetical `27`; (3) only after
  terminal author validation, run a brand-new full specification review and
  then a separate brand-new independent quality review on the same unchanged
  SHA; (4) route any issue only through an independent report author and repeat
  author, specification, and quality gates; (5) only when one unchanged SHA has
  both `SPEC COMPLIANT` and `QUALITY APPROVED`, update only the I01 ledger row
  and require the real post-ledger validator at `27/37`, dependency `70`,
  residual `0`, restoring I01 to pristine `NOT_RUN` on any failure; and (6)
  continue I02-I11 in frozen order through `37/37`.
- At this handoff no author/reviewer subagent, validator, relevant Python,
  Unity, ROS, native, or build process is running. HEAD/local `main`/
  `origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443` with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; there is one worktree, tracked
  and staged diffs are empty, and the only status path is the protected
  untracked 59-byte Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  During I01 only the ignored I01 report and this ignored bootstrap were edited.
  No product code, test, proposed RED, frozen authority/evidence/manifest,
  formal ledger, tracked/staged state, provisional-remediation tree, DeepWiki
  tree, or protected `.meta` was modified. The terminal goal remains all
  `37/37`, paused here only for the requested window switch.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-15, user-requested
  window switch during post-repair I01 author verification):** the progressive
  execution ledger remains at `26/37`, sealed exactly through `187-R2-H08`.
  `187-R2-I01` is active but unsealed, and I01/I02 remain pristine `NOT_RUN`
  with empty verdict, report SHA, and notes. The formal ledger is
  `Developer/187/round2/local-review/execution-ledger.tsv` at SHA-256
  `644d3cb4f767bf08b02cd068f779b6ce88c6452e9bb9427a57bced0d64164fec`
  (9,138 bytes, 38 strict LF-only lines). All lower paragraphs describing H08
  as the live checkpoint, I01 as untouched, or an earlier H04/H02 state are
  historical evidence and are superseded by this checkpoint.
- The current **unapproved** I01 author candidate is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I01.md` at exact
  SHA-256
  `ae996307208eb6e08b5d0f82e4762938def3a9b2c17c079c0b58c9114c51e259`
  (56,888 bytes, 280 strict UTF-8 LF-only lines; no BOM, CR, or NUL; terminal
  LF). It records `FINDINGS_REPORTED` with seven High-confidence findings
  (`P1 x3`, `P2 x4`, `P3 x0`), and all seven proposed deterministic REDs
  remain honestly `NOT RUN`. The second report-only author patch was complete,
  but the user requested this window switch immediately after the author said
  it was starting the two post-patch EOF rereads, full audit, and fresh gates.
  No terminal post-patch author validation exists for this exact SHA, and it
  has received neither a fresh specification verdict nor an independent
  quality verdict. Do not put this SHA in the ledger yet.
- I01 review history that must be preserved but not mistaken for approval:
  the initial five-finding candidate was
  `cba9656c78da8fcdae9932e4842cfa0f4de647de0c5131b26cd2d6e32a13969b`.
  Its first full specification review found three report issues: omitted
  publisher `_encodingOverride`, a non-runnable/prescriptive I01-004 RED, and
  unsafe static-registry cleanup in I01-005. The independent author repaired
  those issues to exact SHA
  `23fb46adcefba35536f5fc31ef81b576dd8a7b35b0c6e6e6a9666fb3e3a582ce`
  (44,945 bytes, 235 lines), completed two EOF rereads/full audit, and passed
  fresh safe `68`, official `26`, and ledger-reader-only hypothetical `27`.
  Controller-side independent mechanics, objects, citations, gates, and
  boundary checks also passed that SHA. A brand-new full specification review
  then independently covered all `25/25` primaries, `2/2` anchors, `1/1`
  dependency, `0/0` exclusions and passed its own fresh `68/26/27`, but
  returned **`SPEC ISSUES FOUND`** for two omitted distinct findings. Therefore
  SHA `23fb46ad...82ce` is superseded and has no reusable approval; no quality
  review ever started on it.
- The current seven-finding candidate adds those two source-backed omissions:
  I01-006 is P1 stale Root-CA trust evidence, where the fingerprint cache is
  invalidated only by path-string change, so certificate B replacing A at the
  same path can leave A displayed/copied while B is the offered import file.
  I01-007 is P2 passive Inspector failure, where malformed raw serialized path
  strings reach unguarded `System.IO.Path` calls from expanded MCAP/Security
  surfaces, including a disabled Security scope, and can repeatedly throw
  during Layout/Repaint. The author accepted both without technical pushback
  and created the exact current candidate above via `apply_patch` only.
- I01 authority remains the frozen master
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  (11,877 bytes) and child
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I01_MANAGER_INSPECTOR_SERIALIZATION_REVIEW_PLAN.md`
  at SHA-256
  `1fe17e220253db45eaa05e69ddadb33f5c506af60f623d60d8b92976f6907ae4`
  (11,412 bytes, 191 physical lines). Its exact frozen scope is `25/25`
  ordered primary paths / `25/25` unique blobs (283,864 bytes / 6,353 lines),
  `2/2` anchors, `1/1` bounded `class FoxgloveManager` dependency, and `0/0`
  exclusions. Frozen manifest identities remain primary
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`,
  dependency
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`,
  exclusion
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`,
  and residual
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`.
- Exact continuation order for the next window: (1) read this file through EOF
  and independently rehash AGENTS, ledger, the exact current I01 report,
  frozen master/child/manifests, refs/tree, I01/I02 rows, protected `.meta`,
  relevant processes, and the single-worktree boundary; (2) use a fresh
  `gpt-5.6-sol` / `max` independent author to validate the current exact report
  from zero, source-check all seven findings and REDs, complete two full
  line-1-to-EOF rereads on one unchanged SHA, run the full
  sections/IDs/fields/counts/NOT-RUN/tokens/triples/objects/citations/stale-text
  audit, and pass fresh safe `68`, official `26`, and ledger-reader-only
  hypothetical `27`; if it finds any issue, edit only the I01 report via
  `apply_patch` and repeat all author validation; (3) on the author-validated
  exact SHA, run a brand-new full specification review from zero, then a
  separate brand-new independent quality review from zero; (4) route any
  review issue only to an independent report author, then repeat author
  validation, fresh specification review, and fresh quality review; (5) only
  when the same unchanged exact SHA has both `SPEC COMPLIANT` and `QUALITY
  APPROVED`, update only the I01 ledger row and require the real post-ledger
  validator at `27/37`, dependency `70`, residual `0`, restoring I01 to
  pristine `NOT_RUN` on any failure; and (6) continue I02-I11 in frozen order
  through `37/37`. No individual I closure is the task endpoint.
- At this handoff no author/reviewer subagent, validator, relevant Python,
  Unity, ROS, native, or build process is running. HEAD/local `main`/
  `origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443` with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; there is one worktree, tracked
  and staged diffs are empty, and the only status path is the protected
  untracked 59-byte Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  During I01 only the ignored I01 report and this ignored bootstrap were
  edited. No product code, test, proposed RED, frozen authority/evidence/
  manifest, formal ledger, tracked/staged state, provisional-remediation tree,
  DeepWiki tree, or protected `.meta` was modified. The terminal goal remains
  all `37/37`, paused here only for the requested window switch.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-14, user-requested
  window switch immediately after the H08 seal):** the progressive execution
  ledger has `26/37` closures sealed, exactly through `187-R2-H08`. H08 is
  `COMPLETE` / `FINDINGS_REPORTED`; `187-R2-I01` is the next untouched closure,
  and I01/I02 are pristine `NOT_RUN` with empty verdict, report SHA, and notes.
  The formal ledger is
  `Developer/187/round2/local-review/execution-ledger.tsv` at SHA-256
  `644d3cb4f767bf08b02cd068f779b6ce88c6452e9bb9427a57bced0d64164fec`
  (9,138 bytes). All later paragraphs describing H04 or another earlier live
  checkpoint, partial review, ledger count, or continuation order are
  historical evidence and are superseded by this checkpoint.
- The sealed H08 report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-H08.md` at exact
  SHA-256
  `20202fb3e41147ff5838cd0acbf3bb4d2e93069c01fbbe4b2800ef4e01233063`
  (34,024 bytes, 157 strict UTF-8 LF-only lines; no BOM, CR, or NUL; terminal
  LF). It contains two High-confidence findings (`P1 x0`, `P2 x1`, `P3 x1`),
  and both proposed deterministic REDs remain honestly `NOT RUN`. H08-001
  covers the cross-RMW four-row probe accepting a mismatched or unbound
  external payload/identity; H08-002 covers `--timeout-ms` accepting trailing
  junk through prefix `std::stoll` parsing. A rejected old-generation
  publisher candidate was removed because the frozen authority proves
  publisher expiry before registry replacement, exposes only a weak observer,
  and contains no production post-release strong owner; a test-held
  `shared_ptr` would manufacture the trigger.
- H08 authority remains
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-H08_FULL_DUPLEX_ORIGIN_FOUR_ROW_REVIEW_PLAN.md`
  at SHA-256
  `9fa8ad8673504eabee002cb92f3a60d930dc25ee4d08363cb89d5c3b7cb3d276`
  (8,621 bytes, 173 lines). The exact frozen dossier is `7/7` ordered primary
  paths / `7/7` unique blobs (84,469 bytes / 2,535 lines), `2/2` anchors,
  `2/2` bounded dependencies, and `0/0` exclusions. The final report received
  two complete author EOF rereads, a full mechanical/object/citation audit, a
  fresh full `SPEC COMPLIANT` review, and a separate fresh independent
  `QUALITY APPROVED` review on the same unchanged SHA. Author/specification/
  quality safe discoveries each passed `68/68`; official pre-ledger execution
  validation passed at `25/37`, dependency `70`, residual `0`; pure-memory
  exact-SHA COMPLETE preflights passed at `26/37`; and the real post-ledger
  validator passed at `26/37`, dependency `70`, residual `0`.
- H04-H08 are now sealed at these exact report identities: H04
  `451d8e449563a0a20fb9708168c4453b712f6ce5c61e26fdbe67d34f366b6a05`
  with 26 findings (`P1 x5` / `P2 x20` / `P3 x1`); H05
  `ad9ca9ebed327b751b2089e8f8d0cc3a920c969af9fe25ca2cb51750dd6b4798`
  with two (`P2 x2`); H06
  `98324333ddb67b00e61e074a59f67eaa946a3662b09d607b3add8473dff6e835`
  with three (`P1 x2` / `P2 x1`); H07
  `67f5b1be857aa87e647e7a031def31be7a415ef2914446f3f00e714d47981203`
  with 13 (`P1 x4` / `P2 x9`); and H08 at the exact identity above. Each
  sealed SHA received fresh complete specification and separate quality
  approval before its real post-ledger validator passed. No proposed RED ran.
- The next closure is `187-R2-I01`. Its untouched child authority is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-I01_MANAGER_INSPECTOR_SERIALIZATION_REVIEW_PLAN.md`
  at SHA-256
  `1fe17e220253db45eaa05e69ddadb33f5c506af60f623d60d8b92976f6907ae4`
  (11,412 bytes, 191 lines). Its frozen scope is `25/25` ordered primary paths
  / `25/25` unique blobs (283,864 bytes / 6,353 lines), `2/2` entry/owner
  anchors, `1/1` bounded D01 `class FoxgloveManager` dependency, and `0/0`
  exclusions. `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-I01.md`
  does not exist, and no I01 review or authoring has started.
- Exact continuation order for the next window: (1) read this file through EOF
  and independently rehash AGENTS, ledger, frozen master/I01 child/manifests,
  refs/tree, I01/I02 rows, protected `.meta`, process state, and the single-
  worktree boundary; (2) start a fresh independent I01 author review from zero,
  cover all `25/25` primaries, `2/2` anchors, `1/1` bounded dependency, `0/0`
  exclusions, and write only the I01 report via `apply_patch`; (3) require two
  author EOF rereads, full audit, fresh safe `68`, official `26`, and ledger-
  reader-only hypothetical `27`; (4) require a new full specification review
  and then a separate new independent quality review on the same unchanged
  exact SHA, routing any issue through a report-only author repair and
  restarting both reviews; (5) only after `SPEC COMPLIANT` and `QUALITY
  APPROVED`, update only I01 to `COMPLETE` / its honest verdict and require the
  real post-ledger validator at `27/37`, dependency `70`, residual `0`, with
  rollback to pristine `NOT_RUN` on failure; and (6) continue I02-I11 in frozen
  order through `37/37`. No individual I closure is the task endpoint.
- The user explicitly requested a pause immediately after H08 for a window
  switch. Do not start I01 in this window. At this handoff no review agent,
  validator, relevant Python, Unity, ROS, native, or build process is running.
  HEAD/local `main`/`origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443` with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; there is one worktree, tracked
  and staged diffs are empty, and the only status path is the protected
  untracked 59-byte Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  During this window only the ignored H04-H08 reports, this ignored bootstrap,
  and the local formal-ledger H04-H08 rows were edited. No product code, test,
  proposed RED, frozen authority/evidence/manifest, tracked/staged state,
  provisional-remediation tree, DeepWiki tree, or protected `.meta` was
  modified. The eventual terminal goal remains all `37/37`, but it is paused
  here at the user's request.
- **Live superseding Phase187 Round 2 checkpoint (2026-08-13, requested window
  switch during the fresh exact-SHA H04 specification review):** the
  progressive execution ledger still has `21/37` closures sealed, exactly
  through `187-R2-H03`. H03 is `COMPLETE` / `FINDINGS_REPORTED`;
  `187-R2-H04` remains the active unsealed closure, while H04 and H05 are both
  pristine `NOT_RUN` with empty verdict, report SHA, and notes. The formal
  ledger is
  `Developer/187/round2/local-review/execution-ledger.tsv` at SHA-256
  `ab048f782472e18fa3f6eed851363d5cb2de861dc0177671704bad7eaa9caa68`
  (7,805 bytes). All later paragraphs that cite an earlier H04 report SHA,
  describe an earlier partial H04 review, or give a different live H04
  continuation order are historical evidence and are superseded by this
  checkpoint.
- The current unsealed H04 author candidate is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-H04.md` at exact
  SHA-256
  `ea3551a71bc0d6e3d42ff72f5806645b29465f974c2bd5d25a95652ae68cc895`
  (169,019 bytes, 554 strict UTF-8 LF-only lines; no BOM, CR, or NUL; terminal
  LF). It contains 24 findings (`P1 x5`, `P2 x19`, `P3 x0`), all High
  confidence, and all 24 proposed deterministic REDs remain honestly
  `NOT RUN`. The fifth report-only author repair retained H04-001 through
  H04-022, expanded H04-006 for movable remote-branch source provenance,
  expanded H04-019 for POSIX empty-identity fail-open as well as stale-lock
  nontermination, and added H04-023 for Jazzy/Lyrical stale whole-source
  overlay replay plus H04-024 for post-Dispose scalable-clock resurrection.
  H04-013 also retains the duplicate ZIP/inventory-path hostile variant.
- The final author pass on exact SHA `ea3551a7...c895` completed two full
  post-final-edit line-1-to-EOF rereads. Its mechanical audit passed all nine
  sections, 24 sequential IDs, nine fields x24, `P1 x5` / `P2 x19` /
  `P3 x0`, High x24, 24 exact `The strategy is NOT RUN.` sentences, all 12
  child-plan exact tokens, 131 sequence-identical report/child/manifest
  triples over 116 unique primary blobs, and 119/119 authorized/referenced/
  reachable Git objects. Citation parsing found 239 range phrases expanding
  to 317 OID-range associations and 253 unique ranges across 52 blobs, with
  zero out-of-bounds ranges. Fresh author gates passed: safe discovery exited
  `0`, `Ran 68 tests in 25.985s`, `OK`; official execution validation passed
  at `21/37`, dependency `70`, residual `0`; and a ledger-reader-only
  pure-memory H04 COMPLETE preflight for the exact SHA passed at `22/37`,
  dependency `70`, residual `0`, `errors=[]`. The formal ledger SHA was
  identical before and after the hypothetical preflight.
- Exact SHA `ea3551a7...c895` has **no terminal independent specification or
  quality verdict**. The fresh `gpt-5.6-sol` / `max` specification reviewer was
  deliberately stopped at this window switch. It completed master, child, and
  report first reads through EOF; `128/131` ordered paths and `113/116` unique
  immutable blobs; and both anchors. Its last complete object was Lyrical sync
  blob `11896c9c...f8b5`. The three unread primary blobs are Lyrical runtime
  validator `de192deb...fc52`, Lyrical package validator
  `6034a34f...c966`, and runtime adoption manifest `26a836c3...3ae`. The
  bounded I03 `def main` was `0/1`; independent zero-exclusion confirmation,
  final mechanics/citation audit, report second EOF reread, and fresh
  `68/21/22` gates were not run. It established no candidate specification
  issue before stopping, but this incomplete `113/116` coverage is not a
  verdict and must not be reused as terminal review. No quality review has
  started on exact SHA `ea3551a7...c895`.
- Exact H04 continuation order for the next window: (1) read this file through
  EOF and independently rehash AGENTS, the exact H04 report, ledger, frozen
  master/child, refs/tree, protected `.meta`, H04/H05 rows, process state, and
  the single-worktree boundary; (2) start a brand-new `gpt-5.6-sol` / `max`
  **specification** review from zero on exact report SHA `ea3551a7...c895`,
  reading all 131 paths / 116 immutable blobs, both anchors, bounded I03
  dependency, zero exclusions, all 24 findings/REDs/uncited bodies, and
  independently running safe `68`, official `21`, and ledger-reader-only
  hypothetical `22` gates; do not reuse the stopped review's `113/116`
  coverage; (3) only after `SPEC COMPLIANT`, run a separate new independent
  full **quality** review from zero on the same unchanged SHA; (4) if either
  review finds an issue, route it through an independent report-only author
  repair via `apply_patch`, repeat two author EOF rereads/full audit and fresh
  `68/21/22`, then restart specification followed by quality review on the new
  exact SHA; (5) only after both reviews approve the same unchanged SHA,
  rehash the boundary, update only H04 via `apply_patch` to `COMPLETE` /
  `FINDINGS_REPORTED` with the exact approved SHA, and require the real
  post-ledger validator at `22/37`, dependency `70`, residual `0`, restoring
  H04 to pristine `NOT_RUN` on any failure; and (6) immediately continue
  H05-H08 and I01-I11 in frozen-index order through `37/37`. No individual
  closure is the task endpoint.
- At this window-switch handoff no subagent, validator, relevant Python,
  Unity, ROS, native, or build process is running. HEAD/local `main`/
  `origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443` with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; there is one worktree,
  tracked and staged diffs are empty, and the only status path is the
  protected untracked 59-byte Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  During the current H04 work only the ignored H04 report and this ignored
  bootstrap were edited. No product code, tests, proposed REDs, frozen
  evidence, manifests, formal ledger, provisional-remediation tree, DeepWiki
  tree, tracked/staged state, or protected `.meta` was modified. The user's
  terminal goal remains **all 37/37**.
- The sealed H03 report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-H03.md` at exact
  SHA-256
  `1950fa6164ca5ea4e58d6c62e61350ff4f86942454beea7aa5cb54c279f08a28`
  (198,468 bytes, 543 strict UTF-8 LF-only lines; no BOM, CR, or NUL; terminal
  LF). It contains 15 findings (`P1 x4`, `P2 x11`, `P3 x0`), all High
  confidence, and all 15 proposed REDs remain honestly `NOT RUN`. H03-014
  covers Provider-only disable/re-enable leaving the Manager's captured
  session non-null while both hubs remain stopped; H03-015 covers the public
  subscription-diagnostics facade concatenating locally sorted hub snapshots
  without a deterministic global sort.
- H03 authority remains the child plan
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-H03_R2FU_RUNTIME_NATIVE_OWNERSHIP_REVIEW_PLAN.md`
  at SHA-256
  `9e4318f1e2f903bb977a34406f47ab7a1defa5c75dab01f519556a7b21a79b76`
  (22,188 bytes, 249 lines), under the frozen master at SHA-256
  `c82fab11e5fe83bc2edc5bbf43272b00e10ed489d6fb79892a327eb134460057`
  (11,877 bytes, 152 lines). Its frozen dossier is `82/82` primary paths and
  `82/82` unique blobs (1,131,062 bytes / 27,412 lines), `2/2` anchors,
  `2/2` bounded dependencies, and `0/0` exclusions. The final exact report
  passed two post-edit line-1-to-EOF author rereads, all nine sections and nine
  fields x15, 15 sequential IDs, all counts, 15 exact `NOT RUN` sentences,
  82 ordered triples, 85 authorized blobs plus baseline commit/tree = 87 Git
  objects, and 262 authorized/in-bounds cited intervals across 43 blobs.
- H03's final exact SHA received a fresh complete `gpt-5.6-sol` / `max` `SPEC
  COMPLIANT` review and a separate fresh independent `gpt-5.6-sol` / `max`
  `QUALITY APPROVED` review. Each independently read the complete 82/82 frozen
  dossier and challenged all 15 findings/REDs plus uncited bodies. Controller
  gates passed: safe Python discovery exited `0`, `Ran 68 tests in 30.022s`,
  `OK`; pre-ledger execution validation passed at `20/37`, dependency `70`,
  residual `0`; pure-memory hypothetical H03 COMPLETE passed at `21/37`,
  dependency `70`, residual `0`, without changing the ledger; and the real
  post-ledger validator passed at `21/37`, dependency `70`, residual `0`.
  The first real sealing attempt was conservatively restored to pristine
  `NOT_RUN` when its outer validator timeout expired without output; a fresh
  pristine validator then passed, the exact approved row was reapplied, and a
  five-minute-budget real post-ledger run passed. Synced-disk validator runs
  have recently taken 52-108 seconds, so use at least a 300-second outer
  timeout and never infer failure or success from missing intermediate output.
- H04 authority is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-H04_R2FU_DISTRO_PACKAGE_SELECTORS_REVIEW_PLAN.md`
  at SHA-256
  `cf536dab759a8cc807dfdaf01fc5ccbc1094e955dd2953bd115124ffa9cedce4`
  (30,603 bytes, 297 lines). Its frozen dossier is `131/131` ordered primary
  paths, `116/116` unique blobs (2,014,070 bytes / 47,050 lines), `2/2`
  anchors, `1/1` bounded dependency, and `0/0` exclusions. Controller-side
  path/OID resolution found zero mismatches, and the pristine H04 execution
  validator passed at `21/37`, dependency `70`, residual `0`.
- **Historical superseded H04 checkpoint:** the earlier unsealed H04 report was
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-H04.md` at exact
  SHA-256
  `4122ed38f2322b69ca4f9aedd2a2b3fdb15f3a377b361105970232769fdc2b3a`
  (98,346 bytes, 413 strict UTF-8 LF-only lines; no BOM, CR, or NUL; terminal
  LF). It contains 14 findings (`P1 x4`, `P2 x10`, `P3 x0`), all High
  confidence, and all 14 proposed deterministic REDs remain honestly
  `NOT RUN`. The report explicitly rejects the proposed Lyrical README/config
  contradiction because immutable evidence assigns the documented
  port-conflict/admin settings to the router profile and the opposite settings
  to a distinct peer-session profile.
- That historical H04 author pass completed all `131/131` ordered primary paths /
  `116/116` unique immutable blobs, `2/2` anchors, the bounded I03 dependency
  `1/1`, and exclusions `0/0`. It performed two complete post-final-edit
  line-1-to-EOF rereads on the exact SHA above. Its final audit passed all nine
  sections, 14 sequential IDs, nine fields x14, 14 exact `NOT RUN` sentences,
  131 sequence-identical path/OID/rule triples, 119/119 authorized/reachable
  Git objects, 142/142 cited intervals in bounds across 32 blobs, and all
  required branch/hostile/paired/repair tokens. Controller-side independent
  double EOF rereads and structural/triple/object/range/boundary audits also
  passed on the same bytes.
- Historical controller gates for exact H04 SHA `4122ed38...c2b3a` passed before
  independent review: safe Python discovery exited `0`, `Ran 68 tests in
  36.019s`, `OK`; official execution validation passed at `21/37`, dependency
  `70`, residual `0`; and a ledger-reader-only in-memory H04 COMPLETE preflight
  passed at `22/37`, dependency `70`, residual `0`. The hypothetical preflight
  left formal ledger SHA `ab048f78...aa68` unchanged.
- A historical `gpt-5.6-sol` / `max` H04 specification review independently
  read `131/131` paths / `116/116` immutable blobs, `2/2` anchors, `1/1`
  dependency, and `0/0` exclusions and returned **`SPEC COMPLIANT`** for exact
  SHA `4122ed38...c2b3a`. It found all 14 findings and REDs specification-sound,
  found no omitted P1/P2/P3 issue, and independently confirmed that the router
  and peer-session profiles are distinct compatible roles. Its own fresh gates
  passed: safe discovery `68/68` in `25.123s`, official `21/37`, and pure-memory
  `22/37`, with dependency `70`, residual `0`, and no ledger mutation.
- The historical independent H04 quality review was deliberately stopped for
  this requested window switch and has **no terminal verdict**. Its last fully
  completed boundary was `123/131` ordered paths / `108/116` unique immutable
  blobs. Blob 109 (`61f29a3...`, Lyrical builder tests) was displayed only
  through line `200/764` and is not counted or relied upon. Blobs 109-116
  remain, covering eight Lyrical builder/default/sync/validator/adoption-
  manifest paths; the quality reviewer also had not yet independently
  completed the two anchors, bounded dependency, zero-exclusion confirmation,
  report mechanics/object/citation audit, final repository boundary, or its
  `68/21/22` gates. It established no quality issue before stopping, but this
  partial coverage is not approval and must not be reused as a terminal review.
- Historical H04 continuation order for that earlier window (superseded by the
  live 2026-08-13 checkpoint above): (1) read this file through
  EOF, then independently rehash AGENTS, the exact H04 report, ledger, frozen
  master/child, refs/tree, protected `.meta`, H04/H05 rows, process state, and
  the single-worktree boundary; (2) start a brand-new `gpt-5.6-sol` / `max`
  **quality** review from zero on exact report SHA `4122ed38...c2b3a`, reading
  all 131 paths / 116 immutable blobs, both anchors, the bounded I03 dependency,
  zero exclusions, all findings/REDs/uncited bodies, and independently running
  safe `68`, official `21`, and ledger-reader-only hypothetical `22` gates;
  do not reuse the stopped review's `108/116` partial coverage; (3) if quality
  finds any issue, route it to an independent author repair, edit only the H04
  report via `apply_patch`, repeat two author EOF rereads/full audit, fresh
  `68/21/22`, fresh complete specification review, and then a new independent
  quality review; (4) only after `QUALITY APPROVED` on the unchanged
  specification-approved SHA, rehash the report/ledger/boundary, update just
  H04 via `apply_patch` to `COMPLETE` / `FINDINGS_REPORTED` with the exact SHA,
  and require the real post-ledger validator at `22/37`, dependency `70`,
  residual `0`, restoring H04 to pristine `NOT_RUN` on any failure; and (5)
  immediately continue H05-H08 and I01-I11 in frozen-index order through
  `37/37`. No individual closure is the task endpoint.
- At that historical window-switch handoff no subagent, validator, relevant Python, Unity,
  ROS, native, or build process is running. HEAD/local `main`/`origin/main` are
  still `8d1223fc4cacf988da419b97c51a0f87bd965443` with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`; there is one worktree, tracked
  and staged diffs are empty, and the only status path is the protected
  untracked 59-byte Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`.
  During H04 only the ignored H04 report and this ignored bootstrap were edited.
  No product code, tests, proposed REDs, child/master/manifests, formal ledger,
  provisional-remediation tree, DeepWiki tree, tracked/staged state, or
  protected `.meta` was modified. The user's terminal goal remains **all
  37/37**.
- `187-R2-G03` is sealed `COMPLETE` / `FINDINGS_REPORTED`. Its exact report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-G03.md` at SHA-256
  `fbbdb31983ab3a2975b7867577c8e39f5945d79189da674186122530c14d7bf8`
  (137,760 bytes, 585 lines), with 19 findings (`P1 x2`, `P2 x17`). The exact
  SHA passed a complete frozen-dossier specification review and a separate
  independent quality review before its ledger row was sealed.
- `187-R2-G04` is sealed `COMPLETE` / `FINDINGS_REPORTED`. Its exact report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-G04.md` at SHA-256
  `1fdabcf0e89ce7988da7dfb768a3d09cdeec6bbd6d590929d2aa7ea4f3a76bcd`
  (65,622 bytes, 332 lines), with five findings (`P1 x1`, `P2 x4`). The exact
  SHA passed a complete frozen-dossier specification review and a separate
  independent quality review before its ledger row was sealed. The G04
  post-ledger execution validator passed at `18/37`, residual `0`.
- All 19 current Round 2 local-review reports, including the unsealed H01
  candidate, were moved byte-for-byte from the former Developer report
  directory to the review-owned
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/` directory. Report SHA-256
  values did not change. The old Developer report directory no longer exists;
  all 37 active report-path references were migrated, and execution validation
  passed afterward at `18/37`, residual `0`. This was ignored local-only state;
  no tracked, staged, product, test, or Git mutation occurred.
- `187-R2-H01` is sealed `COMPLETE` / `FINDINGS_REPORTED`. Its exact report is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-H01.md` at SHA-256
  `a070259f9150ea6f1edffb4dc657f03ca1417eeb1468aee1c3c9844e4bfa3ec4`
  (99,766 bytes, 444 lines), with ten findings (`P1 x2`, `P2 x8`) and all ten
  proposed product REDs still honestly `NOT RUN`. The exact SHA passed a fresh
  full frozen-dossier `SPEC COMPLIANT` review and a separate fresh independent
  `QUALITY APPROVED` review before the H01 ledger row was sealed.
- The first H01 sealing attempt correctly rolled back to pristine `NOT_RUN`
  after the COMPLETE-state validator rejected two paired-variant headings that
  did not contain the child plan's exact tokens `SDK/Bridge/R2FU` and
  `configured/unconfigured`. Only those two report headings were corrected;
  two complete author rereads and both independent reviews were repeated on
  the final SHA above. A read-only hypothetical COMPLETE-state validation then
  passed before the real post-ledger validator passed at `19/37`, dependency
  `70`, residual `0`.
- H01 safe-selector evidence remains Phase184A `9/9`, Phase184B `4/4`,
  Phase184C `4/4`, Phase184D `3/3`, Phase156 `8/8`, and focused Provider xUnit
  `28/28` PASS. Phase184E passed E1-E7 and RED only at E8 because the two
  pre-existing ignored `g03-phase138s-red` `bin`/`obj` scratch directories
  remain. They were not deleted and are not an H01 product defect.
- The active `187-R2-H02` child plan is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_PARTITION/187-R2-H02_R2FU_GENERATION_ENTITY_CONSTRUCTION_REVIEW_PLAN.md`
  at SHA-256
  `a3305b74c1e2db2fca8ca4d28f8a76c8bee243c99be4edadb768245d01b9640e`
  (23,195 bytes, 250 lines; read through EOF). Its frozen scope is `84/84`
  maintained primary paths (`77/77` unique blobs), `2/2` anchors, `3/3` named
  dependency classes, and `0/0` exclusions. Its entry owners are
  `FoxRunProviderAnalyzer` in
  `FoxRunR2fuAnalyzerPipeline.cs` and
  `FoxRunRoslynRos2CustomDtoShapeBuilder` in the same-named source file.
- The current **unapproved H02 author candidate** is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/187-R2-H02.md` at SHA-256
  `504c03403090e30ebe01ad882d5e3d9fc355498af81c301bb59b3a78f4389e42`
  (128,839 bytes, 750 lines). It contains 21 findings (`P1 x5`, `P2 x15`,
  `P3 x1`), and every proposed deterministic RED remains honestly `NOT RUN`.
  Do not copy this SHA into the ledger: its fresh specification review found a
  report-specification problem, and no fresh independent quality review has
  been run on this exact SHA.
- This candidate incorporates all issues found by the preceding complete
  independent quality review: P1 H02-019 covers inherited custom-DTO base
  members silently omitted by both builders; P2 H02-020 covers unbound build
  input/toolchain provenance; P3 H02-021 covers malformed `--next-revision`;
  H02-017 also covers Unicode/non-ASCII ROS field identifiers; H02-018 also
  covers set-only top-level inbound properties; and H02-010's RED now includes
  an ordinary successful cleanup control. The existing H02-017 leading-digit,
  H02-018 static/init-only, and earlier H02-001 through H02-016 corrections are
  retained. E02 is used only for later duplicate disclosure; its verdict is not
  imported as H02 evidence.
- The author read the final 750-line candidate twice from line 1 through EOF
  after its last edit. Its post-read mechanical audit passed: nine mandatory
  `##` sections; 21 sequential finding IDs; all nine fields exactly 21 times;
  `P1 x5`, `P2 x15`, `P3 x1`; 21 exact `The strategy is NOT RUN.` sentences;
  all mandatory/hostile/paired tokens; all 84 report path/OID/rule triples;
  all 86 distinct referenced Git objects; and all 16 shorthand cited ranges.
- Controller-side fresh gates for exact SHA `504c0340...9e42` passed before the
  specification review: safe Python discovery exited `0`, `Ran 68 tests`,
  `OK`; the official execution validator passed at `19/37`, dependency `70`,
  residual `0`; and a pure-memory `builder.read_tsv` monkeypatch of H02 to
  `COMPLETE/FINDINGS_REPORTED/<exact SHA>` passed at `20/37`, dependency `70`,
  residual `0`. The formal ledger SHA was identical before and after the
  hypothetical preflight. These gates do not override the specification issue.
- A fresh `gpt-5.6-sol` / `max` full frozen-dossier specification review of
  exact SHA `504c0340...9e42` ended `SPEC ISSUES FOUND`. It independently read
  `84/84` primary paths (`77/77` unique blobs), `2/2` anchors, `3/3` named
  dependencies, `4/4` supplemental SDK/Bridge witnesses, and `0/0` exclusions
  through EOF; all OIDs, triples, 86 objects, 147 parsed ranges, 21 findings,
  and fresh `68/19/20` gates passed. It found no other P1/P2/P3 specification
  problem, but H02-013's RED is internally unsatisfiable as written.
- Exact unresolved H02-013 issue: report line 558 holds the first replaced live
  file with `FileShare.None` until the caller's `finally`, while also requiring
  the synchronous call to return with the complete pre-call snapshot restored.
  Frozen primary `FoxRunRos2InterfacePackageWriter.cs`, blob
  `b7482df33f7e4e78e03a8da8d8c0fd290ff006cd`, rolls back synchronously inside
  its catch at lines 239-258; restoration paths at lines 262-278 and 300-330
  cannot replace/copy the still-locked file. A journaled rollback therefore
  cannot satisfy the stated oracle, while a pre-publication/directory-swap fix
  removes the specified post-first-live-replacement trigger. The minimum
  report-only correction is either (a) deterministically release the lock after
  observing the first restore failure and require retry/full restoration, or
  (b) split the permanent-lock exception-precedence case from an independent
  all-or-nothing transaction case. Preserve the ordinary control, observation-
  first assertions, and implementation latitude. This correction does not add
  a finding or change the expected `21 / P1 x5 / P2 x15 / P3 x1` counts.
- Exact H02 continuation order for the next window: (1) independently re-hash
  the report and confirm `504c0340...9e42`, pristine H02/H03 `NOT_RUN`, and the
  unchanged Git/meta boundary; (2) route only the H02-013 RED correction above
  through the author, editing only the H02 report via `apply_patch`; (3) after
  the final edit, perform two complete author reads from line 1 through EOF and
  rerun all structural/count/triple/object/range/stale-text audits, then record
  the new exact SHA/bytes/lines; (4) rerun the safe `68`-test discovery, official
  validator at `19/37`/dependency `70`/residual `0`, and pure-memory hypothetical
  COMPLETE at `20/37`; (5) run a **new** `gpt-5.6-sol` / `max` full frozen-
  dossier specification review of the new exact SHA; (6) only after
  `SPEC COMPLIANT`, run a separate **new** independent full quality review of
  that same exact SHA; (7) route any issue back through the author and repeat
  specification then quality review; (8) only after both pass update just H02
  to `COMPLETE` / `FINDINGS_REPORTED` with the approved SHA and rerun the real
  progressive validator at `20/37`. On any post-ledger failure, restore H02 to
  pristine `NOT_RUN` before editing the report.
- After H02 seals, start H03 immediately and repeat the same exact serial
  author/specification/quality/hypothetical/ledger/post-ledger workflow for
  H03, H04, H05, H06, H07, H08, and I01 through I11 until the ledger reaches
  `37/37` and the final Round 2 execution validation passes. No individual
  closure is the task endpoint. No H03 work has started.
- The current ledger remains SHA-256
  `6dd6f92123db2f4d9c5d7e26377a75df1911be4598baf9596970cb50cfcd7e5a`
  (7,267 bytes), and the frozen index remains
  `85cea779d65e365a44ed472d4cf6c5b1a98f0617e6a47452c14991f39881dfe2`.
  At this handoff all review agents have stopped and no validator/test command
  is running.
- HEAD, local `main`, and `origin/main` remain
  `8d1223fc4cacf988da419b97c51a0f87bd965443`, with tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`. Tracked and staged state are
  clean. The only Git-status path remains the protected untracked 59-byte
  Phase186 `.meta` at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`;
  preserve it. During this window only the ignored H02 report and this ignored
  handoff file were edited. No product code, test, proposed RED, frozen
  evidence, index, tracked/staged Git state, formal ledger row, or protected
  `.meta` was modified. The user-owned
  `Plan/187/round2-provisional-remediation/` and
  `Developer/187/round2/deepwiki-review/` trees were not touched and must not be
  moved.
- Approved simplified Round 2 end-to-end sequence: (1) seal all `37/37` local
  behavior-closure reviews; (2) globally reconcile findings by root cause and
  seal the reproduction/remediation plan; (3) run REDs, focused fixes, GREEN
  validation, serial integration, and merge; (4) only from the exact final
  merged GitHub-visible code generate 37 concise response-only DeepWiki review
  prompts, one per Round 2 closure; (5) the user manually pastes those prompts
  into DeepWiki; (6) DeepWiki reviews only the final merged GitHub code; and
  (7) returned reports are verified locally before any follow-up RED/fix/PR.
  DeepWiki is not expected to see ignored local `Plan/`, `Developer/`, or this
  handoff. Do not create or require a Round1-to-Round2 crosswalk, historical
  mapping table, or local-audit traceability package as a gate unless the user
  explicitly asks for one. The continuity authority is the merged code
  baseline: Round 2 baseline `8d1223fc4cacf988da419b97c51a0f87bd965443`
  already contains Round 1 review/remediation, DeepWiki 001-472, and subsequent
  merged fixes. The active frozen master already follows this sequence and
  must not be changed merely to restate it.
- Permission policy for this repository is permanently settled: the effective
  Codex task must use unrestricted/full filesystem access with approval policy
  `never`; the repository is trusted and its `.git` directory is writable by
  `LJB\\LJB`. Agents must never request elevation, pass
  `sandbox_permissions`, create a workaround worktree/clone, or ask the user
  to approve an ordinary repository command. If a tool-level safety classifier
  rejects a command shape, rewrite it as a narrower native PowerShell/Git
  command or use `apply_patch` and continue. Treat that rejection as a command
  formulation issue, not as a repository permission problem.
- Phase187 Round 2 active authority is
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_MASTER_PLAN.md` at frozen baseline
  commit `8d1223fc4cacf988da419b97c51a0f87bd965443` and tree
  `3c7a0b7127fcc9cd3f875691f2ce1bb235cd401b`. The approved local-only review
  set contains 37 semantic closures over 1,850 maintained primary paths.
  Frozen source-universe SHA-256:
  `48f47e1a053ceafaef97c917a4a21f0475784bdf49c195abf6e650a64dbede2a`;
  primary manifest:
  `e2d559a32a78e7b24f6e8234589acaf0c07d8bcdba62a2e6a0624fa44631e453`;
  dependency manifest:
  `ceb8c6bf8fa3cd7522a32c0146954b1f806abe4839c5cc946570fa47640c1c2d`;
  exclusion manifest:
  `3e89ba11898de95272951e6b339eee2950c34fc0022fc4e562feef7f0f4dc76c`;
  sealed empty residual ledger:
  `cf79de268dc4ebbe2c8af2eda97d96f57c23d4d045ad36fe286f9e0d2b182887`.
  Frozen generator/catalog/validator script SHA-256 values are respectively
  `c3d0585ac487e45fe9fb69694a3f4c6b5c01a846a9663e475ae81c524cc9e5c3`,
  `32967536ea4a8350f7d8b33d92c1f646881cc1711497de17f8a03c0f1c96eb07`,
  and `4b91ab971f20fc7d0dc9b77a6d0cf9376a3d4cbf247c6ecef0bcb3d0de442950`.
  The protected Phase186 `.meta` remains untracked at SHA-256
  `a9bb3d38f9b5bc55f51aaedf92bada6ce5e264ab4b279ff358f71a36e8c6c5dc`
  (59 bytes). Independent plan-approval seal SHA-256:
  `0c544aece6d634bf96e4fbdffddd1d1ca847d5ad491a831fc649abf16ac83468`.
  All 37 local reviews must be sealed before any product edit; execute in
  index order with the progressive validator. No Phase187 worktree and no
  direct `main` push.
- Phase185 and Phase186 are merged through PR #301. Phase187 local first review
  and its original 75-commit serial remediation were merged through PR #302.
  The post-merge Claude R0-R2 re-review fixes were merged through PR #303.
  DeepWiki reports 001-100 were then reconciled and fixed in four module-based
  serial commits; only the final branch was pushed, and PR #304 merged the
  complete stack as `6af5718317f9b3e935632fd8fa37e9f0e0dd794f`. The next
  Claude review hardening stack was merged through PR #305. DeepWiki reports
  101-200 were then reconciled in eight module-based serial commits; only the
  final branch was pushed, and PR #306 merged the full stack as
  `7295b7ebef6fd8273678295d7c125e00904bb4a2`. DeepWiki reports 201-300 were
  then reconciled in eleven serial commits across nine module branches; only
  the final branch was pushed, and PR #307 merged the complete stack as
  `f04ff90e61d166efca8af3ac645e8e90f257cb83`. The subsequent Claude R6-R8
  reconciliation was merged through PR #308. DeepWiki reports 301-400 were
  then reconciled in 26 module commits plus two final CI-closure commits; only
  the final branch was pushed, and PR #309 merged the complete 28-commit stack
  as `90f786f782871b06a5cf0191af31ee57471c2d1e`. DeepWiki reports 401-472,
  Claude R1-R4 reproduction fixes, Unity compile closure, stable review-test
  asset identities, and final CI drift fixes were then merged through PR #310
  as `f00a7d6d42c23fca793bde4cc5a4ca8f95464a41`. DeepWiki reports 421-425,
  Claude R5-R6 follow-up fixes, the FoxRun migration-warning closure, and the
  final Phase96 CI-contract repair were then merged through PR #311 as
  `88f30fe754398f9510cfba56c1fcae98dd32111a`. The subsequent Claude A/R7
  reconciliation closed validation-source false greens, the complete R2FU
  package matrix, cross-platform Editor restart identity, opened-asset
  containment, delayed encode-worker retirement, Jazzy publisher rebinding,
  Bridge writer progress during heartbeat waits, root `Developer.meta`
  boundaries, and two Linux-only CI contract defects. The nine-commit stack
  was merged through PR #312 as
  `56c8b136c37c2f49d8fc0e0b69c9dae9857d8d70`. The subsequent Claude B1
  release/CI/provenance reconciliation was merged through PR #313 as
  `8d1223fc4cacf988da419b97c51a0f87bd965443`; local full CI passed `13/13`
  under `build/ci/26512-c35b1f4d/logs`, and all remote checks passed. Local
  `main` and `origin/main` are both at that SHA.
  Never push `main` directly. The
  user explicitly forbids a Phase187 worktree: remain in this primary checkout.
  `git worktree list` contains only this primary checkout; do not create a
  secondary worktree for the remaining Phase187 review batches.
- PR #312 final remote verification passed all `7/7` checks. The first local
  full CI passed `13/13` at `cfe3a2cd6` under
  `build/ci/20364-db87364d/logs`. After Linux exposed POSIX process-argument
  quoting and case-sensitive Windows DLL lookup defects, the permitted second
  full run collected `12/13` under `build/ci/16084-b8be48b9/logs`; its only
  failure was a missing nested-test docstring. That local validation-only
  issue was fixed, and the exact failed `dotnet-runtime` lane passed fresh at
  final feature HEAD `cbcf19f9e` in run `15064-1a54c841`. The user explicitly
  authorized one exceptional second content push to the same final branch.
  All eight local serial branches and the final remote branch were then
  cleaned up; `main` was never pushed directly.
- DeepWiki reports 421-425 were later supplied with their missing bodies and
  received a supplemental local review from the unchanged PR #310 baseline.
  The first two serial commits are `1e0296888` (sample sync contracts) and
  `fe6afc699` (schema tooling contracts).
  Reports 421, 423, and 424 produced verified fixes or
  test hardening. Report 422's alleged Google.Protobuf compile failure was
  disproved against the pinned 3.29.3 API and an actual Release build. Report
  425's alleged missing CI wiring was disproved from the exact remote workflow
  checkout/validator sequence, which is now pinned by a regression test.
- Claude R5 was then independently reconciled in three more serial commits:
  `cb0b74120` (video sidecar queue atomicity), `ab58a3c59` (remote MCAP range
  semantics), and `b67f99c3d` (MCAP reader preallocation guard). The fixes
  close an H264/H265 Stop/submit wakeup race, incorrect 416 responses for
  unsupported Range forms, and seekable MCAP allocation before truncated-file
  rejection. Focused affected tests pass `43/43`; all MCAP unit tests pass
  `204/204`. The detailed local-only report is
  `Developer/187/external-intake/PHASE187_CLAUDE_R5_RECONCILIATION.md`.
  Claude R6 and the final warning/CI closures add `889833566`, `8ae380e22`,
  `fd59dadca`, `7b5e2bce5`, and `f9225d964`. The complete ten-commit stack
  passed `Scripts/release/run_ci.py` `13/13` under
  `build/ci/12096-d65bc81b/logs`; only final branch
  `feature/187ce-foxrun-policy-migration-warning` was pushed once. PR #311 was
  merged as `88f30fe754398f9510cfba56c1fcae98dd32111a`, and all nine local serial
  branches plus the final remote branch were cleaned up. The remote `test` job
  passed; the other five jobs failed before checkout because GitHub Actions
  could not download actions (`Service Unavailable`), so the user completed
  the merge after the local full-CI pass and direct failure-log verification.
- The Claude R0-R2 follow-up closes two current duplex-lifecycle defects and one
  latent inbound-queue exception-safety defect. The runtime-owned connection no
  longer disposes the shared transport before v1 fallback, an abandoned started
  connection is disposed so its reader/writer retirement slots return, and an
  invalid inbound snapshot is rejected before resetting the active queue. The
  final follow-up also pins delayed Windows Editor restart relays to PID plus
  process-start identity and propagates isolated MSBuild roots into nested
  analyzer composition tests. The exact PR #303 feature tip was
  `05950523cc1b98e691dd5fb89e7e1bc2e54db7d4`; complete local CI passed `13/13`
  under `build/ci/14936-df709ba5/logs`, and all six remote checks passed.
- The current Phase187 execution authority is the simplified master plus 187A
  flat-partition plan. Older 187B-K documents are historical context, not the
  active workflow. Review plans, review instructions, and review reports belong
  under `Plan/`; Round 2 reports are in
  `Plan/187_PHASE187_ROUND2_CLOSURE_REVIEW_REPORTS/`. `Developer/187/` is for
  validation and acceptance results plus supporting execution metadata, not
  review plans or review reports. Both roots and this file remain ignored local
  state.
  Phase187A froze 10,855 tracked paths into 1,821 primary maintained files and
  464 review packets (507,462 primary nonblank lines); all `464/464` local first
  reviews are complete. Reconciliation and the complete 75-branch serial order
  are recorded in
  `Developer/187/local-review/PHASE187_ROUND1_FINDING_RECONCILIATION.md`. Its
  only plan output is the flat 465-Markdown directory
  `Plan/187_PHASE187_FULL_REPOSITORY_CODE_REVIEW_PARTITION/`: one index plus one
  child per packet. There is no local Round 2, closure coordinator, annex, or
  separate DeepWiki plan format.
- The original local first review, global reconciliation, 75 serial fixes,
  local final CI, and PR #302 integration are complete. That serial tip's final
  pre-push `Scripts/release/run_ci.py` passed `13/13`; logs are under
  `build/ci/30608-81de85e3/logs`. PR #302 then passed all seven remote checks.
  DeepWiki is the mandatory last double-check gate, not a local review round.
  DeepWiki reports 001-100 were reconciled and merged through PR #304. Reports
  101-150 were subsequently reconciled into four local serial branches ending
  at `52f801e3b2c3ac6230642094cbb0c4e06d323c22`; reports 151-200 were reconciled
  into four more module branches ending at
  `676454b6c4324704408675d9d674d87d586eee6b`. Only that final tip was pushed;
  PR #306 merged the complete eight-commit stack. Report-by-report verdicts
  are local-only at
  `Developer/187/deepwiki-review/PHASE187_DEEPWIKI_101_150_RECONCILIATION.md`
  and `Developer/187/deepwiki-review/PHASE187_DEEPWIKI_151_200_RECONCILIATION.md`.
  Final local CI passed `13/13` under `build/ci/4232-452ddbca/logs`; all six
  PR #306 checks passed. All eight local branches and the final remote branch
  have been cleaned up.
  Reports 201-300 were reconciled and merged through PR #307. Reports 301-400
  were subsequently reconciled into one 28-commit serial stack ending at
  feature tip `c1fdf0f4671243316b64171ac48c2da99f194b83`. The complete
  `Scripts/release/run_ci.py` passed `13/13` under
  `build/ci/6444-1d87d8c2/logs`; the final branch was pushed once, all six PR
  checks passed, and PR #309 merged the stack as
  `90f786f782871b06a5cf0191af31ee57471c2d1e`. All 26 local module branches and
  the final local/remote branch are cleaned up. Detailed verdicts are at
  `Developer/187/deepwiki-review/PHASE187_DEEPWIKI_301_350_RECONCILIATION.md`
  and
  `Developer/187/deepwiki-review/PHASE187_DEEPWIKI_351_400_RECONCILIATION.md`.
  Reports 401-440 were subsequently reconciled into 12 module commits,
  followed by reports 441-472 in four more serial commits. A subsequent
  Claude R1-R4 reproduction pass added module fixes for inbound session health,
  asset link boundaries, protocol rejection semantics, Phase186 fanout
  evidence, and damaged Provider-ID access. The complete post-PR-#309 stack was
  pushed exactly once from final branch `feature/187bv-ci-closure`; PR #310
  passed all six remote checks and merged as
  `f00a7d6d42c23fca793bde4cc5a4ca8f95464a41`. All 24 local Phase187 branches
  and the final remote branch have been cleaned up. Report 472 is the final
  available DeepWiki report; there is no 473+ input.
- Final verification at the pre-merge serial tip `f151e09a1` passed the complete
  `Scripts/release/run_ci.py` aggregate `13/13`; logs are under
  `build/ci/13908-a38ecf38/logs`. The first diagnostic runs exposed and then
  closed only local validation issues: misplaced ignored MSBuild output,
  missing docstrings on new nested test doubles, and a pre-existing race where
  a Windows Job integration helper exposed an empty JSON path before finishing
  its write. That helper now publishes identity JSON by atomic replace. PR #304
  subsequently passed all seven remote checks before merge.
- Tracked worktree/index state is clean. The only untracked artifact is
  `Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/Phase186ManualInteractionTests.cs.meta`.
  It is the missing companion for a tracked Unity test script (all other
  Phase186 test scripts have tracked `.meta` files), so preserve it for a
  dedicated feature-branch commit. The orphan `obj.meta` and the empty
  `TestResults.meta`/directory were removed as regenerable noise on 2026-08-06.
- Final local release verification at feature HEAD
  `5b9b3807b574ce17c68845f73bfb1a87f42871d7` passed the complete
  `Scripts/release/run_ci.py` aggregate `13/13`; logs are under
  `build/ci/19204-cfc21c23/logs`. The explicit provisioned Windows live
  selector then passed all `12/12` Unity + ROS/RMW cases using the repository
  junctions under `ros2-windows` (Humble/FastDDS, Jazzy/FastDDS,
  Lyrical/FastDDS, and Lyrical/Zenoh). Its evidence is
  `build/phase186/windows-live/phase186h-cert-5b9b38071890/certification-summary.json`.
  WSL was not used for this verification.
- Remote PR #301 checks all passed: package structure, docs,
  analyzer-freshness, optional ROS2 Native, optional ROS2 adapter, and the
  complete test job. The final CI environment fix pins `actions/setup-python`
  to Python 3.12 so isolated subprocesses can import pinned `psutil`, and its
  cross-platform PATH regression uses `os.pathsep`.
- Phase185 and Phase186 manual acceptance are both complete. Developer note
  160 records `PHASE186_WINDOWS_LOCAL_EDITOR_PASS`; Developer note 161 records
  `PHASE185_WINDOWS_LOCAL_EDITOR_PASS`. A new task may start Phase187 only
  after refreshing this git/bootstrap state. Use a new serial feature branch
  for Phase187; do not reuse or recreate the Phase186 branch.
- The user accepted the JSON dependency assessment: retain Unity's official
  `com.unity.nuget.newtonsoft-json` for Phase186 and the dynamic `JToken`
  protocol surface. Do not mix a broad `System.Text.Json` migration into this
  phase; any future STJ work must be a separate closed-DTO/source-generation
  experiment with explicit IL2CPP evidence.
- The completed Phase185 worktree
  `C:\Users\LJB\.config\superpowers\worktrees\00-Inbox\phase185-foxrun-typed-messagepack`
  has been removed at the user's request. Do not recreate it.
- The local branch `feature/185-foxrun-typed-messagepack` remains as a pointer
  to the same Phase185 tip; all Phase185 code is now present in the primary
  `main` checkout.
- Phase185-A through Phase185-F are committed on the serial ancestry. Phase185-F
  is `2edb479fadb5342222277439bb93c9391ea80543`. The user-performed
  Unity Editor acceptance is complete and `PHASE185_WINDOWS_LOCAL_EDITOR_PASS`
  is recorded in Developer note 161.
- Phase186-A is complete in the required five-commit order after rebasing onto
  Phase185-F, followed by one reviewed integration-fix commit:

  ```text
  3fe4fc324 test(186a): freeze pre-move bridge authority
  17527570e refactor(186a): add neutral FoxRun transport providers
  cba7e1991 refactor(186a): adapt the R2FU provider edge
  58c224874 feat(186a): extract the ROS2 Bridge Unity package
  f09de1d67 refactor(186a): purge ROS transport concepts from core SDK
  03acffee0 fix(186a): preserve typed MessagePack across provider split
  28cd6833d fix(186a): close transport session and serialization gates
  ```

- A later external Phase186-A audit identified three still-open integration
  failures. Commit `28cd6833d` closes all three: failed Provider capture is now
  a sticky fail-closed gate across StartServer, Update, subscribe, and live
  WebSocket publish; disabled R2FU Provider components cannot be re-registered
  by Manager scanning; and all seven renamed point-cloud serialized fields
  retain their exact historical names through `FormerlySerializedAs`.
- Fresh review-fix evidence passed the RED-then-GREEN boundary checks `12/12`,
  native Hub/session checks `5/5`, Provider lifecycle checks `16/16`, complete
  default Phase186-A checks `12/12`, combined native checks `17/17`, and
  Phase115E generator/golden validation `21/21`. The protected scene remained
  unchanged.
- Phase186-B Commit 1 is complete at `c66a694a1`. The shared C#/C++ U2R2 v2
  authority now covers 21 operations, strict RFC 8259 and UTF-8 rules,
  request-derived response correlation, exact unsigned-64 numeric semantics,
  XCDR1 little-endian framing, 49 decode/model negatives, and 5 encode
  negatives. Both final reviews returned `APPROVED` with no P1/P2/P3.
- Final Phase186-B Commit 1 evidence passed C# `24/24`, direct protocol GTest
  `5/5`, and the Jazzy/FastDDS Windows Bridge build with CTest `2/2`.
  Fixture SHA-256 is
  `544059b415cef3f60ab1764814d95b74a20a2b9e52d37d52d95751d63f85103a`;
  Bridge source digest is
  `0ea1df944da7201406755ad9def6cae38b7ce691c927ae42ab15c5adcfb9982e`.
- Phase186-B Commit 2 is complete at `3f1b47e97`. Its shared C#/C++ authority
  locks 27 immutable limits, 23 stable error classifications, and 56 replay,
  ordering, identity, fairness, close, concurrency, and capacity scenarios.
  Both final independent reviews returned `APPROVED` with no P1/P2/P3.
- Final Commit 2 evidence passed the complete Bridge C# suite `73/73`
  repeatedly, protocol GTest `5/5`, authority GTest `1/1` consuming all 56
  scenarios, and a fresh Jazzy/FastDDS Windows Bridge build with CTest `3/3`.
  The fresh Bridge source digest is
  `97661c79841031ea0370fda213be78001a38109a34f772c30eaf97e1f5893783`.
- Phase186-B Commit 3 is complete at `4c2b9681e`. The release provenance gate
  now freezes the exact upstream Git identity and blobs, the pre-move
  156-path Git tree, the v1 seven-key authority, all 20 recorded implementation
  files, and the normative 23-error/27-limit documentation tables. It is
  fail-closed for path aliases, symlink/reparse boundaries, nested/untracked
  protocol sources, noncanonical Markdown authority, strict JSON schema and
  numeric types, and unexplained source overlap. Both final independent
  reviews returned `APPROVED` with no P1/P2/P3.
- Final Commit 3 evidence passed provenance `33/33`, the release provenance
  CLI, `py_compile`, Bridge C# `25/25`, Bridge Python `13/13`, and the fresh
  Jazzy/FastDDS Windows CTest `3/3`.
- Phase186-C is complete at `a619ef87f`. Phase186-D is complete in the
  required three-commit order:

  ```text
  127da227d refactor(186d): extract sidecar session and writer core
  78ac79359 feat(186d): add bounded sidecar ROS2 subscriptions
  777ac9da3 fix(186d): enforce Bridge local-origin suppression
  ```

- Final Phase186-D native evidence passed all `8/8` CTest targets on
  Humble/FastDDS, Jazzy/FastDDS, Lyrical/FastDDS, and Lyrical/Zenoh with
  source digest
  `8efd3d470de2499417133c8d21a8d7366af521a3ce55b911fc475b20f46cfd4e`.
  Bridge C# passed `135/135`, build/capability-tool regressions passed
  `13/13`, and provenance passed. The implementation uses publisher GID
  classification on all rows, including a narrow Humble rclcpp adapter.
- Phase186-E is complete in the required three-commit order:

  ```text
  043779fa0 feat(186e): add bounded Unity duplex connection
  2e6f0f8f5 feat(186e): add inbound ownership and reconnect leases
  79398e55e fix(186e): preserve Bridge publish across duplex reconnect
  ```

- The production TCP publisher now uses the bounded duplex connection, retains
  preparation state across reconnect, permits v1 only on a fresh publish-only
  socket, and reserves all worker retirement capacity before transport
  creation. Publish-only sessions allocate no inbound pipeline.
- Final Phase186-E evidence passed Bridge C# `165/165`, the dispose-race
  regression `30/30`, package composition `4/4`, and the real
  Jazzy/FastDDS Bridge CTest `8/8`. The Bridge source digest is
  `8efd3d470de2499417133c8d21a8d7366af521a3ce55b911fc475b20f46cfd4e`.
- Phase186-F is complete in the required three functional commits plus one
  separately reviewed enum-width fix:

  ```text
  58b91528a feat(186f): add Bridge FoxRun Provider bindings
  e0b457fd9 feat(186f): add generated Bridge CDR inputs and origin guard
  3dc076f6f fix(186f): align Bridge custom enum CDR identity
  255d41ee5 test(186f): certify Provider routing and lease sharing
  ```

- Final Phase186-F evidence passed core `1294/1294`, pure native R2FU
  `1657/1657`, Bridge `177/177`, analyzer composition `5/5`, all three analyzer
  freshness checks, package matrix `4/4`, package validation `44/44`, Phase186
  runtime `16/16`, Phase186 Python regressions `50/50`, provenance, schema
  freshness, and Unity Batch generation `files=35 types=18`. The fresh
  Jazzy/FastDDS dual-schema overlay and generated-standard plus exact Phase181
  duplex CTest passed `9/9`; Bridge source digest is
  `49249044600326cd38dc2bb28dcff9e92361c5d83e169857c53e7fd77f104c4a`.
- Phase186-G Commit 1 is complete at `f65080406`. The Manager now presents
  one deterministic Provider experience: zero-or-more Publish destinations,
  exactly one independently enabled Subscribe Source, explicit unavailable
  and conflicted IDs, ordered package-owned drawers, and multi-object editing
  that never creates companions. Empty/invalid Source configuration fails
  closed without throwing or coupling into Publish configuration.
- Final Phase186-G Commit 1 evidence passed SDK `1298/1298`, focused default
  `23/23`, native `30/30`, Phase180 `19/19`, Phase176 `10/10`, Phase146A
  `58/58`, Phase186 `16/16`, package matrix `4/4`, package validation
  `44/44`, and Unity Batch generation `files=35 types=18` with no compiler
  errors. Four new Unity GUIDs are unique.
- Phase186-G Commit 2 is complete at `556541ede`. Frozen Provider sessions now
  expose bounded observed status with separate Publish/Subscribe readiness;
  Bridge uses connection, publisher-preparation, inbound-pipeline, and active
  decode-binding observations, while R2FU reports live native binding state.
  Missing/malformed status sources fail closed, and Manager Inspector exposes
  configured failures plus bounded retirement age/final-exit evidence without
  surfacing unrelated unconfigured Provider history.
- Final Phase186-G Commit 2 evidence passed SDK `1303/1303`, Native R2FU
  `1666/1666`, Bridge `179/179`, focused Provider/boundary `34/34`, package
  matrix `4/4`, package validation `44/44`, and Unity Batch generation
  `files=35 types=18` with evidence verdict `PASS`. RED-before-GREEN also
  closed integer-overflow count validation and stale stopped-subscription
  degradation.
- Phase186-G Commit 3 is complete at
  `74f15fbf3 docs(186g): ship Provider samples and upgrade guide`, followed by
  `7859eadfc fix(186g): refresh shipped protocol provenance`.
- Phase186-H implementation, acceptance tooling, controlled Unity harness,
  CI selectors, and reviewed follow-up fixes are committed through
  `7558f495ae377e0f2f045bbd736a16c4b81b0075`.
- Manual Phase185 acceptance exposed an Inspector-only enum mapping defect.
  `5fa986e8d fix(186h): correct FoxRun encoding Inspector mapping` now maps
  popup indices `0/1/2` through serialized `FoxRunEncoding` values `1/2/3`
  via `SerializedProperty.intValue`; it no longer passes underlying value `3`
  to the three-entry `enumValueIndex`. The focused regression followed a real
  RED-to-GREEN `1/1` cycle. Current-head evidence also passed Phase185A
  `15/15`, Phase185E `19/19`, Phase180 `19/19`, and Phase184B `4/4`; Unity
  completed a successful assembly reload with zero compiler errors and no new
  enum exception after reload.
- The same manual session then exposed one real generated Protobuf registry
  collision: the Phase153/154 and Phase155/156 manual probes both lowered
  different logical schema names to
  `unity2foxglove.foxrun.VehicleTelemetry`, while their descriptor bytes were
  intentionally different. Commit `0c2ce03c9` gives the two shipped manual
  aggregate schemas distinct leaf names, adds a RED-to-GREEN regression over
  the real source declarations and real Protobuf name resolver, and refreshes
  the checked-in `TestLog_FoxRun.g.cs` fallback. Focused evidence passed the
  exact regression `1/1`, all `FoxRunProtobufContractTests` `36/36`, generated
  publish-schema registration `4/4`, Phase185A `15/15`, and Phase185E `19/19`.
  Unity Batch generation passed with `files=37 types=19`; the regenerated
  manifest has 24 Protobuf registration rows and zero duplicate keys, and the
  old bare `unity2foxglove.foxrun.VehicleTelemetry` wire name is absent.
- Foxglove Desktop's built-in Raw Messages/3D panels report `Unsupported
  message encoding msgpack` for the Phase115F MessagePack topics. That is a
  known client-visualization limitation, not an SDK codec failure and not the
  Phase185 verdict path. Use the maintained Phase185 Python probe/custom panel
  and MCAP inspector for MessagePack acceptance; do not require the built-in
  Foxglove panels to parse schemaless `msgpack` channels.
- The Phase185 live probe then reached remote A and local B but failed its
  exactly-once quiet window because the four controlled TestLog fields used
  `FoxRunPolicy.Change` together with explicit `Hz = 20f`. That combination
  intentionally emits unchanged 50 ms heartbeats, contradicting the probe and
  MCAP inspector's exactly-one B contract. Commit `137a48985` removes the
  explicit heartbeat from both shipped acceptance-source copies, refreshes the
  physical Player fallback, and adds a RED-to-GREEN source-pair regression.
  Fresh focused evidence passed the Python probe regressions `4/4`, exact
  Roslyn/fallback equality `1/1`, and Phase185 B/C/D/E `6/5/12/19`. The user
  must refocus/refresh the already-open Unity Editor before rerunning the live
  probe; do not count the earlier failed recording or probe report as manual
  acceptance evidence.
- The user reran the repaired Phase185 workflow at `137a48985`. The live probe
  passed with remote A `185001/41`, exactly one local B `185002/82`, three
  deliberate malformed-input rejections, and recovery `185003/123`. The final
  Editor-log slice contained only the three expected rejection warnings and
  zero `FOXRUN616`/`FOXRUN618`/`FOXRUN619`. The maintained MCAP inspector
  passed on
  `build/phase185/manual/phase185-messagepack_20260802_054755_2716092Z.mcap`,
  proving the unique output channel is schemaless `msgpack`, `SchemaId == 0`,
  and exact B appears once. `PHASE185_WINDOWS_LOCAL_EDITOR_PASS` is now
  recorded in `Developer/161 Phase185 Typed MessagePack Manual Acceptance
  Result.md`. The Plan-owned fixed copy
  `build/phase185/manual/phase185-messagepack.mcap` was then created without
  overwriting an old file; its hash matches the timestamped source exactly.
  The exact isolated-output `dotnet run --phase185-inspect-mcap` command exited
  zero and printed `PHASE185_MESSAGEPACK_MCAP_INSPECTOR_PASS`.
- The first Phase186 Jazzy/FastDDS manual command exposed a direct-script
  bootstrap defect before any live acceptance began: the deferred live runner
  used `from Scripts...` after the coordinator had only added its leaf script
  directory to `sys.path`. Commit `13d8cb542` now adds the repository root,
  preserves the documented `python Scripts/...` invocation, and adds an
  isolated direct-launch regression. The regression was observed RED with
  `ModuleNotFoundError`, then GREEN; the complete coordinator regressions pass
  `20/20`. The failed attempt produced no Phase186 manual PASS and must be
  rerun from the new clean HEAD.
- The final serial automatic certification ran once at that exact clean
  tracked HEAD as `phase186h-cert-7558f495-r001` and passed all `12/12`
  cases. Exact rows `humble-fastrtps`, `jazzy-fastrtps`,
  `lyrical-fastrtps`, and `lyrical-zenoh` all passed full-duplex live evidence;
  the four-combination package matrix also reports `PASS`. Evidence root:
  `build/phase186/final-certification-7558f495-r001/phase186h-cert-7558f495-r001/`.
- The Lyrical build now uses exported aggregate ROSIDL targets when available
  and the legacy `ament_target_dependencies` macro only as a guarded fallback;
  the Lyrical/FastDDS native build and CTest passed before the final live
  matrix.
- FastDDS acceptance fixes `ROS_AUTOMATIC_DISCOVERY_RANGE=SUBNET` and explicit
  UDPv4 fanout in maintained configuration/regressions; the final
  fanout/fairness/health live case passed.
- Phase185/186 automatic and code work is complete. Phase185 manual acceptance
  is also complete, and the user-performed Jazzy/FastDDS Phase186 suite now
  passes. Formal Phase186 completion remains blocked only on the
  user-performed Lyrical/Zenoh Unity Editor suite in Phase186-H. Do not claim
  `PHASE186_WINDOWS_LOCAL_EDITOR_PASS` and do not start Phase187 before the
  user finishes or explicitly changes direction.
- Final Unity generation ran through `-batchmode`, not Computer Use. The log
  `build/phase186a/final-unity-generator-r4/unity.log` records
  `PHASE185_BATCH_GENERATOR_PASS files=32 types=18` and a normal Batch exit;
  the adjacent evidence JSON records `PASS`.
- Final focused evidence includes SDK `1285/1285`, combined Native R2FU plus
  Bridge `1652/1652`, Bridge `64/64`, analyzer composition `5/5`, package
  matrix `4/4`, package validation `44/44`, Phase186 `10/10`, Phase179
  `52/52`, Phase181 `53/53`, and Phase185 A-E `15/6/5/12/19`.
- Authoritative Phase186 design is
  `Plan/186_PHASE186_ROS_FREE_CORE_AND_BIDIRECTIONAL_ROS2_BRIDGE_MASTER_PLAN.md`.
  The old flat A-G plans are superseded and archived under
  `Plan/186/_superseded/`. Phase186-A is recorded complete in
  `Plan/186/186A_BREAKING_PACKAGE_BOUNDARY_PLAN.md`; Phase186-B is recorded
  complete in `Plan/186/186B_PROTOCOL_AND_PROVENANCE_PLAN.md`; Phase186-C and
  Phase186-D, Phase186-E, and Phase186-F are complete in their child plans;
  Phase186-G is complete and Phase186-H records automatic completion with the
  combined Phase185/186 user manual gate still pending.
- `AGENTS.md`, `Plan/`, and `Developer/` remain ignored local operator state
  and must not be committed.
- The protected scene remains unchanged:
  `Unity2Foxglove/Assets/Scenes/Phase179FoxRunRos2NativeSubscribeAcceptance.unity`.
- No stashes remain. On 2026-08-06 the user explicitly chose to discard the
  final nine obsolete Phase105/110/115/161/184/185 WIP snapshots after their
  later phase deliveries and review history were verified on current `main`.
- Manual-acceptance UX follow-ups are committed through `907a13efd`:
  `f6a67b9c1`, `e5325684c`, `df3ba46b0`, `a28a44ebb`, `a2fb568cf`,
  `5dad95104`, `e3d489402`, `141267230`, `d9a81aaa5`, `6e993bd5a`,
  `cea9108be`, `0daa9edd1`, `785fc03f9`, `3a362c3aa`, `c490a3caf`, and
  `907a13efd`.
  The last fix closes two defects observed in the real Jazzy manual attempt:
  pure-subscribe generated fields now begin with `_incoming` and no longer
  trigger FOXRUN202, and the launcher no longer tells the operator to enter
  Play before Unity compilation and a second stable schema pass finish. A
  token/head/run-bound prepare failure now terminates immediately instead of
  waiting for the 30-minute manual timeout. The normal handoff is now one-suite
  `phase186_bridge_manual.py jazzy` or `zenoh`, printed `1/5` through `5/5`
  progress, two separately timed Unity actions, and one owned Play session per
  invocation. The automatic
  `phase186h-cert-7558f495-r001` `12/12 PASS` matrix was not rerun for these
  follow-ups. The latest two commits apply manual run identity only after
  schema generation/domain reload stabilizes, emit exact identity-bound
  context failures, retain the active stage message in every heartbeat, add
  official Foxglove ROS 2 messages to the generated Bridge physical publish
  route, and stage the interaction-state source in Bridge-only projects.
  Final focused verification passed the complete Bridge unit suite `208/208`,
  the affected Python acceptance modules `85/85`, Python compilation, fresh
  analyzer byte-for-byte verification, Unity Batch generation
  `files=37 types=19`, and the exact Jazzy/FastDDS automatic
  `slow-main-thread-640hz` case. That live case exercised the mixed
  custom/official topic layout including index 1, recorded all nine observation
  classes, and completed with no residual process, port, overlay, or temporary
  project. Evidence root:
  `build/phase186/manual-fix/slow-main-thread-640hz-r2/phase186h-slow-main-thread-640hz-6ac67ee0a9a9/`.
- The next manual retry exposed that Unity loaded the copied
  `Library/Phase186Acceptance/current-run.json` through a validator that
  required the canonical `outputRoot/run-config.json` pathname. `785fc03f9`
  now verifies byte identity with the canonical file and delegates to the
  canonical validator; failures emit exact run identity immediately. The
  final-HEAD main-project Batch seam passed with
  `PHASE186_MANUAL_POINTER_BATCH_PASS` and exit code zero at
  `build/phase186/manual-pointer-batch-final/phase186h-jazzy-fastrtps-duplex-1d717d282a6a/`.
- A cold Bridge-only staging project then exceeded the retired Python `480s`
  ready deadline while remaining inside the C# probe's valid `900s` startup
  window. `3a362c3aa` aligns the coordinator to `900s` and keeps actor
  readiness beyond it. The timeout regression passed RED-to-GREEN, affected
  Python modules pass `85/85`, and the exact final-HEAD Jazzy/FastDDS
  `full-duplex` script-plus-Unity run passed with all nine live observation
  classes and complete cleanup at
  `build/phase186/manual-debug/full-duplex-3a362c3aa/phase186h-full-duplex-3e5006ce6000/`.
- The next real Jazzy manual attempt reached Provider readiness, external A,
  and local B but could never open the external evidence gate. The exact run
  proved that the generated binding mutated only the first of two duplex
  contracts while the ROS peer correctly required `unity-local-b` on both;
  the Inspector therefore showed the custom Phase181 B while the standard
  Foxglove Log remained at `external-a`. The peer wrote `FAIL_PEER` after its
  bounded 300-second window, but the manual coordinator ignored actor failure
  documents and continued Stage 4 toward the 1800-second operator timeout.
  `c490a3caf` mutates every duplex contract, surfaces owned actor exits
  immediately, includes bounded per-topic observations in peer timeout
  diagnostics, and makes Stage 4 heartbeats follow the latest detailed
  substate instead of repeating stale Play-ready text. Each behavior followed
  a real RED-to-GREEN regression. Fresh focused evidence passed `72/72`
  affected Python tests plus `py_compile`; exact Jazzy/FastDDS Unity Batch live
  runs passed custom `full-duplex` and standard `slow-main-thread-640hz`, both
  with complete cleanup and zero residual processes, ports, or temporary
  projects, at:
  `build/phase186/manual-fix/custom-duplex-c490a3caf/phase186h-full-duplex-eafb1cffe03f/`
  and
  `build/phase186/manual-fix/standard-duplex-c490a3caf/phase186h-slow-main-thread-640hz-a1fdae6a7165/`.
  The exact two-duplex manual binding also compiled in the main project and
  emitted `PHASE186_MANUAL_POINTER_BATCH_PASS` at
  `build/phase186/manual/jazzy-fastrtps/phase186h-jazzy-fastrtps-duplex-616b651030a4/manual-pointer-batch.log`;
  that diagnostic run was deliberately interrupted before Play and cleaned
  completely, so it is not manual acceptance evidence.
- The following real Jazzy attempt reached all nine live observation classes,
  the user clicked the enabled Complete button, and Unity emitted exact PASS
  evidence, but the old coordinator then failed `FAIL_CLEANUP` because it
  required Unity's Foxglove port `51349` to close before allowing the operator
  to leave Play Mode. It subsequently removed the token-specific generated
  partial while Unity was still exiting, causing the observed assembly-reload
  warning, Foxglove restart, and `token-specific generated partial is absent`
  context failure. `907a13efd` closes this circular ordering: Complete requests
  Editor Play exit, an exact run/case/token/head `PHASE186_MANUAL_PLAY_EXITED`
  marker proves Edit Mode, and only then may pointer/generated-source cleanup
  begin. RED-to-GREEN regressions cover the order, identity marker, operator
  text, Editor exit request, and Batch diagnostic driver. Fresh focused
  verification passes Python `74/74`, Bridge `208/208`, `py_compile`, and
  `git diff --check`. The exact script-plus-Unity Batch Jazzy manual-path
  diagnostic then passed all nine observation classes with
  `cleanup.complete=true`, zero residual process/port/overlay/temporary-project,
  and none of the two old reload/context errors at
  `build/phase186/manual/jazzy-fastrtps/phase186h-jazzy-fastrtps-duplex-0d287292598a/`.
  This machine diagnostic proved the repaired orchestration but did not replace
  the then-remaining user-owned Jazzy manual acceptance.
- The user then reran the exact Jazzy launcher at `907a13efd`. Run
  `phase186h-jazzy-fastrtps-duplex-0012e728dd01` passed in 177 seconds after
  Provider readiness, external A, both local B publications, peer verification,
  the enabled Complete action, automatic Play exit, and exact Edit Mode
  acknowledgement. `terminal-summary.json` records `verdict=PASS` and all nine
  observation classes; `cleanup.json` records `complete=true` with empty
  residual processes, ports, overlays, temporary projects, and cleanup errors.
  The pointer and generated binding are absent after cleanup, and the run's
  Unity log contains the exact Complete/evidence/Play-exited sequence with none
  of the old assembly-reload or missing-partial failures. This is accepted user
  manual evidence. Only Lyrical/Zenoh remains.
- The first Lyrical/Zenoh launcher attempt then failed in Stage 2 before Unity:
  its reusable CMake tree recorded `Y:/cpp-build`, while the next temporary
  subst allocation exposed the same row as `Z:/cpp-build`. CMake rejected the
  absolute cache-path mismatch. `95701129a` resets only the owned row
  `cpp-build` when the cached and active aliases differ, while preserving the
  existing tree for case/slash-equivalent aliases. Both behaviors ran
  RED-to-GREEN. Fresh affected Python tests pass `89/89`, Python compilation
  and `git diff --check` pass, and the focused Lyrical/Zenoh configure/build plus
  fresh CTest passes `9/9`. This commit changes only pre-Unity build-cache
  reconciliation, so the accepted Jazzy manual evidence at `907a13efd` remains
  valid and must not be rerun. The failed Zenoh run is not acceptance evidence.

## Current Handoff: Claude R5 Supplemental Reconciliation

Start and remain in the primary repository. Local `main` and `origin/main` are
exactly `f00a7d6d42c23fca793bde4cc5a4ca8f95464a41`, the PR #310 merge commit.
Current HEAD is `b67f99c3d` on
`feature/187ca-mcap-reader-preallocation-guard`. The local serial continuation
after the earlier 421-425 supplemental tip is:

```text
cb0b74120 fix(187): keep video queue wakeups atomic
ab58a3c59 fix(187): ignore unsupported remote MCAP ranges
b67f99c3d fix(187): reject truncated MCAP records before allocation
```

Claude R5 was treated as a hypothesis ledger, not accepted from commit names.
Three current defects reproduced and were closed RED-to-GREEN. Two important
claims were rejected: mandatory official MCAP differential conformance is
already wired through Phase121 `--release-blocking`, and current
`McapReadOptions` has no configurable `RecordSizeLimit` for indexed/replay
callers to ignore. Timestamp enqueue ordering remains an unproven structural
observation; moving it after `FlushAsync` could create a real stdout underflow.
Rollback double-failure remains fail-closed, and unchunked replay remains the
explicit Statistics-plus-ChunkIndex contract.

Final focused evidence passes the three affected test classes `43/43`, all
MCAP unit tests `204/204`, `git diff --check`, serial ancestry, and protected
scene verification. Full `Scripts/release/run_ci.py` and Phase121 official
conformance are `NOT RUN` in this turn. No branch is pushed or merged. Detailed
verdicts are local-only at:

- `Developer/187/external-intake/PHASE187_CLAUDE_R5_RECONCILIATION.md`
- `Developer/187/deepwiki-review/PHASE187_DEEPWIKI_421_425_RECONCILIATION.md`

Preserve the one untracked
`Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/Phase186ManualInteractionTests.cs.meta`
exactly. The protected Phase179 scene remains unchanged. Never push `main`, do
not create a worktree, and do not clean these serial branches until the user
starts the final CI/push/PR/merge workflow.

## Historical Handoff: DeepWiki 421-425 Supplemental Review

Start and remain in the primary repository. Local `main` and `origin/main` are
exactly `f00a7d6d42c23fca793bde4cc5a4ca8f95464a41`, the PR #310 merge commit.
Current HEAD is `fe6afc699` on
`feature/187bx-schema-tooling-contracts`, chained from
`feature/187bw-sample-sync-contract` at `1e0296888`. Never push `main`
directly. Neither supplemental branch has been pushed, merged, or cleaned up.

The formerly missing report bodies for 421-425 are now locally reviewed.
Detailed verdicts and evidence are local-only at:

- `Developer/187/deepwiki-review/PHASE187_DEEPWIKI_421_425_RECONCILIATION.md`
- `Developer/187/deepwiki-review/PHASE187_DEEPWIKI_401_440_RECONCILIATION.md`
- `Developer/187/deepwiki-review/PHASE187_DEEPWIKI_441_472_RECONCILIATION.md`
- `Developer/187/external-intake/PHASE187_CLAUDE_R1_R4_REPRODUCTION_RECONCILIATION.md`

Report 421's three sample-sync findings reproduced and were fixed. Report 423
correctly identified custom-input Git provenance drift; it is fixed by resolving
the repository from the actual input directory. Report 424's reachable timeout
test gap is hardened with a direct subprocess-boundary assertion and verified
by mutation. Report 422's P1 is rejected: Google.Protobuf 3.29.3 exposes the
required `Add(IEnumerable<T>)` overload and the real generated surface builds
with zero errors. Report 425's missing-CI claim is rejected: the remote workflow
checks out the pinned upstream schema snapshot before invoking the validator;
the exact repository/ref/path/order is now regression-pinned.

Focused final-tip evidence passes sample sync `21/21`, schema tooling `22/22`,
remote workflow wiring `1/1`, strict generated-output regeneration `41/41`,
changed Python compilation, and `git diff --check`. The actual isolated C#
Release build passed with `0 errors`. Complete `Scripts/release/run_ci.py` is
`NOT RUN` for this supplemental batch.

Before this supplemental review, reports 401-440 were reconciled into 12
module-based serial commits, reports 441-472 into four more, and Claude R1-R4
claims were independently reproduced and either fixed or explicitly closed. A
Unity Editor compile exposed a Jazzy generic-contract regression; it was closed
by a RED-to-GREEN Roslyn probe and the R2FU ownership review group passed
`14/14`. The four new review test sources also received stable tracked Unity
`.meta` identities.

Report 472 is the final available DeepWiki input; there is no 473+ batch.

For reports 441-472, four module groups were closed: PointCloud multicast
subscriber isolation plus constructor/timing boundaries, C# U2R2 corrupted
counter fail-closed guards, WebSocket null-text/16 MiB smoke frame bounds, and
the missing `hello_ack.capabilities` negotiated-grant documentation.
Final focused verification at the serial tip passed SDK PointCloud plus
WebSocket xUnit `39/39`, Bridge U2R2 authority xUnit `6/6`, U2R2 v2 codec xUnit
`9/9`, Python smoke `36/36`, changed Python compilation, and
`git diff --check`. The earlier 401-440 evidence remains recorded in its
reconciliation note.

The six post-DeepWiki reproduction and compile-closure commits are:

```text
371d47aa7 fix(187): isolate inbound session health
45dfd52c3 fix(187): close asset hard-link escape
f1e7ac6ea fix(187): classify protocol rejections precisely
a1359e8a1 fix(187): require observed fanout readiness
71fadb665 fix(187): fail closed on damaged provider ids
194886509 fix(187): restore Jazzy sensor reference contract
```

The explicit R1-R3 unresolved ledger is closed: H5 and F1 reproduced and were
fixed; H3 was already closed by `3906e5f3e`; C3 and E2 are required fail-closed
state invariants; I3 is the deliberate separation between ordinary tooling CI
and a labeled provisioned Windows live runner. Final focused checks at
`71fadb665` passed Bridge `60/60`, SDK `56/56`, native R2FU status `1/1`, and
CI/live separation `2/2`; `git diff --check` passed.

The first complete `Scripts/release/run_ci.py` diagnostic run passed `11/13`
under `build/ci/26348-e95421b2/logs` and exposed only an ignored MSBuild output
directory plus stale provenance hashes. Focused validation while closing those
also identified missing Python docstrings and a stale source-shape gate; all
were fixed before the sole final complete rerun. The final aggregate passed
`13/13` under `build/ci/10392-5dbb986d/logs`, then all six PR #310 checks
passed. The final feature branch was pushed exactly once, `main` was never
pushed, and PR #310 merged as
`f00a7d6d42c23fca793bde4cc5a4ca8f95464a41`.

All 24 local Phase187 branches were deleted with `git branch -d` after proving
they were merged into both local and remote main. The final remote branch was
deleted after merge. Keep `Plan/`, `Developer/`, and this handoff ignored and
local-only. Preserve the one untracked
`Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/Phase186ManualInteractionTests.cs.meta`
exactly. The protected Phase179 scene remains unchanged, no stashes remain,
and only the primary worktree exists. Do not create a worktree or invent a
473+ batch.

## Historical Handoff: DeepWiki Reports 301-400 Merged

Start and remain in the primary repository. Local `HEAD`, `main`, and
`origin/main` are exactly `90f786f782871b06a5cf0191af31ee57471c2d1e`,
the PR #309 merge commit. Reports 301-400 are complete. Their final feature
tip was `c1fdf0f4671243316b64171ac48c2da99f194b83`; it is an ancestor of the
merge commit. Only the final feature branch was pushed once. `main` was never
pushed directly.

The complete `Scripts/release/run_ci.py` aggregate passed `13/13` at
`build/ci/6444-1d87d8c2/logs`, and PR #309 passed all six remote checks before
merge. All 26 local module branches were deleted with `git branch -d`; the
final local and remote branch were also removed. No local or remote
`feature/187*` branch remains.

Report-by-report verdicts and evidence remain local-only at:

- `Developer/187/deepwiki-review/PHASE187_DEEPWIKI_301_350_RECONCILIATION.md`
- `Developer/187/deepwiki-review/PHASE187_DEEPWIKI_351_400_RECONCILIATION.md`

Do not create a worktree or rerun reports 001-400. Keep Plan/Developer and this
handoff local-only. Reports 401+ are outside scope until the user explicitly
starts the next batch. Do not run or impersonate DeepWiki locally. Expected Git
state is clean tracked `main`, exactly the one retained untracked Unity test
`.meta` file, zero stashes, no protected Phase179 scene diff, and the single
primary worktree. Never push `main` directly.

## Historical Handoff: Phase185 and Phase186 Jazzy Accepted; Run Zenoh

Start and remain in the primary repository, then run:

```powershell
Set-Location 'D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox'
git status -sb --untracked-files=normal
git rev-parse HEAD
git diff --cached --name-status
git stash list
git diff -- Unity2Foxglove/Assets/Scenes/Phase179FoxRunRos2NativeSubscribeAcceptance.unity
```

Expected: `feature/186-ros-free-bidirectional-bridge` at `95701129a`, clean
tracked worktree/index, the preserved unrelated untracked meta artifacts, all
stashes retained, and no protected-scene diff.

Authoritative plan:
`D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox\Plan\186_PHASE186_ROS_FREE_CORE_AND_BIDIRECTIONAL_ROS2_BRIDGE_MASTER_PLAN.md`.

Do not rerun Phase185 or the automatic matrix, and do not start Phase187. Open
`Plan/186/186H_AUTOMATED_AND_MANUAL_ACCEPTANCE_PLAN.md` and hand the user only
the sole remaining Phase186 Bridge suite:

```powershell
python Scripts/smoke/foxrun/phase186_bridge_manual.py zenoh
```

One command owns one Play session. Follow its printed `1/5` through `5/5`
status; in Unity choose **Foxglove > Manual Acceptance > Phase186 > Prepare
Current Bridge Run**, then wait for the separate terminal line
`UNITY ACTION 2: Enter Play Mode once` before entering Play. Click **B** only
when enabled and click **Complete** only when enabled. Complete now exits Play
Mode automatically; do not toggle Play again while the launcher waits for the
exact Edit Mode marker and performs terminal cleanup. The direct coordinator
arguments are advanced diagnostics, not the normal flow. Keep the same
`Unity2Foxglove` project and Phase186 scene; the Bridge launcher owns the
Lyrical/Zenoh sidecar and router, so do not switch the Unity R2FU runtime,
packages, Manager profile, or scene. A Unity restart is not required. The
automatic matrix baseline remains the parent `7558f495` `12/12` PASS evidence;
the current manual-defect follow-ups are covered by the focused current-head
evidence above and must not trigger another 1-12 rerun. Record
`PHASE185_WINDOWS_LOCAL_EDITOR_PASS` is recorded in Developer note 161. Record
`PHASE186_WINDOWS_LOCAL_EDITOR_PASS` only after the Zenoh launcher also passes.
The user explicitly overrode the broad `run_ci.py` execution; do not
retroactively run it or weaken the accepted focused/live evidence boundary.

The executable operator handoff is
`Developer/160 Phase185-186 Combined Manual Acceptance.md`. It explicitly
locks the repair workflow to focused failing-case reruns followed by at most
one final 1-12 certification; that final certification has already passed, so
the current handoff must not rerun it.

This historical handoff is superseded by the current Phase187 handoff above.

## Historical Local State: 2026-07-26 Permission-Reset Snapshot

Date: 2026-07-26

- Repo root: `D:\BaiduSyncdisk\Obsidian Vault\Websocket\00 Inbox`.
- Current branch: `feature/184f-post-review-polish`.
- Current HEAD:
  `495cd3ceb fix(184f): harden stream ownership and certification`.
- `main`, `origin/main`, and `origin/HEAD` are aligned at
  `eba7c7a56 Merge pull request #297 from JianbinLiu-CFLab/feature/183b-r2fu-v0.9.0-runtime-refresh`.
  Never push `main` directly.
- The current branch contains separately committed 184-A through 184-F plus
  the serial review-polish commits below. `495cd3ceb` is the last committed
  baseline. A later verified 184-F review pass and the in-progress 184-G
  implementation are preserved in the dirty working tree.

  ```text
  c0936e035 fix: harden FoxRun input and artifact gates
  f83fbaf8b test(181): lock UNC extended-path normalization
  5a94d3a32 feat(184a): simplify FoxRun scheduling declarations
  1e1aaf250 feat(184b): add directional FoxRun profiles
  dcda6a9c4 feat(184c): align FoxRun QoS with ROS 2 policies
  af26e1a0a fix(184): polish profile and panel integration
  f21870526 fix(184): align shared generation fallbacks
  c60253107 feat(184d): isolate multi-target FoxRun fanout
  10c702178 fix(184d): harden bridge lifecycle and diagnostics
  38150d28d feat(184e): add bounded FoxRun input streams
  729cc1028 test(184f): certify the FoxRun profile model
  84c116afc fix(184f): polish FoxRun certification
  495cd3ceb fix(184f): harden stream ownership and certification
  ```

- The current Codex task was platform-started with a managed
  `workspace-write` sandbox even though the user selected Full Access. Normal
  repository files are writable, but `.git` is read-only and the approval
  policy is `never`. `git add` fails while creating `.git/index.lock` with
  `Permission denied`. This was not an agent-created sandbox or worktree.
  Stop this task, restart Codex, and resume in a new window only after the
  effective task permissions expose `.git` as writable.
- No Phase184 worktree was created. Continue in this original repository when
  permissions are repaired; do not create an isolated worktree or shadow
  clone as a workaround.
- Exactly one tracked file is currently staged:
  `Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/FoxRunRos2StreamSubscriptionBinding.cs`.
  Do not unstage or discard it. All other current changes are unstaged or
  untracked and are enumerated by `git status`.
- The requested post-184-F Claude review has been independently audited.
  Finding #5 about SinkRouter SingleWriter reference counts was withdrawn:
  generated sources use per-instance GUID origins and `FoxTopicBus` rejects
  the second SingleWriter before SinkRouter. The remaining verified fixes are
  implemented and focused-tested but still need their own commit.
- The user explicitly overrode repeated full-CI execution: finish and
  stabilize all 184-G implementation first, then run the approximately
  30-minute complete CI exactly once. Do not run full CI after every fix.
  Do not begin Phase185.
- Retain but do not apply or drop either Phase184 stash:
  - `stash@{0}: wip: phase184e paused for post-184d review polish`;
  - `stash@{1}: wip: phase184 task2 before pre184 review hardening`.
- Another detached historical worktree exists at
  `C:/Users/LJB/.config/superpowers/worktrees/00-Inbox/phase134-42-rviz-ros2-test-fixes`.
  Do not remove or alter it without explicit direction.
- The protected user scene remains
  `Unity2Foxglove/Assets/Scenes/Phase179FoxRunRos2NativeSubscribeAcceptance.unity`.
  Never stage, discard, rewrite, or normalize it without explicit user
  direction, even if Unity touches it later.
- The Phase181 manual scene is allowed Phase184 migration scope:
  `Unity2Foxglove/Assets/Scenes/ManualAcceptance/Phase181FoxRunCustomRos2InterfaceAcceptance.unity`.
- `ros2-windows/` remains a local Windows ROS 2 entry directory with junctions;
  do not expand it in broad untracked scans.
- `AGENTS.md` is ignored and local-only.
- `SESSION_RECOVERY.local.md` has been removed; do not recreate it.
- `Plan/` and `Developer/` are ignored local operator notes.
- The previous 139D attempt changed the cursor/replay state machine in a way
  that made behavior worse. The user reverted that work before commit
  `75e00bb`. Do not resurrect those changes from memory or summaries.
- The user explicitly authorized a forced shutdown after the v0.9.0 acceptance
  run. Do not infer that authority for later sessions.

## Historical Handoff: Phase184-G Paused for Permission Reset

- Stop here and change windows. Do not continue implementation, start Unity,
  run CI, or mutate Git in this restricted task.
- On resume, first read this file and run:

  ```powershell
  git status -sb --untracked-files=normal
  git rev-parse HEAD
  git diff --cached --name-status
  ```

  Confirm branch `feature/184f-post-review-polish`, HEAD `495cd3ceb`, the one
  staged binding file, both retained Phase184 stashes, and an unchanged
  protected Phase179 scene. Then prove `.git` is writable by staging only the
  reviewed 184-F scope. If `.git/index.lock` is still denied, stop again
  without creating a worktree, clone, or alternate Git directory.

### Pending 184-F post-review commit

- The following verified review findings are fixed in the working tree:
  - generated Protobuf multi-stream switch cases use braces and no longer
    produce CS0128 local-name collisions;
  - the native cleanup CAS loser no longer releases the node early;
  - failed native registration is bounded and isolated per contract;
  - delayed cleanup failures remain observable;
  - disposed streams are not misreported as rate drops;
  - queue replacement preserves its configured capacity;
  - final native node release and per-sample disposal respect cleanup thread
    ownership;
  - Phase184-F evidence covers both generated and runtime sides instead of
    relying on a one-sided pass.
- Fresh focused evidence for this uncommitted scope passed:
  - default C# filters: `178/178`;
  - native C# filters: `203/203`;
  - runtime Phase184A/B/C/D/E: `13/5/4/7/8`;
  - `git diff --check`: clean.
  The runtime commands emitted only the known process-environment `NU1900`
  vulnerability-source warning; all selected behavior checks passed.
- Commit only this 184-F scope first. Relevant files are the staged native
  binding, `FoxRunRos2SubscriptionHub.cs`, the generation validator and three
  input/publish emitters, the rebuilt analyzer DLL, `FoxRunStream.cs`,
  Phase184 model validation, stream tests, and native stream ownership tests.
  `FoxRunDeclarationModelTests.cs` is mixed: its early
  `Sequence`/`Confidence` regression belongs to 184-F, while the later
  Phase184-G declaration anchors do not. Use partial staging for that file.
- Keep these files out of the 184-F commit:
  `FoxRunInboundJson.cs`, `FoxRunInboundTests.cs`,
  `Scripts/release/run_ci.py`, `Scripts/smoke/foxrun/`, all three new Unity
  acceptance scripts and metas, and all ROS 2 Bridge changes.

### In-progress 184-G implementation

- The fail-closed Python acceptance protocol and parent orchestrator are
  implemented under `Scripts/smoke/foxrun/`. Their current complete regression
  suite passes `37/37`. It covers immutable case configuration, exact
  actor/applicability sets, owned process identity and cleanup, tokenized
  manual markers, bounded Editor-log rescue scans, exact graph/Bridge QoS
  evidence sources, Bridge health/publisher evidence, stream cross-checks,
  distinct junction/subst spellings, port preflight, and stable terminal
  failures.
- `FoxRunInboundJson.cs` and `FoxRunInboundTests.cs` add the DTO JSON behavior
  needed by the 184-G harness. The later hunks in
  `FoxRunDeclarationModelTests.cs` are also 184-G.
- Three Unity acceptance sources and their metas exist but are untracked:
  `Phase184FoxRunProfileAcceptance.cs`,
  `Phase184FoxRunProfileAcceptanceBuilder.cs`, and
  `Phase184BatchModeProfileProbe.cs`. Their GUIDs are valid and unique.
  The builder is intended to generate the controlled scene; never handwrite
  the Unity YAML.
- The scene
  `Unity2Foxglove/Assets/Scenes/ManualAcceptance/Phase184FoxRunProfileAcceptance.unity`
  and its meta do not yet exist. Both Batch and non-Batch builder attempts
  failed before script import because this task identity lacks Unity
  `com.unity.editor.headless` / `com.unity.editor.ui` entitlements. Logs are:
  - `build/phase184/acceptance/scene-builder-local/unity.log`;
  - `build/phase184/acceptance/scene-builder-interactive/unity.log`.
  The exact owned Unity process from the UI attempt was terminated; no Unity
  process remained at handoff.
- The ROS 2 Bridge has an uncommitted Windows socket port in its CMake,
  production source, and smoke tests. A focused native configure found MSVC
  19.51, `ament_cmake`, `rclcpp`, FastDDS, nlohmann-json, and GTest, but CMake
  could not read OpenSSL/tinyxml through the junction target
  `C:\ros2_jazzy\ros2-windows` because this task sandbox denied access.
  Therefore there is no valid Windows compile/test claim yet.
- Before the Bridge can be considered complete, fix and regress the current
  static-audit finding in `main`: `WinsockRuntime` is scoped inside the inner
  `try`, so `WSACleanup()` currently runs before the later
  `rclcpp::shutdown()`. The Winsock lifetime must encompass ROS initialization,
  node use, and ROS shutdown.

### Locked commit and execution sequence

1. Commit the pending 184-F review scope separately, after exact partial
   staging and `git diff --cached --check`.
2. Commit the fail-closed protocol/orchestrator as
   `test(184g): add fail-closed acceptance protocol and orchestrator`.
3. Generate and verify the controlled Unity scene, then commit the Unity
   harness as `test(184g): add Unity profile acceptance harness`.
4. Complete a real Windows Bridge compile/test and commit it as
   `fix(184g): support Windows-native bridge acceptance`.
5. Wire final gates and commit them as
   `ci(184g): wire automated acceptance gates`.
6. Run the five serial 184-G Batch cases, the four Phase181 rows, dedicated
   Unity-log review, cleanup proof, package/sample gates, and then one final
   full CI.
7. Stop at `AUTOMATION_READY_FOR_MANUAL`. Only then hand the user the two
   focused Jazzy/FastDDS and Lyrical/Zenoh Play Mode suites. Do not record
   `PHASE184G_WINDOWS_LOCAL_EDITOR_PASS` before both user-performed suites.

## Superseded Handoff: Phase184-F Committed Baseline Evidence

- Phase184-E and Phase184-F are committed through `495cd3ceb`. The final
  27-file commit completes the strict post-184-F audit. It hardens
  bounded-stream ownership, native subscription cleanup, per-contract native
  registration isolation, Roslyn legality, controlled generated fallback
  freshness, the Phase184 evidence gate, and maintained documentation checks.
- Accepted review fixes include:
  - braces around generated multi-stream switch cases, preventing CS0128;
  - disposal diagnostics that cannot interrupt remaining cleanup;
  - non-blocking native `Stop()` with exactly-once deferred clear/release;
  - null and borrowed-identity materializer guards plus thread-local copy
    context reuse;
  - per-contract isolation of null stream registration failures;
  - O(1) queue detach for `Clear`, `Dispose`, and `TryTakeLatest`;
  - Roslyn/reflection legality coverage and a freshness gate for the checked-in
    `TestLog_FoxRun.g.cs` Player fallback.
- Strict audit added RED-then-GREEN regressions and fixes beyond the external
  report:
  - valid `PublishAndSubscribe` no longer emits the unconditional FOXRUN400
    authority warning. The diagnostic is retired and permanently reserved;
    ownership remains a documentation contract.
  - a null native stream is marked seen before isolated registration failure,
    so end-of-capture pruning cannot erase its stable Failed/BackendFailure
    diagnostic or replace an existing frozen binding.
  - the old-syntax guard now catches retired FoxRun type families plus
    qualified and combined FoxRun attributes in both C# and maintained
    Markdown. Its scan includes root/docs/Unity/Bridge documentation.
  - Phase184 B/C/D evidence is no longer inflated as Structural when those
    selections execute behavior checks only; A and E retain both labels.
  - Task14 public documentation and package-boundary requirements now have
    permanent Phase184A checks.
- Native teardown coverage also proves that removal failure still executes
  clear/release and preserves the original exception, while `Stop()` remains
  non-blocking when a callback is in flight.
- The controlled analyzer DLL hash is
  `dd59efd9d351ab6253f977bdc19725b26a864bf1785fb664d9deb3fbbc0aaa8f`.
  `TestLog_FoxRun.g.cs` was mechanically regenerated from the current Roslyn
  emitter and now uses the per-instance `__foxRunOrigin` GUID.
- Fresh complete xUnit evidence passed: default `1480/1480`, adapter
  `1480/1480`, and native `1710/1710`. Runtime selections passed Phase184A
  `13`, B `3`, C `3`, D `4`, E `8`, Phase179 `52`, and Phase181 `53` checks.
- Additional gates passed: analyzer Release freshness; panel `43/43`,
  typecheck, and build; sample dry-run sync; Unity package `42/42`; full-demo
  map validation (`25`); release tooling `53/53`; R2FU Jazzy and adapter
  package validation; all Phase179 and Phase181 ROS2 regression rows;
  boundary/changelog; generated-schema provenance; and `git diff --check`.
- Official MCAP differential conformance passed at
  `build/phase184/phase121-conformance-report.json`: streamed reader `208/0`,
  indexed reader `8/0`, writer `208/0`, verdict
  `PASS WITH MEASURED BASELINE`.
- Exactly one full CI was run at
  `build/ci/phase184-post-review-polish-final`. Eight top-level lanes passed,
  including default/adapter xUnit `1479/1479`, native `1707/1707`, panel
  `43/43`, analyzer, Phase179, Phase181, and boundary. Its three failures were
  current-process environment failures, not product regressions:
  - schema provenance and official MCAP Git reads were rejected because
    `CodexSandboxOffline` does not own the nested checkouts;
  - Phase52 PFX loading failed because this sandbox SID has no usable user key
    store (`DefaultKeySet` fails while the same PFX with `MachineKeySet`
    succeeds).
- Non-mutating process-local follow-ups supersede those environment failures:
  schema generated outputs match fresh generation; official MCAP differential
  conformance passed; and the complete current default, adapter, and native
  xUnit suites plus all explicit Phase184/179/181 selections pass. Do not
  describe the original full-CI run itself as eleven-lane green, and do not
  rerun final CI inside the restricted sandbox. Phase184-G owns the fresh
  outside-sandbox full CI.
- The protected Phase179 scene is unchanged and both Phase184 stashes are
  retained. The next gate is the non-sandbox full CI required by Section 1 of
  the Phase184-G Plan. Do not mix Phase184-G implementation into this baseline
  before that gate passes, and do not begin Phase185.

### Earlier Phase184-C and review-polish evidence

- Phase184-C is committed at `dcda6a9c4`.
- The Claude report at
  `C:\Users\LJB\.codex\attachments\92571c48-a36a-4188-970a-11bd713f787d\pasted-text.txt`
  inspected only the Phase184-A/B endpoint `1e1aaf250`, not the completed
  Phase184-C tree. Its historical findings 1-5 were already fixed and
  regression-covered by `dcda6a9c4`: serialized field aliases, catalog `hz`
  schema parity, inherited accessible `OnlyIf`, and inherited encoding
  lowering.
- Its two remaining integration findings are fixed by `af26e1a0a`:
  1. omitted publish `Hz` now consumes the frozen
     `ActiveFoxRunDefaultPublishRateHz`, while explicit member rates remain
     authoritative and mixed-topic explicit rates cannot be masked by the
     normalized legacy default;
  2. `Tools/foxglove-extensions/foxrun-publish-panel` now consumes the
     maintained catalog fields `flow` and `hz`, and tests reject the retired
     `flowMode` / `rateHz` aliases.
- The Polish commit also refreshed the Phase115E Roslyn golden, regenerated
  the controlled Unity output, and rebuilt the analyzer through repository
  tooling.
- The later Claude report at
  `C:\Users\LJB\.codex\attachments\e1cd31e2-fddc-45de-967f-89f023635991\pasted-text.txt`
  audited `1e1aaf250..dcda6a9c4` and reported no Critical or Important finding
  plus three Minor consistency findings. They were verified against
  `af26e1a0a`, not accepted blindly:
  1. its exact Subscribe-first metadata scenario is unreachable because
     `TopicMetadataEmitter` receives publishing members only and FOXRUN615
     rejects mixed publishing endpoint/QoS contracts; `f21870526` still
     hardens the direct emitter path so QoS presence comes from the same
     canonical endpoint as Source and Targets;
  2. the dead v2 discovered-subscription selection was real; bindings now use
     the v3 capability gate while the historical v2 empty subscriptions
     section/hash remains unchanged;
  3. the shared `TopicMember` JSON fallback was real; omitted or blank
     encoding now lowers to `InheritEncoding`, matching
     `FoxRunGenerationMember`.
- `f21870526` adds RED-then-GREEN regressions for all three findings and
  rebuilds the checked-in analyzer through repository tooling. Phase115E
  passed all 21 Roslyn/reflection/golden checks, and the complete default unit
  suite passed 1331 tests before final CI.
- The latest valid final full CI run is
  `build/ci/phase184-review-followup-verified`. All eleven lanes passed:
  analyzer, runtime, default/adapter/native xUnit, FoxRun panel, Phase179,
  Phase181, MCAP, packages, and boundary. Default and adapter xUnit each
  passed 1331 tests, native passed 1495, and the panel passed 41 tests.
- The earlier valid full CI baseline remains
  `build/ci/phase184-polish-verified2`. All eleven lanes passed: analyzer,
  runtime, default/adapter/native xUnit, FoxRun panel, Phase179, Phase181,
  MCAP, packages, and boundary. Default and adapter xUnit each passed 1328
  tests, native passed 1492, and the panel passed 41 tests.
- An extra direct `--phase147` probe passed its generated-source literal,
  determinism, and TopicMember checks 1-13, then hit the pre-existing stale
  check 147-14 because it expects registry name `Phase 147` while the registry
  has used the full descriptive name since Phase183A. Neither file is changed
  by this branch, Phase147 is excluded from the default runtime suite, and the
  final eleven-lane CI is green. Keep any cleanup separate from Phase184
  review-polish scope.
- `build/ci/phase184-polish-verified` ran under
  `CodexSandboxOffline` and produced environmental NuGet/ownership/provenance
  failures. It is invalid evidence and must never be cited as a product or
  code failure.
- Future complete CI runs must execute outside the offline sandbox. Preserve
  their run root and lane logs; do not repair code from a sandbox-only
  environmental failure.
- Independent final review of Phase184-C returned `APPROVED` with no
  P1/P2/P3 finding for the QoS scope. The post-review Polish diff is covered by
  its focused tests and the fresh non-sandbox full CI above.
- Typed FoxRun MessagePack is deliberately deferred to independent Phase185:
  `Plan/185_PHASE185_FOXRUN_TYPED_MESSAGEPACK_PLAN.md`.
  The locked decision is Option A: after Phase184 is completely finished,
  `FoxRunEncoding.MessagePack` must support Publish, Subscribe, and
  PublishAndSubscribe in one vertical closure, matching the directional
  availability of Protobuf/JSON.
- Phase185 cannot start until Phase184-A through Phase184-G are complete and
  the user-performed `PHASE184G_WINDOWS_LOCAL_EDITOR_PASS` is recorded. The
  Phase185 Plan is drafted and locally path/boundary-audited; its independent
  review line remains pending because the extra review agent was stopped to
  avoid needless document-edit permission prompts.
- Remaining implementation order is 184-E bounded stream, 184-F
  migration/validation, then 184-G automated and user-owned manual
  acceptance. Do not fold MessagePack into any of them.

## Historical Handoff: Phase184-C In-Progress Snapshot (Superseded)

The section below preserves the pre-commit implementation snapshot for
diagnostic history. It is superseded by the current handoff above and must not
be used as a resume checklist.

```text
feat(184c): align FoxRun QoS with ROS 2 policies
```

### Product decision and Zenoh boundary

- FoxRun exposes only portable, official ROS 2 QoS vocabulary:
  `Default`, `SensorData`, `SystemDefault`; `Reliable`/`BestEffort`,
  `Volatile`/`TransientLocal`, `KeepLast`/`KeepAll`, and explicit depth.
- DDS and Zenoh receive the same fully resolved portable contract. Native
  R2FU passes it through ros2cs/rcl; the selected RMW performs its own mapping.
  Do not add Zenoh-specific `Priority`, `CongestionControl`, cache, router, or
  storage properties to `[FoxRun]`, and do not add distro/RMW-name branches.
- `SystemDefault` and `KeepAll` must remain real transport values rather than
  being rewritten to the Default/KeepLast profile. The same applies to the
  ROS 2 Bridge U2R2 frame and sidecar publisher construction.
- Reference checked for the boundary:
  `https://github.com/ros2/rmw_zenoh/blob/rolling/docs/design.md`.

### Historical implementation snapshot

- Added the 1-based public enums `FoxRunQosProfile`,
  `FoxRunQosReliability`, `FoxRunQosDurability`, and `FoxRunQosHistory`.
  Zero remains the intentional attribute-omission sentinel.
- Added immutable `FoxRunResolvedQos`,
  `FoxRunRos2QosProfileResolver`, Manager-side
  `FoxRunQosProfileSettings`, and initial resolver tests.
- Extended `[FoxRun]`, `[FoxRunMessage]`, Roslyn/reflection lowering,
  descriptor/manifest models, schema info, and source emitters with:
  `QoS`, `Reliability`, `Durability`, `History`, and `Depth`, including named
  argument presence.
- Added FOXRUN613/614 fail-closed QoS validation. The old unshipped FOXRUN213
  implementation was removed and its ID reserved in the analyzer ledger.
- Native subscription and custom-publisher contracts now resolve one
  `FoxRunResolvedQos`; the R2FU mapper explicitly maps every official axis.
- Native publish, Native subscribe, and Bridge profile defaults are captured
  inside their owning directional session snapshots.
- Manager Inspector work is partially migrated to the official three-profile
  popup plus Advanced override toggles. Existing serialized Native and Bridge
  presets have one-way hidden-field migrations.
- The old public Bridge preset implementation
  `Runtime/Ros2Bridge/Ros2BridgeQosProfile.cs` and its meta are currently
  deleted in the dirty tree. Bridge frames now carry `FoxRunResolvedQos`.
- U2R2 Unity frame JSON now writes:
  `profile`, `reliability`, `durability`, `history`, and `depth`.
- Maintained Phase179/181 source samples and manual scripts were changed from
  `Ros2Qos = FoxRunRos2QosPreset.*` to `QoS = FoxRunQosProfile.*`.
  The imported sample copies and generated Unity fallback files have not yet
  been regenerated/synchronized.

### Historical incomplete work (completed by `dcda6a9c4`)

1. `Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp`
   is still on the old reliability/durability/depth-only contract. Extend its
   frame, parser, validation, reuse signature, logs, and `rclcpp::QoS`
   construction for all five fields. Preserve real System Default and Keep
   All values.
2. Most tests still construct the old `FoxRunRos2QosPreset`,
   `Ros2BridgeQosProfile`, or `ros2Qos:` APIs. Migrate them to the official
   value object; do not add a compatibility overload merely to make them
   compile.
3. The old public files
   `FoxRunRos2QosPreset.cs` and `FoxRunRos2QosResolver.cs` still exist only
   because tests and tracked generated Unity sources reference them. Delete
   both files and metas only after those references reach zero.
4. Update Bridge writer/parser tests and add explicit coverage for Default,
   Sensor Data, System Default, Keep All, non-default Keep Last depth,
   invalid depth/history pairs, and topic reuse with different QoS.
5. Migrate the custom typesupport Inspector/preflight and native diagnostic
   tests to official fields. The product code was changed, but is not yet
   compiled by a green full test project.
6. Run `python Scripts/samples/sync_ros2_samples.py --apply` after package
   sample sources stabilize; never hand-copy imported samples.
7. Regenerate tracked Unity FoxRun source/descriptor/schema artifacts through
   the repository/Unity generator after the API compiles. Do not manually
   patch generated files as the final solution.

### Historical verification state

- The first focused command initially failed because the new serializable
  settings used Unity attributes in the .NET-only test surface. That was fixed
  with `UNITY_5_3_OR_NEWER` guards.
- The latest command was:

  ```powershell
  dotnet test Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj `
    --no-restore `
    --filter "FullyQualifiedName~FoxRunRos2QosProfileResolverTests" `
    --verbosity minimal
  ```

- It now reaches test-source compilation but fails on the expected unfinished
  migration: old session-policy constructors, old generation-model named
  arguments/constants, old Bridge profile types, and old Native QoS tests.
  Do not describe 184-C as compiling or passing yet.
- Keep all ad-hoc output under the repository `build/` root. The test project
  already routes outputs under `build/Tests`; preserve that layout.

### Historical resume sequence (completed)

1. Run `git status -sb --untracked-files=normal` and confirm HEAD/branch.
2. Finish the U2R2 C++ portable QoS mapping.
3. Use the compiler error list to migrate production-adjacent generation,
   session, Bridge, Native mapper, and Inspector tests in bounded groups.
4. Remove the old FoxRun/Bridge QoS public models only when `rg` proves no
   maintained source or test references remain.
5. Synchronize samples and regenerate Unity artifacts.
6. Run focused resolver/generator/Bridge/Native tests, then default,
   adapter, and native compile lanes; run Phase179/181 regressions before the
   dedicated 184-C commit.

## Historical Phase184-G Acceptance Outline (Superseded by Current Handoff)

- Do not begin Phase184-G until Phase184-C through Phase184-F are separately
  committed. Phase184-C is complete; Phase184-D through Phase184-F are pending.
- The approved execution plan is
  `Plan/184G_PHASE184G_AUTOMATED_BATCH_AND_FOCUSED_MANUAL_ACCEPTANCE_PLAN.md`.
- A later window owns all automatic work: five deep Unity Editor Batch cases,
  the four Phase181 Windows runtime/RMW rows, dedicated Unity-log review,
  cleanup proof, and the final full CI run.
- That window must stop at `AUTOMATION_READY_FOR_MANUAL`; it must not ask the
  user to diagnose a failing automatic case or label Batch evidence as manual
  evidence.
- Only after every automatic gate passes, hand the user exactly two interactive
  Play Mode suites:
  1. Jazzy/FastDDS for Inspector/profile/fanout/QoS/degraded behavior.
  2. Lyrical/Zenoh for bounded 640 Hz stream behavior, RMW neutrality, and the
     separate ordinary full-duplex origin probe.
- Final `PHASE184G_WINDOWS_LOCAL_EDITOR_PASS` requires both user-performed
  suites. Windows Editor evidence remains distinct from Player/Linux claims.

## Latest Handoff: R2FU v0.9.0 Import and Phase181 Windows-local Matrix

### Release and import baseline

- The three Windows x86_64 standalone R2FU v0.9.0 ZIPs are published and
  imported on `feature/183b-r2fu-v0.9.0-runtime-refresh`. The source tags are
  `ros2cs` `4252e38a5bafe6102c3f8f3dcb5cb591492ba087`, R2FU
  `6ae8e285115e7ca72c48932df5d673f512b56dd7`, and release orchestration
  `2471b8fbe2a263c123887463e1fa999546bac6c0`.

  | Distro | ZIP SHA-256 |
  | --- | --- |
  | Humble | `6937f348b2abdf40614379173bb81ba55090dc1541cab616d1a0f1e248ceb5b0` |
  | Jazzy | `4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d` |
  | Lyrical | `b31f12cccd2c702ec18c5f5ededce9239d8a2bbe244d54b5526606a96a3a5b71` |

- GitHub's R2FU v0.9.0 Release has exactly nine assets: one ZIP, SHA sidecar,
  and manifest per distro. Their remote ZIP digests match the local values.
  Runtime/add-on validators passed Humble `212/155`, Jazzy `215/157`, and
  Lyrical `232/156` checks respectively.
- ros2cs v0.9.0 carries the real runtime fixes: extended Windows native-library
  loading, no native loader unload from a finalizer, child-process finalizer
  probe coverage, and the expected Lyrical direct-spin fallback at `Info`
  level. No new ros2cs or external R2FU defect was discovered during import.

### Phase181 evidence, fixes, and boundary

- The v0.9.0 Windows-local Unity Editor Batch matrix passed all four rows:
  Humble/FastDDS, Jazzy/FastDDS, Lyrical/FastDDS, and Lyrical/Zenoh. Every
  summary reports the exact outer PASS, `unityBatchExitCode=0`, matched digest
  and RMW, graph evidence, outbound/inbound/remote-origin evidence,
  same-origin suppression, nullable-empty DTO evidence, terminal Unity PASS,
  and clean stop. The local report is
  `Developer/157 Phase181 Custom ROS2 Interface Batch Acceptance.md`.
- FastDDS graph baseline independently passed with delivery, publisher, and
  subscription visibility all true before the matrix.
- The fresh full local CI run `build/ci/22768-7b66dde4` passed all eleven
  lanes: analyzer, runtime, xUnit default/adapter/native, FoxRun panel,
  Phase179/181 ROS2 regressions, MCAP conformance, packages, and boundary.
- `f900e1e33` fixes downstream Unity2Foxglove concerns only; do not transfer
  them into ros2cs/R2FU:
  - extended-path file reads for custom typesupport closure verification and
    Zenoh session-template loading;
  - retention of the short peer workspace alias until the generated Python
    typesupport worker stops;
  - one bounded Batch Play retry after Unity regenerates/compiles schema code.
- The 183B worktree was restored to `lyrical + fastdds` after the Zenoh row.
  Temporary ROS junctions, `subst` mappings, Unity processes, and owned Zenoh
  router processes were verified absent. Do not commit generated `build/`,
  package `bin/obj`, ZIPs, or Unity transient artifacts.
- Scope remains Windows-local Unity Editor only. Windows Player and Linux-peer
  matrix cells remain **PENDING**. Historical human-driven v0.8.3 screenshots
  are not v0.9.0 evidence.

### Next ros2cs/R2FU decision

- No release-blocking ros2cs or external R2FU code change is queued from this
  import. A future isolated lifecycle effort may add repeated Unity
  Init/Shutdown/Play-Exit soak coverage and revisit Lyrical `rcl_wait` only
  when its upstream stability can be demonstrated. Do not remove the current
  direct-spin fallback merely to silence logs.
- A later release may make the four-row downstream Batch matrix an optional
  release-orchestration consumer gate. That belongs to integration/release
  tooling, not ros2cs or R2FU runtime code.

## Historical Handoff: Phase181 Custom FoxRun DTO To ROS2 Interfaces Code Complete

Phase181 A-F now form one committed serial chain on the local feature branch:

```text
6c7ff52d feat(181a): add FoxRun custom ROS2 DTO schema model
e0658d59 feat(181b): generate static FoxRun ROS2 interface package
c485e09f feat(181c): add FoxRun custom ROS2 typesupport add-ons
bd813a7c fix(181c): repair generated typesupport catalogs
1bb885d4 feat(181d): add typed FoxRun custom ROS2 transport
25b6f7cf feat(181e): add custom ROS2 typesupport preflight
42ebb6f2 feat(181f): add custom ROS2 interop acceptance gate
```

- The Phase181 integration commit originated on `feature/181-f-review-polish`;
  at this historical handoff, follow-up work was on
  `feature/181-f-interface-identity-polish`, and neither branch was pushed or
  merged.
- The final commit closes the external-review fixes: the signed analyzer DLL
  was rebuilt, static package identity is lock-derived instead of hardcoding
  `_v1`, replay suppresses native bus fanout, root array DTOs retain a valid
  unsupported-shape identity, and all Phase181 Python fixture scratch output
  stays under `build/Tests/Phase181` rather than `D:\\bin` or `D:\\obj`.
- All build, restore, test, source-generator, and fixture scratch output must
  stay below this repository's explicit `build/` root. For ad-hoc dotnet work,
  pass `BaseOutputPath`, `BaseIntermediateOutputPath`,
  `MSBuildProjectExtensionsPath`, and `RestoreOutputPath` below that root; do
  not use a machine-root fallback or a machine-specific blacklist.
- The package `Samples~/FoxRun Custom ROS2 Interface` is synchronized into the
  imported Unity demo sample copy. Keep this through
  `python Scripts/samples/sync_ros2_samples.py --apply`; do not hand-copy it.
- Fresh full local CI passed from run root `build/ci/26864-7ba4f651`:
  analyzer freshness, default/adapter/native dotnet lanes, Phase179 and
  Phase181 ROS2/tooling regressions, typesupport validation for all four
  distro/RMW rows, package/boundary validators, and MCAP differential
  conformance.
- The Windows-local Editor manual matrix follows the established Phase179
  model and must use the repository junctions under `ros2-windows/`, not a
  Linux peer. From `Scripts/smoke/ros2/`, run one no-argument wrapper, wait
  for the String-publisher prompt (up to 300 seconds), then enter Unity Play
  Mode:
  `phase181_humble_fastrtps_acceptance.py`,
  `phase181_jazzy_fastrtps_acceptance.py`,
  `phase181_lyrical_fastrtps_acceptance.py`, or
  `phase181_lyrical_zenoh_acceptance.py`.
- Each local PASS is limited to Windows ROS2 -> selected RMW/topology -> Unity
  Editor generated custom DTO apply/echo loop. Linux-peer and Windows-Player
  certification remain separate matrix cells and must be recorded PENDING
  until actually executed; never promote a local PASS to either claim.
- That historical worktree protected
  `Unity2Foxglove/Assets/Scenes/Phase179FoxRunRos2NativeSubscribeAcceptance.unity`.
  Its current status must always be established from Git before any action.

## Historical Follow-up: Phase181 Interface Identity Polish

- `feature/181-f-interface-identity-polish` was a narrow serial follow-up to
  `42ebb6f2`; at this handoff it was not pushed or merged.
- The optional Editor selection transaction and Player-safe native catalog
  registry must both use the ROS-free
  `FoxRunRos2InterfaceIdentity.TryParseRosPackageRevision` implementation.
  Do not reintroduce local `_vN` parsing or ROS-package grammar copies: they
  had already drifted on the core 255-character package-name limit.
- `FoxgloveLogHub` replay behavior is deliberate and covered: replay
  suppression prevents both WebSocket output and Phase181 native typed-bus
  fanout. Do not reopen this as an accidental regression without a new product
  decision.
- Unity 6000 generated three two-line `.meta` files for the imported Phase181
  sample. This shape is valid; validate GUID format/uniqueness and track those
  asset metas with the sample rather than adding a MonoImporter-shape rule.

## Historical Handoff: Phase180 Data Transport Inspector And Directional Coordinates Complete

Phase180 is merged on public `main` through PR #295:

```text
53d7edd3 Merge pull request #295 from JianbinLiu-CFLab/feature/180-finalization
71042048 test(180): align bridge inspector validations
b02bdfb8 test(180): align MCAP direction validation
f3c649e7 feat(180): add directional transport coordinates
```

Phase180 product and architecture boundary:

- `Data Transport` is a top-level Manager Inspector workflow at the same level
  as `MCAP Record & Replay`. It owns nested `Publish Data` and `Subscribe Data`
  workflows. Do not return them to separate top-level sections or mix transport
  controls into MCAP UI.
- `ROS 2 Native Runtime (R2FU) — Shared` is conditional: it appears only when
  native output or native subscription policy creates R2FU demand. It is shared
  infrastructure for both directions, not a permanent global configuration
  panel and not a Subscribe-only setting.
- Publish destinations remain independently selectable (Foxglove WebSocket,
  ROS 2 Native (R2FU), and ROS 2 Bridge). The bridge details remain under the
  enabled `ROS 2 Bridge Output` subsection inside `Publish Data`.
- Output and input coordinate modes are intentionally independent. Output
  means Unity -> external transport; input means external transport -> Unity.
  Do not collapse them into a global coordinate setting: a Unity-to-external
  publish path and an external-to-Unity subscription path have opposite
  conversion responsibilities.
- MCAP records the external boundary representation. Output records contain
  the converted external payload; input records retain the received external
  payload before Unity-side conversion. Direction and coordinate-mode channel
  metadata make that distinction explicit. Do not double-convert replay data
  or infer one direction's coordinate mode from the other.
- Inspector byte quantities use human-facing KB/MB presentation while runtime
  policy stays byte-precise. Do not expose MiB-only vocabulary in this workflow
  unless a future product requirement explicitly changes the UI convention.
- Existing ROS2 Native publish QoS remains publisher-owned; do not invent a
  Manager-global native publish QoS just to make the Publish and Subscribe
  panels superficially symmetric.

Validation and acceptance:

- PR #295 passed `check`, `analyzer-freshness`, `test`, `optional ROS2 adapter
  gate`, and `optional ROS2 Native gate`.
- The local focused validation passed Phase180 unit tests (11), Phase180
  runtime validation (29 checks), Phase24D MCAP validation (23 checks), and
  the full runtime validation suite after legacy Inspector assertions were
  aligned with the new partial-file layout.
- The user manually accepted the complete Phase180 Inspector checklist:
  conditional Runtime visibility, JSON/Protobuf round-trip restoration, bridge
  QoS placement, QoS and KB/MB presentation, session freeze behavior, and
  Play-Mode exit lifecycle.

## Historical Handoff: Phase179 FoxRun Native ROS2 Subscribe Complete

Phase179 is merged on public `main` through PR #294:

```text
c93fcae6 Merge pull request #294 from JianbinLiu-CFLab/feature/179-e-local-acceptance-staging
f1ed9d8d fix(179e): repair optional lane CI harnesses
8cc1dba8 test(179e): document local acceptance helpers
7d54440e fix(179e): stage local ROS2 acceptance publishers
```

The complete A-E serial history is preserved below that merge, beginning with
`7bd69847 feat(179a): add FoxRun subscription provider policy` and ending with
the Phase179-E acceptance and CI fixes. Do not recreate those branches for
follow-up work; branch from current `main`.

Phase179 product boundary:

- `Subscribe Data` now has a third provider, `ROS2 Native (R2FU)`, for direct
  communication with native ROS2 runtimes. It is not Foxglove communication,
  is not Foxglove `cdr`, and is not necessarily DDS-backed: Lyrical/Zenoh uses
  `rmw_zenoh_cpp` with an explicit topology.
- Provider selection and WebSocket wire encoding are independent axes.
  `Ros2Native` currently requires `FoxRunMode.SubscribeOnly`; contradictory,
  unsupported, or unavailable declarations fail closed and never fall back to
  WebSocket/JSON/Protobuf.
- Subscription policy is immutable for one enabled session. Inspector changes
  take effect only after subscriptions are disabled and re-enabled; session
  changes rebuild router registrations so inherited contracts cannot retain a
  stale provider.
- Supported Phase179 native message types are existing packaged ROS2 types,
  including `std_msgs/msg/String`, `geometry_msgs/msg/Twist`,
  `sensor_msgs/msg/Joy`, and `sensor_msgs/msg/Imu`. Arbitrary FoxRun DTO to ROS2
  message generation remains Phase181 work and must not be smuggled into
  Phase179 maintenance.
- The native callback owns only a bounded deep copy. Borrowed ros2cs message
  graphs never escape the callback, callbacks call no Unity API, and Unity
  fields are applied on the main thread through a latest-wins slot.
- Runtime/RMW capability is data-driven. Shared code must not branch on Humble,
  Jazzy, or Lyrical names; runtime packages describe supported communication
  modes and RMW implementations.

Final validation and acceptance:

- PR #294 passed `check`, `docs`, `analyzer-freshness`, `test`,
  `optional ROS2 adapter gate`, and `optional ROS2 Native gate`.
- The final targeted CI repairs passed the Zenoh topology regression and all 21
  `RuntimeHarnessTests` in the default, adapter, and native lanes. The full
  local `run_ci.py` was not rerun after the final small fixes at the user's
  request; the fresh remote workflow is the complete merge gate.
- All four Windows-local Editor rows passed: Humble/FastDDS,
  Jazzy/FastDDS, Lyrical/FastDDS, and Lyrical/Zenoh. Evidence is in
  `Developer/156 Phase179 Windows Local ROS2 Native Subscribe Acceptance Matrix.md`
  and `Developer/Images/Phase179_*_Windows_Local_Editor_PASS_20260717.png`.
- That local matrix proves the repo-local Windows ROS2 publisher -> selected
  RMW/topology -> generated Unity subscription -> deep copy -> main-thread
  apply loop. It does not by itself certify a Linux peer or Windows Player.

Operational reminders from acceptance:

- Use the no-argument wrappers in `Scripts/smoke/ros2/` and enter Play Mode
  after the helper reports that the String publisher is waiting. The helper
  stages String first to establish a correlation token, then starts the
  tokenless Twist/Joy publishers.
- The Zenoh wrapper owns and cleans up only its own router. Do not kill external
  topology processes, and tolerate the process-exit race between `poll()` and
  process-group signaling.
- Expected R2FU assembly-reload lock and orderly shutdown transitions are info
  logs, not warnings. Warning/error levels are reserved for degraded or failed
  behavior.

## Historical Handoff: Phase175 Typed FoxRun Wire Contract Complete

Phase175 is merged on public `main` through two serial PRs:

```text
7c94b79e Merge pull request #289 from JianbinLiu-CFLab/feature/175-c-manual-acceptance
7c7b8780 Merge pull request #288 from JianbinLiu-CFLab/feature/175-c-review-polish
1268a522 test(175c): add FoxRun wire acceptance probes
ab383769 fix(175c): polish Protobuf wire diagnostics
```

Serial integration details:

- Parent PR #288 merged the full typed FoxRun Protobuf/JSON contract chain and
  the polish commit. Remote checks `docs`, `test`, `check`, and
  `analyzer-freshness` passed.
- Child PR #289 carried only the Pbuf/JSON manual acceptance probes. After the
  parent merged, its base was retargeted to `main`, the branch was updated from
  `main`, and the same four remote checks passed before merge.
- Both serial branches were deleted locally and remotely after merge. Do not
  recreate them merely to add follow-up work; start a new feature branch from
  current `main`.

Phase175 behavior and acceptance:

- `FoxRunProtobufWire.WriteTag` now shifts a legal large field number as
  `ulong`; signed `int` shifting had emitted a non-canonical tag for field
  `276,595,399` and caused Foxglove `invalid tag encoding`.
- Inbound mismatch diagnostics name both the expected generated encoding and
  the client-advertised encoding. Do not add payload sniffing or JSON fallback.
- Foxglove Desktop's normal Publish panel advertises `json` even when a
  Protobuf descriptor is selected. It is valid for explicit JSON topics but is
  not a Protobuf inbound acceptance client.
- Use `Scripts/smoke/websocket/phase175_protobuf_inbound_publish.py` for the
  direct Protobuf client acceptance path. It explicitly advertises `protobuf`,
  sends the field-1 float payload, and waits for the Protobuf echo.
- `Phase175ProtobufManualAcceptance` validates explicit Pbuf inbound and
  bidirectional state. `Phase175JsonManualAcceptance` validates a
  source-owned explicit JSON topic while the Manager default remains Protobuf.
- The Manager captures its wire policy at session start. Inspector changes made
  during Play Mode are temporary and must not mutate active topic contracts.

Manual evidence is local-only in:

```text
Developer/153 Phase175 Protobuf Outbound UI Smoke Acceptance.md
Developer/Images/Phase175_*.png
```

The recorded complete scope includes Protobuf-default output, direct binary
Protobuf inbound/echo, Manager-selected JSON, session-policy freeze, and an
explicit JSON override while the Manager default is Protobuf.

Fresh validation completed before merge:

- `dotnet test Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj --no-restore`
  reported 794 passed.
- `--phase175a`, `--phase175b`, and `--phase175c` reported 5, 3, and 12 checks
  passed respectively.
- Source-generator build completed with 0 warnings and 0 errors.
- `python Scripts/release/run_ci.py` passed `analyzer`, `dotnet`, `packages`,
  and `boundary`.
- `git diff --check` passed.

The Phase174 plans remain ignored local operator notes. Read them only if the
user explicitly resumes that work; they are not current branch state or public
validation evidence.

## Historical Handoff: Phase164-56 Through Phase164-59 Complete

Phase164 optimization work through PR #233 has been merged. The repository is
back on `main` at `2d8ddcb5`, synced with `origin/main`. The feature branch
`feature/164-59-validation-naming-guards` was merged and deleted remotely; the
local Phase164 feature branches from the last chain were cleaned.

Recent merged commits now on `main`:

```text
2d8ddcb5 Merge pull request #233 from JianbinLiu-CFLab/feature/164-59-validation-naming-guards
9fcdfa5e fix(164-59): enforce validation naming guards
264ce83e fix(164-58): describe validation registry entries
66ab039c fix(164-57): optimize test harness paths
572457b5 fix(164-56): optimize latest runtime validations
f071a9c2 Merge pull request #232 from JianbinLiu-CFLab/feature/164-55-runtime-validation-optimization-fixes
45db159c fix(164-55): optimize remote timeline hot paths
207408bd fix(164-54): optimize phase138 sensor validations
76cfad47 fix(164-53): optimize review governance validations
eb68deb0 fix(164-52): optimize real project r2fu smoke paths
```

Validation completed before PR #233 merge:

- `dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --phase134-32`
  reported `Phase 134-32: 23 checks passed`.
- `dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --phase134-33`
  reported `Phase 134-33: 26 checks passed`.
- `dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --phase164-59`
  reported `Validation naming guardrails: 11 checks passed`.
- `python -m unittest Scripts.package.regression_checks.test_validate_unity_package`
  passed.
- `python Scripts/package/validate_unity_package.py` passed.
- `python Scripts/release/run_ci.py` passed all CI checks.
- GitHub PR #233 checks passed: `analyzer-freshness`, `check`, `docs`, and
  `test`.

Phase164-56 through Phase164-59 finished the queued validation optimization and
naming cleanup chain:

- Phase164-56 optimized the latest runtime validation paths.
- Phase164-57 optimized test harness paths.
- Phase164-58 changed registry display names from generic `Phase N` names to
  descriptive names where clean `// Purpose:` metadata exists.
- Phase164-59 added guardrails so future registry names cannot be bare phase
  numbers and future runtime validation source files cannot use new
  `PhaseNNNValidation.cs`-style filenames beyond the current legacy cutoff.
- A few older registry self-checks were updated to look for the new descriptive
  registry names.

What landed recently:

- Phase153-154:
  - FoxRun topic contracts, local topic bus, and aggregate JSON message support.
  - Manual acceptance evidence is recorded in
    `Developer/137 Phase153-154 FoxRun Topic Bus Aggregate Acceptance.md`.
- Phase155-156:
  - Additive FoxRun sink fanout and optional ROS2/R2FU sink boundary.
  - Manual acceptance evidence is recorded in
    `Developer/138 Phase155-156 FoxRun Multi-Sink ROS2 Boundary Unity Acceptance.md`.
- Phase157:
  - FoxRun `SubscribeOnly` and `PublishAndSubscribe` inbound modes.
  - Loopback-first inbound security gate, typed JSON decoding, allowlisted input
    dispatch, bounded manager queue, and local FoxService call helper.
  - Manual acceptance evidence is recorded in
    `Developer/139 Phase157 FoxRun Inbound Unity Acceptance.md`.
- Phase158:
  - Research spike / ADR-style recommendation path is complete. Do not add core
    DDS, Zenoh, ROS2, or middleware-native product code unless a later plan
    explicitly promotes a track into implementation.
- Phase159:
  - Repo-local entrypoint hygiene is merged. Tracked scripts should use
    `ros2-windows/ros2_<distro>` and `r2fu-runtime-artifacts/<distro>` instead
    of hardcoded machine paths.
- Phase160:
  - Humble runtime import is merged. Humble acceptance used the dedicated
    Phase160 RViz helper path, not a reused Phase138U wrapper. Humble is
    FastRTPS-only.

Historical Phase161 context:

- Plan: `Plan/161_PHASE161_R2FU_JAZZY_WIN64_RUNTIME_REFRESH_PLAN.md`.
- Goal: refresh the existing Jazzy runtime package from the pinned handoff
  artifact while preserving the asset-critical baseline and FastRTPS-only
  boundary.
- Pinned Jazzy artifact hash currently expected by validation:
  `df4806b750435b3a1252f39b46dd2e4e60ddc0eb6ac57989bcf00adb23fe29f3`.
- The refreshed Jazzy package must keep `rosgraph_msgs_assembly.dll` and
  native `rosgraph_msgs` FastRTPS support. Do not accept "new artifact truth"
  if it drops R2FU assets required by `ROS2Clock.cs` or packaged R2FU scripts.
- Unity sample project currently resolves exactly
  `dev.unity2foxglove.ros2forunity.runtime.jazzy.win64` in
  `Unity2Foxglove/Packages/manifest.json` and `packages-lock.json`.
- Validation already run successfully before the latest hang investigation:
  - `python -m py_compile Scripts/ros2forunity/windows/jazzy/build_r2fu_runtime_package.py Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py`
  - `python Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py`
    reported `Runtime package validation passed: 208 checks.`
  - `dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --phase161`
    first reported `Phase 161: 42 checks passed`, then after the native bridge
    lifecycle source-shape check was added reported `Phase 161: 50 checks
    passed`.
  - `dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --phase115h`
    reported `Phase 115H: 43 checks passed`.
  - `git diff --check` passed with only CRLF warnings for the two runtime
    inventory JSON files.

### Phase161 Unity Hang Debugging Snapshot

Unity froze twice during Jazzy/R2FU play-mode shutdown or restart. Do not treat
this as a QoS or DDS data-path problem without new evidence.

Observed evidence:

- First freeze: multiple `Unity.exe` processes were left alive with stale
  `Editor.log`. The tail showed shutdown/domain reload, backup scene load, then
  `ROS2.NativeRcl:.cctor` -> `ROS2.ROS2ForUnity:.ctor()` ->
  `ROS2UnityComponent.Ok()` -> `Ros2ForUnityTransformNativeBridge.TryGetRos2Unity()`
  from a transform publisher callback.
- A mitigation was applied so native bridge callbacks no longer create
  `ROS2UnityComponent` or first-initialize ROS2. The bridges now try to prewarm
  ROS2 from their own `Update()` and callbacks only use an already-ready
  runtime.
- Second freeze after that mitigation: `Editor.log` showed Unity had loaded
  `Temp/__Backupscenes/0.backup`, then `Ros2ForUnityImuNativeBridge.Update()`
  called `EnsureRos2UnityReady()`, which called `ROS2UnityComponent.Ok()` and
  reached `ROS2ForUnity.cs:560` (`Ros2cs.Init()`).

Current root-cause hypothesis:

- Unity can still call bridge `Update()` during Play Mode shutdown/domain
  reload while `Application.isPlaying` has not yet become a reliable shutdown
  signal. The backup scene phase (`Temp/__Backupscenes/0.backup`) must be
  treated as a native R2FU shutdown/no-init window.
- The next fix should prevent any R2FU native bridge prewarm from calling
  `ROS2UnityComponent.Ok()` during backup-scene/domain-reload shutdown, or
  otherwise gate prewarm to stable Play Mode scenes only.
- Do not keep layering QoS, DLL, or RViz changes for this hang. The latest stack
  is explicitly a lifecycle/init-during-shutdown path.
- After changing the lifecycle guard, verify by entering Play Mode with Jazzy
  R2FU native output enabled, observing READY logs, then exiting Play Mode. The
  expected result is no `Ros2cs.Init()` stack after `Temp/__Backupscenes/0.backup`
  and no orphaned Unity process pile-up.

## Session Bootstrap

At session start:

1. Read this file first.
2. Run `git status -sb --untracked-files=normal`. Avoid broad untracked scans
   that expand local junction targets such as `ros2-windows/`.
3. Check recent history with `git log --oneline --decorate -8`.
4. If the user names a phase, read the matching `Plan/` file before changing
   code.
5. If the user asks about validation, regressions, or previous debugging, read
   the matching `Developer/` report.
6. Before editing, identify the owning layer from the architecture rules below
   and inspect nearby implementation plus its existing tests. Do not solve an
   optional-package concern by adding a dependency to the core SDK.
7. For generator work, locate both Roslyn and reflection paths and their parity
   tests before changing either path.
8. Before any ad-hoc `dotnet` restore/build/run/test command, choose an
   explicit output root below this repository's `build/` directory and pass all
   four output/intermediate/restore MSBuild properties. Do not let a tool choose
   a machine-root or generic OS temporary fallback.
9. Treat this file's state section as a handoff, not truth. Git and the actual
   filesystem win when there is a conflict.

## Architecture Decision Rules

These are dependency and ownership rules, not suggestions. If a proposed
change conflicts with them, stop and either redesign it or update an approved
phase plan explicitly.

### Dependency Direction

```text
Unity2Foxglove project / samples / manual acceptance
        -> optional R2FU facade package
        -> distro-specific R2FU runtime package

Unity2Foxglove project
        -> ROS-free core SDK

core SDK <- optional R2FU facade package
core SDK must never reference the facade or a distro runtime
```

- Keep reusable contracts, pure policy, manifests, and ROS-free capability
  descriptions in `dev.unity2foxglove.sdk`.
- Keep `ROS2.*`, ros2cs, node/subscription ownership, native QoS adaptation, and
  RMW lifecycle code in `dev.unity2foxglove.ros2forunity` or a runtime package.
- Distro packages provide artifacts and capability metadata. Shared core/editor
  code consumes capabilities and must not grow `if (distro == ...)` branches.
- Optional integration from the core Editor uses a named, cached, auditable
  reflection seam when an assembly reference would invert dependencies. Keep
  complete type-and-assembly names visible; never split strings merely to evade
  a boundary scan.

### FoxRun Contract Semantics

- `FoxRunMode`, subscription provider, WebSocket encoding, and ROS2 QoS are
  separate axes. Do not overload an enum or infer one axis from another.
- Preserve serialized numeric values. `FoxRunMode = 0/1/2` is frozen. Diagnostic
  IDs are segmented: `FOXRUN001-199` publish, `FOXRUN200-399` SubscribeOnly,
  `FOXRUN400-599` PublishAndSubscribe, and `FOXRUN600+` cross-direction/system.
- Retired diagnostic IDs remain permanently reserved. Add migration entries to
  the append-only analyzer release ledger; never silently rewrite shipped
  history or reuse an old ID.
- Resolve declarations through typed result objects and stable diagnostic
  codes. Invalid serialized values, unavailable capabilities, contradictory
  provider/encoding pairs, and unsupported message shapes fail closed.
- A Manager session freezes provider, WebSocket encoding, QoS, copy budget, and
  apply-rate policy. Live security controls explicitly designed to remain live
  must not be accidentally moved into the frozen snapshot.
- `FoxRunSubscriptionSessionPolicy.Disabled()` contains inert placeholders.
  Consumers must check `SubscriptionsEnabled` before reading the other fields.

### Native Subscription Ownership and Threading

- ROS2 callbacks receive borrowed messages. Deep-copy the complete supported
  graph before returning, reject over-budget copies, and never retain a
  borrowed nested object or array.
- Callback threads may use atomics, owned managed/native message operations,
  and bounded diagnostics only. They must not access Unity objects, invoke Unity
  APIs, mutate Inspector fields, or throw into the ROS executor.
- Use latest-wins bounded storage for inbound values. Replaced pending values
  are disposed by the producer path; applied values are disposed on the main
  thread. Disposal can therefore occur concurrently and must be exactly once.
- Do not assume ros2cs top-level `Dispose()` cascades, is cheap, or is confined
  to one thread. Generated copy/dispose code must detach nested ownership and
  clean partial construction on every exception path.
- Stop is a state machine, not a single flag. Reentrant stop from an apply call,
  callback-in-flight teardown, pending/applied drain, subscription-token
  disposal, and node-lease release must all converge idempotently. Never block
  the Unity main thread waiting for a callback that may itself need main-thread
  progress.
- One host owns the shared node; bindings hold explicit leases. A node may be
  released only after tokens are rolled back/disposed and slot cleanup is
  complete. Zero eligible contracts means zero bindings and no node.

### Source Generation and Editor Fallback

- Roslyn generation is the authoring authority. Reflection fallback, manifest
  output, descriptor JSON, and generated source must agree on the same
  generation model; extend their parity tests with every contract field.
- Change `FoxRunRoslynRos2MessageShapeBuilder` and
  `FoxRunReflectionRos2MessageShapeBuilder` together. Use semantic type identity
  and assembly metadata, not substring matching such as type names containing
  `ROS2`.
- Partial classes leave no CLR metadata. Runtime reflection cannot truthfully
  detect `partial`; `AssumePartialWasEnforcedBySourceGenerator()` documents the
  boundary and FOXRUN001 is the real Roslyn enforcement. Do not replace this
  with another pretend runtime check.
- Keep loaded-assembly/type traversal centralized in
  `VisitLoadedFoxRunComponentTypes()` and member accumulation in
  `AddFoxRunMembers()`. Do not fork nearly identical scanners for FoxRun,
  services, manifests, or link preservation.
- Generated code must remain reflection-free in per-message paths. No
  `MakeGenericMethod`, runtime member lookup, or string-based dispatch in native
  callbacks.

### Runtime and Transport Portability

- Treat DDS and Zenoh as communication modes behind RMW capabilities. Never
  label an unknown RMW as DDS, and never assume every ROS2 distribution uses
  FastDDS.
- QoS mapping is explicit per portable preset. Do not silently downgrade
  reliability, durability, or history to make discovery pass.
- Helper scripts must use argument arrays, explicit environment dictionaries,
  bounded waits, and owned process groups. Never use `shell=True`, machine-wide
  process killing, or implicit external-router ownership.
- Cross-platform tests must exercise or explicitly mock both Windows and POSIX
  branches. A Windows-only fake process must not accidentally execute the POSIX
  `killpg` path in Linux CI.
- Keep optional test outputs isolated under
  `build/Tests/{default|adapter|native}`. If output layout changes, update every
  harness locator and retain a non-vacuous compile-surface check for each lane.

## Documentation Boundaries

Use these folders consistently:

- `Plan/`: implementation plans, technical design plans, and future-work phase
  plans. Use the existing uppercase underscore naming style, for example
  `138J_PHASE138J_CAMERA_ASYNC_JPEG_PIPELINE_PLAN.md`.
- `Developer/`: acceptance reports, manual validation notes, debugging reports,
  postmortems, and follow-up evidence.
  - Markdown notes in `Developer/` must use unique, ascending Arabic numeric
    prefixes such as `001 ...md`, `002 ...md`, with no duplicate numbers.
  - When renumbering `Developer/`, inspect each note's title and body first.
    Keep general/non-phase notes first, then sort phase-specific notes by the
    actual Phase number and suffix. If a title lacks a Phase but the note body
    clearly records one phase's implementation, acceptance, or debugging result,
    add that Phase to the title before sorting. Do not infer from incidental
    historical phase mentions in broad architecture or troubleshooting notes.
  - Normalize malformed phase ranges in `Developer/` titles, for example use
    `Phase106-110` instead of `Phase106110`, before assigning numeric prefixes.
  - Keep screenshots and other image evidence under `Developer/Images/`, not in
    the `Developer/` root. Reference them from notes as
    `Developer/Images/<file>`.
- `docs/`: public user-facing documentation and release documentation.
- `AGENTS.md`: private ignored local session bootstrap and handoff.

`Plan/`, `Developer/`, and `AGENTS.md` are not public CI evidence. Do not make
repository validation depend on them. Do not force-add ignored local notes
unless the user explicitly asks for that exact file to be committed.

## Project Hard Boundaries

These rules survived previous phase debugging and should not be removed during
handoff cleanup.

### Local Resource Entrypoints

- `r2fu-runtime-artifacts/` is the local entry point for optional ROS2 For Unity
  runtime artifacts. Keep its README in place; distro subdirectories such as
  `humble/`, `jazzy/`, and `lyrical/` are ignored local junctions or cache
  folders and must not be treated as source files to stage.
- Current local R2FU artifact junction targets are:
  - `r2fu-runtime-artifacts/humble` -> `D:\ros2unity\artifacts\ros2-for-unity\humble`
  - `r2fu-runtime-artifacts/jazzy` -> `D:\ros2unity\artifacts\ros2-for-unity\jazzy`
  - `r2fu-runtime-artifacts/lyrical` -> `D:\ros2unity\artifacts\ros2-for-unity\lyrical`
- `ros2-windows/` is the local entry point for plain Windows ROS 2
  distributions. Keep its README in place; its distro entries are junctions,
  not repository-owned resources.
- Current local ROS 2 Windows junction targets are:
  - `ros2-windows/ros2_humble` -> `C:\ros2_humble\ros2-windows`
  - `ros2-windows/ros2_jazzy` -> `C:\ros2_jazzy\ros2-windows`
  - `ros2-windows/ros2_lyrical` -> `C:\ros2_lyrical\ros2-windows`
- Do not copy full ROS 2 installs, generated R2FU runtime packages, ZIPs, DLLs,
  manifests, or native build outputs into these entry directories for commit.
  The entry directories are pointers for local scripts and operators.
- Keep the two README scopes separate: `ros2-windows/README.md` is pure ROS 2
  Windows install guidance and should not mention Unity2Foxglove, R2FU,
  FastDDS, Zenoh, or runtime package claims.
- Use stable GitHub release URLs in `ros2-windows/README.md`; do not save
  temporary signed `release-assets.githubusercontent.com` links as canonical
  references.

### Public Validation Boundary

- Public validation must run from a clean open-source checkout.
- Do not use `Plan/`, `Developer/`, `AGENTS.md`, or any ignored local note as
  repository validation evidence.
- Do not use local sibling repositories as validation evidence, especially
  `D:\ros2unity\ros2rc` or `D:\ros2unity\ros2-for-unity`.
- Do not read pre-existing `.gitignore`-covered files as validation evidence.
  If a test needs ignored output, generate it during that command and validate
  only the fresh output.
- Generated scratch/build roots are acceptable only when created by the command
  under validation.
- Manual or machine-local evidence may be summarized in `Developer/`, but tests
  must assert public repository behavior.

### Package Cleanliness Boundary

- `Packages/` is Unity package source, not a build output directory.
- Do not allow `dotnet build`, conformance runners, source generator builds,
  Node/Yarn tools, native builds, downloads, coverage, zips, or temp tooling to
  emit outputs inside package directories.
- Forbidden package outputs include `bin/`, `obj/`, `node_modules/`, `.dll`,
  `.exe`, `.deps.json`, `.runtimeconfig.json`, native build products, caches,
  and generated `.meta` files for those outputs.
- Repo-tool build, restore, test, conformance, generator, and fixture scratch
  artifacts belong below repo-level `build/`. Unity-managed artifacts belong in
  `Library/`/`Temp/`. Do not choose external or machine-root output paths by
  default; an external path requires the user's explicit per-operation approval.
- If Unity reports `CS0436`, `CS1705`, `ZstdSharp`, or
  `System.Runtime.Intrinsics` import/plugin errors, first check for leaked build
  artifacts under `Packages/`.

### Unity / R2FU Boundary

- The core SDK must remain ROS-free.
- R2FU is optional and must stay behind
  `dev.unity2foxglove.ros2forunity` and runtime package boundaries.
- Old direct imports such as `Unity2Foxglove/Assets/Ros2ForUnity/` are ignored
  local assets and must not be committed.
- Package-mode R2FU acceptance requires Unity to actually load the package
  runtime; changing files without loading the package is not acceptance.
- Do not claim ROS2/RViz2 live acceptance without external graph evidence:
  topic list/info, echo or publish, plus Unity Inspector/Console confirmation.
- On this Windows machine, do not rely on bare `ros2`. Use the pinned Jazzy
  Python/`ros2-script.py` path or project Python smoke helpers.
- Prefer Python acceptance helpers over durable PowerShell recipes. Do not add
  project-owned `.ps1` scripts under `Scripts/` unless the user explicitly
  reopens that path.

### Windows ROS2 / RViz2 Live Acceptance Pitfalls

These rules come from the Phase138L PointCloud2 Native DDS acceptance.

- Product PointCloud2 Native acceptance should use the normal Inspector path:
  enable `ROS2 Native (R2FU)` on `FoxgloveManager`, select
  `PointCloud2 Native` on `FoxglovePointCloudPublisher`, set the exact topic and
  frame id, and leave `Publish TF Anchor` enabled unless an external TF tree owns
  the frame. Do not ask normal product users to mount
  `Phase138VirtualLidarPointCloud2Smoke`; that component is diagnostic-only.
- Match external ROS2 commands to the Inspector topic exactly. In Phase138L the
  accepted product topic was `/unity/point_cloud2`; `/points` was only another
  possible user setting and should not be assumed.
- For the current R2FU product path, the built-in TF anchor publishes
  `tf2_msgs/msg/TFMessage` on `/tf` such as `map -> os_lidar`. Do not require
  `/tf_static` acceptance unless the R2FU QoS path has explicitly been upgraded
  and validated for static TF late-join behavior.
- RViz2 visual state alone is not enough evidence. RViz can keep showing an old
  point cloud while Unity is stopped, compiling, or after DDS discovery churn.
  Pair RViz with Unity Console/Inspector evidence and external ROS2 graph data.
- A good Phase138L live evidence set is:
  `ros2 topic list -t --no-daemon`, `ros2 node list --no-daemon`,
  `ros2 topic echo /tf tf2_msgs/msg/TFMessage --once --no-daemon`, and
  `ros2 topic echo <point-topic> sensor_msgs/msg/PointCloud2 --once --no-daemon`.
  Add `topic bw` or a custom subscriber rate probe when bandwidth/rate matters.
- Treat short `ros2 topic hz` failures on Windows/FastDDS as diagnostic, not the
  sole pass/fail gate. During Phase138L, `topic hz` briefly claimed the topic was
  not published while `topic list`, `echo`, `bw`, Unity READY logs, and RViz all
  proved the stream was live.
- If ROS2 CLI probes time out, first inspect for stale diagnostic processes:

```powershell
Get-CimInstance Win32_Process |
  Where-Object {
    $_.Name -match 'rviz2|python|ros2|static_transform' -or
    $_.CommandLine -match 'rviz2|launch_phase138l|static_transform_publisher|ros2-daemon|ros2-script'
  } |
  Select-Object ProcessId,Name,CommandLine |
  Format-List
```

- Clear stale `ros2-daemon`, RViz2, or helper script processes before blaming
  Unity or changing code. Do not kill Unity unless Unity itself is the suspected
  source.
- If Unity reports Play Mode was canceled because it is compiling or updating
  assets, wait for compile/import to finish and restart Play Mode before running
  ROS2/RViz2 acceptance.
- Expected lifecycle warnings during Play Mode shutdown are different from live
  runtime failures. Confirm whether the runtime had already been READY before
  treating an R2FU "runtime is not ready" warning as a blocker.
- For image-only WSL2 acceptance, the cleanest route is direct subscription,
  not a Windows-side bridge. Run Unity on Windows, then in WSL2 source Jazzy,
  set `RMW_IMPLEMENTATION=rmw_fastrtps_cpp`, `ROS_DOMAIN_ID=0`, and
  `ROS_AUTOMATIC_DISCOVERY_RANGE=SUBNET`, then verify
  `/unity/sensor/camera/image/compressed` with `ros2 topic echo ... --field
  format --once --no-daemon`. Saving one `sensor_msgs/msg/CompressedImage`
  frame to a JPEG from WSL2 is valid machine-local evidence that the Unity ->
  WSL2 DDS image path is healthy.
- Do not use Windows `image_transport republish` or a Windows Python
  CompressedImage-to-Image bridge as the primary Phase138M acceptance path.
  They can hang or poison local graph probes on this Windows/FastDDS setup.
- WSLg RViz2 and `image_view` GUI failures are not proof that Unity/R2FU image
  publishing failed. `[WARN:COPY MODE]`, non-interactive RViz windows, and
  `image_view` staying on `/image` were observed even when WSL2 could echo
  `format: jpeg` and save a valid Unity camera frame. Treat those as GUI/tool
  issues unless direct WSL2 subscription also fails.

### Git / Release Boundary

- Never stage or commit ignored operator notes unless the user explicitly names
  the file.
- Do not stage unrelated Unity `.meta` files.
- Do not lower acceptance criteria to match a failing test.
- Never push directly to `main`, even after local validation passes.
- All integration work must go through a branch and PR-style handoff: push only
  the feature/fix branch, then let the PR flow or an explicitly assigned
  external window perform the merge.
- Do not run `git push origin main` unless the user explicitly overrides this
  rule in the current conversation with those exact words.
- Release commits and PRs must not mention Codex, Claude, OpenAI, Anthropic, AI,
  or include bot co-author metadata.
- Before claiming a phase is complete, run fresh validation or state exactly
  what was not run.

## Historical Context: 139D

Plan file:

```text
Plan/139D_PHASE139D_UNITY_CURSOR_BRIDGE_PLAN.md
```

Phase139 is the end-to-end integration smoke path for the maze demo, MCAP
record/replay, Remote File loading, and Unity/Foxglove cursor synchronization.
This is historical context only; the current branch is `main` unless git says
otherwise. Do not resume 139D work unless the user explicitly asks.

139 handoff snapshot:

- `75e00bb fix(139b): support browser remote file preflight` is the latest
  commit. It only changes `RemoteMcapHttpRouter.cs` and
  `Phase139BValidation.cs`.
- Fresh validation before that commit:
  `dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --phase139b`
  passed with `Phase 139B: 36 checks passed`.
- `git diff --check` passed before the commit.
- Existing warnings during `dotnet run` are the known obsolete API/test double
  warnings and source-generator analyzer release tracking warnings. They were
  not introduced by `75e00bb`.
- The user manually validated that Foxglove Desktop can load the Remote File
  direct MCAP URL when the backend is running and the browser preflight/CORS
  behavior is correct.
- The user also manually validated the cursor bridge gate: Unity printed
  `[Foxglove] Replay cursor endpoint ready: http://127.0.0.1:8892/v1/replay-cursor`
  and later
  `[Foxglove] Replay cursor bridge received cursor from foxglove-unity-cursor-bridge ...`.
  That proves the Foxglove extension can reach Unity's localhost endpoint in
  the tested desktop setup.

139D caution:

- The most important current product decision is the ownership model:
  - `Enable Remote File URL` OFF: Unity is the replay owner. Existing MCAP
    Replay and Replay Auto Play should keep driving the Unity scene.
  - `Enable Remote File URL` ON: Foxglove should own the timeline. Unity should
    become a follower of Foxglove cursor/playback state.
- The user discovered that enabling `Replay Auto Play` at the same time as
  `Enable Remote File URL` caused two masters to fight. Treat this as the real
  root cause of the observed stutter/backtracking before changing lower-level
  cursor code.
- Do not overbuild the Foxglove extension workflow. The user expects the
  Inspector to expose the useful local URL/open controls and does not want a
  fragile hidden tool workflow.
- Do not claim full two-way synchronization complete unless the user repeats
  manual acceptance with:
  1. Remote File URL enabled,
  2. Foxglove loaded from the direct `.mcap` URL,
  3. the Unity cursor endpoint ready,
  4. the Foxglove extension forwarding cursor messages,
  5. Unity following Foxglove seek/play/pause without Replay Auto Play fighting
     it.
- If continuing 139D, first preserve the working pieces and make the
  `Replay Auto Play` / `Enable Remote File URL` mutual-exclusion behavior clear
  in Inspector and tests. Avoid speculative changes to `TickCoordinator` or the
  replay clock unless backed by a fresh, reproducible manual trace.

### Unity Asset / Meta Boundary

- Never hand-write or hand-invent Unity `.meta` GUIDs. Unity GUIDs must be
  exactly 32 hexadecimal characters. Invalid GUIDs cause Unity to ignore the
  corresponding asset, including `.cs` files, which means new partial classes
  will not compile into the assembly.
- When adding Unity package `.cs` files outside Unity, either let Unity generate
  the `.meta` files or generate them from a known-good script `.meta` template
  with a valid random 32-hex GUID and the `MonoImporter` block intact.
- Do not use semantic prefixes such as phase numbers in Unity GUIDs. Treat GUIDs
  as opaque random identifiers.
- Before diagnosing C# partial-method or missing-symbol errors after adding new
  Unity scripts, inspect Unity Console/`Editor.log` for asset import errors such
  as "does not have a valid GUID" or "asset file will be ignored". Fix import
  errors before moving C# methods around.
- For partial class refactors, first prove Unity imports every new partial file:
  the `.meta` must have a valid GUID, a `MonoImporter` block, and no Editor.log
  import rejection. Do not claim the refactor is healthy from `dotnet test`
  alone, because the .NET test compile surface can include files that Unity has
  ignored.

## Recent 138I Context

138I stabilized LiDAR/full-fidelity point clouds by protecting the main loop at
the source:

- Use per-`FixedUpdate` LiDAR raycast budget, not a fixed point-cloud Hz cap.
- Keep real source-driven point clouds from being overwritten by transform
  fallback frames.
- Exclude ego-vehicle/self colliders from LiDAR raycasts.
- Treat topic frequency badges as insufficient; inspect point counts, frame
  source, timestamps, and visible behavior.

Useful local reports:

```text
Developer/101 Phase138I OS2-128 Full Fidelity Throughput Debugging Report.md
Developer/102 Phase138I Stable Main Thread Protection Follow-Up.md
```

These are local evidence notes, not public validation dependencies.

## Code Smell Review Checklist

Before implementing or approving a non-trivial change, check the relevant
items below. A match is a prompt to investigate and add a focused test, not an
instruction to perform a broad speculative refactor.

- **Wrong-layer dependency:** core code references `ROS2`, ros2cs, a runtime
  package, or a distro-specific identifier.
- **Axis collapse:** provider, encoding, QoS, publish mode, or runtime demand is
  inferred from another setting instead of resolved independently.
- **Hidden fallback:** invalid/unsupported native behavior quietly becomes
  WebSocket, JSON, Protobuf, another QoS, or another RMW.
- **Split-brain session:** one consumer reads frozen session policy while
  another reads mutable Inspector values for the same decision.
- **Borrowed-data escape:** a callback object, nested array, or native handle is
  stored past callback return without an owned deep copy.
- **Unbounded work:** a callback/main-thread queue, retry loop, diagnostic log,
  copied message, or process wait has no explicit bound.
- **Teardown shortcut:** cleanup clears collections before resources finish
  draining, assumes no reentrancy, releases a node before endpoint cleanup, or
  waits synchronously across mutually dependent threads.
- **One-sided generator edit:** Roslyn, reflection fallback, manifest,
  descriptor, emitted source, or parity tests are changed without checking the
  corresponding paths.
- **Fake platform coverage:** a test passes because the host OS chooses one
  branch, while its process/file/path test double cannot support the other.
- **Vacuous optional lane:** adapter/native tests pass without compiling the
  intended optional surface, or two lanes share stale `obj`/output state.
- **String-obscured seam:** a reflection type name, diagnostic ID, or protocol
  identifier is concatenated to evade grep or validation.
- **Duplicated traversal/policy:** near-identical loops or switch tables drift
  between scanners, emitters, Inspector models, runtimes, or scripts. Extract a
  pure shared core when semantics are truly identical; keep transport-specific
  mechanics separate.
- **Placeholder leakage:** disabled/default placeholder values are displayed or
  acted on without first checking their validity state.
- **Expected lifecycle noise:** normal reload locks, orderly shutdown, or
  explicitly selected fallback operation is logged as a warning/error rather
  than bounded informational diagnostics.
- **Evidence inflation:** a screenshot, stale log, graph-only observation, or
  helper exit code is presented as proving a layer it did not exercise.
- **Handwritten Unity identity:** a `.meta` GUID or importer block is invented,
  semantic, duplicated, or not verified by Unity import.

Prefer the smallest fix that restores the invariant. Do not use this checklist
as permission to rename public APIs, reorder serialized values, or refactor
unrelated code in the same commit.

## Engineering Rules

- Prefer existing repository patterns before adding new architecture.
- Read nearby code before editing.
- Use `rg` / `rg --files` for search.
- Use `apply_patch` for manual edits.
- Do not revert user changes unless explicitly asked.
- Do not stage or commit unrelated files.
- Do not commit `Plan/`, `Developer/`, or `AGENTS.md` unless explicitly asked.
- Keep Unity `MonoBehaviour` and Inspector-facing behavior stable unless a plan
  calls for migration.
- Add diagnostics before performance rewrites.
- For hot paths, protect the main loop before maximizing visualization topic
  frequency.
- Use bounded queues for heavy asynchronous sensor work.
- Drop stale visualization frames before blocking the main loop.
- Do not call `AsyncGPUReadback.WaitAllRequests()` in runtime destroy/disable
  paths.
- Do not use Unity APIs from background workers. Worker code must operate on
  owned Unity-free buffers.
- Keep public/editor-facing diagnostics typed and bounded; include stable error
  categories, but do not expose credentials, tokens, Zenoh secrets, or raw
  machine-specific exception text in persisted summaries.
- When a review reports a smell, verify it against current source and its
  architectural boundary before changing code. Documented no-ops such as the
  CLR `partial` boundary are not fixed by pretending reflection can recover
  erased metadata.

## Validation Defaults

For code changes, prefer these checks when relevant:

```powershell
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
dotnet build Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj
git diff --check
```

For Phase179/FoxRun native-subscription changes, also run the affected optional
lanes. A green lane is invalid if its compile-surface assertion did not observe
the intended adapter/native sources:

```powershell
dotnet test Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj -p:IncludeRos2ForUnityAdapter=true
dotnet test Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj -p:IncludeRos2ForUnityNative=true
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj -- --phase179
python -m unittest Scripts.smoke.ros2.regression_checks.test_phase179_foxrun_ros2_inbound_acceptance Scripts.smoke.ros2.regression_checks.test_phase179_foxrun_ros2_matrix_profiles Scripts.smoke.ros2.regression_checks.test_phase179_foxrun_ros2_player_host Scripts.smoke.ros2.regression_checks.test_phase179_zenoh_topology
```

Use `python Scripts/release/run_ci.py` as the repository-wide local orchestrator
when the requested scope warrants it. It can take substantially longer than
small targeted checks; do not call it hung merely because a short outer command
timeout expired. Inspect the child processes and `build/ci/<run>/logs/` first.

### Test Suite Migration Boundary

Phase140B is migrating tests from the custom phase-validation runner into a
three-track model:

- Pure behavior/unit checks belong in xUnit under
  `Packages/dev.unity2foxglove.sdk/Tests/Unit`.
- Source-shape and architecture checks belong in Roslyn syntax-tree tests.
- Repository hygiene, true socket/integration, Unity, ROS2, and Foxglove
  Desktop checks stay in the existing console runner unless a dedicated plan
  says otherwise.

During the migration, validation is dual-track. For relevant code changes, run
both the new xUnit suite and the existing console runner when those projects
exist:

```powershell
dotnet test Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj
dotnet run --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
```

Do not add new pure behavior checks to the old console phase runner unless
there is a specific integration, hygiene, or compatibility reason. Do not
migrate true socket, Unity, ROS2, Foxglove Desktop, filesystem hygiene, or
repository-shape acceptance into xUnit just to standardize the framework.
When a compatibility phase runner remains, every check label must describe the
behavior being validated in plain language. Do not use phase-number labels such
as `148-1` as the primary assertion name; keep phase numbers in flags, file
names, traits, or migration notes instead.

Keep migrated checks mapped in the tracked migration manifest so coverage can
be audited before old console checks are removed. Do not delete old console
checks merely because xUnit coverage exists; remove them only after the
migration manifest proves equivalent coverage and both validation tracks are
green.

For Unity-specific behavior, automated tests are not enough. Record manual
Foxglove/Unity acceptance in `Developer/` when a phase reaches a stable point.

## Project Map

- Main Unity project: `Unity2Foxglove/`
- Core package: `Packages/dev.unity2foxglove.sdk/`
- Optional ROS2 For Unity facade package:
  `Packages/dev.unity2foxglove.ros2forunity/`
- Optional Humble runtime package:
  `Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/`
- Optional Jazzy runtime package:
  `Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/`
- Optional Lyrical runtime package:
  `Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/`
- Local R2FU runtime artifact entrypoint:
  `r2fu-runtime-artifacts/`
- Local Windows ROS 2 install entrypoint:
  `ros2-windows/`
- Windows R2FU scripts:
  `Scripts/ros2forunity/windows/humble/`,
  `Scripts/ros2forunity/windows/jazzy/`, and
  `Scripts/ros2forunity/windows/lyrical/`
- ROS2/RViz2 smoke helpers:
  `Scripts/smoke/ros2/`
- Phase179 core provider/session policy:
  `Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/`
- Phase179 optional native host, bindings, ownership slot, and diagnostics:
  `Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/`
- Phase179 ROS2 message-shape and input-dispatch generation:
  `Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunReflectionRos2MessageShapeBuilder.cs`,
  `Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxRunRoslynRos2MessageShapeBuilder.cs`,
  and `Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/Ros2InputDispatchEmitter.cs`
- Phase179 acceptance scene and receiver:
  `Unity2Foxglove/Assets/Scenes/Phase179FoxRunRos2NativeSubscribeAcceptance.unity`
  and `Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase179FoxRunRos2NativeSubscribeAcceptance.cs`
- Runtime tests: `Packages/dev.unity2foxglove.sdk/Tests/Runtime/`
- Camera publisher:
  `Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraPublisher.cs`
- Camera editor:
  `Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs`
- Camera backpressure policy:
  `Packages/dev.unity2foxglove.sdk/Runtime/Utilities/CameraBackpressurePolicy.cs`

## Release / Public Boundary Reminders

- The core SDK remains ROS-free.
- R2FU is optional and must stay behind package boundaries.
- Package directories must not receive build outputs.
- Public validation must work from a clean open-source checkout.
- Do not use local sibling repositories as validation evidence.
- Do not read ignored local notes as validation evidence.
- Release commits and public PRs must not include bot/co-author metadata or
  references to private local notes.

# ADDITIVE TERMINAL CHECKPOINT 2026-09-09 — R4.1 REAL ENVIRONMENT REVALIDATION / H08 ORIGIN PROBE HARDENING

Status: R4.1_H08_REPAIRED / ENVIRONMENT_ENTRIES_VERIFIED / FULL_CI_PASSED / PR327_MERGED / MAIN_SYNCED / H_BRANCH_CLEAN

- Frozen start HEAD: 36e5d4a6ea9c2815bece7cd9cf34a338a4859abb; serial fix commit 36e5d4a6e; PR #327 https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk/pull/327; merge SHA 3ab7a149885b77269d69f278f2857ba19a7c4a9f.
- Final main == origin/main == 3ab7a149885b77269d69f278f2857ba19a7c4a9f; remote heads contain only refs/heads/main; merged H branch deleted locally and remotely. Pre-existing dirty Unity files remain unchanged.
- R4.1 overlay: build/phase187-round4/r4.1-real-environment-revalidation/R4.1_REAL_ENV_REVALIDATION_OVERLAY.tsv; 90 historical H BLOCKED candidates rebound at frozen HEAD. The overlay records 88 PENDING_REVALIDATION rows and H08-001/H08-002 PENDING_BEHAVIORAL_VERIFICATION after source-level RED/fix; no row was blocked because of PATH. Canonical queue and execution ledger were not rewritten.
- Environment evidence: all six repository Junctions valid and targets present (ros2-windows/{humble,jazzy,lyrical} and r2fu-runtime-artifacts/{humble,jazzy,lyrical}); Windows setup, ros2 CLI, and rclpy endpoint probes for Humble/Jazzy/Lyrical exited 0. Logs and hashes: build/phase187-round4/r4.1-real-environment-revalidation/entry/environment-results.json.
- H08-001 fix requires exact external serialized payload bytes for both subscriptions. H08-002 fix enforces whole-token --timeout-ms parsing with consumed-length validation. Baseline deterministic RED exited 1; modified probe emitted H08_RED_NOT_REPRODUCED, exit 0.
- Transaction quartet: build/phase187-round4/h-residual-revalidation/transactions/H08-origin-probe-hardening/MODIFIED_FILE SHA ac24caf7c227fdbee994b154401b983ec60e1094306eb3fce06b674a856c8fbd; DIFF_FILE 75cc1cfa5a8fe421ed870e92d5cb0d3e5856dd36e2c4971f6348061c33c127a9; VERIFICATION.txt and executable ROLLBACK.sh are in the same directory. Independent rollback returned ROLLBACK_OK then ROLLBACK_NOOP, exit 0, with seeded_equals_modified=true, restored_equals_original=true, live_modified_remains=true.
- Overall audit: mutation 3/3 killed; correctness C1/C9 pass; scope/evidence pass for the R4.1 entry overlay and quartet.
- Full local CI: python -B Scripts/release/run_ci.py; run 41236-2e26421a; runner CI_EXIT=0; literal All CI checks passed.; all 13 jobs passed. Remote PR #327 checks all passed: check, test, analyzer-freshness, optional ROS2 Native, optional ROS2 adapter.
- git ls-files build remains empty. The three protected pre-existing dirty files, historical worktrees, Junctions, and evidence remain preserved.

AGENTS_PREVIOUS_SHA256=2c42374bfada5489cba29b65cbbfc5cd4e1ec9bc087174237bb7bf37a4db5d29


# ADDITIVE TERMINAL CHECKPOINT 2026-09-12 — R4.1 H RESIDUAL REVALIDATION COMPLETE

Status: `R4.1_H_RESIDUAL_COMPLETE / REMOTE_CI_PASSED / PR328_PR329_MERGED / MAIN_SYNCED / HUORONG_DLL_REGENERATED`

- R4.1 overlay `build/phase187-round4/r4.1-real-environment-revalidation/R4.1_REAL_ENV_REVALIDATION_OVERLAY.tsv` contains 90 terminal residual H rows: `CONFIRMED_FIXED=25`, `REFUTED=8`, `SATISFIED_BY_ROUND4=57`, `PENDING_REVALIDATION=0`; SHA-256 `B1BC311A8ED434CADED55996DB93F8D42A656197B3FE362B432C437C243B704B`.
- Three-role review is PASS: mutation 3/3 killed, correctness C1/C9 PASS, scope/evidence PASS with stale historical snapshot rebound.
- Huorong quarantine handling did not use restore/extract. `ros2cs_common.dll` was regenerated from local ZIP/Junction sources for Humble/Jazzy/Lyrical. All three targets are 24,064 bytes, SHA-256 `CEC8AF656B9084DABA9939C7458B88D12DC9FABE47FD519C4370A2D40F12EB24`; evidence `build/phase187-round4/r4.1-real-environment-revalidation/HUORONG_OBSERVATION_20260911.md`.
- Serial H branch commits included `637d80c31`, `9b4fcecdf`, and `946908075`. PR #328 and follow-up PR #329 merged normally; merge SHA `daf4e0146aa62b178b7b226905e7550a1965d7bc`.
- Complete local CI command `python -B Scripts/release/run_ci.py`; run id `r41-final-ci-remote-provenance-20260912`; log `build/phase187-round4/r4.1-real-environment-revalidation/full-ci-final-remote-provenance-20260912.log`; runner exit `0`; literal `All CI checks passed.`; all jobs passed. Remote PR #329 checks passed: docs, check, test, analyzer-freshness, optional ROS2 Native, optional ROS2 adapter.
- Remote RED from PR #328 run `34663692433` was five Linux-LF provenance hash mismatches. Focused GREEN `python -B Scripts/release/run_ci.py --only phase186-bridge-tooling` exited `0` with `All CI checks passed.`
- Provenance repair quartet `build/phase187-round4/scratch/r41-remote-provenance-repair-20260912/transaction-v2/` returned `ROLLBACK_OK`, `ROLLBACK_NOOP`, exits `0,0`; `seeded_equals_modified=true`, `restored_equals_original=true`, `live_modified_remains=true`.
- Final refs: `HEAD == main == origin/main == daf4e0146aa62b178b7b226905e7550a1965d7bc`; remote heads only `refs/heads/main`; merged H source branches were deleted locally and remotely. `git ls-files build` remains `0`.
- Protected dirty files remain unchanged: `Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs`, `Unity2Foxglove/Packages/manifest.json`, `Unity2Foxglove/Packages/packages-lock.json`.

AGENTS_PREVIOUS_SHA256=097c82292b2db86057ead55d230a032631a2318b959da4caa123303be7435527


# ADDITIVE TERMINAL CHECKPOINT 2026-09-12 — R4.1 I SERIES COMPLETE

Status: `R4.1_I_SERIES_COMPLETE / THREE_ROLE_AUDIT_PASS / FULL_CI_PASSED / I_BRANCH_READY_FOR_INTEGRATION`

- Live queue recheck found 198 I01-I11 rows in selected order 189..452: `CONFIRMED_FIXED=1`, `REFUTED=183`, `SATISFIED_BY_ROUND3=13`, `SATISFIED_BY_ROUND4=2`, `BLOCKED=0`. Overlay `build/phase187-round4/r4.1-real-environment-revalidation/i-series/I_SERIES_REAL_ENV_REVALIDATION_OVERLAY.tsv`, SHA-256 `C12C6BBE3D2A5865E197A756037E0BA101972B61D158DA4CCF096ED03BFA7B7B`.
- I09-009 was reproduced and fixed serially in commit `f9e713979`: truncated MCAP record headers, record payloads, and chunk payloads now fail closed with ValueError. Focused RED exit 1; GREEN 2 tests exit 0; mutation 3/3 killed; positive/negative controls exit 0. Quartet: `build/phase187-round4/r4.1-real-environment-revalidation/i-series/I09-009-transaction/`; rollback `ROLLBACK_OK`, `ROLLBACK_NOOP`, exits 0,0; seeded_equals_modified=true; restored_equals_original=true; live_modified_remains=true.
- MCAP conformance: `python -B Scripts/release/run_ci.py --only mcap-conformance`, exit 0, literal `All CI checks passed.`
- Complete local CI: `python -B Scripts/release/run_ci.py`; run id `i-series-final-ci-20260912`; log `build/phase187-round4/r4.1-real-environment-revalidation/i-series/full-ci-final.log`; exit 0; literal `All CI checks passed.`; all jobs passed.
- Three-role adjudication: mutation PASS (`5B0D949A47BDBC7EFA2D62B80E6222D08CA84AFA13F8A076DC808339F80DC5DA`), correctness PASS (`6E887F72E967C4452AF84FB18F194C2C15B2DB3C62DD1994E881951DF8444A6A`), scope/evidence PASS (`B917CBC329F311C6CC879B09FAAB7E5F2FCA92124E0799FE9A9BF47AC40B1051`). Final checkpoint `build/phase187-round4/r4.1-real-environment-revalidation/i-series/CHECKPOINT_I_SERIES_FINAL_20260912.md`, SHA-256 `C322762314BB244EFE5364F609F4B1E9BBC1EEDF07398D24A180978505FCAA37`.
- Branch `feature/round4/i-series-real-environment-revalidation` is based on `daf4e0146aa62b178b7b226905e7550a1965d7bc`; protected Unity dirty files remain unchanged; canonical queue/ledger originals remain untouched; `git ls-files build` remains 0.

AGENTS_PREVIOUS_SHA256=43784502d0ae1f8721535738e8f4fa33b4580d8a250967d22f7f823e90647a8e


# ADDITIVE TERMINAL CHECKPOINT 2026-09-12 — R4.1 I SERIES MERGED

Status: `R4.1_I_SERIES_MERGED / PR330_REMOTE_CI_PASSED / MAIN_SYNCED / I_BRANCH_CLEAN`

- PR #330 `https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk/pull/330` merged normally; merge SHA `a223fb968c441dcb24821befb94ad38a15be7585`. Remote checks all passed: docs, check, test, analyzer-freshness, optional ROS2 Native, optional ROS2 adapter.
- Local `main` was fast-forwarded to `origin/main`; final `HEAD == main == origin/main == a223fb968c441dcb24821befb94ad38a15be7585`; remote heads contain only `refs/heads/main`; merged I branch deleted locally and remotely.
- I final checkpoint: `build/phase187-round4/r4.1-real-environment-revalidation/i-series/CHECKPOINT_I_SERIES_FINAL_20260912.md`, SHA-256 `C322762314BB244EFE5364F609F4B1E9BBC1EEDF07398D24A180978505FCAA37`.
- Existing protected dirty files, historical worktrees, canonical queue/ledger, and evidence remain preserved; `git ls-files build` remains `0`.

AGENTS_PREVIOUS_SHA256=776d0dd8a7b9630f9a11da9be9d78d083a3e1f2ff984bde4bcbc2cc818d2c0f1

# ADDITIVE CORRECTION 2026-09-12 — R4.1 I SERIES REOPENED

Status: R4.1_I_REOPENED / PRIOR_COMPLETION_CLAIM_SUPERSEDED / LIVE_QUEUE_RECONCILED

- The prior R4.1 I SERIES COMPLETE and R4.1 I SERIES MERGED blocks were incorrect: they evaluated only a 198-row subset and treated historical BLOCKED candidates as REFUTED. Those blocks remain preserved as historical text but are not terminal evidence.
- Current live queue Developer/187/findings/round4-direct-remediation/queue.tsv has 452 rows total and 264 I-family rows. I dispositions are 247 BLOCKED/pending revalidation, 13 SATISFIED_BY_ROUND3, 2 SATISFIED_BY_ROUND4, and 2 REFUTED. Pending distribution is I01=19, I02=13, I03=21, I04=29, I05=25, I06=35, I07=30, I08=11, I09=37, I10=12, I11=15.
- Round2 I01–I11 reports are frozen FINDINGS_REPORTED inputs, not current-environment execution proof. The canonical queue and execution ledger remain unchanged.
- R4.1-I revalidation is reopened on branch eature/round4/i-series-real-environment-revalidation from 223fb968c441dcb24821befb94ad38a15be7585. Opening reconciliation: uild/phase187-round4/r4.1-real-environment-revalidation/i-series/R4.1-I_OPENING_RECONCILIATION_20260912.md; overlay: uild/phase187-round4/r4.1-real-environment-revalidation/i-series/R4.1-I_REAL_ENV_REVALIDATION_OPENING_OVERLAY.tsv; metadata: uild/phase187-round4/r4.1-real-environment-revalidation/i-series/R4.1-I_REVALIDATION_OPENING_RECONCILIATION.json.
- No product source was changed by this correction. Protected Unity dirty files and historical evidence/worktrees remain preserved. The 247 pending rows must be rebound to current source and real/deterministic seams before any terminal disposition or completion claim.

AGENTS_PREVIOUS_SHA256=7bf3cb422871668ba5a37e4efc2dd7b774f65233907c619e060ae98a43bfcec8

# ADDITIVE CHECKPOINT 2026-09-12 — R4.1-I BATCH 1

Status: R4.1_I_BATCH1_IN_PROGRESS / 8_ROWS_REVALIDATED / 239_PENDING

- Branch eature/round4/i-series-real-environment-revalidation is at 8188d5330e56e5a2cae200fce1f4c149b5246ec1; main and origin/main remain 223fb968c441dcb24821befb94ad38a15be7585.
- Revalidated rows: 187-R2-I01-001 and 187-R2-I01-002 REFUTED; 187-R2-I01-006, 187-R2-I01-008, 187-R2-I01-015, 187-R2-I01-016, 187-R2-I02-013, and 187-R2-I02-016 CONFIRMED_FIXED.
- Serial commits: 38c894a03, cdb10073f, 84c502d, 594a452a, 3b1cc099, 8188d533.
- Current overlay progress: CONFIRMED_FIXED=6, REFUTED=4, SATISFIED_BY_ROUND3=13, SATISFIED_BY_ROUND4=2, PENDING_REVALIDATION=239. Every confirmed row has a quartet and independent rollback evidence under uild/phase187-round4/r4.1-real-environment-revalidation/i-series/.
- Full I-series three-role review, complete CI, push, merge, and final refs are intentionally pending until all live I rows reach terminal disposition.

AGENTS_PREVIOUS_SHA256=60c6c42b327acc4aed0b94121cee1658971b5de872e9af548ebbc6e9f6a5f649

# ADDITIVE CHECKPOINT 2026-09-12 — R4.1-I BATCH 2

Status: R4.1_I_BATCH2_IN_PROGRESS / 12_ROWS_REVALIDATED / 237_PENDING

- Additional serial fixes: 8204cdf75 (I02-001 POSIX signal/partial-acquisition cleanup) and 60577b46 (I02-008 bounded log reads and special-file rejection).
- Current branch tip: 60577b4611bca48d69be27b1bab285ca183cb14; main/origin/main remain 223fb968c441dcb24821befb94ad38a15be7585.
- Overlay progress: CONFIRMED_FIXED=8, REFUTED=4, SATISFIED_BY_ROUND3=13, SATISFIED_BY_ROUND4=2, PENDING_REVALIDATION=237.
- Python syntax verification: python -m py_compile Scripts/unity_build/unity_il2cpp.py, exit  .
- Full CI, three-role review, push, merge, and final refs remain pending until all live I rows are terminal.

AGENTS_PREVIOUS_SHA256=ef03a67275149ccc2a2e2280e7381d65538c43459a2ebd43499fb4ebf890cdbc
# ADDITIVE CHECKPOINT 2026-09-12 — R4.1-I BATCH 3

Status: R4.1_I_BATCH3_IN_PROGRESS / 14_PENDING_ROWS_RECLASSIFIED_OR_FIXED / 235_PENDING

- Serial commits added: 8204cdf75 (I02-001), f60577b46 (I02-008), bf5c40d13 (I02-019); I02-015 is SATISFIED_BY_ROUND4 by reuse of the I02-001 seam.
- Current branch tip: bf5c40d137048118bba24733fc3323958afe97af; main/origin/main remain a223fb968c441dcb24821befb94ad38a15be7585.
- Overlay progress: CONFIRMED_FIXED=9, REFUTED=4, SATISFIED_BY_ROUND3=13, SATISFIED_BY_ROUND4=3, PENDING_REVALIDATION=235.
- Python syntax verification remains exit 0; all batch transaction rollbacks returned ROLLBACK_OK then ROLLBACK_NOOP with live modified source retained.
- Full CI, three-role review, push, merge, and final refs remain pending until all live I rows are terminal.

AGENTS_PREVIOUS_SHA256=804d416df58b58d54515c2246ca44986227b3a8557e4b40c6a9a900cd687a1f8

# ADDITIVE CHECKPOINT 2026-09-13 — I SERIES REFUTED-EVIDENCE CORRECTION / PR332 MERGED

Status: `I_SERIES_COMPLETE / CORRECTION_OVERLAY_TERMINAL / THREE_ROLE_AUDIT_PASS / FULL_CI_PASSED / PR332_MERGED / MAIN_SYNCED / NO_SHUTDOWN`

- Live I queue: 264 rows. Correction overlay `build/phase187-round4/r4.1-real-environment-revalidation/i-series/R4.1-I_REFUTED_EVIDENCE_CORRECTION_20260912.tsv`, SHA-256 `f6febbc05fd58be1210dc063e110c181d337d8733e5b11cda926ba9b55163f63`; dispositions `CONFIRMED_FIXED=124`, `SATISFIED_BY_ROUND4=103`, `REFUTED=24`, `SATISFIED_BY_ROUND3=13`, `PENDING_REVALIDATION=0`.
- Correction branch `feature/round4/i-series-refuted-evidence-correction` was pushed once for integration and merged as PR #332: `https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk/pull/332`; merge SHA `8a8bc7e79af95a0621d693d613d03ac996a3b260`. Serial repair/evidence commits and exact hashes are recorded in `build/phase187-round4/r4.1-real-environment-revalidation/i-series/CORRECTION_CHECKPOINT_20260913.json` (SHA-256 `1248b0ec8e33f15714f2c28e39559ec8ec1dd161e13702031bd720d77a921f92`).
- Final three-role adjudication PASS: mutation/adversarial report `build/phase187-round4/r4.1-real-environment-revalidation/i-series/final-review-20260913/mutation-adversarial-report.txt` SHA `12ce1fb74f67cf44cf2e1613791a7c8763030f541e112ab16fda412fa30c2bf3`; correctness/runtime report SHA `69a863d832cddc21300347120a82fe2dd04e17a3d8f19f4fc1627163377cf324`; scope/evidence report SHA `5b5c02be07e12f7e45cff4d29a0e574e626afb0e108eaf3b8bf8028b2ce54e0b`; adjudication SHA `33b141be5901d8c873ac380142f7cb9cc15116a3ccfc927fbad6c323699d2b2e`.
- Full local CI command: `python -B Scripts/release/run_ci.py`; log `build/phase187-round4/r4.1-real-environment-revalidation/i-series/final-ci-correction-20260913-v5.log`, SHA `78ea7b5e3f32dce13d06c02cede0629b74e654f2b10a3a53948da285dcbfb4c0`, runner exit `0`, literal output `All CI checks passed.`; all 13 jobs passed. Remote PR #332 checks run `34735609421` all passed: docs, check, test, analyzer-freshness, optional ROS2 Native, optional ROS2 adapter, Windows runtime/xUnit/panel/package parity, private-boundaries.
- Confirmed repair quartets include `build/phase187-round4/r4.1-real-environment-revalidation/i-series/ci-tooling-correction-20260913/` and `.../posix-process-tree-correction-20260913/`, each retaining `MODIFIED_FILE`, `DIFF_FILE`, `VERIFICATION.txt`, executable `ROLLBACK.sh`, independent rollback copy/output. Exact command `bash ROLLBACK.sh` returned `ROLLBACK_OK`, `seeded_equals_modified=true`, `restored_equals_original=true`, `live_modified_remains=true`, `ROLLBACK_NOOP`, exit `0`; live modified files remained changed.
- Final refs after `git fetch origin` and `git merge --ff-only origin/main`: local `main`, `HEAD == main == origin/main == 8a8bc7e79af95a0621d693d613d03ac996a3b260`; remote `main` is the only canonical release branch after cleanup. Historical worktrees/evidence remain preserved.
- Protected dirty files preserved unchanged: `Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs` SHA `de447dc56ade5b1ceecc59fc7b15c7c07d05b850fff8e2202d0f603b9617e759`; `Unity2Foxglove/Packages/manifest.json` SHA `db9983a07e81f1f8087d76528f9f6d8a14c991147dd075b683d91ad01f0aec15`; `Unity2Foxglove/Packages/packages-lock.json` SHA `a09ac8fdb69e0eb82a94fcee975b2249f140ca34baabef8a556b02652e8f7b5c`. No reset, stash, clean, broad deletion, or shutdown performed.

# ADDITIVE CHECKPOINT 2026-09-13 — FINAL MAIN REF AFTER PR333

Status: `I_SERIES_COMPLETE / PR332_AND_PR333_MERGED / MAIN_SYNCED / NO_SHUTDOWN`

- PR #333 (`https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk/pull/333`) merged the additive AGENTS and correction checkpoint record. Final merge SHA is `da36efafd976d000eacd5de5f96589d0ef028868`; local `main`, `HEAD`, and `origin/main` are synchronized to this SHA. Remote `git ls-remote --heads origin` contains only `refs/heads/main`; both I correction branches were deleted after ancestry verification.
- `build/phase187-round4/r4.1-real-environment-revalidation/i-series/CORRECTION_CHECKPOINT_20260913.json` was updated with this final ref and PR333 sync data. Historical AGENTS content remains the exact suffix before this additive block.
