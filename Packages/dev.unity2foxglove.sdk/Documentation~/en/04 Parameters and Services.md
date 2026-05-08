# 4. Parameters and Services

## Who should read this

Read this if you want to change Unity runtime values from Foxglove or trigger Unity actions from Foxglove.

## What you will do

You will use the Full Demo to edit `/cube/color`, edit `/cube/scale`, and call `/cube/reset_pose`.

## 4.1 What Parameters Are

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

## 4.2 Use the Parameters Panel

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

## 4.3 What Services Are

Services are request-response actions. Foxglove sends a request, Unity performs an action, and Unity sends a response.

In the Full Demo, the main service is:

| Service | Request | Expected response | What it does |
|---|---|---|---|
| `/cube/reset_pose` | `{}` | `{"status":"ok"}` | Resets cube position, rotation, and scale |

## 4.4 Use the Service Call Panel

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

## 4.5 If the Parameter List Is Empty

Check these in order:

1. You are using the Full Demo sample or `Unity2Foxglove`, not the Basic sample.
2. Unity is in Play Mode.
3. Foxglove is connected to `ws://127.0.0.1:8765`.
4. The demo setup object is enabled.
5. Reconnect Foxglove after starting Play Mode.

## 4.6 If Service Call Times Out

Check these in order:

1. The Service Call panel is configured with `/cube/reset_pose`.
2. The request box contains valid JSON, usually `{}`.
3. Unity is still in Play Mode.
4. The Unity Console does not show service handler errors.

## 4.7 Developer API Example

Use `RegisterParameter` and `RegisterService` from `FoxgloveManager` when writing your own scripts.

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
            RequestSchema = "{}",
            ResponseSchema = "{}"
        },
        request =>
        {
            transform.position = Vector3.zero;
            return JToken.FromObject(new { status = "ok" });
        });
    }
}
```

## 4.8 Current Capability Notes

The current user-facing workflow supports reading and setting parameters and calling services. If you need live parameter push subscriptions across multiple external clients, verify that behavior in your target version before relying on it.
