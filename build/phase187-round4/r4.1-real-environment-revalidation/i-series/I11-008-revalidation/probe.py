from pathlib import Path
root=Path('.')
assets=root/'Packages/dev.unity2foxglove.sdk/Tests/Unit/Architecture/UnityDemoSamplesAssetsTests.cs'
phase=root/'Packages/dev.unity2foxglove.sdk/Tests/Unit/Harness/Phase173100ReviewTests.cs'
a=assets.read_text(encoding='utf-8'); s=phase.read_text(encoding='utf-8')
checks={
 'two_distinct_volume_paths': 'Unity2Foxglove/Assets/Settings/DefaultVolumeProfile.asset' in a and 'Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Settings/DefaultVolumeProfile.asset' in a,
 'shell_fact_bound': 'run_bridge_sample.sh' in s and s.count('run_bridge_sample.sh')>=1,
 'three_distro_loop': 'new[] { "humble", "jazzy", "lyrical" }' in s,
 'distro_scripts_exist': all((root/f'Scripts/ros2forunity/windows/{d}/build_r2fu_runtime_package.py').is_file() for d in ('humble','jazzy','lyrical')),
}
print(checks)
print('RESULT=REFUTED' if all(checks.values()) else 'RESULT=RED')
