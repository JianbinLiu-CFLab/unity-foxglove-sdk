# FoxRun 发布、订阅与全双工

## 1. 用途

FoxRun 用一个特性声明生成 Unity 字段或属性的传输契约，支持遥测发布、外部数据订阅，以及显式的全双工调试通道。它适合状态、数值曲线、控制参数、小型 DTO 和事件快照；图像、点云、网格等大体积数据仍应使用专用发布组件。

## 2. 最小示例

```csharp
using UnityEngine;
using Unity.FoxgloveSDK.Components;

public partial class RobotTelemetry : MonoBehaviour
{
    [FoxRun("/robot/pose")]
    private Vector3 _position;

    private void Update()
    {
        _position = transform.position;
    }
}
```

`[FoxRun("/robot/pose")]` 默认表示 `Publish`、`FixedRate`、10 Hz。所在类型必须是 `partial`，topic 必须以 `/` 开头，值类型必须能生成受支持的线格式。

## 3. 声明语法

需要显式选项时，建议静态导入短名称：

```csharp
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;
```

```csharp
[FoxRun("/robot/pose")]
private PoseState _pose;

[FoxRun("/robot/command", Mode = Subscribe, Policy = Change, Hz = 30)]
private RobotCommand _command;

[FoxRun("/debug/state", Mode = PublishAndSubscribe,
    Policy = FixedRate, Hz = 10,
    Encoding = FoxRunEncoding.Protobuf)]
private DebugState _debugState;
```

### Mode

| 值 | 含义 |
|---|---|
| `Publish` | Unity 是数据源并发送当前值；这是默认模式。 |
| `Subscribe` | 一个选定的外部端点是数据源；Unity 在主线程应用已接收的值。 |
| `PublishAndSubscribe` | 同时生成两个方向，主要用于调试和联调，不建议作为生产环境的默认权属模型。 |

每个订阅声明只解析到一个 `Source`；发布可以扇出到一个或多个 `Targets`。

### Policy

| 值 | 发布行为 | 订阅行为 |
|---|---|---|
| `FixedRate` | 按有效节拍发送当前值。 | 只有存在更新的暂存值时才应用，不会因定时器重复应用旧值。 |
| `Change`（无 `Hz`） | 首次及语义变化时发送。 | 在下一次主线程机会应用变化值，并受最大订阅频率保护。 |
| `Change`（带 `Hz`） | 变化时发送，并按 `Hz` 发送心跳。 | 立即应用变化值；新收到的相同值可按 `Hz` 刷新，但不会用旧暂存值制造刷新。 |
| `Trigger` | 仅在调用生成的发布触发方法时发送。 | 保留最新暂存值，直到调用生成的应用触发方法。 |

`Trigger` 不能同时设置正数 `Hz`；生成器会报告 `FOXRUN609`，不会静默忽略其中一个设置。

`Tolerance` 控制支持的浮点数、双精度数和向量变化阈值；`Change + Hz`
直接表达心跳，不再需要第二种策略。`OnlyIf` 指向一个 bool 字段、属性或零参数方法，
只表达正向条件。同一 topic 的成员必须使用一致的 `Policy`、`Hz`、`Tolerance`
和 `OnlyIf`，否则生成器报告 `FOXRUN005`。

## 4. 频率与准入

`Hz` 表示声明级的边界更新节拍：

- `Publish`：生成代码的最大发布频率。
- `Subscribe`：通过传输准入后，在 Unity 主线程应用值的最大频率。
- `PublishAndSubscribe`：同一个显式值分别控制两个方向。

省略 `Hz` 时，FixedRate 发布使用 10 Hz；FixedRate 订阅继承 Manager 会话冻结的 **Default Subscribe Rate Hz**（默认 10 Hz）。`Change` 不带 `Hz` 时在下一次主线程机会应用变化值；带 `Hz` 时增加心跳或新重复值刷新节拍。

在 **Foxglove Manager > Data Transport > Subscribe Data > Subscription Delivery** 中有两个相邻但职责不同的设置：

- **Default Subscribe Rate Hz**：默认值为 10 Hz，仅供未显式设置正数 `Hz` 的订阅声明继承。
- **Maximum Subscribe Rate Hz (per Topic)**：Foxglove WebSocket 与 ROS 2 Native 共用的硬准入上限。超额消息会尽量在 DTO 解码或原生深拷贝之前丢弃。

声明级 `Hz` 不能突破准入上限。通过准入的数据采用有界 latest-wins：Unity 来不及应用全部输入时，新值替换旧的待处理值。

## 5. 订阅输入

订阅是外部控制面，默认关闭。进入 Play Mode 前，在 Manager 中启用 **FoxRun Subscriptions**。建议先写入输入缓冲成员，再在普通 Unity 代码中做业务校验：

```csharp
using UnityEngine;
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

public partial class SpeedController : MonoBehaviour
{
    [FoxRun("/control/target-speed", Mode = Subscribe,
        Policy = Change, Hz = 30,
        Encoding = FoxRunEncoding.JSON)]
    private float _requestedTargetSpeed;

    private void Update()
    {
        ApplyValidatedTarget(Mathf.Clamp(_requestedTargetSpeed, 0f, 10f));
    }
}
```

输入目标必须可写。生成的 allowlist、负载大小限制、编码与 Source 检查、传输准入、有界 latest-wins 暂存和主线程应用都会继续生效。非 loopback 监听默认 fail-closed，只有 Manager 明确允许远程输入并配置认证策略后才开放。

## 6. 方向端点与编码

省略 `Source`、`Targets` 或 `Encoding` 时，声明继承对应方向冻结的 Manager Profile；用户代码不应写数值零哨兵。全双工声明可以分别继承发布与订阅方向的 Foxglove 编码；显式 `Encoding` 则应用于该声明选中的所有 Foxglove 方向。

`Source` 选择唯一输入源，核心 SDK 默认使用 `FoxRunEndpoint.Foxglove`。`FoxRunEndpoint.Ros2Native` 需要可选的 `dev.unity2foxglove.ros2forunity` facade、一个已选发行版 runtime package，以及受支持的原生消息或匹配的 custom typesupport add-on。`FoxRunEndpoint.Ros2Bridge` 目前只作为发布 Target 保留，不能作为订阅 Source。

`Targets` 接受一个或多个端点标志，并替换而不是追加 Publish Profile 的默认值：

```csharp
[FoxRun("/robot/state",
    Targets = FoxRunEndpoint.Foxglove | FoxRunEndpoint.Ros2Native)]
private RobotState _state;
```

JSON 和 Protobuf 只描述 Foxglove 线格式。Native 与 Bridge 使用生成的 ROS 2 消息契约；CDR 不是公开的 `Encoding` 选项。Source、Targets、编码、QoS、复制预算、最大订阅频率和各方向默认频率都会在对应的已启用会话中冻结。

## 7. Trigger 与全双工

发布触发先更新值，再调用生成的 `FoxRun_Publish_<member>()`。订阅触发只暂存最新输入，直到用户代码在 Unity 主线程调用 `FoxRun_Apply_<member>()`。

`PublishAndSubscribe` 为同一个声明生成独立的发布与应用节拍。应用外部值后，该版本会被标记，避免立即作为本地变化回传；之后真正发生的本地修改仍可正常发布。生产环境权属边界通常应拆成独立的 `Publish` 和 `Subscribe` 声明。

## 8. Foxglove 与 Player 工作流

1. 在场景中添加业务组件和 `FoxgloveManager`。
2. 在 Play Mode 前配置 Publish Data；需要输入时再配置并启用 Subscribe Data。
3. 进入 Play Mode，让 Foxglove 连接 `ws://127.0.0.1:8765`。
4. 使用 Topics、Raw Messages 或 Plot 查看输出。
5. 可选的 **FoxRun Publish** 扩展只展示生成的可写 JSON/Protobuf 契约，不猜测 topic 或编码。

Roslyn 生成器是创作期权威。Editor Play Mode 会刷新 canonical descriptor、manifest、hash 和 runtime schema info；Player 构建前还会生成物理 `.g.cs` fallback。MCAP 记录外部边界表示，Replay 会核对 FoxRun schema identity，并在回放权威期间抑制实时 WebSocket 与 native fanout。

规范清单（canonical manifest）是可确定的治理（governance）证据。仅供报告的时间戳
（timestamps）和警告不参与契约指纹；生成的 manifest、descriptor、hash 与 fallback
source 是已忽略的本机（machine-local）构建状态，不是需要纳入版本管理的创作输入。

Editor Play Mode 会注册包含 manifest hash 的 runtime schema info；该证据会写入 MCAP
元数据，并在之后的 Replay 中用于检测契约漂移。

MCAP 以 `unity2foxglove.foxrun.schema` 元数据保存该证据，其中包含
`globalManifestHash`。Replay 发现 schema mismatch 时会按 Manager 的 schema identity
防护策略处理，不会静默应用不兼容的 FoxRun 数据。

更广的 SDK schema manifest 还会编目 Protobuf 与已打包的 ROS2 覆盖面。该聚合清单与
Replay 治理分离；Replay 使用随 MCAP 记录的 FoxRun 契约身份。

调试覆盖层（debug overlay）是非契约（non-contract）诊断，不包含在（not included）
规范 hash 中，也不是 Replay 的防护键。

## 9. 常见问题

| 现象 | 检查项 |
|---|---|
| 看不到 topic | 类型是否为 `partial`、topic 是否以 `/` 开头、组件是否启用、是否已进入 Play Mode。 |
| 订阅没有数据 | 是否启用订阅、Source 与编码是否匹配、传输准入诊断是否出现丢弃。 |
| 输入应用太慢 | 声明 `Hz` 或 Manager 的 **Default Subscribe Rate Hz**。 |
| 消息被丢弃 | **Maximum Subscribe Rate Hz (per Topic)**、负载大小、编码和 native copy budget。 |
| Trigger 不生效 | 是否从 Unity 主线程调用了对应的发布或应用触发方法。 |
| 全双工值没有立即回传 | 刚应用的外部版本会执行一次 echo suppression，这是设计行为。 |
| Editor 正常、Player 异常 | 检查 build preprocess 日志与生成的 fallback source。 |

Player 验证见 [09_IL2CPP构建](09_IL2CPP构建.md)，生成器与运行时边界见 [10_架构说明](10_架构说明.md)。
