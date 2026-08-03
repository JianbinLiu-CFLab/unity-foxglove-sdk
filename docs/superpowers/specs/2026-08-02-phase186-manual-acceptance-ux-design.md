# Phase186 Manual Acceptance UX Design

Date: 2026-08-02
Status: Approved direction; implementation pending

## Goal

Make each Phase186 Unity manual suite start with one short operator command,
show continuous human-readable progress, and prevent invalid Unity button
sequences without weakening the existing token-bound evidence, fail-closed
classification, or owned cleanup contract.

The approved operator model is one runtime suite per invocation:

```powershell
python Scripts/smoke/foxrun/phase186_bridge_manual.py jazzy
python Scripts/smoke/foxrun/phase186_bridge_manual.py zenoh
```

## Current problem

The maintained command exposes five coordinator details that the operator
should not choose: the exact manual case ID, `--manual`, timeout, Git HEAD, and
output root. During runtime preparation and manual waiting it emits almost no
human-readable status. Its sole readiness line contains long machine identity
fields and a filesystem path, so it wraps before communicating the next Unity
action.

The Unity overlay also enables the local-B button before the Bridge and remote
A evidence are ready, and it permits repeated clicks despite the exactly-once
manual instruction.

Phase184 established the desired pattern: the helper selects the immutable
case, tells the operator when one Play session is authorized, reports when
automated evidence is complete, and leaves machine assertions to durable
artifacts.

## Architecture

### Thin operator launcher

Add `phase186_bridge_manual.py` as the only documented human entry point. It
accepts one positional suite alias:

| Alias | Exact coordinator case | Evidence root |
| --- | --- | --- |
| `jazzy` | `manual-jazzy-fastrtps-duplex` | `build/phase186/manual/jazzy-fastrtps` |
| `zenoh` | `manual-lyrical-zenoh-duplex` | `build/phase186/manual/lyrical-zenoh` |

The launcher resolves the repository root, supplies the fixed 1800-second
manual timeout, and invokes the existing coordinator in process. The
coordinator resolves the current Git HEAD only after establishing the owned
run root, so interruption of that check can still produce honest terminal
evidence. The launcher does not duplicate preflight, process ownership,
evidence, or cleanup logic. Unknown or extra operator arguments fail before
any live actor starts.

The existing `phase186_bridge_acceptance.py` CLI remains unchanged for
automatic certification, tests, and advanced/internal use.

### Human status reporter

Manual runs emit transition-based status plus a heartbeat every 10 seconds
while a long stage is unchanged. Automatic cases retain their existing output.
The manual stages are:

1. checking repository, exact HEAD, Unity project, and ports;
2. preparing the selected Bridge runtime/build evidence;
3. starting and proving Sidecar, ROS peer, graph observer, and optional router;
4. waiting for the one user-owned Unity Play session;
5. validating completion and cleaning owned resources.

The readiness block gives the next action directly:

```text
[Phase186 4/5] Unity action required (Jazzy / FastDDS)
  1. Foxglove > Manual Acceptance > Phase186 > Prepare Current Bridge Run
  2. Enter Play Mode once.
```

While Unity is running, status transitions distinguish:

- waiting for Provider readiness;
- waiting for external A;
- external A applied, local B button now available;
- local B verified, completion button now available;
- completion received, cleanup in progress.

The `external-a` transition is a first-class, token-scoped Unity progress
field. It is derived from the current generated binding after the generated
tick and participates in the manual progress fingerprint, so applying A emits
a new marker even when no Provider counter or final gate changed. The manual
Python reporter consumes that marker only after matching the current run ID,
case, token hash, and HEAD. Automatic progress markers retain their exact
existing fields and fingerprint. The reporter is an optional no-op dependency
for automatic runs, preserving their existing output byte-for-byte.

The long token, full HEAD, pointer path, and run ID remain in `run-config.json`
and terminal evidence. The terminal still emits the stable final
`PHASE186_ACCEPTANCE_PASS/FAIL/NOT_RUN` marker required by tooling, preceded by
a compact human summary and evidence path.

The heartbeat implementation must stop and join on every success, failure,
`Ctrl+C`, and cleanup path. It reports elapsed time only and never mutates live
state.

### Unity interaction state

Keep the explicit Prepare menu and user-owned Play transition; do not
automatically replace the user's open scene or enter Play Mode.

The overlay derives an explicit manual step:

1. waiting for Bridge and remote A;
2. ready to publish local B;
3. waiting for automated B/peer evidence;
4. ready to complete;
5. completed; keep Play running until terminal cleanup finishes.

The local-B button is enabled only when all of the following are true:

- current context is valid and manual;
- Provider Publish and Subscribe directions are Ready;
- a current-run `external-a` value has been observed and applied;
- no local mutation has been requested;
- the run is not terminal.

The external gate is deliberately not a prerequisite for B: the ROS peer does
not finish and the coordinator does not write that gate until it observes B.
Making B wait for the gate would deadlock the suite. After one B click the
button remains disabled. The Complete button retains the existing
post-B `CanComplete` evidence gate and also remains one-shot. No token, topic,
payload, or evidence semantics change.

One pure, internal interaction-state decision is shared by `OnGUI` and the
public action methods. It decides the current step and whether B or Complete
is allowed. Calling `PublishLocalMutation` early or twice must be a no-op at
the same boundary used to disable the button; tests exercise the decision and
the action boundary as behavior rather than only searching source text.

## Failure and cleanup behavior

- `Ctrl+C` is handled according to the stage that owns resources. During
  preflight/build, the currently owned subprocess tree is terminated and an
  interrupted-stage failure is persisted; after live ownership starts, the
  coordinator additionally removes the manual pointer and generated binding,
  stops only its actors, and records the real residual port/file/process
  checks. No interrupted path may substitute the nominal clean-cleanup object
  when cleanup was not observed.
- A failed or `NOT RUN` suite prints its stable code, concise reason, evidence
  root, and the next recovery action.
- No build-only or visual state can be promoted into a live PASS.
- The launcher never retries or silently switches runtime rows.
- Existing run-specific directories remain authoritative; the launcher does
  not delete or overwrite earlier evidence.

## Documentation

Update the Phase186 operator section of the ignored Developer handoff and the
Phase186-H plan to show only the two short commands and the transition-based
Unity checklist. Internal coordinator parameters remain documented only as a
diagnostic/advanced interface, not as the normal user workflow.

## Verification

Use RED-before-GREEN focused coverage:

- launcher alias-to-case/output/HEAD mapping and rejection of extra input;
- direct-file launcher import from outside the repository;
- manual-only stage formatting, 10-second heartbeat, and compact readiness
  instructions with a fake clock/output sink;
- token-scoped external-A progress emission and parsing, including the case
  where no other counter or final gate changes;
- Unity manual-step and shared button/action-enable behavior, including early
  click and duplicate-click rejection;
- interrupt handling during preflight/build, live actor startup, and manual
  wait, proving owned-process termination and non-nominal cleanup evidence;
- unchanged exact terminal-marker and cleanup protocol tests;
- complete Phase186 acceptance coordinator/live regression files;
- `py_compile`, focused C# tests, and `git diff --check`.

Do not rerun broad CI or the completed 12-case Phase186 certification. The two
user-run Jazzy/FastDDS and Lyrical/Zenoh suites remain the final live evidence.
