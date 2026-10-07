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
        public void RouterReportsFallbackDiagnosticWhenGeneratedInputRejectsSilently()
        {
            var input = new SilentRejectingInput("/phase187/silent-rejection");
            var router = new FoxRunInputRouter();
            router.Register(input);

            var result = router.Dispatch(
                "/phase187/silent-rejection",
                new byte[] { 1 },
                "json",
                nowSeconds: 1);

            Assert.Equal(FoxRunInputDispatchStatus.DecodeRejected, result.Status);
            Assert.Equal(0, result.StagedCount);
            Assert.Contains("rejected the payload without a diagnostic", result.Diagnostic,
                StringComparison.Ordinal);
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
        [Trait("Phase", "187-R2-E05")]
        public void InputHubReportsDiagnosticForPartialStaging()
        {
            var source = TestSources.Text(
                "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxgloveInputHub.cs");
            var dispatch = TestSources.ExtractMethod(
                source,
                "private void OnClientMessage(uint clientId, uint channelId, string topic, string encoding, byte[] payload)");

            Assert.Contains(
                "result.Status != FoxRunInputDispatchStatus.UnknownTopic",
                dispatch,
                StringComparison.Ordinal);
            Assert.Contains(
                "result.Status != FoxRunInputDispatchStatus.Staged",
                dispatch,
                StringComparison.Ordinal);
            Assert.Contains(
                "!string.IsNullOrEmpty(result.Diagnostic)",
                dispatch,
                StringComparison.Ordinal);
            Assert.True(
                dispatch.IndexOf("result.Status != FoxRunInputDispatchStatus.Staged", StringComparison.Ordinal)
                < dispatch.IndexOf("!string.IsNullOrEmpty(result.Diagnostic)", StringComparison.Ordinal),
                "The Staged branch must inspect its retained diagnostic before suppressing the warning.");
        }

    }
}
