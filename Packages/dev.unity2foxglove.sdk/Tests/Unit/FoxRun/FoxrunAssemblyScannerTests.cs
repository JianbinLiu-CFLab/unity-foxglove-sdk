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
