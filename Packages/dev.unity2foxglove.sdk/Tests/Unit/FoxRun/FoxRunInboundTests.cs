// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.SourceGenerators;
using Unity.FoxgloveSDK.Transport;
using Unity.FoxgloveSDK.UnitTests.Harness;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    public sealed partial class FoxRunInboundTests
    {














        [Fact]
        public void JsonDecoderReadsDeclaredVectorShape()
        {
            var payload = Encoding.UTF8.GetBytes(
                "{\"incomingVelocity\":{\"x\":1.5,\"y\":-2,\"z\":3.25}}");

            var ok = FoxRunInboundJson.TryRead(
                payload,
                "incomingVelocity",
                out Vector3 value,
                out var error);

            Assert.True(ok, error);
            Assert.Equal(1.5f, value.x);
            Assert.Equal(-2f, value.y);
            Assert.Equal(3.25f, value.z);
        }

        [Fact]
        public void JsonDecoderRejectsUnknownRootPropertiesWithoutProducingAValue()
        {
            var payload = Encoding.UTF8.GetBytes("{\"value\":1,\"metadata\":2}");

            var ok = FoxRunInboundJson.TryRead(
                payload,
                "value",
                out int value,
                out var error);

            Assert.False(ok);
            Assert.Equal(0, value);
            Assert.Contains("unknown field", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void JsonDecoderRejectsNonFiniteVectorComponents()
        {
            var payload = Encoding.UTF8.GetBytes(
                "{\"position\":{\"x\":1,\"y\":2,\"z\":1e400}}");

            var ok = FoxRunInboundJson.TryRead(
                payload,
                "position",
                out Vector3 value,
                out var error);

            Assert.False(ok);
            Assert.Equal(0f, value.z);
            Assert.Contains("finite", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void VisualTimeAndDurationSerializeNormalizedCarryAtTheirUpperBoundary()
        {
            var time = new FoxgloveTime
            {
                Sec = ulong.MaxValue - 1,
                Nsec = 1_000_000_000U
            };
            var duration = new FoxgloveDuration
            {
                Sec = long.MaxValue - 1,
                Nsec = 1_000_000_000U
            };

            Assert.Equal(ulong.MaxValue, time.Sec);
            Assert.Equal(long.MaxValue, duration.Sec);
            var timeJson = Newtonsoft.Json.JsonConvert.SerializeObject(time);
            var durationJson = Newtonsoft.Json.JsonConvert.SerializeObject(duration);
            var timeObject = JObject.Parse(timeJson);
            var durationObject = JObject.Parse(durationJson);
            Assert.Equal(ulong.MaxValue, (ulong)timeObject["sec"]);
            Assert.Equal(0U, (uint)timeObject["nsec"]);
            Assert.Equal(long.MaxValue, (long)durationObject["sec"]);
            Assert.Equal(0U, (uint)durationObject["nsec"]);
        }

        [Fact]
        public void JsonDecoderRejectsPolymorphicTypeHints()
        {
            var payload = Encoding.UTF8.GetBytes(
                "{\"incomingVelocity\":{\"$type\":\"System.Version\",\"x\":1,\"y\":2,\"z\":3}}");

            var ok = FoxRunInboundJson.TryRead(
                payload,
                "incomingVelocity",
                out Vector3 _,
                out var error);

            Assert.False(ok);
            Assert.Contains("$type", error, StringComparison.Ordinal);
        }

        [Fact]
        public void JsonDecoderRejectsInvalidUtf8WithoutReplacement()
        {
            var prefix = Encoding.UTF8.GetBytes("{\"value\":\"");
            var suffix = Encoding.UTF8.GetBytes("\"}");
            var payload = new byte[prefix.Length + 1 + suffix.Length];
            Buffer.BlockCopy(prefix, 0, payload, 0, prefix.Length);
            payload[prefix.Length] = 0xff;
            Buffer.BlockCopy(
                suffix,
                0,
                payload,
                prefix.Length + 1,
                suffix.Length);

            var ok = FoxRunInboundJson.TryRead(
                payload,
                "value",
                out string value,
                out var error);

            Assert.False(ok);
            Assert.Null(value);
            Assert.Contains("UTF-8", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void JsonDecoderRoundTripsGeneratorValidatedDtoWithoutTypeMetadata()
        {
            var expected = new GeneratedJsonProbe
            {
                Count = 184,
                Kind = GeneratedJsonProbeKind.Active,
                Label = "p184g_roundtrip",
                Nested = new GeneratedJsonNestedProbe { Enabled = true }
            };
            var json = new StringBuilder("{\"state\":");
            FoxRunInboundJson.AppendObject(json, expected);
            json.Append('}');

            var encoded = json.ToString();
            var ok = FoxRunInboundJson.TryReadObject(
                Encoding.UTF8.GetBytes(encoded),
                "state",
                out GeneratedJsonProbe actual,
                out var error);

            Assert.True(ok, error);
            Assert.DoesNotContain("$type", encoded, StringComparison.Ordinal);
            Assert.NotNull(actual);
            Assert.Equal(expected.Count, actual.Count);
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.Label, actual.Label);
            Assert.NotNull(actual.Nested);
            Assert.True(actual.Nested.Enabled);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void JsonDecoderRejectsTypeMetadataInsideGeneratedDto()
        {
            var payload = Encoding.UTF8.GetBytes(
                "{\"state\":{\"Count\":184,\"$type\":\"System.Version\"}}");

            var ok = FoxRunInboundJson.TryReadObject(
                payload,
                "state",
                out GeneratedJsonProbe _,
                out var error);

            Assert.False(ok);
            Assert.Contains("$type", error, StringComparison.Ordinal);
        }

        [Fact]
        public void JsonDecoderRejectsExcessiveNestingBeforeRecursiveScanOverflows()
        {
            var sb = new StringBuilder("{\"value\":");
            for (var i = 0; i < 40; i++)
                sb.Append('[');
            sb.Append('1');
            for (var i = 0; i < 40; i++)
                sb.Append(']');
            sb.Append('}');

            var ok = FoxRunInboundJson.TryRead(
                Encoding.UTF8.GetBytes(sb.ToString()),
                "value",
                out int _,
                out var error);

            Assert.False(ok);
            Assert.Contains("nesting", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void JsonDecoderConfiguresInitialTokenReaderDepthExplicitly()
        {
            var source = Unity.FoxgloveSDK.UnitTests.Harness.TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/"
                + "FoxRunInboundJson.cs");

            Assert.Contains("new JsonTextReader", source, StringComparison.Ordinal);
            Assert.Contains(
                "MaxDepth = MaxTypeHintScanDepth",
                source,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "JToken.Parse(json, LoadSettings)",
                source,
                StringComparison.Ordinal);
        }

        [Fact]
        public void JsonDecoderReadsGeneratedDecimalAndCharInputs()
        {
            var amountPayload = Encoding.UTF8.GetBytes("{\"amount\":12.5}");
            var keyPayload = Encoding.UTF8.GetBytes("{\"key\":\"A\"}");

            Assert.True(FoxRunInboundJson.TryRead(amountPayload, "amount", out decimal amount, out var decimalError), decimalError);
            Assert.True(FoxRunInboundJson.TryRead(keyPayload, "key", out char key, out var charError), charError);

            Assert.Equal(12.5m, amount);
            Assert.Equal('A', key);
        }

        [Fact]
        public void JsonDecoderRejectsMultiCharacterCharInputs()
        {
            var payload = Encoding.UTF8.GetBytes("{\"key\":\"AB\"}");

            var ok = FoxRunInboundJson.TryRead(payload, "key", out char _, out var error);

            Assert.False(ok);
            Assert.Contains("single character", error, StringComparison.Ordinal);
        }

        [Fact]
        public void RouterUsesGeneratedAllowlistAndRegistrationOrder()
        {
            var first = new RecordingInput("/phase157/cmd");
            var second = new RecordingInput("/phase157/cmd");
            var router = new FoxRunInputRouter();
            router.Register(first);
            router.Register(second);

            var result = router.Dispatch(
                "/phase157/cmd",
                Encoding.UTF8.GetBytes("{\"value\":4}"),
                "json",
                nowSeconds: 1);

            Assert.Equal(FoxRunInputDispatchStatus.Staged, result.Status);
            Assert.Equal(1, first.ApplyCount);
            Assert.Equal(1, second.ApplyCount);
        }

        [Fact]
        public void RouterStagesNewestValueUntilTheMainThreadFlush()
        {
            var input = new StagedRecordingInput("/phase183/staged");
            var router = new FoxRunInputRouter();
            router.Register(input);

            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase183/staged", new byte[] { 1 }, "json", nowSeconds: 1).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase183/staged", new byte[] { 2 }, "json", nowSeconds: 1.01).Status);
            Assert.Equal(0, input.AppliedCount);

            Assert.Equal(1, router.Flush(nowSeconds: 2, inheritedSubscribeRateHz: 60));
            Assert.Equal(1, input.AppliedCount);
            Assert.Equal(2, input.LastAppliedValue);
            Assert.Equal(0, router.Flush(nowSeconds: 3, inheritedSubscribeRateHz: 60));
        }

    }
}
