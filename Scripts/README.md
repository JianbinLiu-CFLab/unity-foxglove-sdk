# Scripts

这个目录放项目级辅助脚本。脚本应尽量使用相对路径，以 `Scripts/` 所在位置推导 workspace root，避免写死本机绝对路径。

## Unity IL2CPP 构建

入口脚本：

```text
Scripts/build_unity_il2cpp.py
```

用途：

- 一条命令启动 Unity batchmode IL2CPP 构建。
- 支持 Windows、Linux、macOS 三个 standalone target。
- 自动创建一次构建一个目录，把 log 和 Player 产物放在一起。
- 调用 Unity 侧 `FoxgloveBuild.BuildIl2CppFromCommandLine`。

### 基本用法

在 workspace root 运行：

```powershell
python Scripts\build_unity_il2cpp.py
```

默认 target 会按当前系统选择：

- Windows -> `win64`
- Linux -> `linux64`
- macOS -> `macos`

### 指定 target

```powershell
python Scripts\build_unity_il2cpp.py --target win64
python Scripts\build_unity_il2cpp.py --target linux64
python Scripts\build_unity_il2cpp.py --target macos
```

### Dry run

只检查路径和参数，不启动 Unity：

```powershell
python Scripts\build_unity_il2cpp.py --target win64 --dry-run
```

输出示例：

```text
[build_unity_il2cpp] Project:   Untiy2Foxglove
[build_unity_il2cpp] Target:    win64
[build_unity_il2cpp] Log:       build\Unity\win64-il2cpp-20260505-141111\build.log
[build_unity_il2cpp] Output:    build\Unity\win64-il2cpp-20260505-141111\WindowsIL2CPP\FoxgloveDemo.exe
[build_unity_il2cpp] Dry run only; Unity was not started.
```

### 构建进度输出

正式构建时，脚本会定期打印心跳和 Unity log 中的关键行：

```text
[build_unity_il2cpp] Elapsed 00:45; still building. Log: build\Unity\win64-il2cpp-...
[unity-log] [FoxrunBuildPreprocess] Generating FoxRun source files...
[unity-log] [FoxgloveBuild] Starting Windows IL2CPP Player build...
[unity-log] Build succeeded: build/Unity/WindowsIL2CPP/FoxgloveDemo.exe
```

默认每 15 秒打印一次心跳。可以调整：

```powershell
python Scripts\build_unity_il2cpp.py --target win64 --progress-interval 30
```

### 指定 Unity 路径

如果脚本没有自动找到 Unity，可以手动传入：

```powershell
python Scripts\build_unity_il2cpp.py --target win64 --unity "C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe"
```

也可以设置环境变量：

```powershell
$env:UNITY_EXE="C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe"
python Scripts\build_unity_il2cpp.py --target win64
```

脚本查找 Unity 的顺序：

1. `--unity`
2. `UNITY_EXE`
3. `UNITY_PATH`
4. Unity Hub 常见安装目录

### 指定 log 路径

`--log` 使用相对 workspace root 的路径：

```powershell
python Scripts\build_unity_il2cpp.py --target win64 --log build\Unity\manual-win64-il2cpp.log
```

默认 log 格式：

```text
build/Unity/<target>-il2cpp-<timestamp>/build.log
```

### 输出路径

默认每次构建创建一个独立目录：

```text
build/Unity/<target>-il2cpp-<timestamp>/
├── build.log
└── <Platform>IL2CPP/
    └── FoxgloveDemo...
```

默认 Player 输出：

```text
build/Unity/<run>/WindowsIL2CPP/FoxgloveDemo.exe
build/Unity/<run>/LinuxIL2CPP/FoxgloveDemo.x86_64
build/Unity/<run>/MacOSIL2CPP/FoxgloveDemo.app
```

可以指定整个构建目录：

```powershell
python Scripts\build_unity_il2cpp.py --target win64 --build-dir build\Unity\manual-win64
```

也可以指定 Player 输出路径：

```powershell
python Scripts\build_unity_il2cpp.py --target win64 --output build\Unity\manual-win64\WindowsIL2CPP\FoxgloveDemo.exe
```

### 跨平台注意事项

- 构建 Linux/macOS 需要安装对应的 Unity Build Support 模块。
- Windows 主机不一定能完整产出可签名/可发布的 macOS Player；macOS Player 最好在 macOS 主机上构建。
- IL2CPP 构建耗时较长，建议先用 `--dry-run` 确认参数。
- 构建前最好关闭 Unity Editor，避免 Library/脚本编译状态互相影响。

### 与 FoxRun ISG 的关系

IL2CPP 构建会触发 `FoxrunBuildPreprocess`：

```text
[FoxrunBuildPreprocess] Generating FoxRun source files...
```

如果 Player 里看不到 `/debug/*` topic，先检查 build log 中是否出现这条日志，以及 `Untiy2Foxglove/Assets/Scripts/Generated/*_FoxRun.g.cs` 是否生成。
