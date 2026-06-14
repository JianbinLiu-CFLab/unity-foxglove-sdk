# 1. Parameters 与 Services

Foxglove WebSocket 协议中的 Parameters 与 Services 是两种独立机制：

- Parameters：运行时状态读写，适合颜色、缩放、速度、开关等可调值。
- Services：请求/响应动作，适合 reset、capture、start、stop 等一次性操作。

## 2. Parameters

Full Demo 中常用参数：

| 参数 | 示例值 | 作用 |
|---|---:|---|
| `/cube/color` | `[0, 1, 0, 1]` | Cube RGBA 颜色 |
| `/cube/scale` | `1` | Cube 统一缩放 |

在 Foxglove 中：

1. 打开 **Parameters** 面板。
2. 连接 Unity：`ws://127.0.0.1:8765`。
3. 修改 `/cube/color` 或 `/cube/scale`。
4. 回到 Unity，确认 Cube 颜色或大小变化。

代码注册示例：

```csharp
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;

var manager = FindFirstObjectByType<FoxgloveManager>();
manager.RegisterParameter("/my/param", JToken.FromObject(42), "int", writable: true);
```

## 3. Services

Services 是请求/响应动作。Foxglove 发起请求，Unity 在主线程执行 handler，然后返回响应。

Full Demo 中主要服务：

| 服务 | 请求 | 响应 | 作用 |
|---|---|---|---|
| `/cube/reset_pose` | `{}` | `{"status":"ok"}` | 重置 Cube 位置、旋转和缩放 |

## 4. 使用 `[FoxService]`

推荐用 `[FoxService]` 把 Unity 方法声明为服务。声明类必须是 `partial`，这样生成代码可以直接调用你的方法，不需要运行时反射。

```csharp
using Unity.FoxgloveSDK.Components;

public partial class CubeControls : MonoBehaviour
{
    [FoxService(
        "/cube/reset_pose",
        Type = "Unity2Foxglove.Demo.ResetPose",
        RequestSchemaName = "Unity2Foxglove.Demo.ResetPoseRequest",
        ResponseSchemaName = "Unity2Foxglove.Demo.ResetPoseResponse")]
    private ResetPoseResponse ResetPose(ResetPoseRequest request)
    {
        transform.position = Vector3.zero;
        return new ResetPoseResponse { status = "ok" };
    }

    private sealed class ResetPoseRequest {}
    private sealed class ResetPoseResponse { public string status; }
}
```

支持的形态：

- 实例方法；
- 0 个或 1 个请求参数；
- 可被 Newtonsoft JSON 序列化的 request/response DTO；
- `void` 返回值，此时响应为 `{}`；
- `partial` 类中的 `private` 方法。

会被拒绝的形态：

- static、generic、async 方法；
- `ref`、`out`、`in` 参数；
- 超过 1 个请求参数；
- open generic、pointer、by-ref、ref-like DTO；
- `Task` 返回值；
- 重复服务名。

DTO 校验会递归检查成员。推荐 DTO 只包含 public 字段或 get/set 属性，以及适合 JSON 的类型：基础类型、enum、`DateTime`、`DateTimeOffset`、`Guid`、`TimeSpan`、nullable、单维数组、`List<T>` / `IReadOnlyList<T>`，以及 key 为 `string` 的 dictionary。

当 DTO 成员不能安全序列化时，生成器会带成员路径报诊断：

- `FOXSERVICE003`：request DTO 中有不支持的成员；
- `FOXSERVICE004`：response DTO 中有不支持的成员；
- `FOXSERVICE007`：get-only 或被忽略的成员，warning；
- `FOXSERVICE008`：DTO 图里有递归引用。

不要把 `GameObject`、`Transform`、`MonoBehaviour` 等 `UnityEngine.Object` 类型放进服务 DTO。请改成稳定数据，例如对象 id 字符串，或只包含数字字段的小型 pose DTO。也应避免 delegate、`object`、不在支持范围内的 interface、多维数组、非 string key dictionary，以及自引用 DTO。

Editor 中由 Roslyn source generator 生成 wrapper。Player 构建前，SDK 会写出物理 `*_FoxService.g.cs` fallback 文件，所以 IL2CPP 不需要运行时反射调用。

## 5. 在 Foxglove 调用服务

1. 添加 **Service Call** 面板。
2. 在面板设置里选择或输入 `/cube/reset_pose`。
3. 请求体填写：

```json
{}
```

4. 点击调用按钮。
5. 确认 Unity 中 Cube 回到默认位姿，并看到 `status: "ok"` 响应。

> 不要把服务名写进 request JSON。服务名属于面板设置，请求体只写 JSON payload。

## 6. 手动注册 API

当服务需要动态创建或销毁时，可以继续使用手动 API。

```csharp
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Protocol;

var manager = FindFirstObjectByType<FoxgloveManager>();
uint serviceId = manager.RegisterService(new ServiceDescriptor
{
    Name = "/my/reset",
    Type = "json",
    Request = new ServiceSchemaDescriptor
    {
        Encoding = "json",
        SchemaName = "MyResetRequest",
        Schema = "{}"
    },
    Response = new ServiceSchemaDescriptor
    {
        Encoding = "json",
        SchemaName = "MyResetResponse",
        Schema = "{}"
    }
},
request =>
{
    transform.position = Vector3.zero;
    return JToken.FromObject(new { status = "ok" });
});
```

静态服务优先使用 `[FoxService]`；动态服务再使用 `RegisterService`。

## 7. 常见排查

参数列表为空时，确认：

- Unity 正在 Play Mode；
- Foxglove 已连接 `ws://127.0.0.1:8765`；
- 使用的是 Full Demo，而不是 Basic sample；
- Demo 里的 `FoxgloveDemoSetup` 组件处于启用状态。

服务调用超时时，确认：

- Service Call 面板选择的是 `/cube/reset_pose`；
- 请求体是合法 JSON，通常是 `{}`；
- Unity Console 没有服务 handler 错误；
- Unity 仍在 Play Mode。
