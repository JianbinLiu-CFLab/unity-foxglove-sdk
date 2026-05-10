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

| Field | Default | What it does | When to change it | Common mistakes |
|---|---:|---|---|---|
| `Topic` | Required | Topic name published to Foxglove. | Change for each debug value you want to expose. | Forgetting the leading `/`. |
| `RateHz` | `10` | Maximum publish frequency for that member. | Lower noisy debug values; raise smooth plots carefully. | Setting very high rates for many JSON values. |
| `SchemaName` | Empty | Optional explicit schema name. | Use when you need a stable named schema. | Adding a schema name without checking the viewer expects it. |

## 7. IL2CPP Notes

In the Unity Editor, FoxRun source is generated during compilation. For Player builds, Unity2Foxglove also generates physical fallback `.g.cs` files before building.

When the IL2CPP build starts, you should see logs like:

```text
[FoxrunBuildPreprocess] Generating FoxRun source files...
[FoxrunCodeGenerator] Generated TestLog_FoxRun.g.cs
```

You normally do not need to manage these files. The build preprocess step writes them only when content changes.

## 8. Troubleshooting

| Symptom | Check |
|---|---|
| No `/debug/...` topics in Editor | The class is `partial`, the component is enabled, and Play Mode is running. |
| Works in Editor but not Player | Run the IL2CPP build script and check for `[FoxrunBuildPreprocess]` logs. |
| Topic exists but value is stale | Check `RateHz`, Play Mode state, and whether the property value changes. |
| Build loops or recompiles too often | Generated fallback files should only be written when content changes. |

## 9. Where to Learn More

- Use [09_IL2CPP_Build_Guide](09_IL2CPP_Build_Guide.md) for build verification.
- Use [10_Architecture](10_Architecture.md) for generator and fallback internals.
