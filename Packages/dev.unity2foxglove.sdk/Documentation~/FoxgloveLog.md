# [FoxRun] — 对标 Rerun `rr.log()` 的零代码自动发布

用 Attribute 标注字段即可自动发布到 Foxglove，**无需理解 channel、schema、subscription 等协议概念**。

---

## 用户使用步骤

### 1. 写脚本，标 `[FoxRun]`

```csharp
// Assets/Scripts/TestLog.cs
using UnityEngine;
using Unity.FoxgloveSDK.Components;

public partial class TestLog : MonoBehaviour          // ← 必须 partial
{
    [FoxRun("/debug/position")]                   // 默认 10Hz
    private Vector3 _pos;

    [FoxRun("/debug/health", RateHz = 5)]         // 自定义频率
    private float _health = 100f;

    void Update() { _pos = transform.position; }
}
```

### 2. 挂到 GameObject

Hierarchy → Create Empty → 命名 `TestLogger` → 拖 `TestLog.cs` 上去。

### 3. 进 Play Mode

- 等 2-3 秒（Hub 扫描间隔）
- Foxglove Studio 连接 `ws://127.0.0.1:8765`
- Topic 列表出现 `/debug/position` 和 `/debug/health`

### 4. 验证数值

- 订阅 `/debug/position` → Raw Messages 面板看到 `{"x":1.0,"y":2.0,"z":3.0}` 每 ~0.1s 更新
- 移动 GameObject → 发布值跟随变化

### 支持的类型

`Vector3`, `Quaternion`, `float`, `int`, `string`, `bool`, `enum`

### Attribute 参数

| 参数 | 默认 | 说明 |
|------|------|------|
| `topic` | 必填 | Foxglove topic |
| `RateHz` | `10` | 发布频率 |
| `SchemaName` | 空 | 为空走 schemaless JSON；填 `foxglove.FrameTransform` 等则 3D 面板可渲染 |

---

## ISG 构建步骤（开发者维护）

### 环境

- Unity 6000.3.x（内置 Roslyn 4.2.2）
- `dotnet` CLI

### 文件结构

```
Editor/SourceGenerators/
├── Directory.Build.props                          # bin/obj → build/SourceGenerators/
├── FoxgloveLogSourceGenerator.csproj              # dotnet build 专用
├── src/
│   ├── FoxgloveLogSourceGenerator.cs              # ISG 源码（Unity 不编译）
│   └── Unity.FoxgloveSDK.SourceGenerators.asmdef # EXCLUDE 隔离
└── analyzers/dotnet/cs/
    └── FoxgloveLogSourceGenerator.dll             # 预编译 DLL
```

### 构建并部署

```bash
cd Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators
dotnet build FoxgloveLogSourceGenerator.csproj -c Release
cp src/bin/Release/netstandard2.0/FoxgloveLogSourceGenerator.dll analyzers/dotnet/cs/
```

回 Unity，等重新编译。

### 关键坑点

| 坑 | 原因 | 解决 |
|----|------|------|
| Roslyn DLL 不能放 Unity 里 | Unity 当 runtime plugin 加载 → 崩溃 | 预编译 DLL 只放 analyzer 路径，Roslyn 由编译器进程提供 |
| csproj 版本必须匹配 | Unity 内置 Roslyn 4.2.2 | `Microsoft.CodeAnalysis.CSharp` 4.2.0 |
| src/ 不能给 Unity 编译 | 源码引用 Microsoft.CodeAnalysis | asmdef 设 `defineConstraints: ["UNITY_EXCLUDE_FROM_COMPILATION"]` |
| bin/obj 不能留在 Unity 内 | Unity 扫描中间产物 | `Directory.Build.props` 重定向到 `build/SourceGenerators/` |
| DLL 需 RoslynAnalyzer label | Unity 不认普通 DLL 为 generator | .meta 加 `labels: [RoslynAnalyzer]` |
| 引用验证需关闭 | Unity 检查 DLL 依赖失败 | `validateReferences: 0` |
| schemaless JSON 走错路径 | PublishJson 调了 RegisterSchemaChannel | Manager 改成分支：空 schemaName → 普通 channel |

---

## 架构

```
用户脚本 (partial class + [FoxRun])
        │
        ▼ 编译时
FoxgloveLogSourceGenerator (ISG)
        │
        ▼ 生成
TestLog_FoxRun.g.cs (impl IFoxgloveLogSource)
        │
        ▼ 运行时
FoxgloveLogHub (singleton, 2s 扫描)
        │
        ▼ 调度
FoxgloveManager.PublishJson (schemaless / schema 分支)
        │
        ▼
Foxglove WebSocket → Foxglove Studio
```

### FoxgloveLogHub

- `[RuntimeInitializeOnLoadMethod]` 自动创建
- 每 2 秒 `FindObjectsByType<MonoBehaviour>` 扫描所有 `IFoxgloveLogSource`
- Per-topic 独立 timer，按配置频率调用 `FoxgloveLog_Publish`

### PublishJson 分支

```csharp
// schemaName 为空 → 普通 channel（schemaless JSON）
// schemaName 非空 → RegisterSchemaChannel（Foxglove 3D 面板可渲染）
```

---

## 常见问题

| 现象 | 原因 | 解决 |
|------|------|------|
| 编译报错 | 类不是 partial | 加 `partial` |
| Play Mode 无 topic | 脚本没挂 GameObject | 挂上去 |
| Play Mode 无 topic | Hub 没扫描到 | 等 3 秒 |
| Schema not found 异常 | schemaless 走错路径 | 确认 Manager.PublishJson 空 schema 分支存在 |
