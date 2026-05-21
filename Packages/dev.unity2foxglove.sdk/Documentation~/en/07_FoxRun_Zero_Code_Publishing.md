## 1. Purpose

Use this page to publish simple debug and telemetry values without writing a custom publisher component.

## 2. Workflow

You will add `[FoxRun]` attributes to fields or properties, see `/debug/...` topics in Foxglove, and verify that the generated Player fallback works in IL2CPP builds.

## 3. FoxRun Use Cases

FoxRun is a convenience layer for debug and telemetry values.

Good uses:

- Current object position
- Current speed
- Runtime state string
- Small diagnostic structs
- Explicit event snapshots, such as a state transition or decoded packet status

Use regular publisher components when you need:

- Full control over schema registration
- High-frequency binary data
- Large image or mesh payloads
- A stable production API surface

## 4. Minimal Example

```csharp
using UnityEngine;
using Unity.FoxgloveSDK.Components;

public partial class TestLog : MonoBehaviour
{
    [FoxRun("/debug/position")]
    public Vector3 Position => transform.position;

    [FoxRun("/debug/name", RateHz = 1)]
    public string ObjectName => gameObject.name;
}
```

Requirements:

- The class must be `partial`.
- The member must be a field or property.
- The topic must start with `/`.
- The value must be serializable by the SDK's JSON path.

## 5. See FoxRun Topics in Foxglove

1. Add the script to a GameObject.
2. Press **Play**.
3. Connect Foxglove to `ws://127.0.0.1:8765`.
4. Open the **Topics** panel.
5. Look for `/debug/position`, `/debug/name`, or your chosen topic.

You can inspect values with a Raw Messages panel or plot numeric fields if the value shape is numeric.

## 6. Attribute Fields

Think of a FoxRun attribute as:

```csharp
[FoxRun("topic path", options...)]
```

The first argument is the Foxglove topic path. Options are named C# attribute properties such as `RateHz`, `SchemaName`, `PublishMode`, `ChangeEpsilon`, and `ForceIntervalSeconds`.

| Field | Default | What it does | When to change it | Common mistakes |
|---|---:|---|---|---|
| `Topic` | Required | Topic name published to Foxglove. | Change for each debug value you want to expose. | Forgetting the leading `/`. |
| `RateHz` | `10` | Maximum publish frequency for that member; `0` or less disables scheduled publishing. | Lower noisy debug values, temporarily disable a debug topic, or raise smooth plots carefully. | Setting very high rates for many JSON values. |
| `SchemaName` | Empty | Optional explicit schema name. | Use when you need a stable named schema. | Adding a schema name without checking the viewer expects it. |
| `PublishMode` | `FixedRate` | Controls when generated code publishes. | Use `OnChange`, `OnChangeOrInterval`, or `OnTrigger` for non-fixed-rate telemetry. | Expecting `OnTrigger` to publish without calling the generated trigger method. |
| `ChangeEpsilon` | `0` | Numeric tolerance for change-driven modes. | Suppress tiny float jitter. | Expecting it to affect `FixedRate` or `OnTrigger`. |
| `ForceIntervalSeconds` | `0` | Heartbeat interval for `OnChangeOrInterval`. | Publish an occasional heartbeat even when unchanged. | Expecting it to affect `OnTrigger`. |

## 7. Publish Modes

FoxRun supports four publish modes:

| Mode | Behavior |
|---|---|
| `FixedRate` | Publishes on the scheduled hub timer after `RateHz` elapses. |
| `OnChange` | Publishes the first value and later changed values after the scheduled timer fires. |
| `OnChangeOrInterval` | Publishes changed values, plus heartbeat samples after `ForceIntervalSeconds`. |
| `OnTrigger` | Publishes only when user code calls the generated trigger method. Scheduled hub ticks skip the topic. |

`OnTrigger` is intentionally explicit. The generator does not subscribe to C# events or UnityEvents and does not serialize event arguments. Set the field or property first, then call the generated trigger method.

For concise examples, you can import the publish-mode enum members:

```csharp
using static Unity.FoxgloveSDK.Components.FoxRunPublishMode;
```

Then `PublishMode = OnTrigger` is equivalent to `PublishMode = FoxRunPublishMode.OnTrigger`.

```csharp
using UnityEngine;
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunPublishMode;

public partial class StateReporter : MonoBehaviour
{
    [FoxRun("/events/state", PublishMode = OnTrigger)]
    private string _state;

    private void OnEnable()
    {
        _state = "enabled";
        FoxRun_Trigger_state();
    }
}
```

For external callbacks, copy decoded data into a field that generated code can read, then call the trigger method from the Unity main thread:

```csharp
using UnityEngine;
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunPublishMode;

public partial class PacketReporter : MonoBehaviour
{
    [FoxRun("/events/ouster", PublishMode = OnTrigger)]
    private string _lastFrameStatus;

    private void OnOusterFrameDecoded(PointCloudFrame frame)
    {
        _lastFrameStatus = $"points={frame.Points.Count}";
        FoxRun_Trigger_lastFrameStatus();
    }
}
```

Generated trigger methods return `true` only when the publish dispatch succeeds. They return `false` when no Foxglove manager is running, the topic index is invalid, or live publishers are suppressed during replay.

If one grouped topic contains any `OnTrigger` member, that topic is trigger-only. It will not publish from scheduled hub ticks; call a generated trigger method to publish the grouped topic.

## 8. Threading Notes

Generated trigger methods are main-thread-oriented. They may read Unity-owned fields, properties, transforms, or objects. Unity callbacks such as `Update`, `OnEnable`, or collision callbacks can call them directly. Background packet, network, or worker callbacks should marshal to the Unity main thread before calling generated trigger methods.

Phase 53 does not add an event queue, dispatcher, automatic event subscription, UnityEvent parsing, collision snapshot generation, or event-argument serialization.

## 9. IL2CPP Notes

In the Unity Editor, FoxRun source is generated during compilation. For Player builds, Unity2Foxglove also generates physical fallback `.g.cs` files before building.

When the IL2CPP build starts, you should see logs like:

```text
[FoxrunBuildPreprocess] Generating FoxRun source files...
[FoxrunCodeGenerator] Generated TestLog_FoxRun.g.cs
```

You normally do not need to manage these files. The build preprocess step writes them only when content changes.

## 10. Canonical Manifest Governance

During build-time FoxRun generation, the SDK also writes a canonical manifest under `Assets/Generated/FoxRun/`. Entering Editor Play Mode refreshes the same manifest artifacts before play starts, without writing physical `_FoxRun.g.cs` fallback files. This manifest is a governance and evidence artifact for the resolved `[FoxRun]` telemetry contract. The manifest artifact itself does not change runtime publishing behavior.

Phase 112 also locks the FoxRun non-positive `RateHz` policy: `RateHz` values of `0` or less disable scheduled publishing for that topic. This keeps the runtime behavior aligned with the canonical policy value recorded as `0`.

The canonical manifest and its SHA-256 fingerprints are computed from deterministic JSON. They ignore generated timestamps, comments, file paths, Unity `Library/` contents, and machine-local state. Timestamps and warnings appear only in the report JSON, not in the canonical manifest hash input.

Phase 112 covers FoxRun automatic telemetry only. Later phases may use these hashes in generated runtime schema info, MCAP metadata, replay checks, or broader schema manifest sections.

## 11. Debug Overlay Topics

For temporary diagnostics that should stay outside the FoxRun contract, publish explicit `/debug/...` schemaless JSON through the debug overlay helper. Debug overlay messages are non-contract data: they are not included in `foxrun.manifest.json`, `foxrun.manifest.hash`, or the canonical manifest fingerprints, and they are not replay guard keys. MCAP recording may still capture them as ordinary JSON frames, but replay schema mismatch checks should ignore them.

## 12. Troubleshooting

| Symptom | Check |
|---|---|
| No `/debug/...` topics in Editor | The class is `partial`, the component is enabled, and Play Mode is running. |
| Works in Editor but not Player | Run the IL2CPP build script and check for `[FoxrunBuildPreprocess]` logs. |
| Topic exists but value is stale | Check `RateHz`, Play Mode state, and whether the property value changes. |
| `OnTrigger` topic exists but does not update | Confirm user code calls the generated `FoxRun_Trigger_...()` method after setting the value. |
| Generated trigger method returns `false` | Confirm the Foxglove manager is running and live publishers are not suppressed by replay mode. |
| Build loops or recompiles too often | Generated fallback files should only be written when content changes. |

## 13. Where to Learn More

- Use [09_IL2CPP_Build_Guide](09_IL2CPP_Build_Guide.md) for build verification.
- Use [10_Architecture](10_Architecture.md) for generator and fallback internals.
