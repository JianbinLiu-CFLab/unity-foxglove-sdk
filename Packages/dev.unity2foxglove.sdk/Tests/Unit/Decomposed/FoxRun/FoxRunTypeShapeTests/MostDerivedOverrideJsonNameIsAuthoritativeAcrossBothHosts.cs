// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: Locks the encoding-neutral recursive FoxRun type-shape contract.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{


public sealed partial class FoxRunTypeShapeTests
    {
        [Fact]
        [Trait("Phase", "185-F")]
        public void MostDerivedOverrideJsonNameIsAuthoritativeAcrossBothHosts()
        {
            const string source = @"
using Newtonsoft.Json;

namespace Demo
{
    public class OverrideRenameBase
    {
        [JsonProperty(""old"")]
        public virtual int Value { get; set; }
    }

    public sealed class OverrideRenameDerived : OverrideRenameBase
    {
        [JsonProperty(""new"")]
        public override int Value { get; set; }
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185JsonOverrideRenameParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName(
                "Demo.OverrideRenameDerived");

            Assert.NotNull(symbol);
            Assert.True(
                FoxRunRoslynTypeShapeBuilder.TryBuild(
                    symbol,
                    out var roslyn));
            var reflection = FoxRunReflectionTypeShapeBuilder.Build(
                typeof(OverrideRenameDerived));
            Assert.Equal("new", Assert.Single(roslyn.Fields).JsonName);
            Assert.Equal(
                "new",
                Assert.Single(reflection.Fields).JsonName);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void UnannotatedDerivedOverrideUsesItsOwnJsonNameAcrossBothHosts()
        {
            const string source = @"
using Newtonsoft.Json;

namespace Demo
{
    public class OverrideUnannotatedBase
    {
        [JsonProperty(""old"")]
        public virtual int Value { get; set; }
    }

    public sealed class OverrideUnannotatedDerived : OverrideUnannotatedBase
    {
        public override int Value { get; set; }
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185JsonOverrideUnannotatedParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName(
                "Demo.OverrideUnannotatedDerived");

            Assert.NotNull(symbol);
            Assert.True(
                FoxRunRoslynTypeShapeBuilder.TryBuild(
                    symbol,
                    out var roslyn));
            var reflection = FoxRunReflectionTypeShapeBuilder.Build(
                typeof(OverrideUnannotatedDerived));
            Assert.Equal("Value", Assert.Single(roslyn.Fields).JsonName);
            Assert.Equal(
                "Value",
                Assert.Single(reflection.Fields).JsonName);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void IgnoredDerivedOverrideSuppressesTheBaseSlotAcrossBothHosts()
        {
            const string source = @"
using Newtonsoft.Json;

namespace Demo
{
    public class OverrideIgnoredBase
    {
        [JsonProperty(""old"")]
        public virtual int Value { get; set; }
    }

    public sealed class OverrideIgnoredDerived : OverrideIgnoredBase
    {
        [JsonIgnore]
        public override int Value { get; set; }
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185JsonOverrideIgnoredParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName(
                "Demo.OverrideIgnoredDerived");

            Assert.NotNull(symbol);
            Assert.True(
                FoxRunRoslynTypeShapeBuilder.TryBuild(
                    symbol,
                    out var roslyn));
            var reflection = FoxRunReflectionTypeShapeBuilder.Build(
                typeof(OverrideIgnoredDerived));
            Assert.Empty(roslyn.Fields);
            Assert.Empty(reflection.Fields);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void IgnoredUnrelatedJsonNameDoesNotSuppressBaseAcrossBothHosts()
        {
            const string source = @"
using Newtonsoft.Json;

namespace Demo
{
    public class IgnoredUnrelatedBase
    {
        [JsonProperty(""shared"")] public int BaseValue;
    }

    public sealed class IgnoredUnrelatedDerived : IgnoredUnrelatedBase
    {
        [JsonIgnore, JsonProperty(""shared"")]
        public string IgnoredDifferent;
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185IgnoredUnrelatedJsonParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName(
                "Demo.IgnoredUnrelatedDerived");

            Assert.NotNull(symbol);
            Assert.True(
                FoxRunRoslynTypeShapeBuilder.TryBuild(
                    symbol,
                    out var roslyn));
            var reflection = FoxRunReflectionTypeShapeBuilder.Build(
                typeof(IgnoredUnrelatedDerived));
            Assert.Equal(
                "BaseValue",
                Assert.Single(roslyn.Fields).MemberName);
            Assert.Equal(
                "BaseValue",
                Assert.Single(reflection.Fields).MemberName);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void IgnoredHiddenClrNameStillFailsClosedAcrossBothHosts()
        {
            const string source = @"
using Newtonsoft.Json;

namespace Demo
{
    public class IgnoredHiddenBase
    {
        public int Value;
    }

    public sealed class IgnoredHiddenDerived : IgnoredHiddenBase
    {
        [JsonIgnore] public new string Value;
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185IgnoredHiddenClrParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName(
                "Demo.IgnoredHiddenDerived");

            Assert.NotNull(symbol);
            Assert.False(
                FoxRunRoslynTypeShapeBuilder.TryBuild(symbol, out _));
            var exception = Assert.Throws<InvalidOperationException>(
                () => FoxRunReflectionTypeShapeBuilder.Build(
                    typeof(IgnoredHiddenDerived)));
            Assert.StartsWith(
                "FOXRUN616:",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Contains(
                "Value",
                exception.Message,
                StringComparison.Ordinal);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void UnreadableUnrelatedJsonNameDoesNotSuppressBaseAcrossBothHosts()
        {
            const string source = @"
using Newtonsoft.Json;

namespace Demo
{
    public class UnreadableUnrelatedBase
    {
        [JsonProperty(""shared"")] public int BaseValue;
    }

    public sealed class UnreadableUnrelatedDerived : UnreadableUnrelatedBase
    {
        [JsonProperty(""shared"")]
        public string UnreadableDifferent { private get; set; }
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185UnreadableUnrelatedJsonParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName(
                "Demo.UnreadableUnrelatedDerived");

            Assert.NotNull(symbol);
            Assert.True(
                FoxRunRoslynTypeShapeBuilder.TryBuild(
                    symbol,
                    out var roslyn));
            var reflection = FoxRunReflectionTypeShapeBuilder.Build(
                typeof(UnreadableUnrelatedDerived));
            Assert.Equal(
                "BaseValue",
                Assert.Single(roslyn.Fields).MemberName);
            Assert.Equal(
                "BaseValue",
                Assert.Single(reflection.Fields).MemberName);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void PrivateUnrelatedJsonNameDoesNotConflictAcrossBothHosts()
        {
            const string source = @"
using Newtonsoft.Json;

namespace Demo
{
    public class PrivateUnrelatedBase
    {
        [JsonProperty(""shared"")] public int BaseValue;
    }

    public sealed class PrivateUnrelatedDerived : PrivateUnrelatedBase
    {
        [JsonProperty(""shared"")]
        private string PrivateDifferent;
    }
}";
            var compilation = CSharpCompilation.Create(
                "Phase185PrivateUnrelatedJsonParity",
                new[] { CSharpSyntaxTree.ParseText(source) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var symbol = compilation.GetTypeByMetadataName(
                "Demo.PrivateUnrelatedDerived");

            Assert.NotNull(symbol);
            Assert.True(
                FoxRunRoslynTypeShapeBuilder.TryBuild(
                    symbol,
                    out var roslyn));
            var reflection = FoxRunReflectionTypeShapeBuilder.Build(
                typeof(PrivateUnrelatedDerived));
            Assert.Equal(
                "BaseValue",
                Assert.Single(roslyn.Fields).MemberName);
            Assert.Equal(
                "BaseValue",
                Assert.Single(reflection.Fields).MemberName);
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void PrivateHiddenClrNameFailsClosedAcrossBothHosts()
        {
            const string source = @"
namespace Demo
{
    public class PrivateHiddenBase { public int Value; }
    public sealed class PrivateHiddenDerived : PrivateHiddenBase
    {
        private new string Value;
    }
}";
            AssertHiddenLookupCollisionRejected(
                source,
                "Demo.PrivateHiddenDerived",
                typeof(PrivateHiddenDerived));
        }

        [Fact]
        [Trait("Phase", "185-F")]
        public void SetterOnlyHiddenClrNameFailsClosedAcrossBothHosts()
        {
            const string source = @"
namespace Demo
{
    public class SetterOnlyHiddenBase { public int Value; }
    public sealed class SetterOnlyHiddenDerived : SetterOnlyHiddenBase
    {
        public new string Value { set { } }
    }
}";
            AssertHiddenLookupCollisionRejected(
                source,
                "Demo.SetterOnlyHiddenDerived",
                typeof(SetterOnlyHiddenDerived));
        }

    }
}
