// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 134-21 validation for ROS2 For Unity adapter facade behavior.

using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase134_21Validation
    {
        private const string FactoryTypeName = "Unity2Foxglove.Ros2ForUnity.Unity2FoxgloveRos2ContextFactory";

        private static int _passed;

        public static void Validate()
        {
            _passed = 0;

            VerifyInstallerHardened();
            VerifyAsmdefAndDocsPolicy();
            VerifyFacadeXmlDocs();
            VerifyFactoryReturnsUnavailableSingleton();
            VerifyUnavailableFacadeIsRepeatableAndDisposable();
            VerifyUnavailableFacadeNormalizesBlankNames();

            Console.WriteLine($"Phase134_21Validation: PASS ({_passed} checks)");
        }

        private static void VerifyInstallerHardened()
        {
            var installer = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeDefineInstaller.cs");

            Check(installer.Contains("if (!Application.isBatchMode)", StringComparison.Ordinal)
                  && installer.Contains("EditorApplication.Exit(exitCode)", StringComparison.Ordinal),
                "134-21-D1: batch define reconciliation is guarded by Unity batch mode");
            Check(installer.Contains("ReconcileCompileSymbolSafely", StringComparison.Ordinal)
                  && installer.Contains("FormatFailureMessage", StringComparison.Ordinal)
                  && installer.Contains("Debug.LogError", StringComparison.Ordinal),
                "134-21-D2: editor delayCall reconciliation reports contextual package failures");
            Check(installer.Contains("\"Packages\", \"packages-lock.json\"", StringComparison.Ordinal)
                  && installer.Contains("Leaving \" + CompileSymbol + \" disabled", StringComparison.Ordinal)
                  && installer.Contains("ContainsPackageKey(lockFile)", StringComparison.Ordinal),
                "134-21-D3: runtime detection cross-checks Unity package lock resolution");
        }

        private static void VerifyAsmdefAndDocsPolicy()
        {
            var runtimeAsmdef = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Runtime/Unity2Foxglove.Ros2ForUnity.asmdef");
            var editorAsmdef = ReadRepoText(
                "Packages/dev.unity2foxglove.ros2forunity/Editor/Unity2Foxglove.Ros2ForUnity.Editor.asmdef");
            var readme = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/README.md");

            Check(runtimeAsmdef.Contains("\"autoReferenced\": true", StringComparison.Ordinal)
                  && editorAsmdef.Contains("\"autoReferenced\": true", StringComparison.Ordinal)
                  && readme.Contains("autoReferenced=true", StringComparison.Ordinal)
                  && readme.Contains("predefined project assemblies", StringComparison.Ordinal),
                "134-21-E1: optional asmdef auto-reference policy is intentional and documented");
            Check(readme.Contains("Packages/packages-lock.json", StringComparison.Ordinal)
                  && readme.Contains("Standalone build target", StringComparison.Ordinal)
                  && readme.Contains("set the symbol manually", StringComparison.Ordinal),
                "134-21-E2: README documents resolved-package and Standalone-only symbol behavior");
        }

        private static void VerifyFacadeXmlDocs()
        {
            var context = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/IUnity2FoxgloveRos2Context.cs");
            var node = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/IUnity2FoxgloveRos2Node.cs");
            var publisher = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/IUnity2FoxgloveRos2Publisher.cs");
            var subscription = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/IUnity2FoxgloveRos2Subscription.cs");
            var factory = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Unity2FoxgloveRos2ContextFactory.cs");
            var unavailable = ReadRepoText("Packages/dev.unity2foxglove.ros2forunity/Runtime/Unity2FoxgloveRos2UnavailableContext.cs");

            Check(context.Contains("Facade-only contexts return", StringComparison.Ordinal)
                  && context.Contains("after disposal; the shared unavailable singleton remains", StringComparison.Ordinal)
                  && context.Contains("Creates or returns a node boundary", StringComparison.Ordinal),
                "134-21-F1: context interface documents availability, status, and node semantics");
            Check(node.Contains("returned disposable intentionally remains", StringComparison.Ordinal)
                  && publisher.Contains("return <c>false</c> with a concise", StringComparison.Ordinal)
                  && subscription.Contains("Message type information is consumed", StringComparison.Ordinal),
                "134-21-F2: node, publisher, and subscription interfaces document v1 asymmetry and error contract");
            Check(factory.Contains("always returns the shared unavailable", StringComparison.Ordinal)
                  && unavailable.Contains("intentionally keeps <see cref=\"Status\"/>", StringComparison.Ordinal)
                  && unavailable.Contains("Unity2FoxgloveRos2UnavailableContext.NormalizeTopic", StringComparison.Ordinal),
                "134-21-F3: factory and unavailable singleton lifecycle/readability contracts are explicit");
        }

        private static void VerifyFactoryReturnsUnavailableSingleton()
        {
            if (!TryCreateContext(out var first))
            {
                Check(true,
                    "134-21-A0: unavailable facade runtime behavior is skipped unless IncludeRos2ForUnityAdapter=true");
                return;
            }

            var second = CreateContext();

            Check(ReferenceEquals(first, second), "134-21-A1: factory returns stable unavailable singleton");
            Check(!GetBoolProperty(first, "IsAvailable")
                  && string.Equals(GetProperty(first, "Status")?.ToString(), "Unavailable", StringComparison.Ordinal)
                  && GetStringProperty(first, "StatusMessage").Contains("runtime", StringComparison.OrdinalIgnoreCase),
                "134-21-A2: unavailable singleton reports explicit runtime status");
        }

        private static void VerifyUnavailableFacadeIsRepeatableAndDisposable()
        {
            if (!TryCreateContext(out var context))
            {
                Check(true,
                    "134-21-B0: repeated unavailable facade behavior is skipped unless IncludeRos2ForUnityAdapter=true");
                return;
            }

            var callbackCount = 0;

            for (var i = 0; i < 8; i++)
            {
                var node = Invoke(context, "CreateNode", "unity2foxglove_phase134_21_" + i);
                var publisher = InvokeGeneric(node, "CreatePublisher", typeof(string), "/unity2foxglove/phase134_21/out_" + i);
                var subscription = InvokeGeneric(
                    node,
                    "CreateSubscription",
                    typeof(string),
                    "/unity2foxglove/phase134_21/in_" + i,
                    new Action<string>(_ => callbackCount++));

                Check(GetStringProperty(node, "Name") == "unity2foxglove_phase134_21_" + i,
                    "134-21-B1: unavailable node preserves explicit name iteration " + i);
                Check(GetStringProperty(publisher, "Topic") == "/unity2foxglove/phase134_21/out_" + i,
                    "134-21-B2: unavailable publisher preserves explicit topic iteration " + i);
                Check(GetStringProperty(subscription, "Topic") == "/unity2foxglove/phase134_21/in_" + i,
                    "134-21-B3: unavailable subscription preserves explicit topic iteration " + i);
                Check(!TryPublish(publisher, typeof(string), "payload", out var error) && !string.IsNullOrWhiteSpace(error),
                    "134-21-B4: unavailable publisher remains no-op with error iteration " + i);

                DisposeTwice(subscription);
                DisposeTwice(publisher);
                DisposeTwice(node);
            }

            DisposeTwice(context);

            Check(callbackCount == 0,
                "134-21-B5: unavailable subscriptions never invoke callbacks during repeated creation");
        }

        private static void VerifyUnavailableFacadeNormalizesBlankNames()
        {
            if (!TryCreateContext(out var context))
            {
                Check(true,
                    "134-21-C0: blank-name unavailable facade behavior is skipped unless IncludeRos2ForUnityAdapter=true");
                return;
            }

            var node = Invoke(context, "CreateNode", " ");
            var publisher = InvokeGeneric(node, "CreatePublisher", typeof(object), (object)null);
            var subscription = InvokeGeneric(node, "CreateSubscription", typeof(object), string.Empty, new Action<object>(_ => { }));

            Check(GetStringProperty(node, "Name") == "unity2foxglove_unavailable",
                "134-21-C1: unavailable node normalizes blank node names");
            Check(GetStringProperty(publisher, "Topic") == "/unity2foxglove/unavailable",
                "134-21-C2: unavailable publisher normalizes blank topics");
            Check(GetStringProperty(subscription, "Topic") == "/unity2foxglove/unavailable",
                "134-21-C3: unavailable subscription normalizes blank topics");

            DisposeTwice(subscription);
            DisposeTwice(publisher);
            DisposeTwice(node);
        }

        private static bool TryCreateContext(out object context)
        {
            var factory = FindType(FactoryTypeName);
            if (factory == null)
            {
                context = null;
                return false;
            }

            context = factory.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            if (context == null)
                throw new InvalidOperationException("Factory returned null context.");
            return true;
        }

        private static object CreateContext()
        {
            if (!TryCreateContext(out var context))
                throw new InvalidOperationException("Adapter facade runtime types are not loaded.");
            return context;
        }

        private static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, throwOnError: false))
                .FirstOrDefault(type => type != null);
        }

        private static object GetProperty(object target, string name)
        {
            return target.GetType()
                .GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(target);
        }

        private static bool GetBoolProperty(object target, string name)
        {
            return GetProperty(target, name) is bool value && value;
        }

        private static string GetStringProperty(object target, string name)
        {
            return GetProperty(target, name) as string ?? string.Empty;
        }

        private static object Invoke(object target, string methodName, params object[] args)
        {
            var method = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate =>
                    candidate.Name == methodName
                    && !candidate.IsGenericMethodDefinition
                    && candidate.GetParameters().Length == args.Length);
            if (method == null)
                throw new MissingMethodException(target.GetType().FullName, methodName);

            return method.Invoke(target, args);
        }

        private static object InvokeGeneric(object target, string methodName, Type genericType, params object[] args)
        {
            var method = target.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate =>
                    candidate.Name == methodName
                    && candidate.IsGenericMethodDefinition
                    && candidate.GetGenericArguments().Length == 1
                    && candidate.GetParameters().Length == args.Length);
            if (method == null)
                throw new MissingMethodException(target.GetType().FullName, methodName);

            return method.MakeGenericMethod(genericType).Invoke(target, args);
        }

        private static bool TryPublish(object publisher, Type payloadType, object payload, out string error)
        {
            var method = publisher.GetType().GetMethod(
                "TryPublish",
                new[] { payloadType, typeof(string).MakeByRefType() });
            if (method == null)
                throw new MissingMethodException(publisher.GetType().FullName, "TryPublish");

            var args = new[] { payload, null };
            var published = (bool)method.Invoke(publisher, args);
            error = args[1] as string;
            return published;
        }

        private static void DisposeTwice(object target)
        {
            if (target is not IDisposable disposable)
                throw new InvalidOperationException("Target is not disposable: " + target.GetType().FullName);

            disposable.Dispose();
            disposable.Dispose();
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            _passed++;
            Console.WriteLine(name);
        }

        private static string ReadRepoText(string relativePath)
            => File.ReadAllText(RepoPath(relativePath));

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
    }
}
