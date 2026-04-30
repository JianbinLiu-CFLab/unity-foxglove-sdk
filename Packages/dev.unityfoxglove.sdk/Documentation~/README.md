# Foxglove Unity SDK

Real-time data streaming from Unity to [Foxglove](https://foxglove.dev) for visualization.

## Status

Phase 0 — package skeleton, architecture, and tech decision. Not yet functional.

## Supported Unity Versions

- Unity 2022 LTS+
- Editor + Standalone Player (Windows first)
- WebGL not supported

## Quick Start (future)

```csharp
var runtime = new FoxgloveRuntime();
runtime.Start("My Unity App", port: 8765);

var channel = new AdvertiseChannel
{
    Id = 1,
    Topic = "/unity/transform",
    Encoding = "json",
    SchemaName = "foxglove.FrameTransform"
};
runtime.Session.Channels.Register(channel);

// Each frame:
runtime.Session.Publish(1, jsonBytes);
```

## Architecture

See [Architecture.md](Architecture.md) for the transport abstraction and module breakdown.

## Dependencies

- `com.unity.nuget.newtonsoft-json` — JSON serialization
- WebSocketSharp-netstandard — pure C# WebSocket server (bundled)
