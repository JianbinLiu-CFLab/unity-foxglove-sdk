// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 173-087 Unity review regression checks.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.FoxgloveSDK.Transport;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{



public sealed partial class Phase173087ReviewTests
    {
        [Fact]
        public void FoxRunNodeReleaseRetainsOwnershipWhenDriverRejectsRemoval()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/FoxRun/Ros2ForUnityFoxRunInboundBackend.cs");
            Assert.Contains("bool ReleaseNode();", source, StringComparison.Ordinal);
            Assert.Contains("_nodeReleaseInProgress", source, StringComparison.Ordinal);
            Assert.Contains("released = _driver.ReleaseNode()", source, StringComparison.Ordinal);
            Assert.Contains("if (released)", source, StringComparison.Ordinal);
            Assert.Contains("CompareExchange(ref _node, null, node)", source, StringComparison.Ordinal);
            Assert.Contains("TryRemoveNode", source, StringComparison.Ordinal);
        }

        [Fact]
        public void RuntimeEnvironmentLeaseRestoresPreviouslyUnsetVariable()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_UNSET_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, null);
                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "lease-value", false);
                    Assert.Equal("lease-value", Environment.GetEnvironmentVariable(name));
                    Assert.True((bool)InvokeLease(lease, "Restore", false));
                    Assert.Null(Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, false);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [Fact]
        public void RuntimeEnvironmentLeaseRestoresAnExistingValue()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_EXISTING_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, "original-value");
                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "lease-value", false);
                    Assert.Equal("lease-value", Environment.GetEnvironmentVariable(name));
                    Assert.True((bool)InvokeLease(lease, "Restore", false));
                    Assert.Equal("original-value", Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, false);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [Fact]
        public void RuntimeEnvironmentLeasePreservesCallerMutation()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_CALLER_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, "original-value");
                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "lease-value", false);
                    Environment.SetEnvironmentVariable(name, "caller-value");
                    Assert.True((bool)InvokeLease(lease, "Restore", false));
                    Assert.Equal("caller-value", Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, false);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [Fact]
        public void RuntimeEnvironmentLeaseRemovesOnlyItsPathEntry()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_PATH_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro);
                var separator = Path.PathSeparator;
                var original = string.Join(separator, new[] { "old-a", "old-b" });
                var runtimeEntry = "runtime-entry";
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, original);
                    InvokeLease(lease, "Begin");
                    InvokeLease(
                        lease,
                        "SetPath",
                        name,
                        original + separator + runtimeEntry,
                        runtimeEntry,
                        separator,
                        false);
                    Environment.SetEnvironmentVariable(
                        name,
                        original + separator + runtimeEntry + separator + "caller-entry");

                    Assert.True((bool)InvokeLease(lease, "Restore", false));
                    Assert.Equal(
                        original + separator + "caller-entry",
                        Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, false);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [Fact]
        public void RuntimeEnvironmentLeaseRejectsASecondBeginUntilCleanupCompletes()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_NESTED_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, null);
                    InvokeLease(lease, "Begin");
                    var secondBegin = Assert.Throws<TargetInvocationException>(
                        () => InvokeLease(lease, "Begin"));
                    Assert.IsType<InvalidOperationException>(secondBegin.InnerException);
                    Assert.True((bool)InvokeLease(lease, "Restore", false));

                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "second-value", false);
                    Assert.True((bool)InvokeLease(lease, "Restore", false));
                    Assert.Null(Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, false);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [Fact]
        public void RuntimeEnvironmentLeaseBlocksRestartWhileCleanupIsPending()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_PENDING_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, null);
                    InvokeLease(lease, "Begin");
                    var failedSet = Assert.Throws<TargetInvocationException>(
                        () => InvokeLease(lease, "Set", name, "value", true));
                    Assert.IsType<PlatformNotSupportedException>(failedSet.InnerException);

                    var restart = Assert.Throws<TargetInvocationException>(
                        () => InvokeLease(lease, "Begin"));
                    Assert.IsType<InvalidOperationException>(restart.InnerException);
                    Assert.False((bool)InvokeLease(lease, "Restore", true));
                    Assert.True((bool)InvokeLease(lease, "Restore", false));
                    Assert.Null(Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, false);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [WindowsFact]
        public void RuntimeEnvironmentLeaseExecutesWindowsCrtBranch()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_WINDOWS_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(distro, windowsSymbols: true);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, null);
                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "lease-value", true);
                    Assert.Equal("lease-value", Environment.GetEnvironmentVariable(name));
                    Assert.True((bool)InvokeLease(lease, "Restore", true));
                    Assert.Null(Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, true);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        [Fact]
        public void RuntimeEnvironmentLeaseRollsBackFailedNativeApplyAfterCallerMutation()
        {
            foreach (var distro in RuntimeDistros)
            {
                var name = "U2F_LEASE_ROLLBACK_" + distro.ToUpperInvariant();
                var lease = RuntimeLeaseType(
                    distro,
                    windowsSymbols: true,
                    replaceWindowsNative: true);
                var previous = Environment.GetEnvironmentVariable(name);
                try
                {
                    Environment.SetEnvironmentVariable(name, "original-value");
                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "lease-value", true);
                    Environment.SetEnvironmentVariable(name, "caller-value");

                    var failedSet = Assert.Throws<TargetInvocationException>(
                        () => InvokeLease(lease, "Set", name, NativeFailureValue, true));
                    Assert.IsType<InvalidOperationException>(failedSet.InnerException);
                    Assert.Equal("caller-value", Environment.GetEnvironmentVariable(name));
                    Assert.True((bool)InvokeLease(lease, "Restore", true));

                    InvokeLease(lease, "Begin");
                    InvokeLease(lease, "Set", name, "second-value", true);
                    Assert.True((bool)InvokeLease(lease, "Restore", true));
                    Assert.Equal("caller-value", Environment.GetEnvironmentVariable(name));
                }
                finally
                {
                    TryRestoreLease(lease, true);
                    Environment.SetEnvironmentVariable(name, previous);
                }
            }
        }

        private const string NativeFailureValue = "__U2F_NATIVE_FAILURE__";

        private static Type RuntimeLeaseType(
            string distro,
            bool windowsSymbols = false,
            bool replaceWindowsNative = false)
        {
            return CompileRuntimeLease(distro, windowsSymbols, replaceWindowsNative).GetType(
                "ROS2.Ros2ForUnityProcessEnvironmentLease",
                throwOnError: true);
        }

        private static object InvokeLease(Type lease, string method, params object[] arguments)
        {
            return lease.GetMethod(
                    method,
                    BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, arguments);
        }

        private static void TryRestoreLease(Type lease, bool windows)
        {
            try
            {
                InvokeLease(lease, "Restore", windows);
            }
            catch (TargetInvocationException)
            {
            }
        }

        private static Assembly CompileRuntimeLease(
            string distro,
            bool windowsSymbols,
            bool replaceWindowsNative)
        {
            var symbols = windowsSymbols
                ? new[] { "UNITY_EDITOR_WIN" }
                : Array.Empty<string>();
            var parseOptions = new CSharpParseOptions(preprocessorSymbols: symbols);
            var source = RuntimeSource(distro, "ROS2ForUnity.cs");
            var root = CSharpSyntaxTree.ParseText(source, parseOptions).GetCompilationUnitRoot();
            var declaration = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Single(node => node.Identifier.Text == "Ros2ForUnityProcessEnvironmentLease")
                .ToFullString();
            if (replaceWindowsNative)
            {
                const string nativeDeclaration =
                    "    [DllImport(\"ucrtbase.dll\", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]\n"
                    + "    private static extern int _wputenv_s(string name, string value);";
                var nativeStub =
                    "    private static int _wputenv_s(string name, string value)\n"
                    + "    {\n"
                    + "        return String.Equals(value, \"" + NativeFailureValue
                    + "\", StringComparison.Ordinal) ? 22 : 0;\n"
                    + "    }";
                var replaced = declaration.Replace(nativeDeclaration, nativeStub, StringComparison.Ordinal);
                Assert.NotEqual(declaration, replaced);
                declaration = replaced;
            }
            var probe = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UnityEngine
{
    internal static class Debug
    {
        internal static void LogException(Exception exception) { }
    }
}

namespace ROS2
{
" + declaration + @"
}
";

            var compilation = CSharpCompilation.Create(
                "Ros2ForUnityProcessEnvironmentLease_" + distro + "_" + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(probe, parseOptions) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var image = new MemoryStream();
            var emit = compilation.Emit(image);
            Assert.True(
                emit.Success,
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics
                        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(image);
        }

        private static MetadataReference[] TrustedPlatformReferences()
        {
            var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            Assert.False(string.IsNullOrEmpty(trustedAssemblies));
            return trustedAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }

        private static void AssertRos2WarningThrottle(string path)
        {
            var source = TestSources.Text(path);

            Assert.Contains("private int rosUnavailableWarningLogged = 0;", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref rosUnavailableWarningLogged, 1)", source, StringComparison.Ordinal);
            Assert.Contains("Volatile.Read(ref rosUnavailableWarningLogged)", source, StringComparison.Ordinal);
            Assert.Contains("Interlocked.Exchange(ref rosUnavailableWarningLogged, 0)", source, StringComparison.Ordinal);
        }

        private static void AssertCleanupClearsPublisherAfterNativeRemoval(string source)
        {
            var remove = source.IndexOf("RemovePublisher", StringComparison.Ordinal);
            var clear = source.IndexOf("_publisher = null", remove, StringComparison.Ordinal);
            var guard = source.LastIndexOf("if (!", remove, StringComparison.Ordinal);
            var retry = source.IndexOf("return;", remove, StringComparison.Ordinal);
            Assert.True(remove >= 0 && guard >= 0 && guard < remove && retry > remove && retry < clear);
        }

        private static readonly string[] RuntimeDistros = { "humble", "jazzy", "lyrical" };

        private static string RuntimeSource(string distro, string file)
            => TestSources.Text(
                "Packages/dev.unity2foxglove.ros2forunity.runtime." + distro +
                ".win64/Runtime/Ros2ForUnity/Scripts/" + file);

        private static void AssertTimeoutRetainsOwner(string source, string shutdownSignature)
        {
            var shutdown = TestSources.ExtractMethod(source, shutdownSignature);
            var failure = shutdown.IndexOf("if (!executorStopped)", StringComparison.Ordinal);
            Assert.True(failure >= 0);

            var retained = shutdown.IndexOf("native ownership remains active", StringComparison.Ordinal);
            var earlyReturn = shutdown.IndexOf("return;", failure, StringComparison.Ordinal);
            var detach = shutdown.IndexOf("TryDetachRuntimeState", StringComparison.Ordinal);
            var quarantine = TestSources.ExtractMethod(source, "private void QuarantineNodesAfterExecutorTimeout()");

            Assert.True(retained > failure);
            Assert.True(earlyReturn > retained);
            Assert.True(detach < 0 || earlyReturn < detach);
            Assert.DoesNotContain("nodes.Clear();", quarantine, StringComparison.Ordinal);
            Assert.DoesNotContain("ros2csNodes.Clear();", quarantine, StringComparison.Ordinal);
        }
    }
}
