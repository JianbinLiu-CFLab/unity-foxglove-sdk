# Phase 14：Inspector UX + [FoxgloveLog] Attribute + Source Generator

## Context

Phase 12 已完成 MCAP 闭环。当前 SDK 功能矩阵基本完整，但开发者体验（DX）还有提升空间：
1. Inspector 路径字段是纯文本输入，用户需要手敲完整路径
2. 接入新项目时需要手写 publisher 脚本，无法像 Rerun 那样一行 `log()` 搞定
3. IL2CPP link.xml 需要手动维护，容易漏

Phase 14 聚焦 DX 改进，不增加新的协议能力。

## Goal

- [ ] `FoxgloveManagerEditor.cs` — 3 个路径字段各配 Browse 按钮。
- [ ] `[FoxgloveLog]` attribute — 标注 C# 字段/属性，自动发布为 Foxglove topic。
- [ ] `FoxgloveLogSourceGenerator` — Roslyn ISG，扫描 `[FoxgloveLog]` 标注，生成 publisher 代码。
- [ ] 全量回归测试 + IL2CPP 无回归。

**砍掉**:
- link.xml 自动生成：现有 `link.xml` 已 preserve `Unity.FoxgloveSDK` 和 `Newtonsoft.Json`，被标注的用户脚本类型由 `[Preserve]` attribute 或用户手动维护 link.xml 覆盖。
- Protobuf：Phase 15 再说。

## Design Decisions

| 决策 | 选择 |
|------|------|
| Browse 按钮实现 | `CustomEditor` + `EditorGUILayout` |
| 路径处理 | 选择后绝对路径→相对路径转换（基于 `Application.dataPath`） |
| Source generator | Roslyn Incremental Source Generator |
| `[FoxgloveLog]` 输出 | `Foxglove.Log` topic，schemaless JSON |
| 支持的类型 | `Vector3`, `Quaternion`, `float`, `int`, `string`, `bool`, `enum`（首批） |
| 类型→JSON 映射 | 硬编码映射表，无运行时反射 |
| ISG 验证 | 先写空 ISG echo（hello-world smoke test），确认 Unity 编译链能发现 generator |
| 注入方式 | **Partial class**：ISG 为用户类生成 partial 块，直接访问私有字段，不额外挂 MonoBehaviour |
| 频率控制 | **全局 Hub 分组采样**：同 topic 字段合并到单个 `Update` 调用，共用计时器 |
| IL2CPP 保留 | 生成的代码加 `[Preserve]`；SDK 核心用静态 `link.xml`；用户类型走 `[Preserve]` |

## References

- Roslyn Source Generators: `Microsoft.CodeAnalysis.CSharp`
- Foxglove Schemas: `foxglove.Log` 可自定义 schema

## Target Files

新增：

- `Editor/FoxgloveManagerEditor.cs`
- `Runtime/Unity/Attributes/FoxgloveLogAttribute.cs`
- `Editor/SourceGenerators/FoxgloveLogSourceGenerator.cs`
- `Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj`

修改：

- `Runtime/Unity/FoxgloveManager.cs`
- `Runtime/Unity.FoxgloveSDK.asmdef`
- `Tests/Runtime/Phase14Validation.cs`
- `Tests/Runtime/FoxgloveSdk.Tests.csproj`
- `Tests/Runtime/Program.cs`
- `14_PHASE14_PLAN.md`
- `00_PLAN.md`
- `Documentation~/README.md`
- `Documentation~/Architecture.md`

## Suggested Execution Order

```
14A (Inspector Browse) → 14B (Attribute 定义) → 14C-0 (空 ISG smoke test) → 14C (ISG 逻辑) → 14D (测试 + 文档)
```

ISG 放最后，每加一点逻辑就编译一次确认生效。空 ISG 先确认 Unity 编译链能发现它，再填充代码生成逻辑。

---

## Batch 14A：Inspector Browse 按钮

- [ ] 新建 `Editor/FoxgloveManagerEditor.cs`，`[CustomEditor(typeof(FoxgloveManager))]`。
- [ ] `_replayFilePath` — 文件选择（`*.mcap` 过滤），调 `EditorUtility.OpenFilePanel`。
- [ ] `_recordingDirectory` — 目录选择，调 `EditorUtility.OpenFolderPanel`。
- [ ] `_assetRoots.localRoot` — 目录选择，调 `EditorUtility.OpenFolderPanel`。
- [ ] 路径用相对路径或绝对路径，保持与项目根目录的关系（如 `Assets/../Recordings`）。
- [ ] Browse 按钮仅在 Editor 下有效，不影响 Runtime builds。
- [ ] 路径回填前做绝对→相对转换（基于 `Application.dataPath`）。

验收：

- [ ] Editor Play Mode：每个路径字段旁有 Browse 按钮，点击打开系统对话框。
- [ ] 选择后路径正确回填到 Inspector 字段（相对路径）。

## Batch 14B：[FoxgloveLog] Attribute

- [ ] 新建 `Attributes/FoxgloveLogAttribute.cs`。
  - `[AttributeUsage(Fields | Properties)]`
  - 参数：`string topic`（topic 名称）、`float rateHz = 10`（发布频率）
  - 可选的 `string schemaName`（若未指定，走 schemaless JSON）
- [ ] 标注 C# 字段/属性，类型支持 `Vector3`、`Quaternion`、`float`、`int`、`string`、`bool`、`enum`。
- [ ] 多个 `[FoxgloveLog]` 字段可聚合到一个 publisher。
- [ ] **要求用户类声明为 `partial class`**。
- [ ] ISG 生成的代码作为用户类的 partial 扩展，直接访问私有字段。
- [ ] 按 topic 分组，同一 topic 的字段共用 `FixedUpdate` 调用（Hub 模式）。
- [ ] topic 冲突时 ISG 输出编译 Diagnostic 警告。
- [ ] 类型→JSON 映射：硬编码 `switch`（无反射、IL2CPP 安全）。

验收：

- [ ] Editor Play Mode：挂 `[FoxgloveLog]` 的 MonoBehaviour 自动在 Foxglove 中显示 topic。
- [ ] 数据值与 Unity Inspector 一致。

## Batch 14C-0：空 Source Generator Smoke Test

- [ ] 新建 `Editor/SourceGenerators/FoxgloveLogSourceGenerator.cs`，实现 `IIncrementalGenerator`。
- [ ] `Initialize()` 仅生成一个空的 `.g.cs`（如 `// FoxgloveLog SG loaded`）。
- [ ] `FoxgloveLogSourceGenerator.csproj`，target `netstandard2.0`，引用 `Microsoft.CodeAnalysis.CSharp` 4.x。
- [ ] 输出路径：`analyzers/dotnet/cs`。
- [ ] 编译 Unity 项目，确认 `.g.cs` 出现在编译输出中。
- [ ] **如果 smoke test 不通过，搁置 Batch 14C/14D，通过文档记录问题和替代方案。**

## Batch 14C：Source Generator 逻辑

- [ ] 扫描所有带 `[FoxgloveLog]` 的字段/属性。
- [ ] 按 topic 聚合：同一 topic 的多个字段合并到同一个 publisher。
- [ ] 生成 `.g.cs`：`<ClassName>_FoxgloveLog.g.cs`。
- [ ] 生成的代码作为用户 `partial class` 的扩展，直接访问私有字段。
- [ ] 生成的代码自动加 `[Preserve]` 标签（IL2CPP 安全，不靠 link.xml）。
- [ ] 结构：
  - 声明 `partial class <ClassName>`。
  - `Awake()` 中 `GetOrRegisterSchemaChannel`。
  - 在 `Update` / `FixedUpdate` 中按频率发布。
  - 类型→JSON 硬编码映射表（无运行时反射，IL2CPP 安全）。
- [ ] 生成的 publisher 作为 `MonoBehaviour` 挂到同 GameObject 上。

**验收**:
- [ ] 编译包含 `[FoxgloveLog]` 标注的脚本 → 自动生成 publisher 代码。
- [ ] IL2CPP build 后无编译错误（标注类型是否被裁剪由用户 link.xml 保证）。

验收：

- [ ] 编译包含 `[FoxgloveLog]` 标注的脚本 → 自动生成 publisher 代码。
- [ ] IL2CPP build 后 `[FoxgloveLog]` 标注的类型未被裁剪。
- [ ] dotnet test 中 source generator 诊断不报错。

## Batch 14D：Tests + Docs

- [ ] `TestBrowseButtonsPresent`：验证 CustomEditor 挂载成功。
- [ ] `TestFoxgloveLogAttribute`：验证 attribute 参数。
- [ ] `TestSourceGeneratorOutput`：验证 ISG 输出的 .g.cs 语法正确。
- [ ] `TestFoxgloveLogPublisherRoundtrip`：标注简单字段 → publisher 正确发布到 FoxgloveManager。
- [ ] `TestFoxgloveLogIl2cppPreserve`：标注类型未��运行时反射裁剪（如果 link.xml 手动配置）。
- [ ] 更新 `Documentation~/README.md`、`Architecture.md`，补充坐标转换文档和 link.xml 手动维护说明。

## Unity / Manual Tests

- [ ] Editor Inspector：`FoxgloveManager` 的 `_replayFilePath` 旁有 Browse 按钮，点击弹出 `.mcap` 文件选择对话框。
- [ ] Editor Inspector：`_recordingDirectory` 旁有 Browse 按钮，点击弹出文件夹选择对话框。
- [ ] Editor Inspector：`_assetRoots` 每项的 `localRoot` 旁有 Browse 按钮，点击弹出文件夹选择对话框。
- [ ] Editor Play Mode：挂 `[FoxgloveLog]` 的 GameObject 自动发布 topic，Foxglove 可看到对应数据。
- [ ] Editor Play Mode：`[FoxgloveLog]` 修改字段值 → Foxglove 中数值实时更新。
- [ ] Editor Play Mode：多个 `[FoxgloveLog]` 字段 → 各自独立 topic，不冲突。
- [ ] IL2CPP Player：`[FoxgloveLog]` 标注的类型未被裁剪，发布功能正常。
- [ ] Regression：Phase 10-14 录制/回放功能不受影响。

## Acceptance Matrix

| 项 | 标准 |
|----|------|
| dotnet tests | Phase 0-14 全通过 |
| Editor compile | 无 C# compile error |
| IL2CPP build | Windows Player build 成功 |
| Browse buttons | 3 个路径字段均可弹窗选择 |
| `[FoxgloveLog]` | 标注字段自动发布到 Foxglove |
| Source generator | 编译时生成 publisher 代码 |
| `[Preserve]` | 生成代码自动保留，IL2CPP 不裁剪 |
| Regression | Phase 10-12 全部通过 |

## Risks

- **Source generator 调试困难**: ISG 错误不在 Visual Studio 中直接显示，需通过 `dotnet build --diag` 或 `Analyzer` 报告排查。处理策略：先在普通 .cs 文件中验证 publisher 逻辑，再迁移到 ISG。
- **IL2CPP + ISG 组合**: ISG 输出在 IL2CPP 编译链中可能遇到路径/时序问题。早期 smoke test 确认 ISG 输出的 `.g.cs` 在 IL2CPP 下正常编译。
- **`[FoxgloveLog]` 过度简化**: schemaless JSON 没有类型安全，若要强类型 schema 仍需手写 publisher。`[FoxgloveLog]` 定位为快速原型/日志场景，不是替代 `FoxglovePublisher<T>`。
- **Topic 冲突**: 多个脚本标注同一 topic → ISG 输出 Diagnostic 编译警告，非运行时覆盖。
- **GC Alloc**: JSON 序列化产生内存碎片 → 使用 `StringBuilder` 或预分配缓冲。
