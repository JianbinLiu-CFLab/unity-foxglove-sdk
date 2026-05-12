# Unity2Foxglove v1.3.0 Release Notes

Release date: 2026-05-12

Unity2Foxglove v1.3.0 prepares the next package release. Replace this summary with the final user-facing release description before publishing.

## Highlights

- Version metadata and release documents have been prepared.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.

## Verification

Run before publishing the release:

```powershell
dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
python Scripts/validate_release_package.py
python Scripts/run_performance_baseline.py --quick --output build/performance/release
```
