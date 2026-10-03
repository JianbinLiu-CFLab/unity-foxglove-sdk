// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Exercises the production reflection-discovery boundary without a
//          Unity Editor process.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed class FoxrunAssemblyScannerTests
    {
        [Fact]
        public void BestEffortScanSkipsUnsupportedHostAndKeepsValidHost()
        {
            var scan = InvokeScan(bestEffort: true);
            var topics = ReadManifestTopics(scan);

            Assert.Contains("/c5/valid", topics);
            Assert.DoesNotContain("/c5/nested", topics);
        }

        [Fact]
        public void StrictScanStillFailsClosedForUnsupportedHost()
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => InvokeScan(bestEffort: false));

            Assert.Contains("FOXRUN623", error.Message, StringComparison.Ordinal);
            Assert.Contains("InvalidNested", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ReflectionGenerationModelIsCachedUntilDiscoveryIsInvalidated()
        {
            _ = ProbeAssembly.Value;
            FoxrunCodeGenerator.InvalidateReflectionDiscoveryCache();

            var first = FoxrunCodeGenerator.CollectReflectionGenerationModelForTransportProviders();
            var second = FoxrunCodeGenerator.CollectReflectionGenerationModelForTransportProviders();

            Assert.Same(first, second);

            FoxrunCodeGenerator.InvalidateReflectionDiscoveryCache();
            var afterInvalidation = FoxrunCodeGenerator.CollectReflectionGenerationModelForTransportProviders();
            Assert.NotSame(first, afterInvalidation);
        }

        [Fact]
        public void IncompleteCanonicalArtifactGateFailsBeforeExistingArtifactsCanChange()
        {
            var root = Path.Combine(
                RepositoryBuildTestRoot(),
                "u2f-phase181-canonical-gate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var sentinelPath = Path.Combine(root, "foxrun.manifest.json");
            const string sentinel = "canonical-sentinel";
            File.WriteAllText(sentinelPath, sentinel);
            try
            {
                var error = Assert.Throws<InvalidOperationException>(
                    () => FoxrunCodeGenerator.EnsureCanonicalArtifactGenerationAllowed(false));

                Assert.Contains("FOXRUN901", error.Message, StringComparison.Ordinal);
                Assert.Equal(sentinel, File.ReadAllText(sentinelPath));
                Assert.Empty(Directory.GetFiles(root, "*.tmp*", SearchOption.TopDirectoryOnly));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void IncompleteDiscoveryThroughPublicGenerationLeavesCanonicalArtifactsUnchanged()
        {
            var outputDirectory = Unity2FoxgloveSchemaEvidencePaths.ResolveFoxRunOutputDirectory();
            var before = SnapshotArtifactTree(outputDirectory);
            var scannerType = typeof(FoxrunCodeGenerator);
            var resultType = scannerType.GetNestedType("FoxRunScanResult", BindingFlags.NonPublic);
            Assert.NotNull(resultType);
            var constructor = resultType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Single(candidate => candidate.GetParameters().Length == 4);
            var incomplete = constructor.Invoke(new object[]
            {
                new Dictionary<(string Ns, string ClassName), List<FoxrunCodeGenerator.MemberData>>(),
                new List<FoxRunManifestMember>(),
                new List<FoxRunReflectionGenerationMember>(),
                false
            });
            var fingerprintMethod = scannerType.GetMethod(
                "GetLoadedAssemblyFingerprint",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(fingerprintMethod);
            var fingerprint = Assert.IsType<string>(fingerprintMethod.Invoke(null, null));
            var cacheField = scannerType.GetField(
                "_cachedFoxRunScan",
                BindingFlags.Static | BindingFlags.NonPublic);
            var fingerprintField = scannerType.GetField(
                "_scanAssemblyFingerprint",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(cacheField);
            Assert.NotNull(fingerprintField);
            cacheField.SetValue(null, incomplete);
            fingerprintField.SetValue(null, fingerprint);

            try
            {
                var error = Assert.Throws<InvalidOperationException>(
                    () => FoxrunCodeGenerator.GenerateManifestAndSchemaInfoFilesOnlyWithResult());

                Assert.Contains("FOXRUN901", error.Message, StringComparison.Ordinal);
                var after = SnapshotArtifactTree(outputDirectory);
                Assert.Equal(before.Keys.OrderBy(path => path), after.Keys.OrderBy(path => path));
                foreach (var pair in before)
                    Assert.True(
                        after[pair.Key].SequenceEqual(pair.Value),
                        "Canonical artifact changed: " + pair.Key);
            }
            finally
            {
                FoxrunCodeGenerator.InvalidateReflectionDiscoveryCache();
            }
        }
        [Fact]
        public void IncompleteCanonicalArtifactGateNeverInvokesWriter()
        {
            var invoked = false;
            var error = Assert.Throws<InvalidOperationException>(
                () => FoxrunCodeGenerator.GenerateCanonicalArtifactsIfComplete(
                    false,
                    () => invoked = true));

            Assert.Contains("FOXRUN901", error.Message, StringComparison.Ordinal);
            Assert.False(invoked);
        }

        [Fact]
        public void BestEffortCombinedScanSkipsUnsupportedServiceHostAndKeepsValidService()
        {
            var scan = InvokeCombinedScan(bestEffort: true);
            var servicesField = scan.GetType().GetField(
                "Services",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(servicesField);

            var services = servicesField.GetValue(scan);
            var byClassField = services.GetType().GetField(
                "ByClass",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(byClassField);

            var byClass = Assert.IsAssignableFrom<IDictionary>(byClassField.GetValue(services));
            var keys = byClass.Keys.Cast<object>().Select(key => key.ToString()).ToArray();
            Assert.Contains(keys, key => key.Contains("ValidHost", StringComparison.Ordinal));
            Assert.DoesNotContain(keys, key => key.Contains("InvalidNested", StringComparison.Ordinal));
        }

        [Fact]
        public void StrictCombinedScanStillFailsClosedForUnsupportedServiceHost()
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => InvokeCombinedScan(bestEffort: false));

            Assert.Contains("FOXRUN623", error.Message, StringComparison.Ordinal);
            Assert.Contains("InvalidNested", error.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("Telemetry.on", "record")]
        [InlineData("Contoso.value", "async")]
        [InlineData("N.dynamic", "file")]
        public void ReflectionHostIdentityAcceptsEscapableKeywordNames(
            string ns,
            string className)
        {
            var method = typeof(FoxrunCodeGenerator).GetMethod(
                "ValidatePhysicalHostIdentity",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(string), typeof(string), typeof(string) },
                modifiers: null);
            Assert.NotNull(method);

            var invocation = Record.Exception(
                () => method.Invoke(
                    null,
                    new object[] { ns, className, "FOXRUN623" }));

            Assert.Null(invocation);
        }

        private static object InvokeScan(bool bestEffort)
        {
            _ = ProbeAssembly.Value;
            var method = typeof(FoxrunCodeGenerator).GetMethod(
                "ScanFoxRunMembers",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(bool) },
                modifiers: null);
            Assert.NotNull(method);

            try
            {
                return method.Invoke(null, new object[] { bestEffort });
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private static object InvokeCombinedScan(bool bestEffort)
        {
            _ = ProbeAssembly.Value;
            var method = typeof(FoxrunCodeGenerator).GetMethod(
                "ScanFoxRunMembersAndServices",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                new[] { typeof(bool) },
                modifiers: null);
            Assert.NotNull(method);

            try
            {
                return method.Invoke(null, new object[] { bestEffort });
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }

        private static IReadOnlyList<string> ReadManifestTopics(object scan)
        {
            Assert.NotNull(scan);
            var field = scan.GetType().GetField(
                "ManifestMembers",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(field);

            var values = Assert.IsAssignableFrom<IEnumerable>(field.GetValue(scan));
            var topics = new List<string>();
            foreach (var value in values)
            {
                Assert.NotNull(value);
                var property = value.GetType().GetProperty("Topic");
                Assert.NotNull(property);
                topics.Add(Assert.IsType<string>(property.GetValue(value)));
            }

            return topics;
        }

        [Fact]
        public void GeneratedLinkXmlIsWellFormedAndOwnsOneMarker()
        {
            var linkXml = FoxrunCodeGenerator.EmitLinkXml(
                new List<(string AsmName, string Ns, string ClassName)>
                {
                    ("Assembly-CSharp", "Demo", "FoxRunPublisher")
                });

            var document = XDocument.Parse(linkXml, LoadOptions.PreserveWhitespace);

            Assert.Equal("linker", document.Root?.Name.LocalName);
            Assert.Single(document.Descendants("type"));
            Assert.Equal(1, CountOccurrences(
                linkXml,
                "<!-- Generated by FoxrunBuildPreprocess - do not edit by hand. -->"));
            Assert.StartsWith("<?xml", linkXml.TrimStart(), StringComparison.Ordinal);
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var offset = 0;
            while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }

            return count;
        }
        private static Dictionary<string, byte[]> SnapshotArtifactTree(string root)
        {
            if (!Directory.Exists(root))
                return new Dictionary<string, byte[]>(StringComparer.Ordinal);

            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(root, path),
                    File.ReadAllBytes,
                    StringComparer.Ordinal);
        }
        private static string RepositoryBuildTestRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Packages")))
                {
                    return Path.Combine(directory.FullName, "build", "Tests", "Phase181");
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }

        private static readonly Lazy<Assembly> ProbeAssembly =
            new Lazy<Assembly>(CompileProbeAssembly);

        private static Assembly CompileProbeAssembly()
        {
            const string source = @"
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace C5Probe
{
    public class ValidHost : MonoBehaviour
    {
        [FoxRun(""/c5/valid"")]
        public int Value;

        [FoxService(""/c5/valid-service"", Type = ""C5Probe.Service"", RequestSchemaName = ""C5Probe.Request"", ResponseSchemaName = ""C5Probe.Response"")]
        public void InvokeService() { }
    }

    public class Outer
    {
        public class InvalidNested : MonoBehaviour
        {
            [FoxRun(""/c5/nested"")]
            public int Value;

            [FoxService(""/c5/nested-service"", Type = ""C5Probe.NestedService"", RequestSchemaName = ""C5Probe.NestedRequest"", ResponseSchemaName = ""C5Probe.NestedResponse"")]
            public void InvokeService() { }
        }
    }
}";

            var trustedAssemblies =
                AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                ?? string.Empty;
            var references = trustedAssemblies
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => MetadataReference.CreateFromFile(path))
                .Concat(new[]
                {
                    MetadataReference.CreateFromFile(
                        typeof(FoxRunAttribute).Assembly.Location),
                    MetadataReference.CreateFromFile(
                        typeof(UnityEngine.MonoBehaviour).Assembly.Location)
                })
                .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First());
            var compilation = CSharpCompilation.Create(
                "C5ReflectionDiscoveryProbe_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(
                emit.Success,
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            return Assembly.Load(image.ToArray());
        }
    }
}
