using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// General-purpose IL2CPP build script for the Foxglove demo project.
/// Usage: Unity -batchmode -quit -projectPath ... -executeMethod FoxgloveBuild.BuildIl2CppFromCommandLine -foxgloveBuildTarget win64 -logFile ...
/// </summary>
public static class FoxgloveBuild
{
    [MenuItem("Foxglove/Build Windows IL2CPP")]
    public static void BuildWindowsIl2Cpp()
    {
        BuildIl2Cpp("win64");
    }

    [MenuItem("Foxglove/Build Linux IL2CPP")]
    public static void BuildLinuxIl2Cpp()
    {
        BuildIl2Cpp("linux64");
    }

    [MenuItem("Foxglove/Build macOS IL2CPP")]
    public static void BuildMacOSIl2Cpp()
    {
        BuildIl2Cpp("macos");
    }

    public static void BuildIl2CppFromCommandLine()
    {
        var target = GetCommandLineValue("-foxgloveBuildTarget") ?? "win64";
        var outputPath = GetCommandLineValue("-foxgloveOutputPath");
        BuildIl2Cpp(target, outputPath);
    }

    private static void BuildIl2Cpp(string targetName, string outputPathOverride = null)
    {
        var scenes = new[]
        {
            "Assets/Scenes/SampleScene.unity"
        };

        var config = ResolveTarget(targetName);
        var outputPath = string.IsNullOrEmpty(outputPathOverride) ? config.OutputPath : outputPathOverride;
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        var namedTarget = NamedBuildTarget.FromBuildTargetGroup(BuildTargetGroup.Standalone);
        PlayerSettings.SetScriptingBackend(namedTarget, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(namedTarget, ManagedStrippingLevel.Medium);

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = config.BuildTarget,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            options = BuildOptions.None
        };

        Debug.Log($"[FoxgloveBuild] Starting {config.DisplayName} Player build...");
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

    private static BuildConfig ResolveTarget(string targetName)
    {
        var normalized = (targetName ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "win":
            case "win64":
            case "windows":
                return new BuildConfig(
                    "Windows IL2CPP",
                    BuildTarget.StandaloneWindows64,
                    "build/Unity/WindowsIL2CPP/FoxgloveDemo.exe");

            case "linux":
            case "linux64":
                return new BuildConfig(
                    "Linux IL2CPP",
                    BuildTarget.StandaloneLinux64,
                    "build/Unity/LinuxIL2CPP/FoxgloveDemo.x86_64");

            case "mac":
            case "macos":
            case "osx":
                return new BuildConfig(
                    "macOS IL2CPP",
                    BuildTarget.StandaloneOSX,
                    "build/Unity/MacOSIL2CPP/FoxgloveDemo.app");

            default:
                throw new System.ArgumentException(
                    $"Unknown -foxgloveBuildTarget '{targetName}'. Expected one of: win64, linux64, macos.");
        }
    }

    private static string GetCommandLineValue(string name)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
                return args[i + 1];
        }

        return null;
    }

    private readonly struct BuildConfig
    {
        public BuildConfig(string displayName, BuildTarget buildTarget, string outputPath)
        {
            DisplayName = displayName;
            BuildTarget = buildTarget;
            OutputPath = outputPath;
        }

        public string DisplayName { get; }
        public BuildTarget BuildTarget { get; }
        public string OutputPath { get; }
    }
}
