# Phase 14：Inspector UX + [FoxgloveLog] Attribute + Source Generator

## Context

Phase 12 已完成 MCAP 闭环。当前 SDK 功能矩阵基本完整，但开发者体验（DX）还有提升空间：
1. Inspector 路径字段是纯文本输入，用户需要手敲完整路径
2. 接入新项目时需要手写 publisher 脚本，无法像 Rerun 那样一行 `log()` 搞定
3. IL2CPP link.xml 需要手动维护，容易漏

Phase 14 聚焦 DX 改进，不增加新的协议能力。

## Goal

- [ ] `FoxgloveManagerEditor.cs` — 3 个路径字段各配 Browse 按钮（`_replayFilePath`、`_recordingDirectory`、`_assetRoots.localRoot`）。
- [ ] `[FoxgloveLog]` attribute — 标注 C# 字段/属性，自动发布为 Foxglove topic。
- [ ] `FoxgloveLogSourceGenerator` — Editor-time source generator，扫描 `[FoxgloveLog]` 标注，生成 publisher 代码和 link.xml。
- [ ] IL2CPP link.xml 自动生成 — source generator 子功能，preserve 被标注的类型。
- [ ] Windows IL2CPP Player 验收通过（无回归）。

## Design Decisions

| 决策 | 选择 |
|------|------|
| Browse 按钮实现 | `CustomEditor` + `EditorGUILayout` |
| Source generator | Roslyn Incremental Source Generator |
| `[FoxgloveLog]` 输出 | `Foxglove.Log` topic，schemaless JSON |
| 反射方式 | ISG 编译时扫描，不依赖运行时反射 |
| link.xml 生成 | ISG 输出 `link.xml` 作为 embedded resource，或直接写 `Assets/link.xml` |
| 发布会自动注册 | `[FoxgloveLog]` 生成的代码在 `Awake` 中自动调用 `Manager.GetOrRegisterSchemaChannel` |
| Protobuf | 不做，Phase 14 |

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

## Batch 14A：Inspector Browse 按钮

- [ ] 新建 `Editor/FoxgloveManagerEditor.cs`，`[CustomEditor(typeof(FoxgloveManager))]`。
- [ ] `_replayFilePath` — 文件选择（`*.mcap` 过滤），调 `EditorUtility.OpenFilePanel`。
- [ ] `_recordingDirectory` — 目录选择，调 `EditorUtility.OpenFolderPanel`。
- [ ] `_assetRoots.localRoot` — 目录选择，调 `EditorUtility.OpenFolderPanel`。
- [ ] 路径用相对路径或绝对路径，保持与项目根目录的关系（如 `Assets/../Recordings`）。
- [ ] Browse 按钮仅在 Editor 下有效，不影响 Runtime builds。

验收：

- [ ] Editor Play Mode：每个路径字段旁有 Browse 按钮，点击打开系统对话框。
- [ ] 选择后路径正确回填到 Inspector 字段。

## Batch 14B：[FoxgloveLog] Attribute

- [ ] 新建 `Attributes/FoxgloveLogAttribute.cs`。
  - `[AttributeUsage(Fields | Properties)]`
  - 参数：`string topic`（topic 名称）、`float rateHz = 10`（发布频率）
  - 可选的 `string schemaName`（若未指定，走 schemaless JSON）
- [ ] 标注 C# 字段/属性，类型支持 `Vector3`、`Quaternion`、`float`、`int`、`string` 等常见类型。
- [ ] 生成的 publisher 代码扫描所有 `[FoxgloveLog]` 标注的成员。
- [ ] 生成的 publisher 在 `Awake` 中获取 `FoxgloveManager` 引用。
- [ ] 生成的 publisher 在 `Update` / `FixedUpdate` 中按指定频率发布。
- [ ] 多个 `[FoxgloveLog]` 字段可聚合到一个 publisher 类。

验收：

- [ ] Editor Play Mode：挂 `[FoxgloveLog]` 的 MonoBehaviour 自动在 Foxglove 中显示 topic。
- [ ] 数据值与 Unity Inspector 一致。

## Batch 14C：Source Generator + link.xml

- [ ] 新建 `FoxgloveLogSourceGenerator.cs`，基于 Roslyn `IIncrementalGenerator`。
- [ ] 扫描所有带 `[FoxgloveLog]` 的字段/属性。
- [ ] 生成 `.g.cs` 文件：`<ClassName>_FoxgloveLog_Publisher.g.cs`。
  - 包含完整的 publisher 类（`FoxglovePublisherBase` 子类或独立逻辑）。
  - 按 topic 聚合：同一 topic 的多个字段合并到同一个 publisher。
- [ ] 生成 `link.xml`：为每个被标注的类型添加 `<assembly preserve="all" />` 或具体类型的 `<type preserve="all" />`。
- [ ] source generator 作为独立的 `.csproj`，引用 `Microsoft.CodeAnalysis.CSharp` 4.x。
- [ ] `FoxgloveLogSourceGenerator.csproj` 输出为 `analyzers/dotnet/cs`。

验收：

- [ ] 编译包含 `[FoxgloveLog]` 标注的脚本 → 自动生成 publisher 代码。
- [ ] IL2CPP build 后 `[FoxgloveLog]` 标注的类型未被裁剪。
- [ ] dotnet test 中 source generator 诊断不报错。

## Batch 14D：Tests + Docs

- [ ] `TestBrowseButtonsPresent`：验证 CustomEditor 挂载成功。
- [ ] `TestFoxgloveLogAttribute`：验证 attribute 参数。
- [ ] `TestSourceGeneratorOutput`：验证 ISG 输出的 .g.cs 语法正确。
- [ ] `TestLinkXmlGeneration`：验证 link.xml 包含正确的 type preserve。
- [ ] `TestFoxgloveLogPublisherRoundtrip`：标注简单字段 → publisher 正确发布到 FoxgloveManager。
- [ ] 更新 `Documentation~/README.md` 和 `Architecture.md`。

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
| link.xml 自动生成 | IL2CPP 不裁剪被标注类型 |
| Regression | Phase 10-12 全部通过 |

## Risks

- **Source generator 调试困难**: ISG 错误不在 Visual Studio 中直接显示，需通过 `dotnet build --diag` 或 `Analyzer` 报告排查。处理策略：先在普通 .cs 文件中验证 publisher 逻辑，再迁移到 ISG。
- **IL2CPP + ISG 组合**: ISG 输出在 IL2CPP 编译链中可能遇到路径/时序问题。早期 smoke test 确认 ISG 输出的 `.g.cs` 在 IL2CPP 下正常编译。
- **`[FoxgloveLog]` 过度简化**: schemaless JSON 没有类型安全，若要强类型 schema 仍需手写 publisher。`[FoxgloveLog]` 定位为快速原型/日志场景，不是替代 `FoxglovePublisher<T>`。
