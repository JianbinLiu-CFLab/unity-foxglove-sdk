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
        private static FoxRunGenerationMember ShapedMember(
            FoxRunTypeShape shape,
            int mode,
            string encoding,
            bool explicitEncoding,
            string memberName = "_incoming",
            float hz = 10f,
            bool explicitHz = false,
            string jsonFieldName = "")
        {
            var presence = explicitEncoding
                ? FoxRunNamedArgumentPresence.Encoding
                : FoxRunNamedArgumentPresence.None;
            if (explicitHz)
                presence |= FoxRunNamedArgumentPresence.Hz;
            return new FoxRunGenerationMember(
                "Demo",
                "ShapeOwner",
                memberName,
                "field",
                shape.TypeName,
                false,
                false,
                string.Empty,
                "/phase185/shape",
                hz,
                shape.TypeName,
                (int)FoxRunPolicy.FixedRate,
                0f,
                "Test",
                0,
                string.Empty,
                jsonFieldName: jsonFieldName,
                mode: mode,
                encoding: encoding,
                typeShape: shape,
                namedArgumentPresence: presence);
        }

        private static FoxRunTypeShape ManualObjectChain(int relativeDepth)
        {
            var shape = FoxRunTypeShape.Canonical("int32");
            for (var index = 0; index < relativeDepth; index++)
            {
                shape = FoxRunTypeShape.Object(
                    "Demo.Depth" + index,
                    new[]
                    {
                        new FoxRunTypeField(
                            "next",
                            "Next",
                            shape)
                    });
            }
            return shape;
        }

        private static void AssertDirectionAvailability(
            FoxRunTypeShape shape,
            bool publishAvailable,
            bool subscribeAvailable)
        {
            var publish = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(
                    shape,
                    (int)FoxRunFlow.Publish,
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    explicitEncoding: false)
            });
            var subscribe = FoxRunGenerationModel.FromMembers(new[]
            {
                ShapedMember(
                    shape,
                    (int)FoxRunFlow.Subscribe,
                    FoxRunGenerationDescriptorConstants.InheritEncoding,
                    explicitEncoding: false)
            });

            Assert.Equal(
                publishAvailable,
                Assert.Single(
                    Assert.Single(publish.Types).Members[0].EncodingVariants,
                    value => value.Encoding == "msgpack").PublishAvailable);
            Assert.Equal(
                subscribeAvailable,
                Assert.Single(
                    Assert.Single(subscribe.Types).Members[0].EncodingVariants,
                    value => value.Encoding == "msgpack").SubscribeAvailable);
        }

        private static bool MessagePackPublishAvailable(FoxRunGenerationMember member)
            => Assert.Single(
                member.EncodingVariants,
                value => value.Encoding == FoxRunGenerationDescriptorConstants.MessagePackEncoding)
                .PublishAvailable;

        private static void AssertComponentShape(
            FoxRunTypeShape shape,
            string typeName,
            IReadOnlyList<string> components)
        {
            Assert.NotNull(shape);
            Assert.Equal(FoxRunTypeShapeKind.Object, shape.Kind);
            Assert.Equal(typeName, shape.TypeName);
            Assert.True(shape.CanConstruct);
            Assert.Equal(components, shape.Fields.Select(field => field.JsonName).ToArray());
            Assert.All(shape.Fields, field =>
            {
                Assert.Equal(field.JsonName, field.MemberName);
                Assert.True(field.CanAssign);
                Assert.Equal(FoxRunTypeShapeKind.Canonical, field.TypeShape.Kind);
                Assert.Equal("float32", field.TypeShape.CanonicalType);
            });
        }

        private static void AssertNullableReuseShape(FoxRunTypeShape shape)
        {
            var required = Assert.Single(
                shape.Fields,
                field => field.MemberName == "Required");
            var optional = Assert.Single(
                shape.Fields,
                field => field.MemberName == "Optional");

            Assert.False(required.IsNullable);
            Assert.False(required.TypeShape.Nullable);
            Assert.True(optional.IsNullable);
            Assert.True(optional.TypeShape.Nullable);
        }

        private static string FieldIdentity(FoxRunTypeField field)
            => field.JsonName
               + "|"
               + field.MemberName
               + "|"
               + field.TypeShape.Kind
               + "|"
               + field.TypeShape.CanonicalType;

        private static string ShapeIdentity(FoxRunTypeShape shape)
            => shape.Kind
               + ":"
               + shape.TypeName
               + ":"
               + shape.CanonicalType
               + "["
               + string.Join(
                   ",",
                   shape.Fields.Select(field =>
                       field.JsonName
                       + "="
                       + ShapeIdentity(field.TypeShape)))
               + "]"
               + (shape.ElementShape == null
                   ? string.Empty
                   : "<" + ShapeIdentity(shape.ElementShape) + ">");

        private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        {
            var locations = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Append(
                    typeof(Newtonsoft.Json.JsonPropertyAttribute)
                        .Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return locations.Select(location => MetadataReference.CreateFromFile(location));
        }

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

    }
}
