# Phase 17 - Protobuf 编码支持，后续增强功能

## Context

当前 SDK 仅支持 JSON 编码：
- `ServerInfo.supportedEncodings = ["json"]`
- Channel advertise 使用 `encoding: "json"`, `schemaEncoding: "jsonschema"`
- Service layer 接收 encoding 字段但仅实际处理 JSON payload
- `ISchemaRegistry` / `SchemaEntry` 接口已预留 `"protobuf"` 枚举值但无实际实现

Foxglove WebSocket protocol 原生支持 protobuf encoding：
- Channel `encoding: "protobuf"` + `schemaEncoding: "protobuf"`
- Schema data 为 `google.protobuf.FileDescriptorSet` 的序列化二进制
- Message payload 直接为 protobuf 序列化字节

本地 `third-party/foxglove-sdk/schemas/proto/foxglove/` 下有 46 个官方 `.proto` 定义文件。

## Goal

- [ ] SDK 支持 protobuf encoding 的 channel 发布（与现有 JSON 并行）
- [ ] 官方 Foxglove proto schema 预编译为 C# 类（Google.Protobuf codegen）
- [ ] ServerInfo 广播 `supportedEncodings: ["json", "protobuf"]`
- [ ] MCAP 录制支持 protobuf channel（schemaEncoding = "protobuf", schema data = FileDescriptorSet）
- [ ] 用户可选择 per-channel encoding（JSON 或 protobuf），默认仍为 JSON
- [ ] Foxglove Studio 能正确解析 protobuf channel 数据

## Design Decisions

| 决策 | 选择 | 理由 |
|------|------|------|
| Protobuf 库 | Google.Protobuf 3.x (nuget) | 官方实现，IL2CPP 兼容（需 link.xml），Unity 生态最稳 |
| Proto codegen | `protoc` + `grpc_tools` 预编译 | 不做运行时 proto 编译，生成代码放 `Runtime/Schemas/Proto/` |
| Schema 传输格式 | FileDescriptorSet 二进制 | Foxglove protocol spec 要求 |
| SchemaEntry 扩展 | 新增 `byte[] RawContent` 字段 | 现有 `string Content` 无法存二进制 FileDescriptorSet |
| Channel encoding 选择 | `FoxgloveChannel` 构造时指定 | `RegisterChannel(name, schemaName, encoding: "protobuf")` |
| 默认 encoding | JSON（向后兼容） | 不改现有用户行为 |
| Service protobuf | 本期不做 | Service 调用频率低，JSON 足够，protobuf service 复杂度高（需双向 encoding negotiation） |
| FoxRun attribute | 本期不改 | ISG 生成的代码仍用 JSON PublishJson，protobuf 走显式 API |

## References

- Foxglove WebSocket protocol: channel advertise 的 `encoding` / `schemaEncoding` 字段
- `third-party/foxglove-sdk/schemas/proto/foxglove/*.proto` — 46 个官方 schema
- `Runtime/Schemas/ISchemaRegistry.cs` — SchemaEntry 结构
- `Runtime/Core/FoxgloveSession.cs` line 195 — SupportedEncodings
- `Runtime/Protocol/JsonMessages.cs` — ServerInfo / ChannelInfo 消息定义
- `Runtime/IO/McapWriter.cs` — MCAP schema/channel record 写入

## Target Files

新增：
- `Runtime/Schemas/Proto/` — protoc 生成的 C# 类（46 个 schema）
- `Runtime/Schemas/Proto/FoxgloveProtos.asmdef` — 可选独立程序集（隔离 Google.Protobuf 依赖）
- `Runtime/Schemas/ProtobufSchemaRegistry.cs` — FileDescriptorSet 构建 + 注册
- `Runtime/Schemas/ProtobufSerializer.cs` — IMessage → byte[] 序列化工具
- `Editor/ProtoGen/` — protoc 构建脚本（开发期用，不进 Runtime）
- `Plugins/Google.Protobuf/` — Google.Protobuf.dll + link.xml
- `Tests/Runtime/Phase17Validation.cs` — protobuf 编码验证测试

修改：
- `Runtime/Schemas/ISchemaRegistry.cs` — SchemaEntry 增加 `byte[] RawContent`
- `Runtime/Core/FoxgloveSession.cs` — SupportedEncodings 加 `"protobuf"`，advertise 逻辑支持 binary schema
- `Runtime/Protocol/JsonMessages.cs` — ChannelInfo 支持 binary schema（base64 或 raw）
- `Runtime/IO/McapRecorder.cs` — 写 protobuf channel 时 schema data 为 FileDescriptorSet bytes
- `Runtime/IO/McapWriter.cs` — WriteSchema 支持 binary data
- `Runtime/Unity/FoxgloveManager.cs` — Inspector 中 default encoding 选择

---

## Batch 17A：Protobuf 基础设施

### Task 17A-1：引入 Google.Protobuf 库

**目标**：将 Google.Protobuf NuGet 包引入 Unity 项目

**步骤**：
1. 从 NuGet 下载 `Google.Protobuf` 3.25.x 的 `netstandard2.0` DLL
2. 放入 `Plugins/Google.Protobuf/Google.Protobuf.dll`
3. 创建对应 `.meta` 文件，设置平台兼容性
4. 创建 `Plugins/Google.Protobuf/link.xml` 保护 IL2CPP 反射：
   ```xml
   <linker>
     <assembly fullname="Google.Protobuf" preserve="all"/>
   </linker>
   ```

**验收**：Unity 编译无错误，Editor + IL2CPP Player 均能加载 Google.Protobuf

### Task 17A-2：Proto codegen — 生成 C# 类

**目标**：使用 protoc 将 46 个官方 .proto 编译为 C# 源文件

**步骤**：
1. 创建 `Editor/ProtoGen/generate_protos.bat`（或 .sh）脚本：
   ```
   protoc --proto_path=../../third-party/foxglove-sdk/schemas/proto \
          --csharp_out=../../Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto \
          foxglove/*.proto
   ```
2. 执行 codegen，生成 `Runtime/Schemas/Proto/` 下的 C# 文件
3. 创建 `Runtime/Schemas/Proto/Unity.FoxgloveSDK.Proto.asmdef`：
   - references: `Google.Protobuf`
   - defineConstraints: 无（正常编译）
4. 主 `Unity.FoxgloveSDK.asmdef` 添加对 `Unity.FoxgloveSDK.Proto` 的引用

**验收**：`Foxglove.FrameTransform` 等类在 Unity 中可用，能 new 并序列化

### Task 17A-3：SchemaEntry 扩展

**目标**：让 SchemaEntry 能存储二进制 schema（FileDescriptorSet）

**文件**：`Runtime/Schemas/ISchemaRegistry.cs`

**改动**：
```csharp
public struct SchemaEntry
{
    public string Name;
    public string Encoding;       // "jsonschema" | "protobuf" | ...
    public string Content;        // JSON Schema text (nullable for binary)
    public byte[] RawContent;     // Binary schema data (FileDescriptorSet for protobuf)
}
```

`TryGetSchema` 和 `Register` 签名不变，但内部需处理 RawContent != null 的情况。

**验收**：现有 JSON schema 注册不受影响，新增 protobuf schema 可存二进制

---

## Batch 17B：FileDescriptorSet 构建 + Schema 注册

### Task 17B-1：构建 FileDescriptorSet

**目标**：将 proto 文件编译为 `FileDescriptorSet` 二进制，供 channel advertise 使用

**方案**：
- 使用 `protoc --descriptor_set_out=foxglove_schemas.pb --include_imports foxglove/*.proto`
- 将生成的 `.pb` 文件嵌入为 `TextAsset` 或 C# `byte[]` 常量
- 运行时按 schema name 从 FileDescriptorSet 中提取对应子集

**文件**：`Runtime/Schemas/ProtobufSchemaRegistry.cs`

```csharp
public class ProtobufSchemaRegistry
{
    /// <summary>Get FileDescriptorSet bytes for a given schema name.</summary>
    public byte[] GetFileDescriptorSet(string schemaName);
    
    /// <summary>Register all foxglove proto schemas into the ISchemaRegistry.</summary>
    public void RegisterAll(ISchemaRegistry registry);
}
```

**关键实现**：
- 预编译时用 protoc 为每个顶级 message 生成独立的 `FileDescriptorSet`（含 imports）
- 或者生成一个大的 FileDescriptorSet，运行时按 message name 过滤 file descriptors
- 推荐方案：一个完整的 FileDescriptorSet，内含所有 46 个 proto + google/protobuf/timestamp.proto

**验收**：`GetFileDescriptorSet("foxglove.FrameTransform")` 返回有效 bytes

### Task 17B-2：Schema 注册集成

**目标**：SDK 启动时将 protobuf schemas 注册到 DefaultSchemaRegistry

**文件**：`Runtime/Core/FoxgloveSession.cs` 或 `Runtime/Unity/FoxgloveManager.cs`

**改动**：
- Session Start 时，如果 protobuf enabled，调用 `ProtobufSchemaRegistry.RegisterAll()`
- SchemaEntry 的 Encoding 设为 `"protobuf"`，RawContent 设为 FileDescriptorSet bytes

---

## Batch 17C：Channel Advertise + Publish 支持 Protobuf

### Task 17C-1：ChannelInfo 支持 binary schema

**目标**：advertise 消息中 schema 字段支持 base64 编码的 FileDescriptorSet

**文件**：`Runtime/Protocol/JsonMessages.cs`

**Foxglove protocol 要求**：
```json
{
  "op": "advertise",
  "channels": [{
    "id": 1,
    "topic": "/tf",
    "encoding": "protobuf",
    "schemaName": "foxglove.FrameTransform",
    "schema": "<base64-encoded FileDescriptorSet>",
    "schemaEncoding": "protobuf"
  }]
}
```

**改动**：
- `ChannelInfo` 的 `Schema` 字段：如果 schemaEncoding 为 "protobuf"，值为 base64 字符串
- 新增 `SchemaEncoding` 字段（当前缺失或硬编码为 "jsonschema"）

### Task 17C-2：Session advertise 逻辑

**文件**：`Runtime/Core/FoxgloveSession.cs`

**改动**：
- `RegisterChannel` 接收 encoding 参数（默认 "json"）
- advertise 时根据 channel encoding 决定：
  - `encoding: "json"` → schemaEncoding = "jsonschema", schema = JSON Schema text
  - `encoding: "protobuf"` → schemaEncoding = "protobuf", schema = base64(FileDescriptorSet)
- `SupportedEncodings` 更新为 `["json", "protobuf"]`

### Task 17C-3：Publish protobuf payload

**目标**：`Publish(channelId, byte[] protobufPayload, logTimeNs)` 直接发送 protobuf bytes

**分析**：现有 `Publish(ushort channelId, ulong logTimeNs, byte[] payload)` 已是 byte[] 接口，理论上 protobuf 序列化后直接传即可，无需改 Publish 内部逻辑。

**需要**：
- 提供 `PublishProto<T>(ushort channelId, T message, ulong logTimeNs) where T : IMessage`
  便捷方法，内部调用 `message.ToByteArray()` 然后 `Publish(channelId, logTimeNs, bytes)`
- 或在 `FoxgloveRuntime` / `FoxgloveManager` 层提供

**验收**：Foxglove Studio 连接后能解析 protobuf channel 数据并正确渲染

---

## Batch 17D：MCAP Protobuf Channel 录制

### Task 17D-1：McapWriter schema binary 支持

**文件**：`Runtime/IO/McapWriter.cs`

**当前**：WriteSchema 接收 `string data`（JSON Schema text）

**改动**：增加重载或修改签名支持 `byte[]`：
```csharp
public void WriteSchema(ushort id, string name, string encoding, byte[] data)
```

protobuf channel 写入时：
- Schema record: encoding = "protobuf", data = FileDescriptorSet bytes
- Channel record: messageEncoding = "protobuf"

### Task 17D-2：McapRecorder 适配

**文件**：`Runtime/IO/McapRecorder.cs`

**改动**：
- `EnsureChannel` / schema 注册路径需区分 JSON vs protobuf
- protobuf channel 的 schema 从 `ProtobufSchemaRegistry.GetFileDescriptorSet()` 获取
- message data 直接为 protobuf bytes（不做 JSON 序列化）

**验收**：录制的 .mcap 文件在 Foxglove Studio 中能正确显示 protobuf channel 数据

---

## Batch 17E：ProtobufPublisher + 集成

### Task 17E-1：ProtobufPublisher<T> 组件

**目标**：提供类型安全的 protobuf publisher，与现有 `FoxglovePublisher<T>` 类似

**文件**：新增 `Runtime/Unity/Publishers/ProtobufPublisher.cs`

```csharp
public abstract class ProtobufPublisher<T> : MonoBehaviour where T : IMessage<T>, new()
{
    [SerializeField] private string _topic;
    private ushort _channelId;
    
    protected void Publish(T message)
    {
        var mgr = FoxgloveManager.Instance;
        mgr.PublishProto(_channelId, message, mgr.NowNs);
    }
}
```

### Task 17E-2：TransformPublisher protobuf 模式

**目标**：现有 TransformPublisher 支持选择 JSON 或 protobuf encoding

**改动**：
- Inspector dropdown: encoding = JSON / Protobuf
- Protobuf 模式下构建 `Foxglove.FrameTransform` message 对象发布
- JSON 模式行为不变

### Task 17E-3：Integration test

**文件**：`Tests/Runtime/Phase17Validation.cs`

**测试项**：
- protobuf schema 注册成功
- FileDescriptorSet 为有效 protobuf binary
- channel advertise 包含正确的 encoding / schemaEncoding / schema
- PublishProto 序列化+发送不报错
- MCAP 录制 protobuf channel 后文件可被 Foxglove Studio 打开
- ServerInfo.supportedEncodings 包含 "protobuf"

---

## Batch 17F：IL2CPP 验证 + 文档

### Task 17F-1：IL2CPP 构建验证

- Google.Protobuf 反射在 IL2CPP 下正常工作（link.xml）
- 生成的 proto C# 类不被 stripping
- Player 中 protobuf channel 数据正确

### Task 17F-2：link.xml 更新

**文件**：`Runtime/link.xml`（或新建 `Plugins/Google.Protobuf/link.xml`）

确保：
```xml
<linker>
  <assembly fullname="Google.Protobuf" preserve="all"/>
  <assembly fullname="Unity.FoxgloveSDK.Proto" preserve="all"/>
</linker>
```

### Task 17F-3：文档更新

- `Documentation~/Architecture.md` — 新增 Protobuf Encoding 章节
- `Documentation~/README.md` — 使用示例
- `00_PLAN.md` — Phase 17 标记进度

---

## 明确不做

- gRPC 支持（Foxglove protocol 不走 gRPC）
- Service layer protobuf encoding（复杂度高，使用场景少）
- 运行时 .proto 文件解析（只用预编译方案）
- FlatBuffers 支持
- ROS1/ROS2 message encoding
- `[FoxRun]` attribute 的 protobuf 模式（ISG 生成仍为 JSON）

## 风险

| 风险 | 对策 |
|------|------|
| Google.Protobuf DLL 体积（~400KB） | 可接受，protobuf 是可选功能 |
| IL2CPP 裁剪 proto 生成类 | link.xml preserve + `[Preserve]` attribute |
| FileDescriptorSet 需含 google/protobuf/timestamp.proto 等 well-known types | protoc `--include_imports` 打包全部依赖 |
| Unity 旧版本兼容性 | Google.Protobuf 3.x netstandard2.0 兼容 Unity 2021+ |
| 46 个 proto 生成类占内存 | 按需加载，不使用的 proto 不实例化，编译体积可通过 managed stripping 控制 |
| protoc 版本差异 | 锁定 protoc 版本（与 Google.Protobuf NuGet 版本对应），写入 `Editor/ProtoGen/VERSION` |

## 工作量估算

| Batch | 预计时间 | 依赖 |
|-------|---------|------|
| 17A（基础设施） | 1-2 天 | 无 |
| 17B（Schema 注册） | 0.5 天 | 17A |
| 17C（Advertise + Publish） | 1 天 | 17B |
| 17D（MCAP 录制） | 0.5 天 | 17C |
| 17E（Publisher + 集成） | 1 天 | 17C |
| 17F（IL2CPP + 文档） | 0.5 天 | 17D, 17E |

**总计：4-5 天**
