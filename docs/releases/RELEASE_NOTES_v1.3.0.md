# Unity2Foxglove v1.3.0 Release Notes

Release date: 2026-05-12

Unity2Foxglove v1.3.0 was a package metadata and release-document synchronization step between the typed sensor publisher release and the later v1.4.0 security/replay documentation refresh.

## Highlights

- Package metadata, README badges, and release-note links were synchronized for v1.3.0.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.

## Verification

Run before publishing a release from this branch:

```powershell
dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
python Scripts/release/validate_package.py
python Scripts/performance/run_baseline.py --quick --output build/performance/release
```
