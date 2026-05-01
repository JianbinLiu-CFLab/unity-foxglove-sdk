using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class FoxglovePhase5Build
{
    [MenuItem("Foxglove/Phase 5: Build Windows IL2CPP")]
    public static void BuildWindowsIl2Cpp()
    {
        var scenes = new[]
        {
            "Assets/Scenes/SampleScene.unity"
        };

        var outputPath = "build/Unity/Phase5WindowsIL2CPP/FoxgloveDemo.exe";

        var namedTarget = NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.Standalone);
        PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(namedTarget, ManagedStrippingLevel.Medium);

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.totalErrors == 0)
        {
            Debug.Log($"Build succeeded: {outputPath}");
        }
        else
        {
            throw new System.Exception($"Build failed with {report.summary.totalErrors} errors: {report.summary}");
        }
    }
}
