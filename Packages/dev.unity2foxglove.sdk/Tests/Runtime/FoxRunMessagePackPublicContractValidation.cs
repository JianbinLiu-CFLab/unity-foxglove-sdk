// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural release gate for the public typed FoxRun MessagePack contract.

using System;
using System.IO;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Tests
{
    public static class FoxRunMessagePackPublicContractValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- FoxRun MessagePack public contract validation ---");
            _passed = 0;

            Check(
                (int)FoxRunEncoding.Protobuf == 1
                && (int)FoxRunEncoding.JSON == 2
                && (int)FoxRunEncoding.MessagePack == 3,
                "185A-1: public FoxRun encoding values remain stable, reserve zero for omission, and append MessagePack");

            var constants = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunGenerationDescriptorConstants.cs");
            Check(
                constants.Contains("public const int DescriptorVersion = 5;", StringComparison.Ordinal)
                && constants.Contains("public const string GeneratorVersion = \"5.0.0\";", StringComparison.Ordinal)
                && constants.Contains("public const string MessagePackEncoding = \"msgpack\";", StringComparison.Ordinal),
                "185A-2: descriptor v5 and generator 5.0.0 share the msgpack wire label");

            var diagnostics = Read("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxgloveLogSourceGenerator.Diagnostics.cs");
            Check(
                ContainsAll(diagnostics, "FOXRUN616", "FOXRUN617", "FOXRUN618", "FOXRUN619"),
                "185A-3: typed MessagePack shape, metadata, topology, and schedule diagnostics are reserved");

            var typeShape = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxRunDescriptor/FoxRunTypeShape.cs");
            var roslynShape = Read("Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/src/FoxRunRoslynTypeShapeBuilder.cs");
            var reflectionShape = Read("Packages/dev.unity2foxglove.sdk/Editor/FoxRun/FoxRunReflectionTypeShapeBuilder.cs");
            Check(
                typeShape.Contains("class FoxRunTypeShape", StringComparison.Ordinal)
                && roslynShape.Contains("FoxRunTypeShape", StringComparison.Ordinal)
                && reflectionShape.Contains("FoxRunTypeShape", StringComparison.Ordinal),
                "185A-4: Roslyn and reflection use the encoding-neutral recursive type shape");

            var compatibility = Read("Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxRun/FoxRunGenerationDescriptorCompatibilityTests.cs");
            Check(
                compatibility.Contains("StrictV5RoundTripPreservesRecursiveShapeAvailabilityAndSchedule", StringComparison.Ordinal)
                && compatibility.Contains("FrozenV4FixtureReadsWithoutInventingMessagePack", StringComparison.Ordinal)
                && compatibility.Contains("CrossPairedAndFutureDescriptorVersionsFailClosed", StringComparison.Ordinal),
                "185A-5: descriptor v5 is strict while the explicit v4 read fixture remains covered");

            VerifyPublicDocumentation();
            VerifyDependencyBoundary();

            Console.WriteLine("FoxRun MessagePack public contract: " + _passed + " checks passed.\n");
        }

        private static void VerifyPublicDocumentation()
        {
            var documentationPaths = new[]
            {
                "README.md",
                "Packages/dev.unity2foxglove.sdk/README.md",
                "Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/README.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/07_FoxRun_Zero_Code_Publishing.md",
                FindChineseFoxRunGuide(),
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/10_Architecture.md",
                "Packages/dev.unity2foxglove.sdk/Documentation~/en/12_Inspector_Reference.md"
            };
            foreach (var path in documentationPaths)
            {
                Check(
                    Read(path).Contains("MessagePack", StringComparison.OrdinalIgnoreCase),
                    "185A-doc: " + path + " includes MessagePack in its maintained FoxRun surface");
            }

            var inventory = Read("docs/research-shared-emitter-architecture.md");
            Check(
                ContainsAll(
                    inventory,
                    "MessagePackPublishDispatchEmitter.cs",
                    "MessagePackInputDispatchEmitter.cs"),
                "185A-6: shared-emitter inventory lists output and bounded-input MessagePack modules together");
        }

        private static void VerifyDependencyBoundary()
        {
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(
                ContainsAll(
                    project,
                    "../../Runtime/Schemas/MsgPack/FoxgloveMsgPackWriter.cs",
                    "../../Runtime/Schemas/MsgPack/FoxgloveMsgPackReader.cs",
                    "../../Runtime/Schemas/MsgPack/FoxgloveMsgPackReadLimits.cs"),
                "185A-7: the maintained writer, reader, and limits are explicit compile surfaces");

            var package = Read("Packages/dev.unity2foxglove.sdk/package.json");
            var panel = Read("Tools/foxglove-extensions/foxrun-publish-panel/package.json");
            Check(
                !package.Contains("MessagePack-CSharp", StringComparison.OrdinalIgnoreCase)
                && !panel.Contains("@msgpack", StringComparison.OrdinalIgnoreCase)
                && !panel.Contains("msgpackr", StringComparison.OrdinalIgnoreCase)
                && !panel.Contains("notepack", StringComparison.OrdinalIgnoreCase),
                "185A-8: no third-party or typeless MessagePack serializer dependency is introduced");
        }

        internal static string Root()
            => Phase16Validation.FindRepoRoot()
               ?? throw new DirectoryNotFoundException("Could not find repository root.");

        internal static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(
                Root(),
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

        internal static bool Exists(string relativePath)
            => File.Exists(Path.Combine(
                Root(),
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

        internal static bool ContainsAll(string source, params string[] values)
        {
            foreach (var value in values)
                if (!source.Contains(value, StringComparison.Ordinal))
                    return false;
            return true;
        }

        private static string FindChineseFoxRunGuide()
        {
            var directory = Path.Combine(
                Root(),
                "Packages",
                "dev.unity2foxglove.sdk",
                "Documentation~",
                "zh");
            var matches = Directory.GetFiles(directory, "07_FoxRun*.md");
            if (matches.Length != 1)
                throw new InvalidOperationException("Expected exactly one maintained Chinese FoxRun guide.");
            return Path.GetRelativePath(Root(), matches[0]).Replace('\\', '/');
        }

        internal static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
