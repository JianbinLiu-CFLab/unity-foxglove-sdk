# Unity2Foxglove v1.9.3 Release Notes

Release date: 2026-05-24

Unity2Foxglove v1.9.3 refreshes the optional ROS2 For Unity Jazzy Windows x64 runtime path using newly rebuilt artifacts from the maintained `ros2-for-unity` and `ros2cs` forks. The release focuses on proving that the packaged runtime works inside the real Unity2Foxglove project, with both automated checks and manual Unity interaction acceptance.

## Highlights

- **Rebuilt ROS2 For Unity runtime package:** The optional Jazzy Windows x64 runtime was rebuilt from the updated `ros2-for-unity` and `ros2cs` sources and integrated as the canonical runtime package artifact.
- **Real Unity project acceptance:** The runtime package was validated inside the Unity2Foxglove demo project rather than only through package-shape or artifact checks.
- **External ROS2 graph verification:** Windows Jazzy graph checks confirmed topic discovery, Unity-to-ROS2 string echo, and ROS2-to-Unity inbound delivery.
- **Direct runtime and facade coverage:** Manual acceptance covered both the direct runtime mode and the normal component/facade path so the runtime is not only exercised through one code path.
- **Scripted acceptance helper:** A Python smoke helper now sets up the Windows Jazzy environment and runs the external graph checks without relying on fragile interactive PowerShell snippets.
- **Architecture boundary evidence:** The repository now includes an architecture coupling gate and supporting evidence for keeping runtime modules cohesive, bounded, and easier to refactor.

## Compatibility Notes

- Existing Unity scenes keep serialized Inspector values unless changed manually.
- The core `dev.unity2foxglove.sdk` package is versioned as v1.9.3. Normal Foxglove WebSocket, MCAP recording, replay, and FoxRun workflows remain ROS-free and do not require ROS2 For Unity.
- The ROS2 For Unity path remains optional. Use `dev.unity2foxglove.ros2forunity` for the adapter/facade boundary and `dev.unity2foxglove.ros2forunity.runtime.jazzy.win64` for the packaged Jazzy Windows x64 runtime.
- Package-mode validation must ensure that Unity is loading the packaged runtime, not an old `Assets/Ros2ForUnity` direct asset import.
- The verified runtime artifact hash is `22baf2b624b0fb171efc94b403876491a66e57b39b6f747a3c2e30644ce32188`.

## Verification

Preparation verification:

```bash
python Scripts/release/bump_version.py 1.9.3 --date 2026-05-24 --dry-run
dotnet run --no-restore --project Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj
python Scripts/release/validate_package.py
python Scripts/performance/run_baseline.py --quick --output build/performance/release
python Scripts/smoke/phase127_r2fu_real_project_acceptance.py --message "phase127 manual unity interaction acceptance"
git diff --check
git ls-files -- 'Plan/**' 'Developer/**'
```

Observed results:

- Version synchronization prepared v1.9.3 package metadata, README references, changelog, and release notes.
- Version synchronization dry-run reported all v1.9.3 references aligned.
- Runtime validation completed with `All checks passed`.
- Release package validation passed.
- The rebuilt ROS2 For Unity runtime artifact hash matched `22baf2b624b0fb171efc94b403876491a66e57b39b6f747a3c2e30644ce32188`.
- Automated package checks confirmed the runtime package shape and canonical package-mode wiring.
- Manual Unity interaction acceptance confirmed `Initial Path Clean`, `Runtime Root Is Package`, ROS2 outbound echo, ROS2 inbound publish, and a green Unity console result.
- External Windows Jazzy graph checks confirmed the Unity node subscription, received Unity outbound string data, and delivered three inbound messages back to Unity.
- Private workspace tracking check returned no `Plan/` or `Developer/` paths.
