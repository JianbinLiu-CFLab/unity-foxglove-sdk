## 1. Purpose

Use this page as the field reference for configuring Unity2Foxglove components in the Unity Inspector.

## 2. Workflow

You will learn what the main Inspector fields do, when to change them, and which mistakes commonly break Foxglove visualization.

## 3. FoxgloveManager

### 3.1 General

| Field | Default | What it does | When to change it | Common mistakes |
|---|---:|---|---|---|
| Server Name | `Unity Foxglove SDK` | Name shown to Foxglove in server info. | Change when multiple Unity apps are visible. | Expecting it to change the WebSocket URL. |
| Transport Mode | `WebSocket` | Chooses plain `ws://` or secure `wss://`. | Use `SecureWebSocket` when Unity should host TLS directly. | Expecting one manager to run both plain and secure listeners at once. |
| Host | `127.0.0.1` | Interface the server binds to. | Use `0.0.0.0` only when remote machines must connect. | Binding publicly without understanding network exposure. |
| Port | `8765` | WebSocket port. | Change if another process uses `8765`. | Connecting Foxglove to the old port after changing it. |
| Start On Enable | Enabled | Starts the server when the component is enabled. | Disable if another script controls lifecycle. | Disabling it and never calling `StartServer()`. |
| Run In Background | Enabled | Keeps Unity running when focus changes. | Usually keep enabled for Foxglove tests. | Disabling it and wondering why updates pause. |

### 3.2 Coordinate System

| Field | Default | What it does | When to change it | Common mistakes |
|---|---:|---|---|---|
| Coordinate Mode | `LeftHand` | Publishes Unity-native coordinates or converts to right-handed coordinates. | Use `RightHand` when integrating with ROS/Foxglove coordinate expectations. | Mixing modes between live publish, recording, and replay. |

### 3.3 Assets and Playback

| Field | Default | What it does | When to change it | Common mistakes |
|---|---:|---|---|---|
| Asset Roots | Empty | Maps asset URI prefixes to local folders. | Use when Foxglove needs to fetch file-backed assets. | Pointing to machine-specific absolute paths in shared samples. |
| Enable Playback Control | Disabled | Enables Foxglove playback commands. | Use for replay/time-control workflows. | Expecting it to move Unity objects without a replay source. |
| Playback Start Offset Seconds | `0` | Start time offset for playback control. | Tune when simulating a time range. | Using negative or confusing offsets without checking timeline. |
| Playback Duration Seconds | `60` | Playback control time window. | Increase for longer manual replay sessions. | Too short a range makes seeking confusing. |

### 3.4 MCAP Recording

| Field | Default | What it does | When to change it | Common mistakes |
|---|---:|---|---|---|
| Enable Recording | Disabled | Writes live data to MCAP while Play Mode runs. | Enable before starting a recording session. | Enabling it after expecting earlier messages to be captured. |
| Recording Prefix | `foxglove` | Prefix for generated `.mcap` file names. | Change per scenario or test case. | Using characters that are awkward in file names. |
| Recording Directory | Empty | Empty means `Recordings/` next to the Unity project. | Set a predictable folder for demos or CI. | Committing personal absolute paths in sample scenes. |
| Recording Chunk Size KB | `1024` | MCAP chunk size target. | Increase for large continuous recordings. | Very small chunks can make files inefficient. |
| Recording Compression | `None` | Compression mode: `None`, `Lz4`, or `Zstd`. | Use `Lz4` or `Zstd` for larger recordings. | Forgetting compression DLLs when packaging. |

### 3.5 MCAP Replay

| Field | Default | What it does | When to change it | Common mistakes |
|---|---:|---|---|---|
| Enable Replay | Disabled | Loads and replays an MCAP file. | Enable for Unity-side replay tests. | Enabling recording and replay at the same time. |
| Replay File Path | Empty | Path to the `.mcap` file. | Set before Play Mode for replay. | Leaving a personal absolute path in shared scenes. |
| Replay Auto Play | Disabled | Starts replay automatically. | Enable for quick acceptance tests. | Expecting replay to advance while paused. |
| Disable Live Publishers | Enabled | Disables live publishers during replay. | Keep enabled for clean replay verification. | Disabling it and mixing live and replayed messages. |

### 3.6 Security / WSS

| Field | Default | What it does | When to change it | Common mistakes |
|---|---:|---|---|---|
| Allow Hosted Foxglove Web | Enabled | Adds `https://app.foxglove.dev` to the browser Origin allowlist at runtime. Layout, project, user, and query-string changes do not affect this origin. | Leave enabled for hosted Foxglove Web. | Disabling it while expecting the hosted web app to connect. |
| Allowed Browser Origins | `https://app.foxglove.dev` | Additional browser Origin allowlist entries for CSWSH protection. Full page URLs are accepted and normalized to `scheme://host[:port]`. | Add a custom/private web app origin once. | Adding `*`; wildcard origins are not supported. |
| Certificate Pfx Path | Empty | PFX certificate and private key for WSS. | Required for `SecureWebSocket`. | Pointing to a `.crt` without a private key. |
| Certificate Password | Empty | Password for the PFX file. | Set when the PFX is password protected. | Logging or sharing real passwords. |
| Shared Token | Empty | Optional query-token gate. | Use with WSS for simple local/LAN gating. | Treating it as strong authentication or using it over plain `ws://`. |
| Certificate Generator | `Built-in` | Selects the local development certificate backend. | Keep `Built-in` for the normal Unity/Mono no-OpenSSL path. Choose `OpenSSL` only when you need the fallback path. | Expecting the SDK to install OpenSSL automatically. |
| Generate Local Dev Certificate | Button | Creates an ignored local self-signed certificate and fills the WSS fields. | Use for local Editor or demo WSS setup. | Treating the generated PFX as production trust material. |
| Root CA Distributor Enabled | Disabled | Starts a small HTTP server for downloading the root CA. | Enable for first-time local trust setup. | Trusting the HTTP download without checking the SHA-256 fingerprint. |
| Root CA Distributor Host | `127.0.0.1` | Bind address for the CA HTTP server. | Keep loopback for local setup; use LAN addresses only intentionally. | Binding `0.0.0.0` on untrusted networks. |
| Root CA Distributor Port | `8766` | HTTP port for the CA distributor. | Change if another process uses it. | Confusing it with the WebSocket port. |
| Root CA File Path | Empty | Root CA file served by the distributor. | Set when the distributor is enabled. | Serving the wrong certificate file. |

When another person or device imports the CA, require a manual verification step: compare the `Root CA SHA-256` value shown in Unity with the fingerprint shown on the distributor root page before trusting the certificate. Keep the shared token out of approval records and deliver it through a separate trusted channel. The local generator fills project fields only; it does not import the CA into any OS trust store. The default built-in generator uses Unity's bundled Mono certificate APIs; OpenSSL is optional and used only when the OpenSSL generator is selected.

## 4. FoxglovePublisherBase Fields

These fields appear on publisher components derived from `FoxglovePublisherBase`.

| Field | Default | What it does | When to change it | Common mistakes |
|---|---:|---|---|---|
| Manager | None | Explicit `FoxgloveManager` reference. | Set when multiple managers exist or auto lookup is unreliable. | Leaving it empty in a scene with multiple managers. |
| Topic | Component-specific | Topic name sent to Foxglove. | Change to match your topic naming convention. | Forgetting the leading `/` or duplicating topics accidentally. |
| Publish Rate Hz | `10` | Maximum publish frequency. | Lower for heavy data; raise for smoother plots. | Setting very high rates for camera/image topics. |
| Publish On Enable | Enabled | Starts publishing when the component is enabled. | Disable when another script controls publishing. | Disabling it and expecting automatic output. |
| Warn If Manager Missing | Enabled | Logs a warning if no manager is found. | Disable only for intentionally inactive prefabs. | Hiding useful setup warnings. |

## 5. FoxgloveTransformPublisher

| Field | Default | What it does | When to change it | Common mistakes |
|---|---:|---|---|---|
| Parent Frame Id | `unity_world` | Parent coordinate frame. | Change when you have a different world/root frame. | Using inconsistent parent frames across objects. |
| Child Frame Id | Empty | Child frame; empty uses the GameObject name. | Set explicitly for stable frame names. | Renaming GameObjects and breaking layouts or plots. |
| Topic | `/tf` | Transform topic. | Rarely change unless separating transform streams. | Using a non-`/tf` topic and forgetting to update Foxglove panels. |
| Publish Rate Hz | `10` | Transform publish rate. | Increase for smoother motion; lower to reduce traffic. | Too high rates for many objects. |

## 6. FoxgloveSceneCubePublisher

| Field | Default | What it does | When to change it | Common mistakes |
|---|---:|---|---|---|
| Entity Id | Empty | Stable scene entity ID; empty uses a generated/default identity. | Set when multiple scene objects need stable IDs. | Reusing the same ID for different objects. |
| Frame Id | Empty | Frame used for the cube primitive. | Set to match a Transform publisher frame. | Frame mismatch makes the cube appear offset. |
| Size | `(1, 1, 1)` | Cube dimensions. | Change to match the Unity object's visual size. | Expecting it to read Unity mesh bounds automatically. |
| Color | Green | Cube RGBA color. | Change for visual grouping. | Forgetting alpha if the color appears invisible. |
| Topic | `/scene` | Scene update topic. | Change only when separating scene streams. | Changing topic without updating Foxglove 3D settings. |

## 7. FoxgloveCameraPublisher

| Field | Default | What it does | When to change it | Common mistakes |
|---|---:|---|---|---|
| Frame Id | `unity_camera` | Frame ID associated with the image. | Set when coordinating camera pose with `/tf`. | Using a frame ID that is never published. |
| Width | `640` | Captured image width. | Increase for sharper images. | Too high a value can hurt performance. |
| Height | `480` | Captured image height. | Increase for sharper images. | Mismatched aspect ratio can stretch output. |
| JPEG Quality | `70` | Compression quality. | Raise for clearer images; lower for bandwidth. | Setting `100` without checking bandwidth. |
| Max Pending Readbacks | `2` | Limits outstanding GPU readbacks. | Increase only if frames are skipped and GPU can handle it. | Too high can increase latency and memory use. |
| Topic | `/unity/camera` | Image topic. | Change when publishing multiple cameras. | Two cameras publishing to the same topic unintentionally. |
| Publish Rate Hz | `10` | Image publish rate. | Lower for slow networks; raise carefully. | High rate plus high resolution can overload the Player. |

## 8. FoxgloveReplayObjectAdapter

| Field | Default | What it does | When to change it | Common mistakes |
|---|---:|---|---|---|
| Manager | None | Manager used for replay messages and coordinate conversion. | Set explicitly in complex scenes. | Leaving it empty when auto lookup finds the wrong manager. |
| Auto Lookup | Enabled | Automatically finds replay targets. | Disable for explicit mappings. | Expecting auto lookup to handle renamed frames/entities. |
| Frame Overrides | Empty | Maps recorded frame IDs to scene objects. | Use when replay file frame names differ from scene names. | Forgetting overrides after renaming objects. |
| Entity Overrides | Empty | Maps recorded entity IDs to scene objects. | Use for scene primitive replay. | Reusing one target for multiple recorded entities. |
| Drive TF | Enabled | Applies replayed `/tf` transforms. | Disable if another system drives transforms. | Live scripts fighting replay updates. |
| Drive Scene | Enabled | Applies replayed scene primitives. | Disable for TF-only replay. | Expecting scene primitives to move when disabled. |

## 9. Demo-Only Scripts

`FoxgloveDemoSetup` and `MouseDragCube` are demo/sample scripts. They are documented in the Full Demo sample and `Unity2Foxglove` demo documentation rather than treated as SDK core API.

Use them as examples, not as required components for your own project.
