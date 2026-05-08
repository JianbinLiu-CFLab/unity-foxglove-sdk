# Scripts

Project-level helper scripts. All scripts use relative paths derived from their own location to resolve the workspace root — no hardcoded absolute paths.

## Unity IL2CPP Build

Entry script:

```text
Scripts/build_unity_il2cpp.py
```

Purpose:

- One command to start a Unity batchmode IL2CPP build.
- Supports Windows, Linux, and macOS standalone targets.
- Creates a timestamped directory per build, keeping the log and Player output together.
- Invokes Unity-side `FoxgloveBuild.BuildIl2CppFromCommandLine`.

### Basic Usage

Run from the workspace root:

```powershell
python Scripts\build_unity_il2cpp.py
```

The default target is selected based on the current system:

- Windows -> `win64`
- Linux -> `linux64`
- macOS -> `macos`

### Specifying Target

```powershell
python Scripts\build_unity_il2cpp.py --target win64
python Scripts\build_unity_il2cpp.py --target linux64
python Scripts\build_unity_il2cpp.py --target macos
```

### Dry Run

Validate paths and parameters without launching Unity:

```powershell
python Scripts\build_unity_il2cpp.py --target win64 --dry-run
```

Sample output:

```text
[build_unity_il2cpp] Project:   Unity2Foxglove
[build_unity_il2cpp] Target:    win64
[build_unity_il2cpp] Log:       build\Unity\win64-il2cpp-20260505-141111\build.log
[build_unity_il2cpp] Output:    build\Unity\win64-il2cpp-20260505-141111\WindowsIL2CPP\FoxgloveDemo.exe
[build_unity_il2cpp] Dry run only; Unity was not started.
```

### Build Progress Output

During a real build, the script prints a heartbeat periodically and captures key lines from the Unity log:

```text
[build_unity_il2cpp] Elapsed 00:45; still building. Log: build\Unity\win64-il2cpp-...
[unity-log] [FoxrunBuildPreprocess] Generating FoxRun source files...
[unity-log] [FoxgloveBuild] Starting Windows IL2CPP Player build...
[unity-log] Build succeeded: build/Unity/WindowsIL2CPP/FoxgloveDemo.exe
```

Default heartbeat interval is 15 seconds. To adjust:

```powershell
python Scripts\build_unity_il2cpp.py --target win64 --progress-interval 30
```

### Specifying Unity Path

If the script cannot auto-detect Unity, pass the path manually:

```powershell
python Scripts\build_unity_il2cpp.py --target win64 --unity "C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe"
```

Or set an environment variable:

```powershell
$env:UNITY_EXE="C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe"
python Scripts\build_unity_il2cpp.py --target win64
```

Script search order:

1. `--unity`
2. `UNITY_EXE`
3. `UNITY_PATH`
4. Common Unity Hub installation directories

### Specifying Log Path

`--log` uses a path relative to the workspace root:

```powershell
python Scripts\build_unity_il2cpp.py --target win64 --log build\Unity\manual-win64-il2cpp.log
```

Default log format:

```text
build/Unity/<target>-il2cpp-<timestamp>/build.log
```

### Output Path

Each build creates an isolated timestamped directory by default:

```text
build/Unity/<target>-il2cpp-<timestamp>/
├── build.log
└── <Platform>IL2CPP/
    └── FoxgloveDemo...
```

Default Player output:

```text
build/Unity/<run>/WindowsIL2CPP/FoxgloveDemo.exe
build/Unity/<run>/LinuxIL2CPP/FoxgloveDemo.x86_64
build/Unity/<run>/MacOSIL2CPP/FoxgloveDemo.app
```

You can specify the entire build directory:

```powershell
python Scripts\build_unity_il2cpp.py --target win64 --build-dir build\Unity\manual-win64
```

Or specify the Player output path:

```powershell
python Scripts\build_unity_il2cpp.py --target win64 --output build\Unity\manual-win64\WindowsIL2CPP\FoxgloveDemo.exe
```

### Cross-Platform Notes

- Building for Linux/macOS requires the corresponding Unity Build Support modules.
- A Windows host may not produce fully signed/releasable macOS Players; build macOS on macOS when possible.
- IL2CPP builds are time-consuming — use `--dry-run` first to validate parameters.
- Close the Unity Editor before building to avoid Library/script compilation state conflicts.

### Relationship with FoxRun ISG

IL2CPP builds trigger `FoxrunBuildPreprocess`:

```text
[FoxrunBuildPreprocess] Generating FoxRun source files...
```

If `/debug/*` topics are not visible in the Player, first check the build log for this message, and verify that `Unity2Foxglove/Assets/Scripts/Generated/*_FoxRun.g.cs` files were generated.
