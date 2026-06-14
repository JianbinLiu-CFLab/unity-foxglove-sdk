## 1. Purpose

Use this page to control Unity runtime values from Foxglove and trigger Unity actions through Foxglove services.

## 2. Workflow

You will use the Full Demo to edit `/cube/color`, edit `/cube/scale`, and call `/cube/reset_pose`.

## 3. Parameter Model

Parameters are runtime values exposed by Unity.

Use them for values you want to inspect or edit while Play Mode is running, such as:

- Color
- Scale
- Debug values
- Runtime tuning values

In the Full Demo, the important parameters are:

| Parameter | Example value | What it controls |
|---|---:|---|
| `/cube/color` | `[0, 1, 0, 1]` | Cube RGBA color |
| `/cube/scale` | `1` | Cube uniform scale |

## 4. Use the Parameters Panel

1. Open the Full Demo sample or `Unity2Foxglove`.
2. Press **Play** in Unity.
3. Connect Foxglove to `ws://127.0.0.1:8765`.
4. Add a **Parameters** panel.
5. Find `/cube/color` and `/cube/scale`.
6. Edit the value.

Examples:

```json
[1, 0, 0, 1]
```

sets the cube to red.

```json
2
```

sets the cube scale to `2`.

> [!NOTE]
> Parameter values are JSON values. A color is an array, while scale is a number.

## 5. Service Model

Services are request-response actions. Foxglove sends a request, Unity performs an action, and Unity sends a response.

In the Full Demo, the main service is declared with `[FoxService]`:

| Service | Request | Expected response | What it does |
|---|---|---|---|
| `/cube/reset_pose` | `{}` | `{"status":"ok"}` | Resets cube position, rotation, and scale |

## 6. Declarative Services With `[FoxService]`

Use `[FoxService]` when a service should live next to the Unity method that performs the action. The declaring `MonoBehaviour` must be `partial` so the generated wrapper can call the method directly.

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

Supported method shapes:

- instance methods only;
- zero or one request parameter;
- JSON-serializable request and response DTOs;
- `void` response when `{}` is enough;
- private methods are valid on `partial` classes.

Rejected shapes include static, generic, async, `ref`/`out`/`in`, more than one parameter, open generic DTOs, pointer/by-ref/ref-like DTOs, and `Task` responses.

The generated wrapper deserializes the request from `JToken`, calls the method directly, and serializes the response back to `JToken`. In the Unity Editor this comes from the Roslyn source generator. Before Player builds, the SDK writes physical `*_FoxService.g.cs` fallback files so IL2CPP builds do not need runtime reflection invocation.

## 7. Use the Service Call Panel

1. Add a **Service Call** panel.
2. Open the panel settings.
3. Set **Service name** to `/cube/reset_pose`.
4. Put this in the request box:

```json
{}
```

5. Click **Call service /cube/reset_pose**.

The cube should reset and the response should show `status: "ok"`.

> [!WARNING]
> Do not type `{cube/reset_pose}` or `"/cube/reset_pose"` in the request box. The service name belongs in panel settings. The request body is only the JSON payload.

## 8. Empty Parameter List

Check these in order:

1. You are using the Full Demo sample or `Unity2Foxglove`, not the Basic sample.
2. Unity is in Play Mode.
3. Foxglove is connected to `ws://127.0.0.1:8765`.
4. The demo setup object is enabled.
5. Reconnect Foxglove after starting Play Mode.

## 9. Service Call Timeout

Check these in order:

1. The Service Call panel is configured with `/cube/reset_pose`.
2. The request box contains valid JSON, usually `{}`.
3. Unity is still in Play Mode.
4. The Unity Console does not show service handler errors.

## 10. Manual Service API Example

Use `RegisterParameter` and `RegisterService` from `FoxgloveManager` when you need to create or remove services dynamically at runtime. For static Unity actions, prefer `[FoxService]`.

```csharp
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Protocol;

public class MyControls : MonoBehaviour
{
    [SerializeField] private FoxgloveManager manager;

    private void Start()
    {
        manager.RegisterParameter("/my/speed", JToken.FromObject(1.0f), "float", writable: true);

        manager.RegisterService(new ServiceDescriptor
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
    }
}
```

## 11. Current Capability Notes

The current user-facing workflow supports reading and setting parameters and calling services. Service handlers run on Unity's main thread through the existing service drain path, so normal Unity API access is allowed. Payload limits and handler failures are governed by the existing Foxglove service registry and are reported to the client as service-call failures.
