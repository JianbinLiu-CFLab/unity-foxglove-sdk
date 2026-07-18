// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Phase181
// Purpose: Let Unity generate the restricted PluginImporter metadata for a
//          candidate custom ROS2 managed assembly without activating the add-on.

#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-only helper for the Phase181 candidate builder.  It stages one
/// generated DLL under <c>Assets/</c>, lets Unity create and configure its
/// <see cref="PluginImporter"/>, copies that Unity-generated metadata to the
/// out-of-tree candidate, and deletes the staging asset again.  It never edits
/// a package, project manifest, or lock file.
/// </summary>
public static class Phase181TypesupportPluginImporterBuilder
{
    private const string InputArgument = "-phase181TypesupportManagedInput";
    private const string OutputArgument = "-phase181TypesupportManagedMetaOutput";
    private const string StageRootAssetPath = "Assets/__Phase181TypesupportPluginImporterStage";

    /// <summary>Unity <c>-executeMethod</c> entry point.</summary>
    public static void Run()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Phase181TypesupportPluginImporterBuilder requires Unity batch mode.");

        var input = RequirePathArgument(InputArgument, ".dll");
        var output = RequirePathArgument(OutputArgument, ".dll.meta");
        var repositoryRoot = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        var buildRoot = Path.Combine(repositoryRoot, "build", "phase181");
        if (!IsUnder(input, buildRoot) || !IsUnder(output, buildRoot))
            throw new InvalidOperationException("Phase181 typesupport importer input/output must stay below build/phase181.");
        if (!File.Exists(input))
            throw new FileNotFoundException("Phase181 managed candidate DLL is missing.");

        var uniqueFolder = StageRootAssetPath + "/" + System.Diagnostics.Process.GetCurrentProcess().Id;
        var stageAssetPath = uniqueFolder + "/" + Path.GetFileName(input);
        var stageDiskPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            stageAssetPath.Replace('/', Path.DirectorySeparatorChar));
        var stageMetaPath = stageDiskPath + ".meta";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stageDiskPath));
            File.Copy(input, stageDiskPath, true);
            AssetDatabase.ImportAsset(stageAssetPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(stageAssetPath) as PluginImporter;
            if (importer == null)
                throw new InvalidOperationException("Unity did not create a PluginImporter for the Phase181 managed DLL.");

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
            importer.SetEditorData("CPU", "x86_64");
            importer.SetEditorData("OS", "Windows");
            importer.SetPlatformData("Standalone", "CPU", "x86_64");
            importer.SaveAndReimport();

            if (!File.Exists(stageMetaPath))
                throw new InvalidOperationException("Unity did not serialize PluginImporter metadata for the Phase181 managed DLL.");

            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.Copy(stageMetaPath, output, true);
            Debug.Log("PHASE181_TYPESUPPORT_PLUGIN_IMPORTER_GENERATED");
        }
        finally
        {
            if (AssetDatabase.IsValidFolder(uniqueFolder))
                AssetDatabase.DeleteAsset(uniqueFolder);
            DeleteEmptyStageRoot();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }
    }

    private static void DeleteEmptyStageRoot()
    {
        var stageRootDiskPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            StageRootAssetPath.Replace('/', Path.DirectorySeparatorChar));
        if (!AssetDatabase.IsValidFolder(StageRootAssetPath)
            || !Directory.Exists(stageRootDiskPath)
            || Directory.GetFileSystemEntries(stageRootDiskPath).Length != 0)
        {
            return;
        }

        // The root is private to this batch-only helper. Removing it through
        // AssetDatabase also removes Unity's generated parent .meta, so a
        // candidate import cannot leave a tracked project artifact behind.
        AssetDatabase.DeleteAsset(StageRootAssetPath);
    }

    private static string RequirePathArgument(string argumentName, string expectedSuffix)
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, argumentName);
        if (index < 0 || index + 1 >= arguments.Length)
            throw new InvalidOperationException("Phase181 typesupport importer argument is missing: " + argumentName);
        var value = Path.GetFullPath(arguments[index + 1]);
        if (!value.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Phase181 typesupport importer argument has an unexpected file type.");
        return value;
    }

    private static bool IsUnder(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
#endif
