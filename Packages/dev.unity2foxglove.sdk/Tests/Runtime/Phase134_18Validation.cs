// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-18 validation for stale FoxRun physical fallback cleanup.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase134_18Validation.
    /// </summary>
    public static class Phase134_18Validation
    {
        private static int _passed;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            _passed = 0;

            VerifyStaleGeneratedFileCleanup();
            VerifyAllOwnedFilesCanBeRemovedWhenNoTypesRemain();
            VerifyReadOnlyGeneratedFileReportsActionableFailure();
            VerifyMetaDeleteFailureReportsWarning();
            VerifyCodeGeneratorCallsReconciler();
            VerifyAnalyzerReleaseSeverity();
            VerifyEmitSourceFileRejectsEmptyMembers();
            VerifySharedDiagnosticsMapStrictly();
            VerifySchemaInfoWriterIsAtomicAndEscapeAware();
            VerifyBuildPreprocessRefreshesGeneratedAssets();

            Console.WriteLine($"Phase134_18Validation: PASS ({_passed} checks)");
        }

        private static void VerifyStaleGeneratedFileCleanup()
        {
            var directory = CreateTempDirectory();
            try
            {
                var current = Path.Combine(directory, "Current_FoxRun.g.cs");
                var stale = Path.Combine(directory, "Stale_FoxRun.g.cs");
                var user = Path.Combine(directory, "User_FoxRun.g.cs");
                File.WriteAllText(current, OwnedSource("Current"));
                File.WriteAllText(stale, OwnedSource("Stale"));
                File.WriteAllText(stale + ".meta", "fileFormatVersion: 2\n");
                File.WriteAllText(user, "// user-authored source must survive\n");

                var deleted = FoxRunGeneratedSourceReconciler.ReconcileGeneratedSourceFiles(
                    directory,
                    new[] { "Current_FoxRun.g.cs" });

                Check(deleted.SequenceEqual(new[] { "Stale_FoxRun.g.cs" }),
                    "134-18-A1: stale owned FoxRun fallback file is reported as deleted");
                Check(File.Exists(current), "134-18-A2: current generated FoxRun fallback file is preserved");
                Check(!File.Exists(stale) && !File.Exists(stale + ".meta"),
                    "134-18-A3: stale generated FoxRun fallback file and meta sidecar are removed");
                Check(File.Exists(user), "134-18-A4: matching user file without ownership sentinel is preserved");
            }
            finally
            {
                TryDeleteDirectory(directory);
            }
        }

        private static void VerifyAllOwnedFilesCanBeRemovedWhenNoTypesRemain()
        {
            var directory = CreateTempDirectory();
            try
            {
                var stale = Path.Combine(directory, "RemovedType_FoxRun.g.cs");
                File.WriteAllText(stale, OwnedSource("RemovedType"));

                var deleted = FoxRunGeneratedSourceReconciler.ReconcileGeneratedSourceFiles(
                    directory,
                    Array.Empty<string>());

                Check(deleted.SequenceEqual(new[] { "RemovedType_FoxRun.g.cs" }) && !File.Exists(stale),
                    "134-18-B1: all owned fallback files are removed when no FoxRun types remain");
            }
            finally
            {
                TryDeleteDirectory(directory);
            }
        }

        private static void VerifyReadOnlyGeneratedFileReportsActionableFailure()
        {
            var directory = CreateTempDirectory();
            try
            {
                var stale = Path.Combine(directory, "ReadOnly_FoxRun.g.cs");
                File.WriteAllText(stale, OwnedSource("ReadOnly"));
                File.SetAttributes(stale, File.GetAttributes(stale) | FileAttributes.ReadOnly);

                try
                {
                    FoxRunGeneratedSourceReconciler.ReconcileGeneratedSourceFiles(
                        directory,
                        Array.Empty<string>());
                }
                catch (InvalidOperationException ex)
                {
                    Check(ex.Message.Contains("ReadOnly_FoxRun.g.cs", StringComparison.Ordinal)
                          && ex.Message.Contains("Close", StringComparison.OrdinalIgnoreCase),
                        "134-18-B2: read-only stale fallback file reports actionable cleanup failure");
                    return;
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new InvalidOperationException(
                        "134-18-B2: read-only stale fallback file should not leak raw UnauthorizedAccessException: " + ex.Message,
                        ex);
                }
                catch (IOException ex)
                {
                    throw new InvalidOperationException(
                        "134-18-B2: read-only stale fallback file should not leak raw IOException: " + ex.Message,
                        ex);
                }

                throw new InvalidOperationException("134-18-B2: read-only stale fallback file should fail reconciliation");
            }
            finally
            {
                ClearReadOnlyFiles(directory);
                TryDeleteDirectory(directory);
            }
        }

        private static void VerifyMetaDeleteFailureReportsWarning()
        {
            var directory = CreateTempDirectory();
            var originalWarningSink = FoxRunGeneratedSourceReconciler.MetaDeleteWarningSink;
            try
            {
                var stale = Path.Combine(directory, "MetaLocked_FoxRun.g.cs");
                var meta = stale + ".meta";
                string warning = null;
                File.WriteAllText(stale, OwnedSource("MetaLocked"));
                File.WriteAllText(meta, "fileFormatVersion: 2\n");
                File.SetAttributes(meta, File.GetAttributes(meta) | FileAttributes.ReadOnly);
                FoxRunGeneratedSourceReconciler.MetaDeleteWarningSink = message => warning = message;

                var deleted = FoxRunGeneratedSourceReconciler.ReconcileGeneratedSourceFiles(
                    directory,
                    Array.Empty<string>());

                Check(deleted.SequenceEqual(new[] { "MetaLocked_FoxRun.g.cs" })
                      && !File.Exists(stale)
                      && File.Exists(meta),
                    "173-054-A1: stale generated source still deletes when its meta sidecar cannot be removed");
                Check(warning != null
                      && warning.Contains("MetaLocked_FoxRun.g.cs.meta", StringComparison.Ordinal)
                      && warning.Contains("Failed to remove stale FoxRun generated source meta file", StringComparison.Ordinal),
                    "173-054-A2: stale meta cleanup failure reports a warning");
            }
            finally
            {
                FoxRunGeneratedSourceReconciler.MetaDeleteWarningSink = originalWarningSink;
                ClearReadOnlyFiles(directory);
                TryDeleteDirectory(directory);
            }
        }

        private static void VerifyCodeGeneratorCallsReconciler()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            Check(source.Contains("FoxRunGeneratedSourceReconciler.ReconcileGeneratedSourceFiles", StringComparison.Ordinal)
                  && source.Contains("GeneratedSourceSentinel", StringComparison.Ordinal),
                "134-18-C1: physical fallback generator reconciles stale files using owned sentinel");
        }

        private static void VerifyAnalyzerReleaseSeverity()
        {
            var shipped = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/AnalyzerReleases.Shipped.md");
            Check(shipped.Contains("FOXRUN008 | FoxRun | Error |", StringComparison.Ordinal),
                "134-18-D1: shipped analyzer release notes record FOXRUN008 as Error");
        }

        private static void VerifyEmitSourceFileRejectsEmptyMembers()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunCodeGenerator.cs");
            Check(source.Contains("throw new ArgumentNullException(nameof(members))", StringComparison.Ordinal)
                  && source.Contains("throw new ArgumentException(\"At least one FoxRun member is required", StringComparison.Ordinal)
                  && source.Contains("model.Types.Count != 1", StringComparison.Ordinal),
                "134-18-E1: EmitSourceFile reports clear argument errors for null, empty, and degenerate inputs");
        }

        private static void VerifySharedDiagnosticsMapStrictly()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.cs");
            Check(source.Contains("case \"FOXRUN011\": return MissingClassName;", StringComparison.Ordinal)
                  && source.Contains("case \"FOXRUN012\": return MissingMemberName;", StringComparison.Ordinal)
                  && source.Contains("case \"FOXRUN013\": return InvalidPublishMode;", StringComparison.Ordinal),
                "134-18-F1: Roslyn shared diagnostic map covers every shared validator error id");
            Check(source.Contains("return UnknownFoxRunDiagnostic(id);", StringComparison.Ordinal)
                  && source.Contains("return UnknownFoxServiceDiagnostic(id);", StringComparison.Ordinal)
                  && !source.Contains("throw new ArgumentOutOfRangeException(nameof(id), id", StringComparison.Ordinal),
                "134-18-F2: Roslyn shared diagnostic map uses fallback descriptors for unmapped ids");
        }

        private static void VerifySchemaInfoWriterIsAtomicAndEscapeAware()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunSchemaInfoWriter.cs");
            Check(source.Contains("File.Replace(tempPath, path, null)", StringComparison.Ordinal)
                  && source.Contains("CopyTempOverDestination(tempPath, path", StringComparison.Ordinal)
                  && source.Contains("Stable Unity GUID", StringComparison.Ordinal),
                "134-18-G1: schema info writer uses atomic temp replace/copy fallback and documents stable meta GUID");
            var verifyIndex = source.IndexOf("var verification = VerifyGeneratedInfo(manifest, source);", StringComparison.Ordinal);
            var writeIndex = source.IndexOf("var sourceChanged = WriteIfChanged(sourcePath, source);", StringComparison.Ordinal);
            Check(verifyIndex >= 0 && writeIndex >= 0 && verifyIndex < writeIndex,
                "173-022A: schema info writer verifies generated source before touching files");
            Check(source.Contains("FoxRun policy float values must be finite.", StringComparison.Ordinal)
                  && !source.Contains("return \"0f\";", StringComparison.Ordinal),
                "173-022B: schema info writer rejects non-finite policy floats");

            var escapedManifest = new FoxRunCanonicalManifest(
                1,
                "package",
                new FoxRunManifestGenerator("generator", 1),
                new FoxRunManifestSections(new FoxRunManifestFoxRunSection("fox\\\"hash", Array.Empty<FoxRunManifestType>())),
                "global\\\"hash");
            var generated = FoxRunSchemaInfoWriter.GenerateSource(escapedManifest);
            var verification = FoxRunSchemaInfoWriter.VerifyGeneratedInfo(escapedManifest, generated);
            Check(verification.IsValid,
                "134-18-G2: schema info verification parses escaped string constants correctly");
            try
            {
                FoxRunSchemaInfoWriter.GenerateSource(BuildManifestWithRate("BadFloat", "/bad_float", float.NaN));
                throw new InvalidOperationException("173-022C: non-finite FoxRun policy float should throw");
            }
            catch (ArgumentOutOfRangeException)
            {
                Check(true, "173-022C: non-finite FoxRun policy float fails before source emission");
            }

            var directory = CreateTempDirectory();
            try
            {
                var first = BuildManifest("ProbeA", "/probe_a");
                var second = BuildManifest("ProbeB", "/probe_b");
                FoxRunSchemaInfoWriter.WriteGeneratedInfoFiles(directory, first);
                var sourcePath = Path.Combine(directory, FoxRunSchemaInfoWriter.SchemaInfoFileName);
                var metaPath = Path.Combine(directory, FoxRunSchemaInfoWriter.SchemaInfoMetaFileName);
                File.SetAttributes(sourcePath, File.GetAttributes(sourcePath) | FileAttributes.ReadOnly);
                FoxRunSchemaInfoWriter.WriteGeneratedInfoFiles(directory, second);
                var rewritten = File.ReadAllText(sourcePath);
                Check(rewritten.Contains("ProbeB", StringComparison.Ordinal)
                      && !Directory.EnumerateFiles(directory, "*.tmp-*").Any()
                      && File.Exists(metaPath),
                    "134-18-G3: schema info writer replaces read-only generated files without temp leftovers");
            }
            finally
            {
                ClearReadOnlyFiles(directory);
                TryDeleteDirectory(directory);
            }
        }

        private static void VerifyBuildPreprocessRefreshesGeneratedAssets()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxrunBuildPreprocess.cs");
            Check(source.Contains("AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport)", StringComparison.Ordinal)
                  && source.Contains("Failed at: asset-refresh", StringComparison.Ordinal),
                "134-18-H1: build preprocess synchronously imports generated FoxRun assets before continuing");
        }

        private static string OwnedSource(string className)
        {
            return "// <auto-generated/>\n"
                   + "// " + FoxRunGeneratedSourceReconciler.GeneratedSourceSentinel + "\n"
                   + "public partial class " + className + " {}\n";
        }

        private static FoxRunCanonicalManifest BuildManifest(string className, string topic)
            => BuildManifestWithRate(className, topic, 1f);

        private static FoxRunCanonicalManifest BuildManifestWithRate(string className, string topic, float rateHz)
        {
            var field = new FoxRunManifestField(
                "_value",
                "_value",
                "field",
                "System.Single",
                true,
                false);
            var policy = new FoxRunManifestPolicy("", rateHz, 0f, 0f);
            var contract = new FoxRunManifestContract(
                "Validation." + className,
                topic,
                "Validation." + className,
                "json",
                "contract",
                "binding",
                "policy",
                new[] { field },
                policy);
            var type = new FoxRunManifestType("Validation." + className, new[] { contract });
            return new FoxRunCanonicalManifest(
                1,
                "package",
                new FoxRunManifestGenerator("generator", 1),
                new FoxRunManifestSections(new FoxRunManifestFoxRunSection("foxhash", new[] { type })),
                "globalhash");
        }

        private static FoxRunCanonicalManifest BuildManifestThroughBuilder(string className, string topic, float rateHz)
        {
            return FoxRunManifestBuilder.Build(new[]
            {
                new FoxRunManifestMember(
                    "Validation",
                    className,
                    "_value",
                    "field",
                    "System.Single",
                    true,
                    false,
                    "",
                    topic,
                    rateHz,
                    "",
                    0,
                    0f,
                    0f)
            });
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "foxrun_reconcile_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch
            {
            }
        }

        private static void ClearReadOnlyFiles(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    return;

                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch
            {
            }
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            _passed++;
            Console.WriteLine(name);
        }
    }
}
