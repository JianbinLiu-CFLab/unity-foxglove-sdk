# 1. Parameters and Services

Parameters and Services are two independent communication mechanisms in the Foxglove WebSocket protocol, distinct from Topic publish/subscribe. This document explains their purpose, how to configure them in the SDK, and how to use them in Foxglove.

## 1.1 Purpose

Use this guide to expose runtime configuration and user-triggered actions from Unity to Foxglove.

## 1.2 Application

Use Parameters for values that should be readable or writable during Play Mode. Use Services for discrete commands such as reset, capture, start, stop, or mode switching.

## 1.3 Parameters

### 1.3.1 Purpose

Parameters allow Foxglove to **read and modify** runtime variables in Unity. Typical use cases:

- Modify a Cube's color `/cube/color`
- Modify a Cube's scale `/cube/scale`
- Read or modify any value that needs runtime adjustment

### 1.1.2 Operating in Foxglove

1. Open the **Parameters panel** (not the Topics panel! Parameters has its own dedicated panel)
2. The panel lists all registered parameters, e.g., `/cube/color`, `/cube/scale`
3. Click the edit icon next to a parameter to modify its value; Unity responds in real time
4. Changes can be pushed to all connected Foxglove clients via the `parametersSubscribe` mechanism

### 1.1.3 Registering parameters in Unity

**Option 1: Drag-and-drop (recommended)**

Add `FoxgloveParameterComponent` to the GameObject whose parameters you want to expose:

- **Parameter Name**: e.g., `/cube/color`
- **Type**: e.g., `json` (supports int, float, string, json)
- **Writable**: check to allow Foxglove-side modification
- **Default Value**: initial value, e.g., `[0, 1, 0, 1]`

**Option 2: Code registration**

```csharp
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;

var mgr = FindFirstObjectByType<FoxgloveManager>();
mgr.RegisterParameter("/my/param", JToken.FromObject(42), "int", writable: true);
```

You can dynamically modify parameter values at runtime via `FoxgloveManager.Runtime.Parameters`.

### 1.1.4 Recording

MCAP recording automatically captures:
- A parameter snapshot at recording start (`foxglove.parameters.snapshot` metadata)
- Every parameter change during recording (`foxglove.parameters` metadata)

---

## 1.4 Services

### 1.4.1 Purpose

Services provide a **request-response** pattern. Foxglove sends a call request and Unity executes it and returns a result. Typical use cases:

- `/cube/reset_pose`: reset the Cube to its initial position, rotation, and scale
- Any scenario requiring Unity to execute an action and return a result

### 1.2.2 Operating in Foxglove

1. Open the **Service Call panel**
2. Select the service name from the dropdown, e.g., `/cube/reset_pose`
3. Fill in parameters in the Request box (in most cases `{}` is sufficient -- note that `{}` is valid JSON)
4. Click the **Call service** button
5. Wait for Unity to execute and return the result (default 5-second timeout)

> **Common misconception**: The service name is selected from the dropdown. Do **not** put the service name in the request JSON. The request only needs parameters.

### 1.2.3 Registering services in Unity

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
    // request is a JToken -- the parameters sent from Foxglove
    // Execute your business logic
    cube.transform.position = Vector3.zero;
    // Return the result
    return JToken.FromObject(new { status = "ok" });
});
```

The handler executes on the main thread (dispatched via `DrainServiceCalls()`), so it can directly access the Unity API.

### 1.2.4 Timeout and errors

- Default timeout: `FoxgloveServiceRegistry.DefaultTimeout`
- If the handler does not complete within the timeout, a `serviceCallFailure` is automatically sent
- Exceptions thrown inside the handler are caught and converted to failure responses

### 1.2.5 Recording

MCAP recording captures the completion/failure status of each service call (`foxglove.services` metadata).

---

## 1.3 FoxgloveDemoSetup reference

The Demo project's `Untiy2Foxglove/Assets/Scripts/FoxgloveDemoSetup.cs` is a complete Parameters + Services example, including:

- Registering `/cube/color` (writable json parameter)
- Registering `/cube/scale` (writable float parameter)
- Registering `/cube/reset_pose` (service, returns `{"status":"ok"}`)
- Listening to parameter change events and updating the Cube's Material.color and transform.localScale in real time

Refer to this script for a complete usage example.

---

## 1.4 Comparison with Topics

| Feature | Topics | Parameters | Services |
|---------|--------|------------|----------|
| Communication pattern | Publish/Subscribe (one-way stream) | Read/Write (two-way state sync) | Request/Response (RPC) |
| Panels | Topics, 3D, Plot, Image | Parameters | Service Call |
| Typical use | Real-time data streams (Transform, Camera) | Tunable configuration (color, scale) | Action triggers (reset, mode switch) |
| Recording support | Yes (messages in chunks) | Yes (metadata) | Yes (metadata) |
