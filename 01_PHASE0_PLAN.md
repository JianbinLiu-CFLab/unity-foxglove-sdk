---
title: Foxglove Unity SDK Phase 0 执行计划
aliases:
  - Phase 0 Plan
  - FoxgloveSDK Phase 0
  - PHASE0_PLAN
tags:
  - plan
  - phase0
  - todo
  - unity
  - foxglove
status: draft
updated: 2026-04-30
---

# Foxglove Unity SDK Phase 0 执行计划

> [!summary]
> 这份笔记是从 [[00_PLAN]] 里拆出来的 Phase 0 执行版，目标不是再讲方案，而是把当前阶段变成可以直接勾选推进的 Todo。

## 对应关系

- 上位计划：[[00_PLAN]]
- 对应阶段：`Phase 0 - 技术决策与 Unity 包落地`
- 本阶段目标：
  把当前 `FoxgloveSDK` 从验证性质的 .NET 工程，推进到可进入 Unity 的 SDK 骨架，并产出首轮技术决策。

## 当前约定

- package id：`dev.unityfoxglove.sdk`
- 根命名空间：`Unity.FoxgloveSDK`
- 子命名空间示例：
  - `Unity.FoxgloveSDK.Protocol`
  - `Unity.FoxgloveSDK.Transport`
  - `Unity.FoxgloveSDK.Schemas`
  - `Unity.FoxgloveSDK.Components`

## 本阶段不做

- 不进入 `advertise` / `subscribe` / `publish` 的完整协议实现
- 不做 MCAP
- 不做 Parameters / Services / Assets
- 不做跨平台 Native Plugin 全覆盖

## 完成标准

- [x] 明确首版技术路线：`纯 C# MVP` 或 `官方 C FFI Native Plugin`
- [x] 产出一份简短决策记录，说明为什么这样选
- [x] 明确 Unity 版本、目标平台和包命名
- [x] 输出 Unity Package 骨架，而不是继续堆在验证用 .NET 项目里
- [x] 明确 transport / schema / clock 三个抽象边界
- [x] 确认 `Newtonsoft.Json` 作为首版序列化方案
- [x] 有一个最小验证入口，能证明后续 Phase 1 可以接上

## 交付物

- [x] `01_PHASE0_PLAN.md` 自身持续更新
- [x] 一份 Unity Package 目录骨架草案
- [x] 一份 backend 选择结论
- [x] 一份最小验证说明
- [x] 从 `00_PLAN.md` 到本笔记的链接

## Todo

### A. 现状盘点

- [x] 记录当前 `FoxgloveSDK` 目录中哪些文件只是验证代码，哪些值得保留
- [x] 确认当前项目对 Unity 不友好的点
  - `net9.0`
  - 非 UPM 结构
  - 没有 asmdef
  - 没有 Samples / Documentation / Tests 分层
- [x] 确认本地 `foxglove-sdk` 仓库哪些内容是后续事实来源
  - `schemas/jsonschema/`
  - `c/include/foxglove-c/foxglove-c.h`
  - `rust/.../protocol/`

### B. 技术决策

- [x] 做一个 0.5 到 1 天的 spike，比较两条路线
  - 纯 C# 实现 ws-protocol
  - 封装官方 C FFI 为 Unity Native Plugin
- [x] 输出推荐结论
  - 默认推荐：先做 `纯 C# MVP`
- [x] 给出不选另一条路线的原因
- [x] 明确“什么情况下在 Phase 5 切到 Native Backend”

### C. 包结构设计

- [x] 确定 package id
  - 当前定稿：`dev.unityfoxglove.sdk`
- [x] 确定 Unity 最低版本
  - 建议值：`Unity 2022 LTS`
- [x] 设计 UPM 目录结构
  - `Runtime/`
  - `Tests/Editor/`
  - `Tests/Runtime/`
  - `Samples~/`
  - `Documentation~/`
  - `package.json`
- [x] 确定命名空间策略
  - 当前定稿：`Unity.FoxgloveSDK`
- [x] 明确哪些代码应该留在 `Runtime/Core`
- [x] 明确哪些代码应该隔离到 `Transport`

### D. 抽象边界

- [x] 定义 `IFoxgloveTransport`
- [x] 定义 `IFoxgloveClock`
- [x] 定义 `ISchemaRegistry`
- [x] 明确 `ManagedWsBackend` 是当前默认 backend
- [x] 预留 `NativeFoxgloveBackend` 接口位置，但本阶段不强行实现

### E. 序列化与兼容性

- [x] 确定首版统一使用 `Newtonsoft.Json`
- [x] 列出 `JsonUtility` 不适合作为主方案的原因
  - 可选字段支持弱
  - 字典支持差
  - camelCase 对齐麻烦
  - 协议 DTO 不够舒服
- [x] 记录 IL2CPP / AOT 风险点
- [x] 预留后续 link.xml 或裁剪配置的检查项

### F. 最小验证入口

- [x] 确定一个最小 harness 形式
  - Editor 内组件
  - 或保留一个独立协议验证入口
- [x] 明确这个入口只验证“结构和生命周期是否可接入 Unity”
- [x] 不在本阶段追求完整 Foxglove 连通

## 建议执行顺序

1. 先做 `A. 现状盘点`
2. 再做 `B. 技术决策`
3. 决策稳定后做 `C. 包结构设计`
4. 然后落 `D. 抽象边界`
5. 同步补 `E. 序列化与兼容性`
6. 最后收口到 `F. 最小验证入口`

## 决策记录模板

> [!note]
> 可以直接复用下面这个模板记录 Phase 0 的结论。

### 决策项

- 决策主题：
- 备选方案：
- 结论：
- 选择原因：
- 放弃原因：
- 后续影响：

## 风险检查

- [x] 不要在 Phase 0 里把协议实现做深，避免过早进入 Phase 1
- [x] 不要同时推进两套 backend 的完整实现
- [x] 不要先写大量 Unity 业务 API，再补底层抽象
- [x] 不要跳过 UPM 结构，继续在当前验证工程上叠加

## 下一阶段入口

> [!success]
> 当本笔记里的“完成标准”全部勾完，就可以进入 [[00_PLAN]] 里的 Phase 1。
