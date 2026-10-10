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

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "Packages",
                        "dev.unity2foxglove.ros2forunity",
                        "Editor",
                        "SourceGenerators",
                        "src",
                        "FoxRunRoslynRos2CustomDtoShapeBuilder.cs")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Repository root was not found from the test base directory.");
        }

        private static CSharpCompilation CreateDepthReuseCompilation(
            int deepestIndex,
            bool shallowFirst)
        {
            var source = new System.Text.StringBuilder();
            source.AppendLine("namespace Demo {");
            source.AppendLine(
                "public sealed class Shared { public int Value; }");
            for (var index = deepestIndex; index >= 0; index--)
            {
                var target = index == deepestIndex
                    ? "Shared"
                    : "Deep" + (index + 1);
                source.Append("public sealed class Deep")
                    .Append(index)
                    .Append(" { public ")
                    .Append(target)
                    .AppendLine(" Next; }");
            }
            source.AppendLine("public sealed class Root {");
            if (shallowFirst)
            {
                source.AppendLine("public Shared Shallow;");
                source.AppendLine("public Deep0 Deep;");
            }
            else
            {
                source.AppendLine("public Deep0 Deep;");
                source.AppendLine("public Shared Shallow;");
            }
            source.AppendLine("} }");
            return CSharpCompilation.Create(
                "Phase185MemoDepth_"
                + deepestIndex
                + "_"
                + shallowFirst
                + "_"
                + Guid.NewGuid().ToString("N"),
                new[] { CSharpSyntaxTree.ParseText(source.ToString()) },
                TrustedPlatformReferences(),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
        }

        private enum WideEnum : long
        {
            TooLarge = (long)int.MaxValue + 1L
        }

        private enum SmallEnum
        {
            Unknown = 0,
            Active = 1
        }

        private enum NoZeroEnum
        {
            First = 1,
            Second = 2
        }

        private sealed class CollectionPayload
        {
            public List<CollectionSample> Samples { get; set; }
            public byte[] Payload { get; set; }
        }

        private sealed class CollectionSample
        {
            public int Value { get; set; }
        }

        private struct ValuePayload
        {
            public int Value;
        }

        private sealed class ReferencePayload
        {
            public int Value;
        }

        private sealed class NoDefaultConstructorPayload
        {
            public NoDefaultConstructorPayload(int value)
            {
                Value = value;
            }

            public int Value { get; set; }
        }

        private sealed class InitOnlyPayload
        {
            public int Value { get; init; }
        }


        private struct NullableReuseSample
        {
            public int Value;
        }

        private sealed class RequiredFirstNullableReusePayload
        {
            public NullableReuseSample Required;
            public NullableReuseSample? Optional;
        }

        private sealed class OptionalFirstNullableReusePayload
        {
            public NullableReuseSample? Optional;
            public NullableReuseSample Required;
        }

        private sealed class DuplicateJsonNamePayload
        {
            [Newtonsoft.Json.JsonProperty("same")]
            public int First;

            [Newtonsoft.Json.JsonProperty("same")]
            public int Second;
        }

        private class DuplicateJsonNameBase
        {
            [Newtonsoft.Json.JsonProperty("same")]
            public int First;
        }

        private sealed class DuplicateJsonNameDerived :
            DuplicateJsonNameBase
        {
            [Newtonsoft.Json.JsonProperty("same")]
            public int Second;
        }

        private class OverrideBase
        {
            [Newtonsoft.Json.JsonProperty("same")]
            public virtual int Value { get; set; }
        }

        private sealed class OverrideDerived : OverrideBase
        {
            [Newtonsoft.Json.JsonProperty("same")]
            public override int Value { get; set; }
        }

        private class OverrideRenameBase
        {
            [Newtonsoft.Json.JsonProperty("old")]
            public virtual int Value { get; set; }
        }

        private sealed class OverrideRenameDerived :
            OverrideRenameBase
        {
            [Newtonsoft.Json.JsonProperty("new")]
            public override int Value { get; set; }
        }

        private class OverrideUnannotatedBase
        {
            [Newtonsoft.Json.JsonProperty("old")]
            public virtual int Value { get; set; }
        }

        private sealed class OverrideUnannotatedDerived :
            OverrideUnannotatedBase
        {
            public override int Value { get; set; }
        }

        private class OverrideIgnoredBase
        {
            [Newtonsoft.Json.JsonProperty("old")]
            public virtual int Value { get; set; }
        }

        private sealed class OverrideIgnoredDerived :
            OverrideIgnoredBase
        {
            [Newtonsoft.Json.JsonIgnore]
            public override int Value { get; set; }
        }

        private class IgnoredUnrelatedBase
        {
            [Newtonsoft.Json.JsonProperty("shared")]
            public int BaseValue;
        }

        private sealed class IgnoredUnrelatedDerived :
            IgnoredUnrelatedBase
        {
            [Newtonsoft.Json.JsonIgnore]
            [Newtonsoft.Json.JsonProperty("shared")]
            public string IgnoredDifferent;
        }

        private class IgnoredHiddenBase
        {
            public int Value;
        }

        private sealed class IgnoredHiddenDerived :
            IgnoredHiddenBase
        {
            [Newtonsoft.Json.JsonIgnore]
            public new string Value;
        }

        private class UnreadableUnrelatedBase
        {
            [Newtonsoft.Json.JsonProperty("shared")]
            public int BaseValue;
        }

        private sealed class UnreadableUnrelatedDerived :
            UnreadableUnrelatedBase
        {
            [Newtonsoft.Json.JsonProperty("shared")]
            public string UnreadableDifferent { private get; set; }
        }

        private class PrivateUnrelatedBase
        {
            [Newtonsoft.Json.JsonProperty("shared")]
            public int BaseValue;
        }

        private sealed class PrivateUnrelatedDerived :
            PrivateUnrelatedBase
        {
            [Newtonsoft.Json.JsonProperty("shared")]
            private string PrivateDifferent;
        }

        private class PrivateHiddenBase
        {
            public int Value;
        }

        private sealed class PrivateHiddenDerived :
            PrivateHiddenBase
        {
            private new string Value;
        }

        private class SetterOnlyHiddenBase
        {
            public int Value;
        }

        private sealed class SetterOnlyHiddenDerived :
            SetterOnlyHiddenBase
        {
            public new string Value { set { } }
        }

        private class HiddenMemberBase
        {
            [Newtonsoft.Json.JsonProperty("baseValue")]
            public int Value;
        }

        private sealed class HiddenMemberDerived :
            HiddenMemberBase
        {
            [Newtonsoft.Json.JsonProperty("derivedValue")]
            public new int Value;
        }

        private sealed class PrivateGetterPayload
        {
            public int Value { private get; set; }
        }

        private sealed class ListContractPayload
        {
            public List<int> Concrete { get; set; }
            public IList<int> Mutable { get; set; }
            public IReadOnlyList<int> ReadOnly { get; set; }
        }

        private abstract class AbstractPayload
        {
            public int Value { get; set; }
        }

        private sealed class CyclicPayload
        {
            public CyclicPayload Next { get; set; }
        }
    }
}
