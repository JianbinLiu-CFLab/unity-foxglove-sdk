# Phase186 Manual Acceptance UX Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Phase186 manual operator's multi-argument, silent coordinator invocation with one suite alias, continuous bounded status, and a Unity interaction flow that cannot publish B early or twice.

**Architecture:** A thin launcher maps `jazzy` and `zenoh` onto the existing immutable coordinator inputs. An optional manual-only status reporter is threaded through coordinator/live preparation without changing automatic output. A dependency-free C# interaction-state helper is shared by the Unity overlay and action boundary, while existing current-run identity, final external gate, evidence, and cleanup remain authoritative.

**Tech Stack:** Python 3 standard library and `unittest`; C# 9, xUnit, Unity 6000.3.14f1; existing Phase186 coordinator, Bridge build, live-owner, and generated acceptance harness.

---

## File map

- Create `Scripts/smoke/foxrun/phase186_bridge_manual.py`: two-alias human entry point and concise final summary.
- Create `Scripts/smoke/foxrun/phase186_bridge_manual_status.py`: manual-only transition reporter and 10-second heartbeat with injected clock/sink.
- Create `Scripts/smoke/foxrun/regression_checks/test_phase186_bridge_manual.py`: launcher and reporter RED/GREEN behavior.
- Modify `Scripts/smoke/foxrun/phase186_bridge_acceptance.py`: optional status dependency, stage transitions, direct-launch-safe controlled interruption evidence.
- Modify `Scripts/smoke/foxrun/phase186_bridge_live.py`: runtime/startup/manual-wait transitions and identity-bound Unity progress parsing.
- Modify `Scripts/smoke/foxrun/phase186_bridge_build.py`: interrupt-safe termination of its one owned process tree and partial command evidence.
- Modify the three corresponding Phase186 Python regression files for interrupt, identity, and unchanged automatic-output tests.
- Create `Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186ManualInteractionState.cs` and `.meta`: dependency-free manual step and one-shot action decision.
- Modify `Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186Ros2BridgeAcceptance.cs`: external-A progress transition and shared button/action gate.
- Create `Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/Phase186ManualInteractionTests.cs`: real behavior tests.
- Modify `Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Unity2Foxglove.Ros2Bridge.UnitTests.csproj`: link the dependency-free Unity helper into xUnit.
- Modify `Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/BridgeProductSurfaceTests.cs`: retain only integration/source anchors not expressible by the behavior test.
- Modify ignored operator sources `Developer/160 Phase185-186 Combined Manual Acceptance.md` and `Plan/186/186H_AUTOMATED_AND_MANUAL_ACCEPTANCE_PLAN.md`: document only the two short commands as the normal workflow.

### Task 1: Thin launcher and manual status reporter

**Files:**
- Create: `Scripts/smoke/foxrun/phase186_bridge_manual.py`
- Create: `Scripts/smoke/foxrun/phase186_bridge_manual_status.py`
- Create: `Scripts/smoke/foxrun/regression_checks/test_phase186_bridge_manual.py`

- [ ] **Step 1: Write failing launcher and reporter tests**

Assert `jazzy` maps only to `manual-jazzy-fastrtps-duplex` and `build/phase186/manual/jazzy-fastrtps`, `zenoh` maps only to `manual-lyrical-zenoh-duplex` and its matching root, coordinator-owned HEAD resolution is requested, timeout is fixed at 1800, and unknown/extra arguments return usage before invoking the coordinator. Add an isolated direct-file import test from a temporary directory.

With fake monotonic time, an in-memory sink, and a short injected heartbeat
interval, assert transition lines are emitted once, unchanged stages heartbeat
with elapsed time, the Unity readiness block is two short actions, and
`close()` prevents later output and joins the worker. Assert the no-op reporter
emits nothing.

- [ ] **Step 2: Run RED**

Run:

```powershell
python -m unittest Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_manual -v
```

Expected: import/module failures because the launcher and reporter do not exist.

- [ ] **Step 3: Implement the minimal launcher and reporter APIs**

Expose immutable `Suite` records and `main(argv=None)`. Resolve the repository
from `__file__`, construct the existing coordinator argv without resolving
HEAD in the launcher, create one reporter, and invoke
`phase186_bridge_acceptance.main(..., status=reporter,
resolve_current_head=True)`. Implement
`ManualStatusReporter.transition(stage, message)`, `unity_ready(label)`,
`detail(message)`, and idempotent `close()`. The heartbeat reads state only.
Preserve coordinator exit codes; add no retries or advanced flags.

- [ ] **Step 4: Run GREEN**

Re-run the test module and expect all tests PASS with no live process launches.

- [ ] **Step 5: Commit Task 1**

```powershell
git add -- Scripts/smoke/foxrun/phase186_bridge_manual.py Scripts/smoke/foxrun/phase186_bridge_manual_status.py Scripts/smoke/foxrun/regression_checks/test_phase186_bridge_manual.py
git commit -m "feat(186h): add focused manual acceptance launcher"
```

### Task 2: Thread status through real stages and make interruption honest

**Files:**
- Modify: `Scripts/smoke/foxrun/phase186_bridge_acceptance.py`
- Modify: `Scripts/smoke/foxrun/phase186_bridge_live.py`
- Modify: `Scripts/smoke/foxrun/phase186_bridge_build.py`
- Modify: `Scripts/smoke/foxrun/regression_checks/test_phase186_bridge_acceptance.py`
- Modify: `Scripts/smoke/foxrun/regression_checks/test_phase186_bridge_live.py`
- Modify: `Scripts/smoke/foxrun/regression_checks/test_phase186_bridge_build.py`

- [ ] **Step 1: Write RED tests for optional status and automatic compatibility**

Pass a recording reporter into the coordinator/live helpers and assert ordered
stages 1-5. Call the same helpers without a reporter and compare captured
stdout and stderr, including order, with the existing exact automatic output.
Assert readiness output no longer exposes the long pointer/token line in
manual launcher mode while `run-config.json` retains those fields.

- [ ] **Step 2: Write RED tests for token-scoped Unity progress**

Feed mirrored log lines with wrong and correct run/case/token/HEAD fields. Assert only the exact current run advances `provider-ready`, `external-a`, `local-b`, and `can-complete`; a duplicate marker emits no duplicate transition.

- [ ] **Step 3: Write RED tests for interruption ownership**

Cover interruption during launcher-requested HEAD resolution/preflight,
`run_logged`, live startup, and manual wait. Assert reporter shutdown, the
build child/process tree is stopped, live owner `close()` runs, generated
binding and pointer cleanup are attempted when acquired, the terminal result
is FAIL with an interrupted-stage reason, and missing observed cleanup is
never replaced with `protocol.clean_cleanup_evidence()`.

- [ ] **Step 4: Run RED**

```powershell
python -m unittest `
  Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_acceptance `
  Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_live `
  Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_build -v
```

Expected: failures at the new optional reporter/progress/interrupt contracts.

- [ ] **Step 5: Implement minimal status plumbing**

Add a no-op-by-default reporter parameter. Establish run identity/root before
the optional coordinator-owned current-HEAD resolution. Start stage 1 before
that resolution/preflight, stage 2 before `prepare_runtime`, stage 3 before
actors, stage 4 only after pointer readiness, and stage 5 before terminal
validation/cleanup. Parse only exact current-run manual
`PHASE186_ACCEPTANCE_PROGRESS` fields and translate changes to human text.

- [ ] **Step 6: Implement interrupt-safe ownership**

Wrap the build subprocess in the existing Windows kill-on-close/process-group ownership primitive and settle it on every `BaseException` before re-raising. At coordinator scope, classify `KeyboardInterrupt` as a stable failed manual run, persist the interrupted stage, and use only cleanup evidence actually produced by that stage. Retain bounded cleanup and never kill global ROS/Unity process classes.

- [ ] **Step 7: Run GREEN and regression set**

Run the three modules above plus `test_phase186_bridge_acceptance_protocol`; expect all PASS and no spawned live prerequisites.

- [ ] **Step 8: Commit Task 2**

```powershell
git add -- Scripts/smoke/foxrun/phase186_bridge_acceptance.py Scripts/smoke/foxrun/phase186_bridge_live.py Scripts/smoke/foxrun/phase186_bridge_build.py Scripts/smoke/foxrun/regression_checks/test_phase186_bridge_acceptance.py Scripts/smoke/foxrun/regression_checks/test_phase186_bridge_live.py Scripts/smoke/foxrun/regression_checks/test_phase186_bridge_build.py
git commit -m "fix(186h): expose manual progress and owned interruption"
```

### Task 3: Make Unity's manual sequence self-enforcing

**Files:**
- Create: `Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186ManualInteractionState.cs`
- Create: `Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186ManualInteractionState.cs.meta`
- Modify: `Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186Ros2BridgeAcceptance.cs`
- Create: `Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/Phase186ManualInteractionTests.cs`
- Modify: `Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Unity2Foxglove.Ros2Bridge.UnitTests.csproj`
- Modify: `Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/BridgeProductSurfaceTests.cs`

- [ ] **Step 1: Write RED behavior tests for the pure interaction boundary**

Test the exact step progression through an extracted callable boundary that
the public Unity actions use. B is disabled until manual context, valid current
run, Provider Publish+Subscribe Ready, and external A are all true.
`TryRequestLocalMutation` changes the one-shot flag once; early and duplicate
requests leave it unchanged. Complete remains disabled until the existing
post-B `CanComplete` evidence is true and is one-shot. Also test that the
manual external-A latch changes only after a generated tick observes A and
resets for a newly configured run.

- [ ] **Step 2: Link the wished-for helper and run RED**

```powershell
dotnet test Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Unity2Foxglove.Ros2Bridge.UnitTests.csproj `
  --filter FullyQualifiedName~Phase186ManualInteractionTests
```

Expected: compile failure because the helper types are absent.

- [ ] **Step 3: Implement the dependency-free helper**

Create an internal enum/readonly state plus pure evaluation, external-A latch,
and one-shot request functions. Keep it free of `UnityEngine` so the real
source can be linked into xUnit. Generate a unique valid Unity `.meta` GUID and
verify it has no repository collision.

- [ ] **Step 4: Integrate the helper and external-A transition**

Latch external A after `Phase186Generated_Tick`, add
`externalA=true|false` only to the manual progress marker/fingerprint, and
reset it for every configured run. Preserve the automatic marker and
fingerprint byte-for-byte. Use the same callable state/action boundary in
`OnGUI`, `PublishLocalMutation`, and `CompleteManualAcceptance`. Do not require
`_externalGateReady` for B; retain it in `CanComplete`.

- [ ] **Step 5: Run GREEN and focused Bridge tests**

Run the filtered behavior test, `BridgeProductSurfaceTests`, and the full
Bridge unit project. Include an exact automatic progress-output regression.
Expect all PASS; automatic mutation behavior and all automatic progress and
terminal output remain unchanged.

- [ ] **Step 6: Commit Task 3**

```powershell
git add -- Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186ManualInteractionState.cs Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186ManualInteractionState.cs.meta Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186Ros2BridgeAcceptance.cs Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/Phase186ManualInteractionTests.cs Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Phase186/BridgeProductSurfaceTests.cs Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Unity2Foxglove.Ros2Bridge.UnitTests.csproj
git commit -m "fix(186h): enforce the manual duplex sequence"
```

### Task 4: Replace the operator handoff and verify the focused change

**Files:**
- Modify: `Developer/160 Phase185-186 Combined Manual Acceptance.md`
- Modify: `Plan/186/186H_AUTOMATED_AND_MANUAL_ACCEPTANCE_PLAN.md`
- Modify: `AGENTS.md`

- [ ] **Step 1: Update the ignored operator handoff**

Replace each long coordinator command with exactly one launcher command. Document that one invocation owns one Play session; follow the printed stages; click B only when enabled; click Complete only when enabled; exit Play Mode only after the terminal asks; then run the other suite separately. Keep the old detailed coordinator form only in an advanced diagnostic note.

- [ ] **Step 2: Run focused Python verification**

```powershell
python -m unittest discover -s Scripts/smoke/foxrun/regression_checks -p 'test_phase186_bridge_*.py' -v
python -m py_compile Scripts/smoke/foxrun/phase186_bridge_manual.py Scripts/smoke/foxrun/phase186_bridge_manual_status.py Scripts/smoke/foxrun/phase186_bridge_acceptance.py Scripts/smoke/foxrun/phase186_bridge_live.py Scripts/smoke/foxrun/phase186_bridge_build.py
```

Expected: all focused tests PASS and compilation exits zero.

- [ ] **Step 3: Run focused C# verification**

```powershell
dotnet test Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Unity2Foxglove.Ros2Bridge.UnitTests.csproj
```

Expected: complete Bridge unit suite PASS.

- [ ] **Step 4: Verify repository and command UX**

Run `python Scripts/smoke/foxrun/phase186_bridge_manual.py --help`, invalid alias, and mocked/dry unit coverage only; do not launch either real suite. Run `git diff --check`, confirm the protected Phase179 scene has no diff, preserve all stashes, and leave the unrelated `obj.meta` untracked.

- [ ] **Step 5: Commit tracked documentation/test polish if needed**

Commit only tracked production/test changes not already committed. Do not commit `AGENTS.md`, `Plan/`, `Developer/`, or the unrelated `obj.meta`. Update `AGENTS.md` with the new HEAD and exact two-command manual handoff.

## Completion boundary

Do not run broad CI, any automatic Phase186 case, or the completed 12-case certification. Do not claim `PHASE186_WINDOWS_LOCAL_EDITOR_PASS`. Stop after focused verification and hand the user the first command only:

```powershell
python Scripts/smoke/foxrun/phase186_bridge_manual.py jazzy
```

Only after that suite completes should the user receive the separate `zenoh` command.
