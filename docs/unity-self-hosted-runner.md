# Unity Self-Hosted Runner

The `Unity batch compile and path-aware gate` workflow (`.github/workflows/unity-compile.yml`) compiles the Unity
project on a self-hosted Windows runner. Branch protection requires its `unity-required` check.

- A pull request that changes Unity import inputs (`Packages/*/Runtime|Editor|Plugins/`, `Packages/*/package.json`,
  `Unity2Foxglove/Assets|Packages|ProjectSettings/`, `*.asmdef`, `*.asmref`, `*.meta`) needs a successful
  `unity-compile` run before it can merge.
- Other pull requests skip `unity-compile`, and `unity-required` passes.
- When no runner is online, a Unity-scoped pull request waits. Merging it requires a runner; the gate does not fall
  back to an advisory result.
- Same-repository pull requests use the regular `pull_request` workflow. The trusted `pull_request_target` path evaluates
  fork scope, fails `unity-required`, and does not schedule the self-hosted runner. The repository
  requires approval for every outside collaborator (`all_external_contributors`) before any fork workflow can run; do
  not approve untrusted `.github/workflows` changes. A maintainer must move trusted Unity-scoped changes to a branch in
  this repository.

## Machine prerequisites

| Requirement | Check |
|---|---|
| Windows x64 | |
| Unity `6000.3.14f1` at `%ProgramFiles%\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe`, activated for the runner user | open the project once in that Editor |
| PowerShell 7 **MSI** installation at `%ProgramFiles%\PowerShell\7\pwsh.exe` (the runner does not find the Microsoft Store/MSIX package) | `Test-Path "$env:ProgramFiles\PowerShell\7\pwsh.exe"` |
| Git for Windows | `git --version` |
| Windows long paths enabled | `(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem').LongPathsEnabled` is `1` |

Install the PowerShell 7 MSI with:

```powershell
winget install --id Microsoft.PowerShell --source winget --installer-type wix --force
```

The workflow enables `core.longpaths` for its own checkout through a run-scoped `GIT_CONFIG_GLOBAL`; no global Git
setting is needed.

## Register the runner

1. Open **Settings → Actions → Runners → New self-hosted runner → Windows x64** and follow the download steps into
   `%SystemDrive%\actions-runner`. Keep the folder outside synchronized drives.
2. In a normal (not elevated) PowerShell window, configure it with the label the workflow requests:

   ```powershell
   cd "$env:SystemDrive\actions-runner"
   .\config.cmd --url https://github.com/JianbinLiu-CFLab/unity-foxglove-sdk --token <registration token> --name <machine name> --labels unity2foxglove-phase186-live --work _work --unattended
   ```

   `self-hosted`, `Windows` and `X64` are added automatically. Run the runner as the user who owns the Unity license;
   running it as a Windows service has not been validated for Unity licensing.

3. Make sure the repository variable `UNITY_SELF_HOSTED_RUNNER` is `enabled`. It is set once per repository, not per
   machine: `gh variable set UNITY_SELF_HOSTED_RUNNER --body enabled`.

## Start the runner at logon

Save this as `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\actions-runner-unity2foxglove.vbs`. It starts
`run.cmd` hidden at logon and does nothing when a listener from `%SystemDrive%\actions-runner` is already running.

```vbscript
Option Explicit

Dim shell, runnerRoot, processes, process
Set shell = CreateObject("WScript.Shell")
runnerRoot = shell.ExpandEnvironmentStrings("%SystemDrive%\actions-runner")

Set processes = GetObject("winmgmts:").ExecQuery( _
    "SELECT ExecutablePath FROM Win32_Process WHERE Name = 'Runner.Listener.exe'")
For Each process In processes
    If Not IsNull(process.ExecutablePath) Then
        If LCase(Left(process.ExecutablePath, Len(runnerRoot))) = LCase(runnerRoot) Then
            WScript.Quit 0
        End If
    End If
Next

shell.CurrentDirectory = runnerRoot
shell.Run """" & runnerRoot & "\run.cmd""", 0, False
```

The runner writes its logs to `%SystemDrive%\actions-runner\_diag`. Stop it with
`Stop-Process -Name Runner.Listener,Runner.Worker`.

## Move to another machine

1. Prepare the new machine and register a runner with the same label. Several runners can be registered at once;
   GitHub sends each job to any online runner with matching labels.
2. Run the verification below on the new runner.
3. Remove the old runner: `.\config.cmd remove --token <removal token>` on the old machine, or delete it under
   **Settings → Actions → Runners**.

## Verify

```bash
gh workflow run unity-compile.yml --ref main
```

`workflow_dispatch` always requires Unity evidence. Expect `unity-compile` to succeed, the uploaded
`unity-batch-compile-*` artifact to contain `unity.json` with `"verdict": "PASS"`, and `unity-required` to print
`UNITY_GATE_PASS`. The first run on a new machine imports the whole project; later runs keep
`Unity2Foxglove/Library`.

## Troubleshooting

| Symptom | Cause |
|---|---|
| Job stays queued | No online runner with all four labels |
| `unity-required`: `UNITY_SELF_HOSTED_RUNNER is not enabled` | Repository variable missing or not `enabled` |
| `PowerShell 7 MSI installation is required` | Only the Store/MSIX PowerShell is installed |
| `Filename too long` during checkout | The long-path step did not run before checkout |
| `unity.json` verdict `NOT_RUN` | The runner user lacks the interactive environment, or another Unity instance owns the project |
