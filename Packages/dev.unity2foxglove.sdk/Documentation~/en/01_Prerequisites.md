## 1. Purpose

Use this page to prepare the software needed for a first Unity2Foxglove setup.

This page is intentionally short. It does not explain package internals, sample implementation details, or network troubleshooting. Those topics are covered later in the manual.

## 2. Required Software

Install these before following the quick start.

| Software | Website | Requirement | Notes |
|---|---|---|---|
| Unity Editor | [Unity official website](https://unity.com/) | Unity 6000.0 LTSC or later | The SDK core is compatible with 6000.0.74f1 LTSC or later. The repository demo project is developed and tested on 6000.3.14f1 LTSC. Unity 2022 is not supported. |
| Foxglove Desktop | [Foxglove download page](https://foxglove.dev/download) | Latest stable desktop app | Used to connect to Unity, inspect topics, view 3D data, view images, edit parameters, call services, and open MCAP files. |
| Git | [Git download page](https://git-scm.com/downloads) | Any recent version | Needed if you clone the repository or install the package from a Git URL. |

You do not need ROS to use Unity2Foxglove.

> [!NOTE]
> Current manual validation has been performed on Windows 10 LTSC. Other desktop platforms have not been fully validated yet. If you hit a compatibility issue, contact the maintainer, open a GitHub issue, or submit a pull request with the platform details and reproduction steps.

## 3. Recommended Unity Setup

Install Unity through Unity Hub. During installation, keep the Visual Studio option enabled if Unity Hub offers it. This is the simplest setup for most Windows users because Unity selects the matching editor integration/workload.

For normal Editor Play Mode, the standard Unity installation is enough.

For standalone Player builds, also install the platform module you plan to build:

| Target | Unity Hub module |
|---|---|
| Windows | Windows Build Support with IL2CPP |
| Linux | Linux Build Support with IL2CPP |
| macOS | macOS Build Support |

If you only want to run the quick start in the Unity Editor, you can skip the Player build modules for now.

## 4. Optional Developer Tools

These tools are useful when you edit code, run validation tests, or use repository scripts. They are not required for simply importing the package and pressing Play.

| Tool | Website | Use it when |
|---|---|---|
| Visual Studio | [Visual Studio download page](https://visualstudio.microsoft.com/downloads/) | You write C# scripts, custom publishers, `[FoxRun]` fields, Parameters, or Services. Recommended default on Windows. |
| JetBrains Rider | [Rider product page](https://www.jetbrains.com/rider/) | You prefer Rider for Unity/C# development. |
| Visual Studio Code | [VS Code download page](https://code.visualstudio.com/) | You want a lightweight editor with C# and Unity extensions. |
| Python 3 | [Python download page](https://www.python.org/downloads/) | You run repository helper scripts such as `Scripts/build_unity_il2cpp.py`. |
| .NET SDK | [.NET download page](https://dotnet.microsoft.com/download) | You run runtime validation tests or performance baselines from the command line. |

## 5. First-Run Checklist

Before continuing, confirm:

- Unity opens your project without package errors.
- Foxglove Desktop is installed.
- You can press Play in Unity.
- If you plan to build a standalone Player, the matching Unity build module is installed.
- If you plan to run repository scripts, Python is available from your terminal.

For the smallest first test, continue to [02_Installation_and_Quick_Start](02_Installation_and_Quick_Start.md).

For a package sample instead of a blank setup, use [05_Verifying_Basic_Visualization](05_Verifying_Basic_Visualization.md).
