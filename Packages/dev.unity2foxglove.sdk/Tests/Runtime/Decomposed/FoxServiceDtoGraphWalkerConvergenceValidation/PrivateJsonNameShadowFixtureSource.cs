// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates Phase 141F FoxService DTO graph walker convergence.

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class FoxServiceDtoGraphWalkerConvergenceValidation
    {
        private static string PrivateJsonNameShadowFixtureSource()
            => @"
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Components;

namespace Phase141FPrivateShadow
{
    public class PrivateJsonNameShadowBase
    {
        [JsonProperty(""shared"")]
        public int BaseValue { get; set; }
    }

    public sealed class PrivateJsonNameShadowDerived : PrivateJsonNameShadowBase
    {
        [JsonProperty(""shared"")]
        private string PrivateValue { get; set; }
    }

    public sealed class PrivateJsonNameShadowRequest
    {
        public PrivateJsonNameShadowDerived Item { get; set; }
    }

    public partial class Fixture
    {
        [FoxService(""/phase141f/private-shadow"", Type = ""Phase141F.PrivateShadow"", RequestSchemaName = ""Phase141F.PrivateShadow.Request"", ResponseSchemaName = ""Phase141F.PrivateShadow.Response"")]
        private void Check(PrivateJsonNameShadowRequest request) {}
    }
}
";

        private static string MultiDimensionalArrayFixtureSource()
            => @"
using Unity.FoxgloveSDK.Components;

namespace Phase141FMultiDim
{
    public sealed class MultiDimensionalArrayRequest
    {
        public int[,] Grid { get; set; }
    }

    public partial class Fixture
    {
        [FoxService(""/phase141f/multidim"", Type = ""Phase141F.MultiDim"", RequestSchemaName = ""Phase141F.MultiDim.Request"", ResponseSchemaName = ""Phase141F.MultiDim.Response"")]
        private void Check(MultiDimensionalArrayRequest request) {}
    }
}
";

        private sealed class Leaf
        {
            public int Value { get; set; }
        }

        private sealed class DepthMemoRequest
        {
            public Leaf Shallow { get; set; }
            public DepthNode0 Deep { get; set; }
        }

        private sealed class DepthNode0 { public DepthNode1 Next { get; set; } }
        private sealed class DepthNode1 { public DepthNode2 Next { get; set; } }
        private sealed class DepthNode2 { public DepthNode3 Next { get; set; } }
        private sealed class DepthNode3 { public DepthNode4 Next { get; set; } }
        private sealed class DepthNode4 { public DepthNode5 Next { get; set; } }
        private sealed class DepthNode5 { public DepthNode6 Next { get; set; } }
        private sealed class DepthNode6 { public DepthNode7 Next { get; set; } }
        private sealed class DepthNode7 { public DepthNode8 Next { get; set; } }
        private sealed class DepthNode8 { public DepthNode9 Next { get; set; } }
        private sealed class DepthNode9 { public DepthNode10 Next { get; set; } }
        private sealed class DepthNode10 { public DepthNode11 Next { get; set; } }
        private sealed class DepthNode11 { public DepthNode12 Next { get; set; } }
        private sealed class DepthNode12 { public DepthNode13 Next { get; set; } }
        private sealed class DepthNode13 { public DepthNode14 Next { get; set; } }
        private sealed class DepthNode14 { public DepthNode15 Next { get; set; } }
        private sealed class DepthNode15 { public DepthNode16 Next { get; set; } }
        private sealed class DepthNode16 { public DepthNode17 Next { get; set; } }
        private sealed class DepthNode17 { public DepthNode18 Next { get; set; } }
        private sealed class DepthNode18 { public DepthNode19 Next { get; set; } }
        private sealed class DepthNode19 { public DepthNode20 Next { get; set; } }
        private sealed class DepthNode20 { public DepthNode21 Next { get; set; } }
        private sealed class DepthNode21 { public DepthNode22 Next { get; set; } }
        private sealed class DepthNode22 { public DepthNode23 Next { get; set; } }
        private sealed class DepthNode23 { public DepthNode24 Next { get; set; } }
        private sealed class DepthNode24 { public DepthNode25 Next { get; set; } }
        private sealed class DepthNode25 { public DepthNode26 Next { get; set; } }
        private sealed class DepthNode26 { public DepthNode27 Next { get; set; } }
        private sealed class DepthNode27 { public DepthNode28 Next { get; set; } }
        private sealed class DepthNode28 { public DepthNode29 Next { get; set; } }
        private sealed class DepthNode29 { public DepthNode30 Next { get; set; } }
        private sealed class DepthNode30 { public DepthNode31 Next { get; set; } }
        private sealed class DepthNode31 { public DepthNode32 Next { get; set; } }
        private sealed class DepthNode32 { public DepthNode33 Next { get; set; } }
        private sealed class DepthNode33 { public Leaf Leaf { get; set; } }

        private class HiddenBase
        {
            public object Value { get; set; }

            [JsonProperty("alias")]
            public object BaseAlias { get; set; }
        }

        private sealed class HiddenDerived : HiddenBase
        {
            public new int Value { get; set; }

            [JsonProperty("alias")]
            public int DerivedAlias { get; set; }
        }

        private sealed class HiddenMemberRequest
        {
            public HiddenDerived Item { get; set; }
        }

        private class PrivateJsonNameShadowBase
        {
            [JsonProperty("shared")]
            public int BaseValue { get; set; }
        }

        private sealed class PrivateJsonNameShadowDerived : PrivateJsonNameShadowBase
        {
            [JsonProperty("shared")]
            private string PrivateValue { get; set; }
        }

        private sealed class PrivateJsonNameShadowRequest
        {
            public PrivateJsonNameShadowDerived Item { get; set; }
        }

        private sealed class MultiDimensionalArrayRequest
        {
            public int[,] Grid { get; set; }
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passCount++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
