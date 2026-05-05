# Phase 15 - DX 增强 + MCAP 补齐

## Context

Phase 14 完成了 `[FoxgloveLog]` attribute + source generator + Hub，开发者可以一行标注自动发布 topic。但命名太臃肿——`FoxgloveLog` 14 个字符。

Phase 15 首项任务：将用户可见的 `[FoxgloveLog]` 重命名为 `[FoxRun]`（PascalCase，C# attribute 惯例）。`Fox` = Foxglove 简称，`Log` = 记录。其余 MCAP 补齐任务后续细化。

## Goal

- [ ] `[FoxgloveLog]` → `[FoxRun]` 全量重命名，用户写 `[FoxRun("/debug/health", RateHz = 5)]`
- [ ] Source generator 适配新 attribute 名，诊断 ID 同步更新
- [ ] 预编译 ISG DLL 重新构建并替换
- [ ] 测试、示例、文档全部更新
- [ ] dotnet test 全量通过

## Design Decisions

| 决策 | 选择 |
|------|------|
| Attribute 名 | `[FoxRun]` PascalCase（C# attribute 惯例） |
| 类名 | `FoxRunAttribute`（`[FoxRun]` 自动匹配） |
| 内部接口/类 | `IFoxgloveLogSource`、`FoxgloveLogHub` 等不改（用户不可见） |
| 旧名兼容 | 不兼容 `[FoxgloveLog]`（Phase 14 刚完成，无下游用户） |
| 诊断 ID | `FXLOG001/002` → `FOXRUN001/002` |
| 生成文件名 | `_FoxgloveLog.g.cs` → `_FoxRun.g.cs` |
| Hub GameObject | `[FoxgloveLogHub]` → `[FoxRunHub]`（Unity Hierarchy 可见） |

## References

- Phase 14 plan: `[[15_PHASE14_PLAN]]`
- `FoxgloveLogAttribute.cs` — attribute 定义
- `FoxgloveLogSourceGenerator.cs` — Roslyn ISG
- `FoxgloveLogHub.cs` — 运行时 singleton hub
- `FoxgloveLog.md` — 用户文档

## Target Files

修改：

- `Runtime/Unity/Attributes/FoxgloveLogAttribute.cs` — 类名 `FoxgloveLogAttribute` → `FoxRunAttribute`
- `Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs` — ISG 搜索字符串/诊断/生成文件名
- `Runtime/Unity/FoxgloveLogHub.cs` — GameObject 名称 `[FoxgloveLogHub]` → `[FoxRunHub]`
- `Tests/Runtime/Phase14Validation.cs` — 类型引用和诊断字符串更新
- `Tests/Runtime/Phase7Validation.cs` — 如有 FoxgloveLog 引用需更新
- `Untiy2Foxglove/Assets/Scripts/TestLog.cs` — `[FoxgloveLog]` → `[FoxRun]`
- `Documentation~/FoxgloveLog.md` — 文档全量更新
- `Documentation~/ISG 构建过程.md` — 文档更新
- `Documentation~/Architecture.md` — Logger 部分是 `IFoxgloveLogger`，与 `[FoxgloveLog]` 无关，不改
- `Documentation~/README.md` — 同上 `IFoxgloveLogger` 不变，如有 `[FoxgloveLog]` 引用则更新
- `16_PHASE15_PLAN.md` — 本文档
- `00_PLAN.md` — 更新 Phase 14 的描述引用

重新构建（需 Unity 侧操作）：

- `Editor/SourceGenerators/analyzers/dotnet/cs/FoxgloveLogSourceGenerator.dll` — 重新 `dotnet build` 并替换

## Suggested Execution Order

```
15A-1 (Attribute 改名) → 15A-2 (ISG 适配) → 15A-3 (Hub GameObject 名) → 15A-4 (测试更新) → 15A-5 (示例更新) → 15A-6 (重新构建 ISG DLL) → 15A-7 (文档更新) → 15A-8 (plan 文档更新)
```

**关键依赖**：15A-1 必须先改，因为 ISG 引用了 attribute 的完整类型名。15A-6 必须在 ISG 源码改完后才能 re-build。

---

## Batch 15A：`[FoxgloveLog]` → `[FoxRun]` 重命名

### Task 15A-1：Rename `FoxgloveLogAttribute` → `FoxRunAttribute`

**文件**：`Runtime/Unity/Attributes/FoxgloveLogAttribute.cs`

改动：

```csharp
// class 名：FoxgloveLogAttribute → FoxRunAttribute

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Mark a field or property to be auto-published as a Foxglove topic.
    /// Usage: [FoxRun("/debug/health", RateHz = 5)]
    /// The annotated class must be declared as partial.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
    public class FoxRunAttribute : Attribute
    {
        /// <summary>Foxglove topic name (e.g. "/debug/pose").</summary>
        public string Topic { get; }

        /// <summary>Publish rate in Hz (default 10).</summary>
        public float RateHz { get; set; } = 10f;

        /// <summary>Optional Foxglove schema name. If empty, publishes schemaless JSON.</summary>
        public string SchemaName { get; set; }

        public FoxRunAttribute(string topic)
        {
            Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        }
    }
}
```

验收：
- [ ] 类名正确：`typeof(FoxRunAttribute)` 可解析
- [ ] `[FoxRun("/test")]` 编译通过

---

### Task 15A-2：Update `FoxgloveLogSourceGenerator` ISG

**文件**：`Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs`

改动点（共 6 处）：

1. **搜索常量**（第 15-16 行）：
```csharp
private const string AttrShortName = "FoxRun";
private const string AttrFullName = "Unity.FoxgloveSDK.Components.FoxRunAttribute";
```

2. **诊断 ID**（第 246-252 行）：
```csharp
"FOXRUN001", "Class not partial",
"Class '{0}' must be declared partial to use [FoxRun]",
"FoxRun", DiagnosticSeverity.Error, true);

"FOXRUN002", "Topic schema conflict",
"Topic '{0}' has conflicting SchemaName values across fields",
"FoxRun", DiagnosticSeverity.Warning, true);
```

3. **生成文件名**（第 209 行）：
```csharp
spc.AddSource($"{className}_FoxRun.g.cs", sb.ToString());
```

4. **诊断消息字符串**（第 247 行）：`"Class '{0}' must be declared partial to use [FoxRun]"`

5. **`Diags` 类上的 `DiagnosticDescriptor` `category` 参数**（第 248/252 行）：`"FoxRun"`

6. **注释**（第 70 行）：`// Verify attribute is actually FoxRunAttribute`

7. **字段名去下划线冲突检测**（`EmitClass` 方法）：`TrimStart('_')` 可能导致同名（如 `_x` 和 `x` 在同一 topic），加冲突检测 + `NameConflict` 诊断：
   - 新增 `FOXRUN003` diagnostic
   - 检测逻辑：`cleanNames.Distinct().Count() < cleanNames.Count` 时报告

验收：
- [ ] ISG 源码无编译错误
- [ ] 搜索 `FoxgloveLog` 全文无残留（除类名 `FoxgloveLogSourceGenerator` 本身和注释中的历史引用）

---

### Task 15A-3：Update `FoxgloveLogHub`

**文件**：`Runtime/Unity/FoxgloveLogHub.cs`

改动 2 处：

1. **GameObject 名称**（第 33 行）：`[FoxgloveLogHub]` → `[FoxRunHub]`

```csharp
var go = new GameObject("[FoxRunHub]");
```

2. **`_mgr` 查找加 cooldown**（Code Review Bug #3）：当前 `_mgr == null` 时每帧都调 `FindFirstObjectByType`，浪费性能。

```csharp
// 新增字段
private float _mgrSearchCooldown;

// Update() 开头改为
if (_mgr == null)
{
    _mgrSearchCooldown -= Time.deltaTime;
    if (_mgrSearchCooldown <= 0f)
    {
        _mgrSearchCooldown = 3f;
        _mgr = FindFirstObjectByType<FoxgloveManager>();
    }
    if (_mgr == null) return;
}
```

其余 `IFoxgloveLogSource` 接口、`FoxgloveLogHub` 类名、`FoxgloveLogTopicInfo` struct 名均不变。

验收：
- [ ] Unity Play Mode 中 Hierarchy 显示 `[FoxRunHub]`
- [ ] 无 FoxgloveManager 时不会每帧调用 `FindFirstObjectByType`

---

### Task 15A-4：Update Tests

**文件**：`Tests/Runtime/Phase14Validation.cs`

改动：
- 所有 `new Components.FoxgloveLogAttribute(...)` → `new Components.FoxRunAttribute(...)`
- `typeof(Components.FoxgloveLogAttribute)` → `typeof(Components.FoxRunAttribute)`
- 注释 `[FoxgloveLog]` → `[FoxRun]`
- 测试方法名可保留 `TestFoxgloveLog...` 不改（内部方法名，无影响）

**文件**：`Tests/Runtime/Phase7Validation.cs`（如引用 FoxgloveLog 相关类型）

复查并更新。

验收：
- [ ] `dotnet test` Phase 14 全部通过
- [ ] 无编译错误

---

### Task 15A-5：Update Sample Script

**文件**：`Untiy2Foxglove/Assets/Scripts/TestLog.cs`

```csharp
using UnityEngine;
using Unity.FoxgloveSDK.Components;

public partial class TestLog : MonoBehaviour
{
    [FoxRun("/debug/position")]
    private Vector3 _pos;

    [FoxRun("/debug/health", RateHz = 5)]
    private float _health = 100f;

    void Update() { _pos = transform.position; }
}
```

验收：
- [ ] Unity 编译通过，ISG 生成 `TestLog_FoxRun.g.cs`
- [ ] Play Mode 中 topic `/debug/position` 和 `/debug/health` 正常发布

---

### Task 15A-6：Rebuild ISG DLL

源码改完后，重新构建预编译 DLL：

```bash
cd Editor/SourceGenerators
dotnet restore FoxgloveLogSourceGenerator.csproj
dotnet build FoxgloveLogSourceGenerator.csproj -c Release
# Directory.Build.props 已将 obj/bin 重定向到 ../../build/SourceGenerators/
cp ../../../../build/SourceGenerators/Release/netstandard2.0/FoxgloveLogSourceGenerator.dll analyzers/dotnet/cs/
```

> 注意：csproj 文件名和输出 DLL 名保持 `FoxgloveLogSourceGenerator` 不变（Unity 通过 .meta 文件 `RoslynAnalyzer` label 引用 DLL，改名需要重新配置 .meta）。

验收：
- [ ] DLL 成功覆盖 `analyzers/dotnet/cs/FoxgloveLogSourceGenerator.dll`
- [ ] Unity 重启后 ISG 正常工作，扫描 `[FoxRun]` 标注

---

### Task 15A-7：Update Documentation

**文件**：`Documentation~/FoxgloveLog.md`
- 标题 `# [FoxgloveLog]` → `# [FoxRun]`
- 全文 `[FoxgloveLog]` → `[FoxRun]`
- `FoxgloveLogAttribute` → `FoxRunAttribute`
- 代码示例更新
- 架构图中生成文件名 `TestLog_FoxgloveLog.g.cs` → `TestLog_FoxRun.g.cs`

**文件**：`Documentation~/ISG 构建过程.md`
- 全文 `[FoxgloveLog]` → `[FoxRun]`
- `FoxgloveLogAttribute` → `FoxRunAttribute`
- 生成文件名更新

**文件**：`Documentation~/README.md` 和 `Documentation~/Architecture.md`
- `IFoxgloveLogger` 相关保持不变（这是内部日志接口，与 attribute 无关）
- 如有 `[FoxgloveLog]` 引用则更新

---

### Task 15A-8：Update Plan Documents

**文件**：`15_PHASE14_PLAN.md`
- 文档中对 `[FoxgloveLog]` 的描述性引用更新为 `[FoxRun]`
- Target Files 中的 `FoxgloveLogAttribute.cs` 已在 15A-1 改名，留记录

**文件**：`00_PLAN.md`
- Phase 14 描述中的 `[FoxgloveLog]` 更新为 `[FoxRun]`

**文件**：`16_PHASE15_PLAN.md`（本文档）
- checkbox 打勾

---

## Acceptance Matrix

| 项 | 标准 |
|----|------|
| `[FoxRun]` 编译 | `[FoxRun("/t")]` 语法通过，C# 自动匹配 `FoxRunAttribute` |
| ISG 扫描 | 源码生成器扫描 `[FoxRun]`，生成 `_FoxRun.g.cs` |
| 诊断 | `FOXRUN001`/`FOXRUN002` 正确触发 |
| Hub GameObject | `[FoxRunHub]` 出现在 Hierarchy |
| dotnet test | Phase 0-14 全通过 |
| `dotnet build` ISG | `FoxgloveLogSourceGenerator.dll` 重新构建成功 |
| 文档 | 无 `[FoxgloveLog]` 残留（内部接口名除外） |
| Regression | 已有功能不受影响 |

## Unity / Manual Tests

- [ ] Unity Editor 编译无错误，ISG 生成 `TestLog_FoxRun.g.cs`
- [ ] Play Mode：`[FoxRun]` 标注的字段自动发布到 Foxglove
- [ ] Hierarchy 中可见 `[FoxRunHub]` GameObject
- [ ] IL2CPP Player 构建成功，发布功能正常

## Risks

- **ISG DLL 替换**：DLL 文件名保持 `FoxgloveLogSourceGenerator.dll` 不变，只需替换文件内容，.meta 无需改动。改名可能引发 Unity 插件缓存问题，需重启 Unity。
- **下游影响**：Phase 14 刚完成，无外部用户，改名无兼容性负担。
- **IL2CPP 下 ISG 不生效**：以下 Batch 15B 专门解决。

---

## Batch 15B：IL2CPP Player 构建修复 — 生成真实 .g.cs 文件

### Context

Batch 15A 的 ISG DLL 只在 Editor 编译管线中执行（Editor 的 `Assembly-CSharp.rsp` 有 `-analyzer:FoxgloveLogSourceGenerator.dll`，Player 的没有）。导致 IL2CPP Player 构建的 `Assembly-CSharp.dll` 中缺少 `IFoxgloveLogSource` 实现代码，Hub 扫描不到任何 source，`[FoxRun]` 标注的字段不发布。

### Design Decisions

| 决策 | 选择 |
|------|------|
| 架构 | 双轨：Editor 继续 ISG `AddSource`，Player 构建前生成真实 `.g.cs` 文件 |
| 生成位置 | `Assets/Scripts/Generated/`（.gitignore 排除） |
| 死循环防护 | 生成前对比文件内容，完全一致不写入 |
| 触发时机 | `IPreprocessBuildWithReport.OnPreprocessBuild` — Unity `BuildPlayer` 前自动调用 |
| 生成逻辑 | 抽取 ISG 的 `EmitClass` 逻辑为独立的 `FoxrunCodeGenerator` 静态方法，ISG 和 build step 共用 |

### Root Cause

- Editor 编译 rsp (`1900b0aE.dag/Assembly-CSharp.rsp`) 含 `-analyzer:FoxgloveLogSourceGenerator.dll`
- Player 编译 rsp (`1900b0aP.dag/Assembly-CSharp.rsp`) **没有**此 analyzer
- IL2CPP 输出的 `Assembly-CSharp.cpp` 中 `TestLog` 只有 `Update` 和 `.ctor`，无 `IFoxgloveLogSource` 实现

### Task 15B-1：Extract code generation logic from ISG

**文件**：新建 `Editor/FoxrunCodeGenerator.cs`

将 ISG 的 `EmitClass` / `ValueExpr` / topic 聚合逻辑抽取为独立静态类：

```csharp
public static class FoxrunCodeGenerator
{
    /// <summary>
    /// Scans all assemblies for [FoxRun] fields/properties,
    /// generates source files under Assets/Scripts/Generated/.
    /// Returns list of generated file paths.
    /// </summary>
    public static List<string> GenerateSourceFiles();
}
```

验收：
- [ ] `dotnet build` ISG 编译通过
- [ ] 抽取后 ISG 仍然正常工作（共享逻辑，不改 ISG 行为）

### Task 15B-2：Add `IPreprocessBuildWithReport` build step

**文件**：新建 `Editor/FoxrunBuildPreprocess.cs`

```csharp
public class FoxrunBuildPreprocess : IPreprocessBuildWithReport
{
    public int callbackOrder => -100; // Before other preprocessors
    
    public void OnPreprocessBuild(BuildReport report)
    {
        // 1. Call FoxrunCodeGenerator.GenerateSourceFiles()
        // 2. AssetDatabase.Refresh() if any file was written
        // 3. Log generated files and topics
    }
}
```

死循环防护：
```csharp
// 生成前读取旧文件
if (File.Exists(outputPath) && File.ReadAllText(outputPath) == newContent)
    continue; // 内容相同，跳过
File.WriteAllText(outputPath, newContent);
```

验收：
- [ ] batchmode `BuildPlayer` 前自动生成 `.g.cs` 文件
- [ ] 内容不变时第二次构建不触发重新编译

### Task 15B-3：Add `.gitignore` for generated files

**文件**：新建 `Assets/Scripts/Generated/.gitignore`

```
*.g.cs
*.g.cs.meta
```

验收：
- [ ] 生成的文件不在 git 版本控制中

### Task 15B-4：Remove `RequestScriptCompilation` hack from FoxgloveBuild.cs

**文件**：修改 `Untiy2Foxglove/Assets/Editor/FoxgloveBuild.cs`

恢复为原始版本，移除 15A 中加的 `AssetDatabase.Refresh` + `RequestScriptCompilation` 临时方案。`IPreprocessBuildWithReport` 自动在 `BuildPlayer` 前触发。

### Task 15B-5：Rebuild ISG DLL + IL2CPP verification

1. 重新构建 ISG DLL（`dotnet build` + 复制到 analyzers）
2. batchmode IL2CPP build
3. 验证点：
   - [ ] `il2cppOutput/Assembly-CSharp.cpp` 含 `IFoxgloveLogSource.FoxgloveLog_Publish`
   - [ ] `il2cppOutput/Assembly-CSharp.cpp` 含 `/debug/position`
   - [ ] IL2CPP Player 运行时 Foxglove 可看到 `/debug/position` 和 `/debug/health` topic

---

## Acceptance Matrix（更新）

| 项 | 标准 |
|----|------|
| `[FoxRun]` 编译 | Editor + Player 均通过 |
| Editor Play Mode | `/debug/*` topic 正常发布 |
| IL2CPP Player | `/debug/position`、`/debug/health` topic 正常发布 |
| `.g.cs` 生成 | `Assets/Scripts/Generated/TestLog_FoxRun.g.cs` 在 build 前生成 |
| 死循环防护 | 内容无变化时不写入，不触发额外编译 |
| dotnet test | Phase 0-14 全通过 |
| Regression | Phase 10-12 录制/回放功能不受影响 |

## Risks

- **抽取 ISG 逻辑**：共享代码需要兼容 ISG（Roslyn API）和 build step（`System.Reflection`）两种上下文。可能需要两个入口，中间层共享 topic 聚合 + source 字符串拼接逻辑。
- **`OnPreprocessBuild` 时机**：如果生成文件触发 `AssetDatabase.Refresh` 太慢，可能影响构建时长。内容无变化时跳过 `Refresh` 可避免。
- **`.g.cs` 文件的 `.meta`**：Unity 可能在文件写入后自动导入，生成 `.meta` 文件。需要在 `.gitignore` 里排除。
