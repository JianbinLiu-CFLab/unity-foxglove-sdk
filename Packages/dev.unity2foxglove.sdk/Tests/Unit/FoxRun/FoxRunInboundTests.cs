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
    public sealed class FoxRunInboundTests
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
            var payload = Encoding.UTF8.GetBytes("{\"amount\":12.5,\"key\":\"A\"}");

            Assert.True(FoxRunInboundJson.TryRead(payload, "amount", out decimal amount, out var decimalError), decimalError);
            Assert.True(FoxRunInboundJson.TryRead(payload, "key", out char key, out var charError), charError);

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

        [Fact]
        public void SubscribeOnlyIfClearsPendingAndNeverAppliesItAfterConditionRecovers()
        {
            var compilation = CSharpCompilation.Create(
                "Phase184ConditionalInput_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class ConditionalInput
    {
        public bool Enabled;
        public int ConditionEvaluationCount;

        private bool CanApply()
        {
            ConditionEvaluationCount++;
            return Enabled;
        }

        [FoxRun(""/phase184/conditional"", Mode = Subscribe,
            Encoding = FoxRunEncoding.JSON, Policy = FoxRunPolicy.Change,
            OnlyIf = nameof(CanApply))]
        public int Value;
    }
}")
                },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
            using var image = new MemoryStream();
            var emit = output.Emit(image);

            Assert.True(
                emit.Success,
                "Conditional input fixture failed to compile: " +
                string.Join("; ", emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var receiverType = assembly.GetType("Demo.ConditionalInput", throwOnError: true);
            var receiver = Activator.CreateInstance(receiverType);
            var input = Assert.IsAssignableFrom<IFoxgloveInputSource>(receiver);
            var enabled = receiverType.GetField("Enabled");
            var value = receiverType.GetField("Value");
            var conditionEvaluationCount = receiverType.GetField("ConditionEvaluationCount");

            Assert.NotNull(enabled);
            Assert.NotNull(value);
            Assert.NotNull(conditionEvaluationCount);
            Assert.Equal(1, input.FoxgloveInput_TopicCount);
            Assert.True(input.FoxgloveInput_TryStage(
                0,
                Encoding.UTF8.GetBytes("{\"Value\":1}"),
                "json",
                out var firstError), firstError);
            Assert.Equal(0, input.FoxgloveInput_Flush(1d, 60));
            Assert.Equal(0, value.GetValue(receiver));
            Assert.Equal(1, conditionEvaluationCount.GetValue(receiver));
            Assert.Equal(1, input.FoxgloveInput_TopicCount);

            enabled.SetValue(receiver, true);
            Assert.Equal(0, input.FoxgloveInput_Flush(2d, 60));
            Assert.Equal(0, value.GetValue(receiver));
            Assert.Equal(1, conditionEvaluationCount.GetValue(receiver));

            Assert.True(input.FoxgloveInput_TryStage(
                0,
                Encoding.UTF8.GetBytes("{\"Value\":2}"),
                "json",
                out var secondError), secondError);
            Assert.Equal(1, input.FoxgloveInput_Flush(3d, 60));
            Assert.Equal(2, value.GetValue(receiver));
            Assert.Equal(2, conditionEvaluationCount.GetValue(receiver));

            enabled.SetValue(receiver, false);
            Assert.True(input.FoxgloveInput_TryStage(
                0,
                Encoding.UTF8.GetBytes("{\"Value\":2}"),
                "json",
                out var rejectedDuplicateError), rejectedDuplicateError);
            Assert.Equal(0, input.FoxgloveInput_Flush(4d, 60));
            Assert.Equal(3, conditionEvaluationCount.GetValue(receiver));

            enabled.SetValue(receiver, true);
            Assert.True(input.FoxgloveInput_TryStage(
                0,
                Encoding.UTF8.GetBytes("{\"Value\":2}"),
                "json",
                out var recoveredDuplicateError), recoveredDuplicateError);
            Assert.Equal(1, input.FoxgloveInput_Flush(5d, 60));
            Assert.Equal(2, value.GetValue(receiver));
            Assert.Equal(4, conditionEvaluationCount.GetValue(receiver));
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        [Fact]
        public void FullDuplexRemoteApplyBlocksScheduledHeartbeatUntilLocalMutationButExplicitTriggerBypassesIt()
        {
            var compilation = CSharpCompilation.Create(
                "Phase184FullDuplexOrigin_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(@"
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunFlow;

namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}

namespace Demo
{
    public partial class FullDuplexOrigin
    {
        [FoxRun(""/phase184/full-duplex-origin"", Mode = PublishAndSubscribe,
            Encoding = FoxRunEncoding.JSON, Policy = FoxRunPolicy.Change,
            Hz = 10f)]
        public int Value;
    }
}")
                },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
            using var image = new MemoryStream();
            var emit = output.Emit(image);

            Assert.True(
                emit.Success,
                "Full-duplex fixture failed to compile: " +
                string.Join("; ", emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var receiverType = assembly.GetType("Demo.FullDuplexOrigin", throwOnError: true);
            var receiver = Activator.CreateInstance(receiverType);
            var input = Assert.IsAssignableFrom<IFoxgloveInputSource>(receiver);
            var policy = Assert.IsAssignableFrom<IFoxgloveLogPolicySource>(receiver);
            var origin = Assert.IsAssignableFrom<IFoxglovePublishOriginSource>(receiver);
            var value = receiverType.GetField("Value");
            Assert.NotNull(value);

            value.SetValue(receiver, 4);
            Assert.True(origin.FoxgloveLog_CanPublishOrigin(0, explicitTrigger: false));
            Assert.True(policy.FoxgloveLog_ShouldPublish(0, 0d));
            policy.FoxgloveLog_MarkPublished(0, 0d);

            Assert.True(input.FoxgloveInput_TryStage(
                0,
                Encoding.UTF8.GetBytes("{\"Value\":7}"),
                "json",
                out var error), error);
            Assert.Equal(1, input.FoxgloveInput_Flush(1d, 60));
            Assert.Equal(7, value.GetValue(receiver));

            Assert.False(origin.FoxgloveLog_CanPublishOrigin(0, explicitTrigger: false));
            Assert.False(origin.FoxgloveLog_CanPublishOrigin(0, explicitTrigger: false));

            // Returning to the previously published local value is still a
            // new local ownership claim. Origin release must invalidate the
            // Change-policy snapshot before ShouldPublish evaluates it.
            value.SetValue(receiver, 4);
            Assert.True(origin.FoxgloveLog_CanPublishOrigin(0, explicitTrigger: false));
            Assert.True(policy.FoxgloveLog_ShouldPublish(0, 0.01d));
            policy.FoxgloveLog_MarkPublished(0, 0.01d);

            Assert.True(input.FoxgloveInput_TryStage(
                0,
                Encoding.UTF8.GetBytes("{\"Value\":9}"),
                "json",
                out error), error);
            Assert.Equal(1, input.FoxgloveInput_Flush(3d, 60));
            Assert.True(origin.FoxgloveLog_CanPublishOrigin(0, explicitTrigger: true));
        }

#endif

        [Fact]
        public void RouterRejectsUnknownOversizedAndRateLimitedMessages()
        {
            var input = new RecordingInput("/phase157/cmd");
            var router = new FoxRunInputRouter(maxPayloadBytes: 16, maxMessagesPerSecondPerTopic: 1);
            router.Register(input);

            Assert.Equal(
                FoxRunInputDispatchStatus.UnknownTopic,
                router.Dispatch("/other", Array.Empty<byte>(), "json", 0).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.PayloadTooLarge,
                router.Dispatch("/phase157/cmd", new byte[17], "json", 0).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase157/cmd", Encoding.UTF8.GetBytes("{}"), "json", 1).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.RateLimited,
                router.Dispatch("/phase157/cmd", Encoding.UTF8.GetBytes("{}"), "json", 1.1).Status);
        }

        [Fact]
        public void RouterUnregisterStopsAssignment()
        {
            var input = new RecordingInput("/phase157/cmd");
            var router = new FoxRunInputRouter();
            router.Register(input);
            router.Unregister(input);

            var result = router.Dispatch("/phase157/cmd", Array.Empty<byte>(), "json", 1);

            Assert.Equal(FoxRunInputDispatchStatus.UnknownTopic, result.Status);
            Assert.Equal(0, input.ApplyCount);
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void RouterUnregisterClearsOptionalOwnedInputExactlyOncePerRegistrationLifetime()
        {
            var input = new OwnedRecordingInput("/phase184/stream");
            var router = new FoxRunInputRouter();
            router.Register(input);

            router.Unregister(input);
            router.Unregister(input);

            Assert.Equal(1, input.ClearCount);
            Assert.Equal(
                FoxRunInputDispatchStatus.UnknownTopic,
                router.Dispatch("/phase184/stream", Array.Empty<byte>(), "json", 1).Status);
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public async Task RouterUnregisterClosesIngressThenWaitsBeforeClearingOwnedStream()
        {
            var input = new BlockingOwnedStreamInput("/phase184/stream-race");
            var router = new FoxRunInputRouter();
            router.Register(input);

            var dispatch = Task.Run(() => router.Dispatch(
                "/phase184/stream-race",
                new byte[] { 7 },
                "json",
                nowSeconds: 1d));
            Assert.True(await Task.Run(() => input.StageEntered.Wait(TimeSpan.FromSeconds(5))));
            var unregister = Task.Factory.StartNew(
                () => router.Unregister(input),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            var firstCompletion = await Task.WhenAny(
                unregister,
                Task.Delay(TimeSpan.FromMilliseconds(250)));
            var unregisterWaitedForDispatch = !ReferenceEquals(firstCompletion, unregister);
            input.FinishStage.Set();
            var result = await dispatch.WaitAsync(TimeSpan.FromSeconds(5));
            await unregister.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(unregisterWaitedForDispatch);
            Assert.Equal(FoxRunInputDispatchStatus.Staged, result.Status);
            Assert.Equal(0, input.Stream.Count);
            Assert.Equal(1, input.DisposedCount);
            Assert.Equal(1, input.ClearCount);
        }



        [Fact]
        [Trait("Phase", "184-E")]
        public void FailedTopicEnumerationEndsLifetimeWithoutAcquiringOwnedMembers()
        {
            var input = new ThrowingSecondTopicOwnedInput();
            var router = new FoxRunInputRouter();

            Assert.Throws<InvalidOperationException>(() => router.Register(input));
            Exception unregisterFailure = null;
            var unregister = new Thread(() =>
            {
                try
                {
                    router.Unregister(input);
                }
                catch (Exception exception)
                {
                    unregisterFailure = exception;
                }
            }) { IsBackground = true };
            unregister.Start();

            Assert.True(unregister.Join(TimeSpan.FromSeconds(2)));
            Assert.Null(unregisterFailure);
            Assert.Equal(0, input.ClearCount);
            Assert.Equal(
                FoxRunInputDispatchStatus.UnknownTopic,
                router.Dispatch("/phase184/partial", Array.Empty<byte>(), "json", 1d).Status);
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void FailedSecondOwnershipAcquisitionClearsTheFirstMemberExactlyOnce()
        {
            var input = new ThrowingSecondAcquireOwnedInput();
            var router = new FoxRunInputRouter();

            Assert.Throws<InvalidOperationException>(() => router.Register(input));
            router.Unregister(input);

            Assert.Equal(1, input.FirstClearCount);
            Assert.Equal(0, input.SecondClearCount);
            Assert.Equal(
                FoxRunInputDispatchStatus.UnknownTopic,
                router.Dispatch("/phase184/acquire-first", Array.Empty<byte>(), "json", 1d).Status);
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void GeneratedWebSocketStreamFreezesRegisteredInstanceUntilOwnedClear()
        {
            var compilation = CSharpCompilation.Create(
                "Phase184WebStreamFreeze_" + Guid.NewGuid().ToString("N"),
                new[]
                {
                    CSharpSyntaxTree.ParseText(@"
using Unity.FoxgloveSDK.Components;
namespace UnityEngine.Scripting
{
    [System.AttributeUsage(System.AttributeTargets.All)]
    public sealed class PreserveAttribute : System.Attribute { }
}
namespace Demo
{
    public partial class StreamReceiver
    {
        [FoxRun(""/phase184/frozen-stream"", Mode = FoxRunFlow.Subscribe,
            Encoding = FoxRunEncoding.JSON)]
        public FoxRunStream<int> Stream = new FoxRunStream<int>();
    }
}")
                },
                DynamicCompilationReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new FoxgloveLogSourceGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
            using var image = new MemoryStream();
            var emit = output.Emit(image);
            Assert.True(
                emit.Success,
                "Stream freeze fixture failed to compile: "
                + string.Join("; ", emit.Diagnostics.Select(diagnostic => diagnostic.ToString())));

            image.Position = 0;
            var assembly = AssemblyLoadContext.Default.LoadFromStream(image);
            var receiverType = assembly.GetType("Demo.StreamReceiver", throwOnError: true);
            var receiver = Activator.CreateInstance(receiverType);
            var input = Assert.IsAssignableFrom<IFoxgloveInputSource>(receiver);
            var owned = Assert.IsAssignableFrom<IFoxgloveOwnedInputSource>(receiver);
            var field = receiverType.GetField("Stream");
            Assert.NotNull(field);
            var first = Assert.IsType<FoxRunStream<int>>(field.GetValue(receiver));
            var second = new FoxRunStream<int>();

            Assert.True(owned.FoxgloveInput_TryAcquireOwned(0, out var validationError), validationError);
            field.SetValue(receiver, second);
            Assert.True(input.FoxgloveInput_TryStage(
                0,
                Encoding.UTF8.GetBytes("{\"Stream\":7}"),
                "json",
                out var stageError), stageError);

            Assert.Equal(1, first.Count);
            Assert.Equal(0, second.Count);
            owned.FoxgloveInput_ClearOwned(0);
            Assert.Equal(0, first.Count);
            Assert.Equal(0, second.Count);

            Assert.True(owned.FoxgloveInput_TryAcquireOwned(0, out validationError), validationError);
            Assert.True(input.FoxgloveInput_TryStage(
                0,
                Encoding.UTF8.GetBytes("{\"Stream\":8}"),
                "json",
                out stageError), stageError);
            Assert.Equal(1, second.Count);
            owned.FoxgloveInput_ClearOwned(0);
            Assert.Equal(0, second.Count);
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void StreamRegistrationUsesItsOwnAdmissionCeilingInsteadOfOrdinaryRouterLimit()
        {
            var input = new OwnedRecordingInput("/phase184/stream-rate");
            var router = new FoxRunInputRouter(maxMessagesPerSecondPerTopic: 1);
            router.Register(input);

            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase184/stream-rate", Array.Empty<byte>(), "json", 1d).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase184/stream-rate", Array.Empty<byte>(), "json", 1.1d).Status);
            Assert.Equal(2, input.StageCount);
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void MixedTopicKeepsOrdinaryRateLimitWhileStreamUsesItsOwnCeiling()
        {
            const string topic = "/phase184/mixed-rate";
            var ordinary = new RecordingInput(topic);
            var stream = new OwnedRecordingInput(topic);
            var router = new FoxRunInputRouter(maxMessagesPerSecondPerTopic: 1);
            router.Register(ordinary);
            router.Register(stream);

            var first = router.Dispatch(topic, Array.Empty<byte>(), "json", 1d);
            var second = router.Dispatch(topic, Array.Empty<byte>(), "json", 1.1d);

            Assert.Equal(FoxRunInputDispatchStatus.Staged, first.Status);
            Assert.Equal(2, first.StagedCount);
            Assert.Equal(FoxRunInputDispatchStatus.Staged, second.Status);
            Assert.Equal(1, second.StagedCount);
            Assert.Equal(1, ordinary.ApplyCount);
            Assert.Equal(2, stream.StageCount);
        }

        [Fact]
        [Trait("Phase", "184-E")]
        public void NullOwnedInputFailsBeforeRouterRegistration()
        {
            var input = new OwnedRecordingInput("/phase184/null-stream", ready: false);
            var diagnostics = new List<string>();
            var router = new FoxRunInputRouter();

            router.Register(input, diagnostics.Add);

            Assert.Equal(
                FoxRunInputDispatchStatus.UnknownTopic,
                router.Dispatch("/phase184/null-stream", Array.Empty<byte>(), "json", 1d).Status);
            Assert.Contains("initialized before registration", Assert.Single(diagnostics), StringComparison.Ordinal);
        }

        [Fact]
        public void RouterResolvesInheritedInputAgainstTheCurrentSubscriptionDefault()
        {
            var input = new InheritedRecordingInput("/phase175/inherit");
            var router = new FoxRunInputRouter();
            router.Register(input);

            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase175/inherit", Array.Empty<byte>(), "protobuf", 1).Status);
            Assert.Equal(
                FoxRunInputDispatchStatus.DecodeRejected,
                router.Dispatch("/phase175/inherit", Array.Empty<byte>(), "json", 2).Status);
            Assert.Equal(1, input.ApplyCount);

            router.DefaultSubscriptionEncoding = FoxRunEncoding.JSON;

            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch("/phase175/inherit", Array.Empty<byte>(), "json", 3).Status);
            Assert.Equal(2, input.ApplyCount);
        }

        [Fact]
        public void RouterEncodingMismatchNamesExpectedAndClientAdvertisedEncodings()
        {
            var input = new InheritedRecordingInput("/phase175/protobuf");
            var router = new FoxRunInputRouter();
            router.Register(input);

            var result = router.Dispatch("/phase175/protobuf", Array.Empty<byte>(), "json", 1);

            Assert.Equal(FoxRunInputDispatchStatus.DecodeRejected, result.Status);
            Assert.Contains("expected \"protobuf\"", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("client advertised \"json\"", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RouterRejectsWrongEncodingBeforeItConsumesTheTopicRateQuota()
        {
            var input = new RecordingInput("/phase182/encoding");
            var router = new FoxRunInputRouter(maxMessagesPerSecondPerTopic: 1);
            router.Register(input);

            var wrongEncoding = router.Dispatch(
                "/phase182/encoding",
                Encoding.UTF8.GetBytes("{\"value\":1}"),
                "protobuf",
                nowSeconds: 1);
            var matchingEncoding = router.Dispatch(
                "/phase182/encoding",
                Encoding.UTF8.GetBytes("{\"value\":2}"),
                "json",
                nowSeconds: 1.1);
            var rateLimited = router.Dispatch(
                "/phase182/encoding",
                Encoding.UTF8.GetBytes("{\"value\":3}"),
                "json",
                nowSeconds: 1.2);

            Assert.Equal(FoxRunInputDispatchStatus.DecodeRejected, wrongEncoding.Status);
            Assert.Equal(FoxRunInputDispatchStatus.Staged, matchingEncoding.Status);
            Assert.Equal(FoxRunInputDispatchStatus.RateLimited, rateLimited.Status);
            Assert.Equal(1, input.ApplyCount);
        }

        [Fact]
        public void RouterConsumesOneQuotaAndAppliesOnlyMatchingSharedTopicRegistrations()
        {
            var json = new RecordingInput("/phase182/shared");
            var protobuf = new InheritedRecordingInput("/phase182/shared");
            var router = new FoxRunInputRouter(maxMessagesPerSecondPerTopic: 1);
            router.Register(json);
            router.Register(protobuf);

            var matching = router.Dispatch(
                "/phase182/shared",
                Encoding.UTF8.GetBytes("{\"value\":1}"),
                "protobuf",
                nowSeconds: 1);
            var rateLimited = router.Dispatch(
                "/phase182/shared",
                Encoding.UTF8.GetBytes("{\"value\":2}"),
                "protobuf",
                nowSeconds: 1.1);

            Assert.Equal(FoxRunInputDispatchStatus.Staged, matching.Status);
            Assert.Equal(0, json.ApplyCount);
            Assert.Equal(1, protobuf.ApplyCount);
            Assert.Equal(FoxRunInputDispatchStatus.RateLimited, rateLimited.Status);
        }

        [Theory]
        [InlineData("ros2")]
        [InlineData("cdr")]
        public void RouterRejectsNativeAdvertisedEncodingWithoutApplyingSource(string encoding)
        {
            var input = new RecordingInput("/phase179/websocket-only");
            var router = new FoxRunInputRouter();
            router.Register(input);

            var result = router.Dispatch(
                "/phase179/websocket-only",
                Encoding.UTF8.GetBytes("{\"value\":4}"),
                encoding,
                nowSeconds: 1);

            Assert.Equal(FoxRunInputDispatchStatus.DecodeRejected, result.Status);
            Assert.Equal(0, input.ApplyCount);
            Assert.Contains("client advertised \"" + encoding + "\"", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void InputHubSafelyRebindsSessionPolicyAndAppliesTheCurrentSnapshotImmediately()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveInputHub.cs");
            var setManager = TestSources.ExtractMethod(source, "private void SetManager(FoxgloveManager manager)");
            var unsubscribeIndex = setManager.IndexOf(
                "_manager.FoxRunSubscriptionSessionChanged -= OnFoxRunSubscriptionSessionChanged;",
                StringComparison.Ordinal);
            var assignIndex = setManager.IndexOf("_manager = manager;", StringComparison.Ordinal);
            var subscribeIndex = setManager.IndexOf(
                "_manager.FoxRunSubscriptionSessionChanged += OnFoxRunSubscriptionSessionChanged;",
                StringComparison.Ordinal);
            var applyIndex = setManager.IndexOf("ApplyManagerPolicy();", StringComparison.Ordinal);

            Assert.True(unsubscribeIndex >= 0, "SetManager must unsubscribe the previous Manager session event.");
            Assert.True(assignIndex >= 0, "SetManager must assign the new Manager.");
            Assert.True(subscribeIndex >= 0, "SetManager must subscribe the new Manager session event.");
            Assert.True(applyIndex >= 0, "SetManager must immediately apply the current session snapshot.");
            Assert.True(unsubscribeIndex < assignIndex, "Unsubscribe must happen before Manager assignment.");
            Assert.True(assignIndex < subscribeIndex, "Manager assignment must happen before subscription.");
            Assert.True(subscribeIndex < applyIndex, "Subscription must happen before the current snapshot is applied.");

            var onDisable = TestSources.ExtractMethod(source, "private void OnDisable()");
            var onDestroy = TestSources.ExtractMethod(source, "private void OnDestroy()");
            Assert.Contains("SetManager(null);", onDisable, StringComparison.Ordinal);
            Assert.Contains("SetManager(null);", onDestroy, StringComparison.Ordinal);
        }



        [Fact]
        public void InputHubRefreshesSessionPolicyBeforeFirstMessageDispatch()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveInputHub.cs");
            var dispatch = TestSources.ExtractMethod(
                source,
                "private void OnClientMessage(uint clientId, uint channelId, string topic, string encoding, byte[] payload)");
            var refreshIndex = dispatch.IndexOf("ApplyManagerPolicy();", StringComparison.Ordinal);
            var enabledIndex = dispatch.IndexOf("if (!_subscriptionsEnabled)", StringComparison.Ordinal);

            Assert.True(refreshIndex >= 0, "Dispatch must refresh the current session snapshot.");
            Assert.True(enabledIndex > refreshIndex, "Snapshot refresh must happen before the enabled-state gate.");
            Assert.DoesNotContain("EnableFoxRunInbound", dispatch, StringComparison.Ordinal);
            Assert.Contains("IsFoxRunInboundAuthorized", dispatch, StringComparison.Ordinal);
        }





        [Fact]
        public void RouterIsolatesAssignmentExceptionsAndContinuesInRegistrationOrder()
        {
            var throwing = new ThrowingInput("/phase157/cmd");
            var recording = new RecordingInput("/phase157/cmd");
            var router = new FoxRunInputRouter();
            router.Register(throwing);
            router.Register(recording);

            var result = router.Dispatch(
                "/phase157/cmd",
                Encoding.UTF8.GetBytes("{\"value\":4}"),
                "json",
                nowSeconds: 1);

            Assert.Equal(FoxRunInputDispatchStatus.Staged, result.Status);
            Assert.Equal(1, result.StagedCount);
            Assert.Contains("staging failed", result.Diagnostic, StringComparison.Ordinal);
            Assert.Equal(1, recording.ApplyCount);
        }

        [Fact]
        public void RouterReportsFlushExceptionsWithoutBlockingHealthySources()
        {
            var throwing = new ThrowingFlushInput("/phase184/throwing");
            var healthy = new StagedRecordingInput("/phase184/healthy");
            var diagnostics = new List<string>();
            var router = new FoxRunInputRouter();
            router.Register(throwing);
            router.Register(healthy);
            Assert.Equal(
                FoxRunInputDispatchStatus.Staged,
                router.Dispatch(
                    "/phase184/healthy",
                    new byte[] { 7 },
                    "json",
                    nowSeconds: 1).Status);

            Assert.Equal(
                1,
                router.Flush(
                    nowSeconds: 2,
                    inheritedSubscribeRateHz: 60,
                    reportApplyFailure: diagnostics.Add));

            var diagnostic = Assert.Single(diagnostics);
            Assert.Contains(nameof(ThrowingFlushInput), diagnostic, StringComparison.Ordinal);
            Assert.Contains(nameof(InvalidOperationException), diagnostic, StringComparison.Ordinal);
            Assert.Equal(7, healthy.LastAppliedValue);
        }

        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("127.20.30.40")]
        [InlineData("localhost")]
        [InlineData("::1")]
        public void InboundAuthorizationAllowsEnabledLoopback(string host)
        {
            Assert.True(FoxRunInboundAuthorization.IsRemoteInboundPolicyMet(
                true,
                host,
                false,
                "",
                out var diagnostic));
            Assert.Empty(diagnostic);
        }

        [Fact]
        public void InboundAuthorizationFailsClosedForRemoteWithoutExplicitTokenPolicy()
        {
            Assert.False(FoxRunInboundAuthorization.IsRemoteInboundPolicyMet(
                true,
                "0.0.0.0",
                false,
                "secret",
                out var noOptIn));
            Assert.False(FoxRunInboundAuthorization.IsRemoteInboundPolicyMet(
                true,
                "0.0.0.0",
                true,
                "",
                out var noToken));
            Assert.Contains("explicitly enabled", noOptIn, StringComparison.Ordinal);
            Assert.Contains("shared token", noToken, StringComparison.Ordinal);
        }

        [Fact]
        public void InboundAuthorizationRequiresMatchingRemoteTokenWhenTokenIsAvailable()
        {
            Assert.False(FoxRunInboundAuthorization.IsAuthorized(
                true,
                "0.0.0.0",
                true,
                "secret",
                "wrong",
                out var mismatch));
            Assert.True(FoxRunInboundAuthorization.IsAuthorized(
                true,
                "0.0.0.0",
                true,
                "secret",
                "secret",
                out var diagnostic));

            Assert.Contains("token", mismatch, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(diagnostic);
        }

        [Fact]
        public void RouterDispatchUsesRegistrationSnapshotWithoutPerMessageArrayCopy()
        {
            var source = TestSources.Text("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunInputRouter.cs");
            var dispatch = TestSources.Slice(
                source,
                "public FoxRunInputDispatchResult Dispatch",
                "        private void AddSourceSnapshotEntry");

            Assert.Contains("Dictionary<string, Registration[]> _registrationSnapshots", source, StringComparison.Ordinal);
            Assert.DoesNotContain(".ToArray()", dispatch, StringComparison.Ordinal);
        }

        private sealed class RecordingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public RecordingInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, "json", FoxRunFlow.Subscribe);
            }

            public int ApplyCount { get; private set; }
            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                ApplyCount++;
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
        }

        private sealed class OwnedRecordingInput : IFoxgloveInputSource, IFoxgloveOwnedInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;
            private readonly bool _ready;

            public OwnedRecordingInput(string topic, bool ready = true)
            {
                _topic = InputTopic(
                    topic,
                    FoxRunEncoding.JSON,
                    isStream: true);
                _ready = ready;
            }

            public int ClearCount { get; private set; }
            public int StageCount { get; private set; }
            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(
                int topicIndex,
                byte[] payload,
                string encoding,
                out string error)
            {
                StageCount++;
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
            public bool FoxgloveInput_TryAcquireOwned(int topicIndex, out string error)
            {
                error = _ready
                    ? string.Empty
                    : "FoxRunStream field must be initialized before registration.";
                return _ready;
            }
            public void FoxgloveInput_ClearOwned(int topicIndex) => ClearCount++;
        }

        private sealed class BlockingOwnedStreamInput : IFoxgloveInputSource, IFoxgloveOwnedInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public BlockingOwnedStreamInput(string topic)
            {
                _topic = InputTopic(
                    topic,
                    FoxRunEncoding.JSON,
                    isStream: true);
                Stream = new FoxRunStream<byte>();
            }

            public ManualResetEventSlim StageEntered { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim FinishStage { get; } = new ManualResetEventSlim();
            public FoxRunStream<byte> Stream { get; }
            public int ClearCount;
            public int DisposedCount;
            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(
                int topicIndex,
                byte[] payload,
                string encoding,
                out string error)
            {
                StageEntered.Set();
                if (!FinishStage.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The teardown race did not release staging.");
                Stream.TryEnqueueOwned(
                    payload[0],
                    _ => Interlocked.Increment(ref DisposedCount));
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
            public bool FoxgloveInput_TryAcquireOwned(int topicIndex, out string error)
            {
                error = string.Empty;
                return true;
            }

            public void FoxgloveInput_ClearOwned(int topicIndex)
            {
                Interlocked.Increment(ref ClearCount);
                Stream.Clear();
            }
        }

        private sealed class ThrowingSecondTopicOwnedInput : IFoxgloveInputSource, IFoxgloveOwnedInputSource
        {
            private readonly FoxgloveInputTopicInfo _first = InputTopic(
                "/phase184/partial",
                FoxRunEncoding.JSON,
                isStream: true);

            public int ClearCount { get; private set; }
            public int FoxgloveInput_TopicCount => 2;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index)
                => index == 0 ? _first : throw new InvalidOperationException("second topic failed");
            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                error = string.Empty;
                return true;
            }
            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
            public bool FoxgloveInput_TryAcquireOwned(int topicIndex, out string error)
            {
                error = string.Empty;
                return true;
            }
            public void FoxgloveInput_ClearOwned(int topicIndex) => ClearCount++;
        }

        private sealed class ThrowingSecondAcquireOwnedInput : IFoxgloveInputSource, IFoxgloveOwnedInputSource
        {
            private readonly FoxgloveInputTopicInfo[] _topics =
            {
                InputTopic(
                    "/phase184/acquire-first",
                    FoxRunEncoding.JSON,
                    isStream: true),
                InputTopic(
                    "/phase184/acquire-second",
                    FoxRunEncoding.JSON,
                    isStream: true),
            };

            public int FirstClearCount { get; private set; }
            public int SecondClearCount { get; private set; }
            public int FoxgloveInput_TopicCount => _topics.Length;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topics[index];
            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                error = string.Empty;
                return true;
            }
            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
            public bool FoxgloveInput_TryAcquireOwned(int topicIndex, out string error)
            {
                if (topicIndex == 1)
                    throw new InvalidOperationException("second ownership acquisition failed");
                error = string.Empty;
                return true;
            }
            public void FoxgloveInput_ClearOwned(int topicIndex)
            {
                if (topicIndex == 0)
                    FirstClearCount++;
                else if (topicIndex == 1)
                    SecondClearCount++;
            }
        }

        private static MetadataReference[] DynamicCompilationReferences()
        {
            var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                                    ?? string.Empty;
            return trustedAssemblies
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Append(typeof(FoxRunAttribute).Assembly.Location)
                .Append(typeof(UnityEngine.Vector3).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToArray();
        }


        private sealed class StagedRecordingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;
            private bool _hasPending;
            private byte _pending;

            public StagedRecordingInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, "json", FoxRunFlow.Subscribe);
            }

            public int AppliedCount { get; private set; }
            public byte LastAppliedValue { get; private set; }
            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                _pending = payload[0];
                _hasPending = true;
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz)
            {
                if (!_hasPending)
                    return 0;

                LastAppliedValue = _pending;
                AppliedCount++;
                _hasPending = false;
                return 1;
            }
        }

        private sealed class ThrowingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public ThrowingInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, "json", FoxRunFlow.Subscribe);
            }

            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                throw new InvalidOperationException("staging failed");
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
        }

        private sealed class ThrowingFlushInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public ThrowingFlushInput(string topic)
            {
                _topic = new FoxgloveInputTopicInfo(topic, "json", FoxRunFlow.Subscribe);
            }

            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(
                int topicIndex,
                byte[] payload,
                string encoding,
                out string error)
            {
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz)
                => throw new InvalidOperationException("apply failed");
        }

        private sealed class InheritedRecordingInput : IFoxgloveInputSource
        {
            private readonly FoxgloveInputTopicInfo _topic;

            public InheritedRecordingInput(string topic)
            {
                _topic = InputTopic(
                    topic,
                    (FoxRunEncoding)0,
                    hasExplicitEncoding: false);
            }

            public int ApplyCount { get; private set; }
            public int FoxgloveInput_TopicCount => 1;
            public FoxgloveInputTopicInfo FoxgloveInput_GetTopic(int index) => _topic;

            public bool FoxgloveInput_TryStage(int topicIndex, byte[] payload, string encoding, out string error)
            {
                ApplyCount++;
                error = string.Empty;
                return true;
            }

            public int FoxgloveInput_Flush(double nowSeconds, int inheritedSubscribeRateHz) => 0;
        }

        private static byte[] ClientMessageFrame(uint channelId, byte[] payload)
        {
            var frame = new byte[5 + payload.Length];
            frame[0] = ClientOpcode.MessageData;
            BinaryEncoding.WriteU32LE(frame, 1, channelId);
            Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);
            return frame;
        }

        private static FoxgloveInputTopicInfo InputTopic(
            string topic,
            FoxRunEncoding encoding,
            bool hasExplicitEncoding = true,
            bool isStream = false)
            => new(
                topic,
                encoding,
                FoxRunFlow.Subscribe,
                publishTransportIds: null,
                subscribeTransportId:
                    FoxgloveWebSocketTransport.Id,
                hasExplicitEncoding: hasExplicitEncoding,
                supportsWebSocket: true,
                deliveryPolicy:
                    FoxRunDeliveryPolicy.ProviderDefault,
                hasExplicitDeliveryPolicy: false,
                isStream: isStream);

        private enum GeneratedJsonProbeKind
        {
            Inactive,
            Active
        }

        private sealed class GeneratedJsonNestedProbe
        {
            public bool Enabled { get; set; }
        }

        private sealed class GeneratedJsonProbe
        {
            public int Count { get; set; }
            public GeneratedJsonProbeKind Kind { get; set; }
            public string Label { get; set; }
            public GeneratedJsonNestedProbe Nested { get; set; }
        }

        private sealed class RestartInputTransport : IFoxgloveTransport
        {
            public bool IsRunning { get; private set; }
            public event Action<uint> OnClientConnected { add { } remove { } }
            public event Action<uint> OnClientDisconnected { add { } remove { } }
            public event Action<uint, string> OnTextReceived;
            public event Action<uint, byte[]> OnBinaryReceived;

            public void Start(string host, int port) => IsRunning = true;
            public void Stop() => IsRunning = false;
            public void Dispose() { }
            public void BroadcastText(string json) { }
            public void BroadcastBinary(byte[] data) { }
            public void SendText(uint clientId, string json) { }
            public void SendBinary(uint clientId, byte[] data) { }
            public void ReceiveText(uint clientId, string json) => OnTextReceived?.Invoke(clientId, json);
            public void ReceiveBinary(uint clientId, byte[] data) => OnBinaryReceived?.Invoke(clientId, data);
        }
    }
}
