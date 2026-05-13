# 15. Secure WSS

## 1. 目的

当你希望 Unity2Foxglove 直接从 Unity Editor 或 Standalone Player 监听 `wss://` 时，使用本页。

默认的 `ws://127.0.0.1:8765` 仍然是同机本地开发的推荐路径。`SecureWebSocket` 适合演示、实验室网络、局域网测试，或者你希望 TLS 由 Unity 进程直接承担的场景。

## 2. 当前能力边界

Unity2Foxglove 已支持：

- Unity-native `wss://` transport，也就是 `ManagedWssBackend`。
- PFX 证书和私钥加载。
- Inspector 一键生成本地开发证书。
- 默认内置证书生成器，不要求普通用户安装 OpenSSL。
- Root CA 分发辅助页面。
- SHA-256 fingerprint 显示，用于人工核对 Root CA。
- hosted Foxglove Web Origin 允许项。
- 可选 shared query-token gate。

仍然不支持：

- OAuth、用户账号、角色权限。
- mTLS 客户端证书身份认证。
- 面向公网的完整生产认证/授权系统。

所以不要再把项目描述为“无 TLS”。更准确的表述是：**支持可选 WSS/TLS 和轻量 shared token gate，但没有生产级身份认证和授权系统。**

## 3. Inspector 设置

1. 选中带有 `FoxgloveManager` 的 GameObject。
2. 将 **Transport Mode** 设为 `SecureWebSocket`。
3. 本地测试时保持 **Host** 为 `127.0.0.1`。
4. 保持 **Port** 为 `8765`，或换成空闲端口。
5. 保持 **Certificate Generator** 为 `Built-in`。
6. 点击 **Generate Local Dev Certificate**。
7. 确认 **Certificate Pfx Path** 和 **Root Ca File Path** 已自动回填。
8. 核对 Inspector 显示的 **Root CA SHA-256**。
9. 手动导入并信任 Root CA。
10. 重新进入 Play Mode 或重启 WSS server。
11. 使用 `wss://127.0.0.1:8765` 连接 Foxglove Desktop 或 Foxglove Web。

本地开发证书写入：

```text
UserSettings/Unity2Foxglove/Certificates/
```

`UserSettings/` 不应提交到 git。不要把生成的 PFX 私钥文件复制到受版本控制的源码目录。

## 4. OpenSSL 策略

默认路径不需要 OpenSSL。内置生成器使用 Unity 自带的 Mono 证书 API 生成本地开发 PFX 和 Root CA。

OpenSSL 只作为显式 fallback：

- 用户选择 `OpenSSL` 证书生成器时才使用。
- 如果没找到 OpenSSL，Inspector 会提示安装或手动选择 `openssl.exe`。
- SDK 不会静默安装 OpenSSL，也不会修改系统 PATH。

## 5. Foxglove Web Origin

Foxglove Web 会发送浏览器 `Origin` header。Origin 只包含：

```text
scheme://host[:port]
```

它不包含用户、项目、layout 或 query string。因此 hosted Foxglove Web 应允许：

```text
https://app.foxglove.dev
```

不要把完整 layout URL 写成唯一允许项。SDK 会把粘贴的完整 URL 规范化为 origin 后再匹配。

## 6. Shared Token 限制

shared token 是轻量本地/局域网 gate：

- token 放在 WebSocket URL query string 中。
- 在 `ws://` 中是明文。
- 可能出现在浏览器历史、截图、代理日志或客户端诊断信息里。
- 在 `wss://` 中，TLS 握手成功后传输会被加密。
- 它不是 OAuth、mTLS、用户身份或权限系统。

建议 shared token 配合 WSS 使用，不要把账号密码等敏感长期凭据当 token。

## 7. 常见问题

| 现象 | 检查 |
|---|---|
| WSS 立即失败 | 检查 PFX 路径、密码、私钥和证书有效期。 |
| 浏览器拒绝 WSS | 导入并信任 Root CA，确认连接 host 被证书 SAN 覆盖。 |
| Unity 显示 TLS/WebSocket handshake warning | 客户端在 WebSocket upgrade 前断开。证书重新生成后，浏览器需要重新信任新的 Root CA。 |
| Foxglove Web Origin 被拒绝 | 启用 **Allow Hosted Foxglove Web**，或把自部署 Web app 的 origin 加入 allowlist。 |
| token 连接失败 | 确认 URL 中的 token 和 Inspector token 完全一致。 |
| OpenSSL not found | 只有选择 OpenSSL backend 时才需要安装或选择 OpenSSL。普通路径请使用 `Built-in`。 |

英文完整说明见 [Secure WSS](../en/15_Secure_WSS.md)。
