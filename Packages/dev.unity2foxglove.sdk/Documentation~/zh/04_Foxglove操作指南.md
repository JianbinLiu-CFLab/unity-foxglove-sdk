# 1. Foxglove Desktop 操作指南

本文档介绍 Foxglove Desktop 各核心面板的使用方法，以及如何导入预置的布局配置。

## 1.1 目的

这份文档用于说明 Unity 发布数据后，如何在 Foxglove Desktop 中查看、配置和操作各类面板。

## 1.2 应用场景

当你已经能连接 `ws://127.0.0.1:8765`，但不知道 3D、Image、Plot、Parameters、Service Call 等面板该怎么配置时，阅读这份文档。

## 1.3 前置条件

- Unity 已进入 Play Mode，FoxgloveManager 已启动
- Foxglove Desktop 已通过 `ws://127.0.0.1:8765` 连接

## Topics 面板

Topics（话题）面板位于左侧边栏，显示所有当前可订阅的数据话题。

### 查看话题

- 连接成功后，话题列表自动更新
- 每条话题显示名称（如 `/tf`、`/scene`、`/unity/camera`）
- 点击话题可展开，查看 schema 信息和消息频率

### 订阅话题

- 在 Raw Messages 面板中选择对应话题即可查看实时数据
- 话题默认未订阅，仅在显示时才拉取数据

### 常见话题

| 话题 | Schema | 说明 |
|------|--------|------|
| `/tf` | `foxglove.FrameTransform` | 坐标变换，对应 FoxgloveTransformPublisher |
| `/scene` | `foxglove.SceneUpdate` | 场景实体，对应 FoxgloveSceneCubePublisher |
| `/unity/camera` | `foxglove.CompressedImage` | JPEG 压缩相机画面，对应 FoxgloveCameraPublisher |
| `/debug/*` | schemaless JSON | [FoxRun] 自动发布的自定义字段 |

## 3D 面板

3D 面板用于在三维空间中可视化数据。

### 基本操作

1. 点击 **+** > 选择 **3D** 面板
2. 在面板顶部的设置中配置：
   - **Display frame**: 选择参考坐标系，通常为 `unity_world`
   - 勾选需要显示的 topics（`/tf`、`/scene` 等）

### 视角控制

| 操作 | 鼠标 |
|------|------|
| 旋转 | 左键拖动 |
| 平移 | 右键拖动 |
| 缩放 | 滚轮 |
| 聚焦 | 双击某个对象 |

### 坐标系说明

- **LeftHand 模式**（默认）：X=右, Y=上, Z=前（Unity 原生）
- **RightHand 模式**：X=前, Y=左, Z=上（ROS/标准机器人坐标系）

可通过 FoxgloveManager Inspector 中的 **Coordinate Mode** 切换。

### 显示网格

在 3D 面板的 Layers 设置中，可添加 Grid 图层显示参考网格：
- **Size**: 网格大小（默认 10）
- **Divisions**: 分割数（默认 10）

## Image / Camera 面板

用于显示 Unity 相机画面。

### 设置步骤

1. 确保场景中的 Camera 已添加 **FoxgloveCameraPublisher** 组件
2. 在 Foxglove 中点击 **+** > 选择 **Image** 面板
3. 在面板设置中，**Image topic** 选择 `/unity/camera`
4. 画面即实时显示

### 参数调整

在 FoxgloveCameraPublisher 组件中可调整：
- **Width / Height**: 分辨率（默认 640x480）
- **Jpeg Quality**: 10-100（默认 70）
- **Publish Rate Hz**: 发布频率（默认 10）

## Plot 面板

Plot 面板用于绘制数值随时间变化的曲线图。

### 添加曲线

1. 点击 **+** > 选择 **Plot** 面板
2. 点击面板中的 **+** 添加曲线（Series）
3. 在 **value** 输入框中填入数据路径：
   - `/tf.translation.x` -- X 轴位移
   - `/tf.translation.y` -- Y 轴位移
   - `/tf.translation.z` -- Z 轴位移
   - `/tf.rotation.w` -- 旋转四元数 W 分量
4. 可自定义每条曲线的颜色、线宽

### 典型用法

同时添加 `/tf.translation.x`、`/tf.translation.y`、`/tf.translation.z` 三条曲线，观察物体在三个轴上的运动轨迹。

### 时间范围

Plot 面板底部的时间轴可拖动，支持显示不同时间窗口的数据。默认显示最近 30 秒。

## Parameters 面板

Parameters 面板用于查看和修改 Unity 端注册的可写参数。

### 查看参数

1. 点击 **+** > 选择 **Parameters** 面板
2. 参数列表自动显示所有注册的参数及其当前值

### 修改参数值

- 仅 `Writable` 为 `true` 的参数可以编辑
- 点击参数值，输入新值后回车确认
- 修改会实时同步至 Unity 端，触发 `OnParameterChanged` 回调

### Cube 示例参数

当场景中有 FoxgloveSceneCubePublisher 时，可注册以下参数以动态控制立方体外观：

| 参数名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `/cube/color` | number[] | `[0, 1, 0, 1]` | RGBA 颜色，值域 0-1 |
| `/cube/scale` | number[] | `[1, 1, 1]` | XYZ 缩放 |
| `/cube/reset_pose` | -- | -- | 对应的 Service，非参数 |

参数通过 `FoxgloveParameterComponent` 组件注册，或通过代码调用 `FoxgloveManager.RegisterParameter()`。

## Service Call 面板

Service Call 面板用于调用 Unity 端注册的远程服务。

### 调用服务

1. 点击 **+** > 选择 **Call Service** 面板
2. **Service** 下拉选择要调用的服务，如 `/cube/reset_pose`
3. **Request** 区域填写 JSON 请求体，通常为 `{}`
4. 点击 **Call service** 按钮
5. 底部显示响应结果

### 注意事项

- 服务处理通过 Unity 主线程的 `DrainServiceCalls` 驱动
- 超时时间默认为 10 秒（`FoxgloveServiceRegistry.DefaultTimeout`）
- 请求体最大 1 MiB

## Problems 面板

Problems 面板显示当前连接中的问题和错误。

### 常见错误与含义

| 错误信息 | 含义 | 解决方法 |
|---------|------|---------|
| Schema not found | 使用了未注册的 Schema 名称 | 检查拼写，确认已通过 `DefaultSchemaRegistry` 注册 |
| Channel not found | 引用了不存在的 channel | 检查 Channel ID 是否正确 |
| Connection refused | 无法连接到 Unity 服务器 | 确认 Unity 在 Play Mode，端口未被占用 |
| Timeout | 服务调用超时 | 检查服务处理逻辑，或调整 timeout 参数 |
| Unsupported compression | 不支持的压缩算法 | 仅支持 lz4、zstd、无压缩 |

## 布局导入

SDK 提供了预配置的 Foxglove 布局文件 `FoxgloveLayout.json`，位于包目录：

```
Packages/dev.unity2foxglove.sdk/Samples~/BasicVisualization/FoxgloveLayout.json
```

### 导入步骤

1. 在 Foxglove Desktop 中，点击顶部菜单 **Layout** > **Import layout...**
2. 选择 `FoxgloveLayout.json` 文件
3. 导入后自动打开以下预配置面板：

| 面板 | 配置 |
|------|------|
| 3D（左上） | 坐标系 `unity_world`，显示 `/scene`，隐藏 `/unity/camera` |
| Raw Messages（左侧中） | 订阅 `/tf` |
| Image（左中） | 显示 `/unity/camera` |
| Call Service（左中下） | 预配置 `/cube/reset_pose`，请求体 `{}` |
| Topic Graph（左中下） | 显示话题拓扑 |
| Plot（左下） | 绘制 `/tf.translation.x/y/z` 三条曲线 |
| Parameters（右下） | 显示可写参数 |
| Publish（右下） | 预配置 `/unity/camera` 的 `foxglove.CompressedImage` 发布 |

### 布局结构

导入后的布局分为三列：
- **左列**：3D 面板（主视图）+ Raw Messages（/tf）
- **中列**：Image（相机画面）+ Call Service + Topic Graph
- **右列**：Plot（/tf 曲线）+ Parameters + Publish + Raw Messages（/debug）

可以根据需要自由调整面板位置和大小，调整后通过 **Layout** > **Export layout...** 保存自定义布局。
