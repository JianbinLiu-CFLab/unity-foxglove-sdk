// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Phase181
// Purpose: Let Unity generate the restricted PluginImporter metadata for
//          candidate custom ROS2 DLLs without activating the add-on.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-only helper for the Phase181 candidate builder.  It stages generated
/// DLLs under <c>Assets/</c>, lets Unity create and configure their
/// <see cref="PluginImporter"/>, copies that Unity-generated metadata to the
/// out-of-tree candidate, and deletes the staging asset again.  It never edits
/// a package, project manifest, or lock file.
/// </summary>
public static class Phase181TypesupportPluginImporterBuilder
{
    private const string ManagedInputArgument = "-phase181TypesupportManagedInput";
    private const string ManagedOutputArgument = "-phase181TypesupportManagedMetaOutput";
    private const string PluginInputDirectoryArgument = "-phase181TypesupportPluginInputDirectory";
    private const string PluginOutputDirectoryArgument = "-phase181TypesupportPluginMetaOutputDirectory";
    private const string StageRootAssetPath = "Assets/__Phase181TypesupportPluginImporterStage";

    /// <summary>Unity <c>-executeMethod</c> entry point.</summary>
    public static void Run()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Phase181TypesupportPluginImporterBuilder requires Unity batch mode.");

        var usesManagedInput = HasArgument(ManagedInputArgument);
        var usesDirectoryInput = HasArgument(PluginInputDirectoryArgument);
        if (usesManagedInput == usesDirectoryInput)
            throw new InvalidOperationException("Phase181 typesupport importer requires exactly one managed DLL or DLL-directory input.");

        var repositoryRoot = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        var buildRoot = Path.Combine(repositoryRoot, "build", "phase181");
        var workItems = usesManagedInput
            ? BuildManagedWorkItem()
            : BuildDirectoryWorkItems();
        foreach (var workItem in workItems)
        {
            if (!IsUnder(workItem.Key, buildRoot) || !IsUnder(workItem.Value, buildRoot))
                throw new InvalidOperationException("Phase181 typesupport importer input/output must stay below build/phase181.");
            if (!File.Exists(workItem.Key))
                throw new FileNotFoundException("Phase181 candidate DLL is missing.");
        }

        var uniqueFolder = StageRootAssetPath + "/" + System.Diagnostics.Process.GetCurrentProcess().Id;

        try
        {
            foreach (var workItem in workItems)
                GeneratePluginImporter(uniqueFolder, workItem.Key, workItem.Value);
            Debug.Log("PHASE181_TYPESUPPORT_PLUGIN_IMPORTER_GENERATED count=" + workItems.Count);
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

    private static List<KeyValuePair<string, string>> BuildManagedWorkItem()
    {
        var input = RequirePathArgument(ManagedInputArgument, ".dll");
        var output = RequirePathArgument(ManagedOutputArgument, ".dll.meta");
        return new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(input, output) };
    }

    private static List<KeyValuePair<string, string>> BuildDirectoryWorkItems()
    {
        var inputDirectory = RequireDirectoryArgument(PluginInputDirectoryArgument);
        var outputDirectory = RequireDirectoryArgument(PluginOutputDirectoryArgument);
        var inputs = Directory.GetFiles(inputDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (inputs.Length == 0)
            throw new InvalidOperationException("Phase181 candidate DLL directory is empty.");
        if (inputs.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != inputs.Length)
            throw new InvalidOperationException("Phase181 candidate DLL directory contains colliding file names.");
        return inputs
            .Select(input => new KeyValuePair<string, string>(input, Path.Combine(outputDirectory, Path.GetFileName(input) + ".meta")))
            .ToList();
    }

    private static void GeneratePluginImporter(string uniqueFolder, string input, string output)
    {
        var stageAssetPath = uniqueFolder + "/" + Path.GetFileName(input);
        var stageDiskPath = Path.Combine(
            Directory.GetParent(Application.dataPath).FullName,
            stageAssetPath.Replace('/', Path.DirectorySeparatorChar));
        var stageMetaPath = stageDiskPath + ".meta";
        Directory.CreateDirectory(Path.GetDirectoryName(stageDiskPath));
        File.Copy(input, stageDiskPath, true);
        AssetDatabase.ImportAsset(stageAssetPath, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(stageAssetPath) as PluginImporter;
        if (importer == null)
            throw new InvalidOperationException("Unity did not create a PluginImporter for the Phase181 candidate DLL.");

        importer.SetCompatibleWithAnyPlatform(false);
        importer.SetCompatibleWithEditor(true);
        importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
        importer.SetEditorData("CPU", "x86_64");
        importer.SetEditorData("OS", "Windows");
        importer.SetPlatformData("Standalone", "CPU", "x86_64");
        importer.SaveAndReimport();

        if (!File.Exists(stageMetaPath))
            throw new InvalidOperationException("Unity did not serialize PluginImporter metadata for the Phase181 candidate DLL.");

        Directory.CreateDirectory(Path.GetDirectoryName(output));
        File.Copy(stageMetaPath, output, true);
        AssetDatabase.DeleteAsset(stageAssetPath);
    }

    private static bool HasArgument(string argumentName)
    {
        return Array.IndexOf(Environment.GetCommandLineArgs(), argumentName) >= 0;
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

    private static string RequireDirectoryArgument(string argumentName)
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, argumentName);
        if (index < 0 || index + 1 >= arguments.Length)
            throw new InvalidOperationException("Phase181 typesupport importer argument is missing: " + argumentName);
        return Path.GetFullPath(arguments[index + 1]);
    }

    private static bool IsUnder(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
#endif
