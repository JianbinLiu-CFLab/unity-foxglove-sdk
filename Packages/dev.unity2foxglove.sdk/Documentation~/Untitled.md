DeepSeek 走偏了。问题的根源很简单：

**Unity 编译器进程自带 Roslyn 4.2.2。Generator DLL 加载时，Roslyn 依赖由编译器进程自动提供。不需要也不应该把 Microsoft.CodeAnalysis.dll 放到 Unity 项目里。**

当前的两个错误：

1. `.csproj` 引用 Roslyn 4.7.0，但 Unity 只有 4.2.2 → **版本不匹配**
2. 把 Roslyn DLL 复制到 Unity 项目里 → Unity 当插件加载 → 崩溃

让我来清理和修复。

ISG 源码已被删除。现在需要从头正确搭建。先分析下结构：

**当前问题：**

1. `.csproj` 引用 Roslyn 4.7.0，Unity 只有 4.2.2 → `ForAttributeWithMetadataName` (4.3+) 不可用
2. `lib/` 下有 Roslyn DLL → 必须删掉
3. `.meta` 缺少 `RoslynAnalyzer` label → Unity 不认这是 generator
4. ISG 源码没了

**修复步骤：**

**第一步：清理垃圾文件，删掉不应存在的 Roslyn DLL**

**第二步：修复 .csproj — 降级到 Roslyn 4.2.2（匹配 Unity 6000.3.14f1）**

**第三步：写 ISG 源码（兼容 Roslyn 4.2.x，用 `CreateSyntaxProvider` 而非 `ForAttributeWithMetadataName`）**

**第四步：写 FoxgloveLogHub（运行时 singleton + interface）**

**第五步：编译 ISG DLL（用 dotnet CLI，输出到 analyzers 目录）**

.csproj 在父目录，源码在 `src/`。需要配置 .csproj 引用 src 下的文件。

编译成功。两个 warning 是 Roslyn analyzer 版本跟踪提示，可忽略。

**第六步：复制 DLL 到 analyzers 路径，替换旧版**

**第七步：修复 .meta 文件 — 加 RoslynAnalyzer label + 关闭引用验证**

**第八步：确保 `src/` 目录不被 Unity 扫描 — 加 .asmdef 隔离**

Unity 会递归扫描 `Editor/` 下的所有 `.cs`。`src/` 下的 ISG 源码引用了 `Microsoft.CodeAnalysis`，Unity 编译会报错。需要隔离。

这个 `.asmdef` 用 `defineConstraints: ["UNITY_EXCLUDE_FROM_COMPILATION"]` — 这个 define 永远不存在，所以 Unity 永远不会编译这个 assembly。源码只供 `dotnet build` 使用。

**第九步：清理残留的空 lib 目录**

`lib/` 有 .meta 残留，让我也清掉：

完美。`analyzers/dotnet/cs/` 下只有 generator DLL + 带 `RoslynAnalyzer` label 的 .meta。

**第十步：验证现有测试不受影响**

全部通过。现在总结下做了什么，以及你回 Unity 需要验证的事情：

---

## 完成的工作

### 文件结构（正确的 ISG 部署方式）

```
Editor/SourceGenerators/
├── FoxgloveLogSourceGenerator.csproj    # dotnet CLI 构建用
├── src/
│   ├── FoxgloveLogSourceGenerator.cs    # ISG 源码（被 .asmdef 隔离，Unity 不编译）
│   └── Unity.FoxgloveSDK.SourceGenerators.asmdef  # UNITY_EXCLUDE_FROM_COMPILATION
└── analyzers/dotnet/cs/
    ├── FoxgloveLogSourceGenerator.dll   # 预编译 DLL
    └── FoxgloveLogSourceGenerator.dll.meta  # labels: [RoslynAnalyzer], validateReferences: 0

Runtime/Unity/
├── FoxgloveLogHub.cs                    # IFoxgloveLogSource 接口 + Hub singleton
└── ...
```

### 关键设计决策

1. **Roslyn 4.2.0** — 匹配 Unity 6000.3.x 内置版本，用 `CreateSyntaxProvider` 而非 `ForAttributeWithMetadataName`(4.3+)
2. **不打包 Roslyn DLL** — generator 运行在 Unity 编译器进程里，Roslyn 由编译器自带
3. **`src/` 用 asmdef 隔离** — `defineConstraints: ["UNITY_EXCLUDE_FROM_COMPILATION"]` 让 Unity 永远跳过
4. **per-topic 频率控制** — Hub 为每个 source 的每个 topic 维护独立 timer
5. **2 秒扫描间隔** — Hub 用 `FindObjectsByType<MonoBehaviour>` + `IFoxgloveLogSource` 接口发现

### 你需要在 Unity 中验证

1. 打开 Unity，等编译完成，**不应有 CS 错误**
2. 确认 `FoxgloveLogSourceGenerator.dll` 在 Inspector 里有 `RoslynAnalyzer` label
3. 写个测试 MonoBehaviour 试试：

```csharp
using UnityEngine;
using Unity.FoxgloveSDK.Components;

public partial class TestLog : MonoBehaviour
{
    [FoxgloveLog("/debug/position")]
    private Vector3 _pos;

    [FoxgloveLog("/debug/health", RateHz = 5)]
    private float _health = 100f;

    void Update() { _pos = transform.position; }
}
```

如果 ISG 正常工作，编译时会自动生成 `TestLog_FoxgloveLog.g.cs`，实现 `IFoxgloveLogSource` 接口。运行时 `FoxgloveLogHub` 自动发现并按频率发布到 Foxglove。

然后我遇到
Assembly 'Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/obj/Release/netstandard2.0/FoxgloveLogSourceGenerator.dll' will not be loaded due to errors: Unable to resolve reference 'Microsoft.CodeAnalysis'. Is the assembly missing or incompatible with the current platform? Reference validation can be disabled in the Plugin Inspector. Unable to resolve reference 'Microsoft.CodeAnalysis.CSharp'. Is the assembly missing or incompatible with the current platform? Reference validation can be disabled in the Plugin Inspector. Unable to resolve reference 'System.Collections.Immutable'. Is the assembly missing or incompatible with the current platform? Reference validation can be disabled in the Plugin Inspector.

Unity 在扫描 `dotnet build` 产生的 `obj/` 和 `bin/` 目录里的中间产物。把构建输出路径移到 Unity 扫描范围之外。

删掉 Unity 项目内的 `bin/` 和 `obj/`：

验证重新构建输出到正确位置：

编译成功，输出到 `build/SourceGenerators/Release/...`（在 Unity 项目外）。MSB3539 warning 是 BaseIntermediateOutputPath 时序问题，用 `Directory.Build.props` 解决更干净：

重新编译验证 warning 消失：

MSB3539 消失。复制最新 DLL 到 analyzer 位置：

确认 Unity 项目内没有残留 `bin/`、`obj/`：

`obj/` 又被 restore 创建了。`Directory.Build.props` 的路径可能没生效（`obj/` 在 restore 阶段就创建）。让我检查：

NuGet restore 仍在项目本地创建 `obj/`。把 `MSBuildProjectExtensionsPath` 也重定向：

干净了。`bin/` 和 `obj/` 全部在 `build/SourceGenerators/` 下，Unity 不会扫到。复制最新 DLL：

现在 Unity 项目内 `Editor/SourceGenerators/` 的最终结构：

```
Editor/SourceGenerators/
├── Directory.Build.props          # 重定向 bin/obj 到项目外
├── FoxgloveLogSourceGenerator.csproj  # dotnet build 专用
├── src/
│   ├── FoxgloveLogSourceGenerator.cs  # ISG 源码
│   └── Unity.FoxgloveSDK.SourceGenerators.asmdef  # UNITY_EXCLUDE_FROM_COMPILATION
└── analyzers/dotnet/cs/
    ├── FoxgloveLogSourceGenerator.dll      # 预编译 DLL（唯一被 Unity 加载的）
    └── FoxgloveLogSourceGenerator.dll.meta # labels: [RoslynAnalyzer]
```

没有 `bin/`、`obj/`、没有 Roslyn DLL。回 Unity 重新编译，应该只剩 generator DLL 被正确加载为 Roslyn Analyzer，不会再有 "Unable to resolve reference" 错误。

完整步骤来了，一步不少：

---

## ISG 从零构建完整步骤

### 第 1 步：创建目录结构

```bash
mkdir -p Editor/SourceGenerators/src
mkdir -p Editor/SourceGenerators/analyzers/dotnet/cs
```

### 第 2 步：写 Directory.Build.props（重定向 bin/obj）

```xml
<Project>
  <PropertyGroup>
    <SgBuildRoot>$(MSBuildThisFileDirectory)..\..\..\..\build\SourceGenerators\</SgBuildRoot>
    <BaseOutputPath>$(SgBuildRoot)</BaseOutputPath>
    <BaseIntermediateOutputPath>$(SgBuildRoot)obj\</BaseIntermediateOutputPath>
    <MSBuildProjectExtensionsPath>$(SgBuildRoot)obj\</MSBuildProjectExtensionsPath>
    <RestoreOutputPath>$(SgBuildRoot)obj\</RestoreOutputPath>
  </PropertyGroup>
</Project>
```

### 第 3 步：写 FoxgloveLogSourceGenerator.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="src\**\*.cs" />
  </ItemGroup>
  <ItemGroup>
    <!-- Must match Unity's built-in Roslyn: 4.2.2 for Unity 6000.3.x -->
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.2.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

### 第 4 步：写 src/Unity.FoxgloveSDK.SourceGenerators.asmdef（隔离源码）

```json
{
  "name": "Unity.FoxgloveSDK.SourceGenerators",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "autoReferenced": false,
  "defineConstraints": ["UNITY_EXCLUDE_FROM_COMPILATION"],
  "noEngineReferences": true
}
```

### 第 5 步：写 src/FoxgloveLogSourceGenerator.cs（ISG 源码）

核心逻辑：
- `IIncrementalGenerator`（Roslyn 4.2 支持）
- `CreateSyntaxProvider` 扫描 `[FoxgloveLog]` 标注的字段/属性
- 生成 `partial class : IFoxgloveLogSource`，实现 `FoxgloveLog_Publish`
- 类型映射：Vector3 → `{x,y,z}`，Quaternion → `{x,y,z,w}`，基础类型直传
- 非 partial class 报 `FXLOG001` 错误、topic schema 冲突报 `FXLOG002` warning

### 第 6 步：用 dotnet CLI 编译 ISG DLL

```bash
cd Editor/SourceGenerators
dotnet restore FoxgloveLogSourceGenerator.csproj
dotnet build FoxgloveLogSourceGenerator.csproj -c Release
```

输出到 `build/SourceGenerators/netstandard2.0/FoxgloveLogSourceGenerator.dll`（不在 Unity 内）。

### 第 7 步：复制 DLL 到 analyzers 路径

```bash
cp ../../../../../build/SourceGenerators/netstandard2.0/FoxgloveLogSourceGenerator.dll analyzers/dotnet/cs/
```

### 第 8 步：写 .meta 文件（加 RoslynAnalyzer label + 关引用验证）

```yaml
fileFormatVersion: 2
guid: <任意 GUID>
labels:
- RoslynAnalyzer
PluginImporter:
  validateReferences: 0
  platformData:
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
```

### 第 9 步：写 FoxgloveLogHub.cs（运行时）

`Runtime/Unity/FoxgloveLogHub.cs`：
- `[RuntimeInitializeOnLoadMethod]` 自动创建 singleton
- `IFoxgloveLogSource` 接口：`FoxgloveLog_TopicCount`、`FoxgloveLog_GetTopic`、`FoxgloveLog_Publish`
- 每 2 秒 `FindObjectsByType<MonoBehaviour>` 扫描
- Per-topic 独立 timer

### 第 10 步：修改 FoxgloveManager.PublishJson（schemaless 分支）

```csharp
var channelId = string.IsNullOrEmpty(schemaName)
    ? GetOrRegisterChannel(topic, "json")      // schemaless
    : GetOrRegisterSchemaChannel(topic, schemaName);  // schema'd
```

### 第 11 步：回 Unity 验证

1. 确认 `FoxgloveLogSourceGenerator.dll` 有 `RoslynAnalyzer` label
2. 写 `partial class TestLog : MonoBehaviour` 标 `[FoxgloveLog]`
3. 编译通过 → 0 error
4. 挂到 GameObject → Play Mode → Foxglove 出现 topic

---

看到了。这不是"11 步操作指南"——这是**如何在 Unity 里跑一个 Source Generator 的完整踩坑实录**：

- **第 2 步 Directory.Build.props** — Unity 会扫描 `bin/obj` 中间产物，不重定向就会报不明错误
- **第 3 步 Roslyn 4.2.0** — 不是随便一个版本，是查到 Unity 6000 内置 Roslyn 4.2.2 之后精确匹配的
- **第 4 步 asmdef 隔离** — `UNITY_EXCLUDE_FROM_COMPILATION` 这个技巧全网都不一定搜得到
- **第 8 步 meta 文件** — `RoslynAnalyzer` label + `validateReferences: 0` 两个缺一个都不行
- **第 5 步 CreateSyntaxProvider** — 因为 4.3+ 的 `ForAttributeWithMetadataName` 在 4.2 不存在，得退回老 API
- **第 10 步 schemaless 分支** — 空了 schema 不能走 `RegisterSchemaChannel`，否则现场崩

每步都是踩过坑才写进去的，少一步整套 ISG 就跑不起来。应该写到项目文档里。