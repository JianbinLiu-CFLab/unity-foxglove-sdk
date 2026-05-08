# 0. Prerequisites

## Who should read this

Read this before you install Unity2Foxglove, open the demo project, or build an IL2CPP Player for the first time.

## What you will do

You will check which Unity version, Foxglove app, IDE, command-line tools, and optional modules you need for each workflow.

## 0.1 Required for Everyone

| Tool             | Recommended                                                         | Why you need it                                                | Notes                                                              |
| ---------------- | ------------------------------------------------------------------- | -------------------------------------------------------------- | ------------------------------------------------------------------ |
| Unity Editor     | Unity 6000.0 LTSC or later (developed on 6000.3.14f1 LTSC; compatible with 6000.0.74f1 LTSC) | Opens your project, imports the package, and runs Play Mode.   | Unity 2022 is not supported. |
| Foxglove Desktop | Latest stable desktop app                                           | Connects to Unity over Foxglove WebSocket and displays panels. | Use `ws://127.0.0.1:8765` for the default local connection.        |
| Git              | Any recent version                                                  | Clones the repository or tracks package changes.               | Needed if you install from a repository path.                      |

You do not need ROS to use Unity2Foxglove.

## 0.2 Unity Modules

For Editor Play Mode, the standard Unity installation is enough.

For Player builds, install the target platform module in Unity Hub:

| Target | Unity Hub module |
|---|---|
| Windows | Windows Build Support with IL2CPP |
| Linux | Linux Build Support with IL2CPP |
| macOS | macOS Build Support |

If the build script fails before compiling C#, first check that the target platform module is installed.

## 0.3 IDE and C# Editing

An IDE is optional for running the samples, but recommended if you write scripts.

Good options:

- Visual Studio with the Unity workload
- JetBrains Rider
- Visual Studio Code with C# and Unity extensions

Use the IDE for:

- Writing custom publisher scripts
- Adding `[FoxRun]` debug fields
- Registering Parameters and Services
- Inspecting compile errors

## 0.4 Command-Line Tools

| Tool | Required for | How to check |
|---|---|---|
| Python 3 | `Scripts/build_unity_il2cpp.py` | `python --version` |
| .NET SDK | Runtime validation tests | `dotnet --version` |
| PowerShell or terminal | Build and test commands | Open a shell at the repository root |

You only need Python if you use the repository build script. You only need the .NET SDK if you run the package test project.

## 0.5 Package Dependencies

Unity resolves the main package dependency automatically:

- `com.unity.nuget.newtonsoft-json`

The package also includes runtime plugin assemblies for MCAP compression support. If you only use live Foxglove streaming, you can start without thinking about compression.

## 0.6 Optional Demo Dependencies

The Full Demo sample and `Unity2Foxglove` demo project use more Unity features than the Basic sample.

| Feature | Used by | Notes |
|---|---|---|
| Input System | Mouse drag demo controls | Needed for the full interactive demo. |
| URP | Full demo visuals | Basic sample is intentionally smaller. |
| MCAP files | Recording/replay demos | You can skip this until you need offline playback. |

If you want the simplest possible first test, use [03_Verifying_Basic_Visualization](03_Verifying_Basic_Visualization.md).

## 0.7 Network Assumptions

The default server address is:

```text
ws://127.0.0.1:8765
```

This means Foxglove and Unity are running on the same machine.

For another machine to connect:

1. Set `FoxgloveManager > Host` to `0.0.0.0`.
2. Keep or change the port.
3. Allow the port through your firewall.
4. Connect Foxglove to `ws://<unity-machine-ip>:8765`.

Use this only on trusted networks.

## 0.8 Quick Readiness Checklist

- Unity opens your project without package errors.
- Foxglove Desktop is installed.
- You can press Play in Unity.
- You know whether you are using Basic sample, Full Demo sample, or `Unity2Foxglove`.
- If building IL2CPP, Python and the target Unity build module are installed.

Once this checklist passes, continue to [01_Installation_and_Quick_Start](01_Installation_and_Quick_Start.md).
