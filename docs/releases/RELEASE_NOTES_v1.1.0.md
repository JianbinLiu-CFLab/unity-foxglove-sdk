# Unity2Foxglove v1.1.0 Release Notes

Release date: 2026-05-10

Unity2Foxglove v1.1.0 focuses on camera protobuf parity and makes Protobuf the default publisher encoding for new `FoxgloveManager` components.

## Highlights

- `/unity/camera` can now publish official `foxglove.CompressedImage` protobuf payloads.
- Camera JPEG bytes are written directly to the protobuf `bytes data` field instead of being base64-wrapped as JSON text.
- JSON camera publishing remains available through the Manager or per-publisher encoding override.
- JSON-only publishers still fall back to JSON automatically under the Protobuf default.

## Compatibility Notes

- Existing Unity scenes that already serialized `Publisher Encoding = Json` will keep that value until changed in the Inspector.
- New `FoxgloveManager` components default to `Protobuf`.
- The paper-evidence DOI remains tied to the archived evidence release and is separate from this functional package release.

## Verification

Validated before release:

- `dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj`
- `python Scripts/release/validate_package.py`
- `python Scripts/performance/run_baseline.py --quick --output build/performance/phase48-default-protobuf`

Manual smoke:

- FullDemo camera stream was verified in Foxglove with Global Publisher Encoding set to `Protobuf`.
- Switching back to `Json` keeps `/unity/camera` working through the compatibility path.
