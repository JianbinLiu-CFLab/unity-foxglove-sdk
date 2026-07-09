// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-89/90/91/92/94 runtime validation optimization checks.

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140-89")]
    [Trait("Domain", "Harness")]
    public sealed class Ros2BridgeSchemaOptimizationTests
    {
        [Fact]
        public void Phase90CachesStableRelativePathsBeforeHashing()
        {
            var method = TestSources.Slice(TestSources.Runtime("Phase90Validation.cs"), "private static string ComputeSourceTreeSha256", "        private static string ToStableRelativePath");

            Assert.Contains("Select(path => new SourceFilePath(path, ToStableRelativePath(sourceRoot, path)))", method, StringComparison.Ordinal);
            Assert.Contains("OrderBy(file => file.RelativePath", method, StringComparison.Ordinal);
            Assert.Contains("Encoding.UTF8.GetBytes(file.RelativePath)", method, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase91ReusesPointCloudFrameAcrossBuilderChecks()
        {
            var source = TestSources.Runtime("Phase91Validation.cs");
            var verifyMethod = TestSources.Slice(source, "private static void VerifyMessageBuilders", "        private static void VerifyFrameTransformBuilder");

            Assert.Contains("var pointFrame = BuildPointCloudFrame();", verifyMethod, StringComparison.Ordinal);
            Assert.Contains("VerifyPointCloudBuilder(pointFrame);", verifyMethod, StringComparison.Ordinal);
            Assert.Contains("VerifyCompressedPointCloudBuilder(pointFrame);", verifyMethod, StringComparison.Ordinal);
            Assert.Contains("private static void VerifyPointCloudSharedPacking(PointCloudFrame frame)", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase100CachesMethodSignatureRegexesByMethodName()
        {
            var source = TestSources.Runtime("Phase100Validation.cs");
            var method = TestSources.Slice(source, "private static int FindMethodSignature", "        private static void Check");

            Assert.Contains("private static readonly Dictionary<string, Regex> MethodSignatureRegexes", source, StringComparison.Ordinal);
            Assert.Contains("lock (MethodSignatureRegexes)", method, StringComparison.Ordinal);
            Assert.Contains("MethodSignatureRegexes.TryGetValue(methodName, out", method, StringComparison.Ordinal);
            Assert.Contains("regex.Match(source)", method, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14089MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_89Validation.cs", "--phase140-89", "Phase140_89Validation.Validate");
    }

    [Trait("Phase", "140-90")]
    [Trait("Domain", "Harness")]
    public sealed class OptionalRos2ValidationOptimizationTests
    {
        [Fact]
        public void Phase115RoslynValidationsCacheMetadataReferences()
        {
            var phase115E = TestSources.Runtime("Phase115EValidation.cs");
            var phase115F = TestSources.Runtime("Phase115FValidation.cs");
            var phase115G = TestSources.Runtime("Phase115GValidation.cs");

            Assert.Contains("private static readonly Lazy<MetadataReference[]> CachedReferences", phase115E, StringComparison.Ordinal);
            Assert.Contains("private static MetadataReference[] References() => CachedReferences.Value;", phase115E, StringComparison.Ordinal);
            Assert.Contains("private static readonly Lazy<MetadataReference[]> CachedReferences", phase115F, StringComparison.Ordinal);
            Assert.Contains("private static MetadataReference[] References() => CachedReferences.Value;", phase115F, StringComparison.Ordinal);
            Assert.Contains("private static readonly Lazy<MetadataReference[]> CachedReferences", phase115G, StringComparison.Ordinal);
            Assert.Contains("CachedReferences.Value", phase115G, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase105ReusesNormalizedSummaryLines()
        {
            var source = TestSources.Runtime("Phase105Validation.cs");

            Assert.Contains("private static readonly Dictionary<string, string[]> SummaryLineCache", source, StringComparison.Ordinal);
            Assert.Contains("SummaryLineCache.Clear();", source, StringComparison.Ordinal);
            Assert.Contains("private static string[] ReadRepoLines", source, StringComparison.Ordinal);
            Assert.Contains("WindowBefore(lines, declaration", source, StringComparison.Ordinal);
        }

        [Fact]
        public void OptionalEditorAndRuntimeScansAvoidRepeatedEnumeration()
        {
            var phase107 = TestSources.Runtime("Phase107Validation.cs");
            var phase108 = TestSources.Runtime("Phase108Validation.cs");

            Assert.Equal(1, TestSources.Count(phase107, "Directory.GetFiles(editorRoot, \"*.*\", SearchOption.AllDirectories)"));
            Assert.Contains("var editorFiles = Directory.GetFiles(editorRoot", phase107, StringComparison.Ordinal);
            Assert.Contains("foreach (var path in editorFiles.Where(HasTextExtension))", phase107, StringComparison.Ordinal);
            Assert.Contains("private static IReadOnlyList<string> _runtimeTextFiles", phase108, StringComparison.Ordinal);
            Assert.Contains("_runtimeTextFiles = null;", phase108, StringComparison.Ordinal);
            Assert.Contains("private static IReadOnlyList<string> RuntimeTextFiles()", phase108, StringComparison.Ordinal);
            Assert.Contains("return _runtimeTextFiles;", phase108, StringComparison.Ordinal);
        }

        [Fact]
        public void HashSidecarsAreDecodedFromSingleByteRead()
        {
            var phase112 = TestSources.Runtime("Phase112Validation.cs");
            var phase115 = TestSources.Runtime("Phase115Validation.cs");

            Assert.DoesNotContain("File.ReadAllText(hashPath)", phase112, StringComparison.Ordinal);
            Assert.Contains("Encoding.ASCII.GetString(hashBytes)", phase112, StringComparison.Ordinal);
            Assert.DoesNotContain("File.ReadAllText(hashPath)", phase115, StringComparison.Ordinal);
            Assert.Contains("Encoding.ASCII.GetString(hashBytes)", phase115, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14090MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_90Validation.cs", "--phase140-90", "Phase140_90Validation.Validate");
    }

    [Trait("Phase", "140-91")]
    [Trait("Domain", "Harness")]
    public sealed class SchemaEvidencePackageOptimizationTests
    {
        [Fact]
        public void Phase110CachesRepoRootAndForbiddenTokenArrays()
        {
            var source = TestSources.Runtime("Phase110Validation.cs");

            Assert.Contains("private static string _repoRoot;", source, StringComparison.Ordinal);
            Assert.Contains("_repoRoot = root;", source, StringComparison.Ordinal);
            Assert.Contains("private static readonly string[] OptionalRuntimeForbiddenTokenList", source, StringComparison.Ordinal);
            Assert.Contains("private static readonly string[] CoreProductionForbiddenTokenList", source, StringComparison.Ordinal);
            Assert.Contains("private static readonly string[] R2fuReferenceTokens", source, StringComparison.Ordinal);
            Assert.DoesNotContain("private static IEnumerable<string> OptionalRuntimeForbiddenTokens()", source, StringComparison.Ordinal);
        }

        [Fact]
        public void FixtureManifestAndReflectionLookupsAreCached()
        {
            var phase113 = TestSources.Runtime("Phase113Validation.cs");
            var phase114 = TestSources.Runtime("Phase114Validation.cs");
            var phase116 = TestSources.Runtime("Phase116Validation.cs");

            Assert.Contains("private static readonly Lazy<FoxRunCanonicalManifest> FixtureManifestCache", phase113, StringComparison.Ordinal);
            Assert.Contains("private static FoxRunCanonicalManifest FixtureManifest() => FixtureManifestCache.Value;", phase113, StringComparison.Ordinal);
            Assert.Contains("private static readonly Lazy<FoxRunCanonicalManifest> FixtureManifestCache", phase114, StringComparison.Ordinal);
            Assert.Contains("private static FoxRunCanonicalManifest FixtureManifest() => FixtureManifestCache.Value;", phase114, StringComparison.Ordinal);
            Assert.Contains("private static readonly Dictionary<string, MethodInfo> MethodCache", phase116, StringComparison.Ordinal);
            Assert.Contains("private static readonly Dictionary<string, MemberInfo> MemberCache", phase116, StringComparison.Ordinal);
            Assert.Contains("MethodCache.TryGetValue", phase116, StringComparison.Ordinal);
            Assert.Contains("MemberCache.TryGetValue", phase116, StringComparison.Ordinal);
            Assert.DoesNotContain(".GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)\r\n                .Where", phase116, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase122ReusesOpcodeCountsPerRecordArray()
        {
            var source = TestSources.Runtime("Phase122Validation.cs");

            Assert.Contains("private static Dictionary<byte, int> OpcodeCounts", source, StringComparison.Ordinal);
            Assert.Contains("counts.TryGetValue(opcode, out var count)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("=> records.Count(r => r.Opcode == opcode);", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14091MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_91Validation.cs", "--phase140-91", "Phase140_91Validation.Validate");
    }

    [Trait("Phase", "140-92")]
    [Trait("Domain", "Harness")]
    public sealed class RvizStandardRos2ValidationOptimizationTests
    {
        [Fact]
        public void BoundaryScansReadEachFileOncePerTokenGroup()
        {
            foreach (var file in new[] { "Phase128Validation.cs", "Phase129Validation.cs", "Phase130Validation.cs", "Phase131Validation.cs", "Phase132Validation.cs", "Phase143Validation.cs" })
            {
                var source = TestSources.Runtime(file);
                Assert.DoesNotContain("File.ReadAllText(path).Contains", source, StringComparison.Ordinal);
                Assert.Contains("var text = File.ReadAllText(path)", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void McapRecordWritersUseTryGetBufferWithFallback()
        {
            foreach (var file in new[] { "Phase11Validation.cs", "Phase12Validation.cs" })
            {
                var source = TestSources.Runtime(file);
                Assert.Contains("content.TryGetBuffer(out var segment)", source, StringComparison.Ordinal);
                Assert.Contains("s.Write(segment.Array, segment.Offset, length)", source, StringComparison.Ordinal);
                Assert.Contains("var data = content.ToArray();", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Phase12AndPhase13AvoidRepeatedTextAndSnapshotWork()
        {
            var phase12 = TestSources.Runtime("Phase12Validation.cs");
            var phase13 = TestSources.Runtime("Phase13Validation.cs");

            Assert.Contains("raw.SequenceEqual(lz4Result)", phase12, StringComparison.Ordinal);
            Assert.Contains("raw.SequenceEqual(zstdResult)", phase12, StringComparison.Ordinal);
            Assert.DoesNotContain("Encoding.UTF8.GetString(raw)", phase12, StringComparison.Ordinal);
            Assert.Contains("private static readonly byte[] PlaybackControlRequestIdBytes", phase13, StringComparison.Ordinal);
            Assert.Equal(1, TestSources.Count(phase13, "Encoding.UTF8.GetBytes(\"phase13-paused-seek\")"));
            Assert.Contains("var requestIdBytes = PlaybackControlRequestIdBytes;", phase13, StringComparison.Ordinal);
            Assert.Contains("public int SentBinaryFrameCount(uint clientId)", phase13, StringComparison.Ordinal);
            Assert.Contains("transport.SentBinaryFrameCount(7)", phase13, StringComparison.Ordinal);
            Assert.Contains("Contains(\"\\\"serverInfo\\\"\", StringComparison.Ordinal)", phase13, StringComparison.Ordinal);
            Assert.Contains("Contains(\"\\\"advertise\\\"\", StringComparison.Ordinal)", phase13, StringComparison.Ordinal);
        }

        [Fact]
        public void SecretScanAvoidsSplitArray()
        {
            var source = TestSources.Runtime("Phase134_1Validation.cs");

            Assert.Contains("using var reader = new StringReader(text);", source, StringComparison.Ordinal);
            Assert.DoesNotContain("text.Split('\\n')", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14092MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_92Validation.cs", "--phase140-92", "Phase140_92Validation.Validate");
    }

    [Trait("Phase", "140-94")]
    [Trait("Domain", "Harness")]
    public sealed class VirtualSensorThroughputOptimizationTests
    {
        [Fact]
        public void CameraPublisherReadersUseStringBuilderAndSourceCaches()
        {
            foreach (var file in new[] { "Phase138MValidation.cs", "Phase138PValidation.cs", "Phase138QValidation.cs", "Phase138TValidation.cs" })
            {
                var method = TestSources.ExtractMethod(TestSources.Runtime(file), "private static string ReadCameraPublisherSources()");
                Assert.Contains("StringBuilder", method, StringComparison.Ordinal);
                Assert.Contains("AppendLine(File.ReadAllText(file))", method, StringComparison.Ordinal);
                Assert.DoesNotContain("output += File.ReadAllText(file)", method, StringComparison.Ordinal);
            }

            foreach (var file in new[] { "Phase138HValidation.cs", "Phase138IValidation.cs", "Phase138JValidation.cs", "Phase138MValidation.cs", "Phase138QValidation.cs" })
            {
                var source = TestSources.Runtime(file);
                Assert.Contains("private static readonly Dictionary<string, string>", source, StringComparison.Ordinal);
                Assert.Contains("TryGetValue", source, StringComparison.Ordinal);
                Assert.Contains("File.ReadAllText", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void VirtualSensorValidationHotPathsAvoidExtraReadsAndAllocations()
        {
            var phase138P = TestSources.ExtractMethod(TestSources.Runtime("Phase138PValidation.cs"), "private static SourceHygieneFile ReadSourceHygieneFile(");
            var phase138PSource = TestSources.Runtime("Phase138PValidation.cs");
            var phase138S = TestSources.ExtractMethod(TestSources.Runtime("Phase138SValidation.cs"), "private static string ReadDirectory(");
            var phase138L = TestSources.ExtractMethod(TestSources.Runtime("Phase138LValidation.cs"), "private static bool HasField(");
            var phase138C2 = TestSources.Runtime("Phase138C2Validation.cs");

            Assert.Contains("FileStream", phase138P, StringComparison.Ordinal);
            Assert.Contains("Span<byte>", phase138P, StringComparison.Ordinal);
            Assert.Contains("stream.Read(header)", phase138P, StringComparison.Ordinal);
            Assert.DoesNotContain("File.ReadAllBytes(path)", phase138PSource, StringComparison.Ordinal);
            Assert.Contains("ScanTrackedSourceHygiene", phase138PSource, StringComparison.Ordinal);
            Assert.DoesNotContain("private static bool TrackedSourceHasUtf8Bom()", phase138PSource, StringComparison.Ordinal);
            Assert.Contains("var content = File.ReadAllText(file);", phase138S, StringComparison.Ordinal);
            Assert.Contains("sb.AppendLine(content)", phase138S, StringComparison.Ordinal);
            Assert.Contains("bytes += content.Length", phase138S, StringComparison.Ordinal);
            Assert.Equal(1, TestSources.Count(phase138S, "File.ReadAllText(file)"));
            Assert.Contains("for (var i = 0; i < fields.Length; i++)", phase138L, StringComparison.Ordinal);
            Assert.DoesNotContain(".Any(", phase138L, StringComparison.Ordinal);
            Assert.Contains("private static readonly byte[] OkPayload", phase138C2, StringComparison.Ordinal);
            Assert.Equal(1, TestSources.Count(phase138C2, "Encoding.UTF8.GetBytes(\"{\\\"ok\\\":true}\")"));
            Assert.Contains("session.Publish(77, OkPayload, 1)", phase138C2, StringComparison.Ordinal);
            Assert.Contains("session.Publish(78, OkPayload, 2)", phase138C2, StringComparison.Ordinal);
            Assert.Contains("session.Publish(79, OkPayload, 3)", phase138C2, StringComparison.Ordinal);
        }

        [Fact]
        public void RayUnitChecksUseSquaredMagnitude()
        {
            foreach (var file in new[] { "Phase138Validation.cs", "Phase138BValidation.cs" })
            {
                var source = TestSources.Runtime(file);
                Assert.Contains("LengthSquared()", source, StringComparison.Ordinal);
                Assert.DoesNotContain("var mag = dir.Length();", source, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Phase14094MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_94Validation.cs", "--phase140-94", "Phase140_94Validation.Validate");
    }

    internal static class TestSources
    {
        private static readonly string CachedRepoRoot = FindRepoRoot();

        public static string Runtime(string fileName)
            => Text("Packages/dev.unity2foxglove.sdk/Tests/Runtime/" + fileName);

        public static string Text(string relativePath)
        {
            var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "Source file not found: " + relativePath + " (" + path + ")");
            return File.ReadAllText(path);
        }

        public static string SourceGeneratorSources()
        {
            var dir = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "SourceGenerators",
                "src");
            Assert.True(Directory.Exists(dir), "Source generator src directory not found: " + dir);

            var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        }

        public static string ManagerEditorSources()
        {
            var dir = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Editor",
                "Manager");
            Assert.True(Directory.Exists(dir), "Manager editor directory not found: " + dir);

            var files = Directory.GetFiles(dir, "FoxgloveManagerEditor*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        }

        public static string ManagerPublishingSources()
        {
            var dir = Path.Combine(
                RepoRoot,
                "Packages",
                "dev.unity2foxglove.sdk",
                "Runtime",
                "Components",
                "Manager");
            Assert.True(Directory.Exists(dir), "Manager directory not found: " + dir);

            var files = Directory.GetFiles(dir, "FoxgloveManager.Publishing*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
        }

        public static void AssertConsolePhaseRemoved(string validationFile, string flag, string entryPoint)
        {
            Assert.DoesNotContain(validationFile, Runtime("FoxgloveSdk.Tests.csproj"), StringComparison.Ordinal);
            var registry = Runtime("PhaseValidationRegistry.cs");
            Assert.DoesNotContain("\"" + flag + "\"", registry, StringComparison.Ordinal);
            Assert.DoesNotContain(entryPoint, registry, StringComparison.Ordinal);
        }

        public static string Slice(string source, string startText, string endText)
        {
            var normalized = NormalizeLineEndings(source);
            var normalizedStart = NormalizeLineEndings(startText);
            var normalizedEnd = NormalizeLineEndings(endText);
            var start = normalized.IndexOf(normalizedStart, StringComparison.Ordinal);
            Assert.True(start >= 0, "Could not locate source slice start: " + startText);
            var end = normalized.IndexOf(normalizedEnd, start + normalizedStart.Length, StringComparison.Ordinal);
            if (end < 0)
                end = normalized.Length;
            return normalized.Substring(start, end - start);
        }

        public static string ExtractMethod(string source, string signature)
        {
            var index = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(index >= 0, "Could not locate method signature: " + signature);
            var brace = source.IndexOf('{', index);
            Assert.True(brace >= 0, "Could not locate method body: " + signature);

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(index, i - index + 1);
                }
            }

            return source.Substring(index);
        }

        public static int Count(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string NormalizeLineEndings(string text)
            => (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

        private static string RepoRoot
            => CachedRepoRoot;

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Unity2Foxglove.sln"))
                    || Directory.Exists(Path.Combine(dir.FullName, ".git")))
                    return dir.FullName;

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }
    }
}
