// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Harness
// Purpose: Load independently packaged FoxRun analyzers for combination tests.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    internal static class FoxRunAnalyzerTestComposition
    {
        private static readonly Lazy<IIncrementalGenerator> R2fuGenerator =
            new Lazy<IIncrementalGenerator>(LoadR2fuGenerator);
        private static readonly Lazy<IIncrementalGenerator> BridgeGenerator =
            new Lazy<IIncrementalGenerator>(LoadBridgeGenerator);

        internal static ISourceGenerator[] CoreAndR2fu()
            => new[]
            {
                new FoxgloveLogSourceGenerator().AsSourceGenerator(),
                R2fuGenerator.Value.AsSourceGenerator()
            };

        internal static ISourceGenerator[] CoreAndBridge()
            => new[]
            {
                new FoxgloveLogSourceGenerator().AsSourceGenerator(),
                BridgeGenerator.Value.AsSourceGenerator()
            };

        internal static ISourceGenerator[] AllProviders()
            => new[]
            {
                new FoxgloveLogSourceGenerator().AsSourceGenerator(),
                R2fuGenerator.Value.AsSourceGenerator(),
                BridgeGenerator.Value.AsSourceGenerator()
            };

        internal static ISourceGenerator[] LegacyCombined()
            => new[]
            {
                new FoxgloveLogSourceGenerator(
                    emitLegacyCombinedRos2Partial: true)
                    .AsSourceGenerator()
            };

        private static IIncrementalGenerator LoadR2fuGenerator()
        {
            var root = FindRepositoryRoot();
            var path = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2forunity",
                "Editor",
                "SourceGenerators",
                "analyzers",
                "dotnet",
                "cs",
                "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "The checked-in R2FU FoxRun analyzer is missing.",
                    path);
            }

            var assembly = Assembly.LoadFrom(path);
            var generatorType = assembly
                .GetTypes()
                .Single(type =>
                    !type.IsAbstract
                    && typeof(IIncrementalGenerator).IsAssignableFrom(type));
            return (IIncrementalGenerator)Activator.CreateInstance(generatorType);
        }

        private static IIncrementalGenerator LoadBridgeGenerator()
        {
            var root = FindRepositoryRoot();
            var path = Path.Combine(
                root,
                "Packages",
                "dev.unity2foxglove.ros2bridge",
                "Editor",
                "SourceGenerators",
                "analyzers",
                "dotnet",
                "cs",
                "Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "The checked-in ROS2 Bridge FoxRun analyzer is missing.",
                    path);
            }

            var assembly = Assembly.LoadFrom(path);
            var generatorType = assembly
                .GetTypes()
                .Single(type =>
                    !type.IsAbstract
                    && typeof(IIncrementalGenerator).IsAssignableFrom(type));
            return (IIncrementalGenerator)Activator.CreateInstance(generatorType);
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(
                        current.FullName,
                        "Packages",
                        "dev.unity2foxglove.sdk")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Unity2Foxglove repository root.");
        }
    }
}
