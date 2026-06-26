// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase163-4 review regression checks for registries, assets, parameters, and services.

using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase163_4Validation
    {
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 163-4: Registries, Assets, Parameters, and Services Review ===");

            ParameterStoreDoesNotExposeMutableTokens();
            ParameterStoreRejectsUnknownTypes();
            ParameterStoreKeepsBoolAliasCompatible();
            ChannelRegistryRejectsNullChannels();
            SubscriptionRegistryReportsBudgetFailure();
            ConnectionGraphRegistryRejectsInvalidTopologyValues();
            AssetRegistryChoosesLongestNormalizedPrefix();
            ServiceRegistryOwnsDescriptorCopies();
            ServiceDrainReportsNullHandlerResult();
            SourceContractsRemainExplicit();

            Console.WriteLine("Phase 163-4: 11 checks passed.");
            Console.WriteLine();
        }

        private static void ParameterStoreDoesNotExposeMutableTokens()
        {
            var store = new FoxgloveParameterStore();
            store.OnParameterChanged += (_, value, _) => ((JArray)value).Add(999);

            store.Register("/values", new JArray(1, 2), "number[]", writable: true);
            var first = store.GetWireParameter("/values");
            ((JArray)first.Value).Add(3);
            var second = store.GetWireParameter("/values");

            Check(((JArray)second.Value).Count == 2,
                "163-4A: parameter events and wire DTO reads clone mutable JToken values");
        }

        private static void ParameterStoreRejectsUnknownTypes()
        {
            Check(!FoxgloveParameterStore.TryNormalizeValueForType("int64", new JArray(1), out _),
                "163-4B: unknown parameter types do not accept arbitrary JSON values");

            var store = new FoxgloveParameterStore();
            var threw = false;
            try { store.Register("/bad", new JValue(1), "int64", writable: true); }
            catch (ArgumentException) { threw = true; }

            Check(threw, "163-4C: registering an unknown parameter type fails explicitly");
        }

        private static void ParameterStoreKeepsBoolAliasCompatible()
        {
            var store = new FoxgloveParameterStore();
            store.Register("/enabled", new JValue(true), "bool", writable: true);

            var parameter = store.GetWireParameter("/enabled");
            Check(parameter.Type == "boolean" && parameter.Value.Type == JTokenType.Boolean,
                "163-4C2: legacy bool parameter declarations normalize to boolean");
        }

        private static void ChannelRegistryRejectsNullChannels()
        {
            var registry = new ChannelRegistry();
            var threw = false;
            try { registry.Register(null); }
            catch (ArgumentNullException) { threw = true; }

            Check(threw, "163-4D: ChannelRegistry.Register rejects null descriptors with ArgumentNullException");
        }

        private static void SubscriptionRegistryReportsBudgetFailure()
        {
            var registry = new SubscriptionRegistry();
            var accepted = true;
            for (uint i = 0; i < SubscriptionRegistry.MaxSubscriptionsPerClient; i++)
                accepted &= registry.TryAddSubscription(7, i, 1, out _);

            var rejected = !registry.TryAddSubscription(
                7,
                SubscriptionRegistry.MaxSubscriptionsPerClient + 1u,
                1,
                out var error);
            Check(accepted && rejected && !string.IsNullOrWhiteSpace(error),
                "163-4E: AddSubscription reports budget rejection instead of silently dropping it");
        }

        private static void ConnectionGraphRegistryRejectsInvalidTopologyValues()
        {
            var registry = new ConnectionGraphRegistry();
            var nullTopicRejected = false;
            var nullIdRejected = false;
            try { registry.AddPublishedTopic(null, "pub"); }
            catch (ArgumentException) { nullTopicRejected = true; }

            try { registry.AddSubscribedTopic("/topic", null); }
            catch (ArgumentException) { nullIdRejected = true; }

            Check(nullTopicRejected && nullIdRejected,
                "163-4F: connection graph topology methods reject null topic names and identifiers");
        }

        private static void AssetRegistryChoosesLongestNormalizedPrefix()
        {
            var baseDir = Path.Combine(Path.GetTempPath(), "u2f-phase163-4-" + Guid.NewGuid().ToString("N"));
            var assetsRoot = Path.Combine(baseDir, "assets");
            var evilRoot = Path.Combine(baseDir, "assets-evil");
            Directory.CreateDirectory(assetsRoot);
            Directory.CreateDirectory(evilRoot);
            var expected = Path.Combine(evilRoot, "file.bin");
            File.WriteAllBytes(expected, new byte[] { 1, 2, 3 });

            try
            {
                var registry = new FoxgloveAssetRegistry();
                registry.RegisterRoot("assets", assetsRoot);
                registry.RegisterRoot("assets-evil", evilRoot);

                var ok = registry.TryResolve("assets-evil/file.bin", out var resolved, out _);
                Check(ok && string.Equals(Path.GetFullPath(expected), resolved, StringComparison.OrdinalIgnoreCase),
                    "163-4G: asset roots normalize prefixes and resolve longest matching root");
            }
            finally
            {
                try { Directory.Delete(baseDir, recursive: true); } catch { }
            }
        }

        private static void ServiceRegistryOwnsDescriptorCopies()
        {
            var registry = new FoxgloveServiceRegistry();
            var descriptor = new ServiceDescriptor
            {
                Name = "/svc",
                Type = "example.Service",
                Request = new ServiceSchemaDescriptor { Encoding = "json", SchemaName = "Req" },
                Response = new ServiceSchemaDescriptor { Encoding = "json", SchemaName = "Resp" }
            };

            var firstId = registry.Register(descriptor);
            var secondId = registry.Register(descriptor);
            var first = registry.GetById(firstId);
            var second = registry.GetById(secondId);
            first.Name = "/mutated";

            Check(descriptor.Id == 0
                  && first.Id == firstId
                  && second.Id == secondId
                  && registry.GetById(firstId).Name == "/svc",
                "163-4H: service registry stores and returns descriptor copies");
        }

        private static void ServiceDrainReportsNullHandlerResult()
        {
            var root = Phase16Validation.FindRepoRoot();
            var services = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Session/FoxgloveSession.Services.cs");
            Check(services.Contains("\"Service handler returned null\"", StringComparison.Ordinal),
                "163-4I: service drain reports null handler results without a NullReferenceException");
        }

        private static void SourceContractsRemainExplicit()
        {
            var root = Phase16Validation.FindRepoRoot();
            var assets = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Assets/FoxgloveAssetRegistry.cs");
            var serviceRegistry = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Services/FoxgloveServiceRegistry.cs");
            var paramSubs = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/ParameterSubscriptionRegistry.cs");
            var subscriptions = Read(root, "Packages/dev.unity2foxglove.sdk/Runtime/Core/Registries/SubscriptionRegistry.cs");
            var registry = Read(root, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");

            Check(assets.Contains("System.Security.SecurityException", StringComparison.Ordinal)
                  && assets.Contains("NormalizeUriPrefix", StringComparison.Ordinal)
                  && serviceRegistry.Contains("Completed timeout failures remain pending until DrainCompleted", StringComparison.Ordinal)
                  && paramSubs.Contains("named unsubscription", StringComparison.Ordinal)
                  && !subscriptions.Contains("RemoveEmptyClientEntriesLocked", StringComparison.Ordinal)
                  && registry.Contains("Ci(\"--phase163-4\", \"Phase 163-4\", Phase163_4Validation.Validate", StringComparison.Ordinal),
                "163-4J: source-level compatibility and docs contracts are wired");
        }

        private static string Read(string root, string relativePath)
            => File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + message);

            Console.WriteLine("[PASS] " + message);
        }
    }
}
