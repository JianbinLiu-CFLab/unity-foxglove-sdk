# 1. Parameters 与 Services

Foxglove WebSocket 协议中的 Parameters 和 Services 是两个独立的通信机制，与 Topic（话题）发布/订阅不同。本文档说明它们的用途、在 SDK 中的配置方式和在 Foxglove 中的操作步骤。

## 1.1 目的

这份文档用于说明如何把 Unity 运行时参数和可触发动作暴露给 Foxglove。

## 1.2 应用场景

当你希望 Foxglove 修改 Unity 中的颜色、缩放、开关、速度等值，或触发 reset、capture、start、stop 等动作时，使用 Parameters 和 Services。

## 1.3 Parameters（参数）

### 用途

Parameters 让 Foxglove 侧可以**读取和修改** Unity 中的运行时变量。典型场景：

- 修改 Cube 颜色 `/cube/color`
- 修改 Cube 缩放 `/cube/scale`
- 读取或修改任何需要在运行时调整的数值

### 在 Foxglove 中操作

1. 打开 **Parameters 面板**（不是 Topics 面板！Parameters 有独立的面板）
2. 面板中会列出所有已注册的参数，如 `/cube/color`、`/cube/scale`
3. 点击参数旁的编辑图标修改值，Unity 侧实时响应
4. 修改后可通过 `parametersSubscribe` 机制推送到所有已连接的 Foxglove 客户端

### 在 Unity 中注册参数

**方式一：拖拽式（推荐）**

在需要暴露参数的 GameObject 上添加 `FoxgloveParameterComponent`：

- **Parameter Name**：如 `/cube/color`
- **Type**：如 `json`（支持 int、float、string、json）
- **Writable**：勾选表示 Foxglove 侧可以修改
- **Default Value**：初始值，如 `[0, 1, 0, 1]`

**方式二：代码注册**

```csharp
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;

var mgr = FindFirstObjectByType<FoxgloveManager>();
mgr.RegisterParameter("/my/param", JToken.FromObject(42), "int", writable: true);
```

可以通过 `FoxgloveManager.Runtime.Parameters` 在运行时动态修改参数值。

### 录制

MCAP 录制时会自动记录：
- 录制开始时的参数快照（`foxglove.parameters.snapshot` metadata）
- 录制过程中每次参数变化的记录（`foxglove.parameters` metadata）

---

## Services（服务）

### 用途

Services 提供 **请求-响应** 模式。Foxglove 侧发起一个调用请求，Unity 侧执行并返回结果。典型场景：

- `/cube/reset_pose`：将 Cube 恢复到初始位置、旋转、缩放
- 任何需要 Unity 执行动作并返回结果的场景

### 在 Foxglove 中操作

1. 打开 **Service Call 面板**
2. 从下拉列表中选择服务名，如 `/cube/reset_pose`
3. Request 框中填入参数（大多数情况填 `{}` 即可——注意 `{}` 是合法 JSON）
4. 点击 **Call service** 按钮
5. 等待 Unity 执行并返回结果（默认 5 秒超时）

> **常见误解**：Service name 是在下拉列表中选择的，**不要**把 service name 填进 request JSON。request 只需要填参数。

### 在 Unity 中注册服务

```csharp
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;

var descriptor = new ServiceDescriptor
{
    Name = "/cube/reset_pose",
    Type = "json",
    RequestSchema = "{}",
    ResponseSchema = "{}"
};

var mgr = FindFirstObjectByType<FoxgloveManager>();
uint serviceId = mgr.RegisterService(descriptor, handler: (request) =>
{
    // request 是 JToken，即 Foxglove 侧发来的参数
    // 执行你的业务逻辑
    cube.transform.position = Vector3.zero;
    // 返回结果
    return JToken.FromObject(new { status = "ok" });
});
```

Handler 在主线程执行（通过 `DrainServiceCalls()` 调度），可以直接访问 Unity API。

### 超时与错误

- 默认超时时间：`FoxgloveServiceRegistry.DefaultTimeout`
- 如果 handler 未在超时内完成，会自动发送 `serviceCallFailure`
- Handler 内抛出的异常会被捕获并转换为 failure 响应

### 录制

MCAP 录制时会记录每次 service call 的完成/失败状态（`foxglove.services` metadata）。

---

## FoxgloveDemoSetup 参考

Demo 项目中的 `Untiy2Foxglove/Assets/Scripts/FoxgloveDemoSetup.cs` 是一个完整的 Parameters + Services 示例，包含：

- 注册 `/cube/color`（可写 json 参数）
- 注册 `/cube/scale`（可写 float 参数）
- 注册 `/cube/reset_pose`（服务，返回 `{"status":"ok"}`）
- 监听参数变化事件，实时更新 Cube 的 Material.color 和 transform.localScale

建议参考此脚本了解完整用法。

---

## 与 Topics 的对比

| 特性 | Topics（话题） | Parameters（参数） | Services（服务） |
|------|---------------|-------------------|-----------------|
| 通信模式 | 发布/订阅（单向流） | 读取/写入（双向状态同步） | 请求/响应（RPC） |
| 面板 | Topics、3D、Plot、Image | Parameters | Service Call |
| 典型用途 | 实时数据流（Transform、Camera） | 可调配置（颜色、缩放） | 动作触发（复位、切换模式） |
| 录制支持 | 是（消息记录在 chunk 中） | 是（metadata） | 是（metadata） |
