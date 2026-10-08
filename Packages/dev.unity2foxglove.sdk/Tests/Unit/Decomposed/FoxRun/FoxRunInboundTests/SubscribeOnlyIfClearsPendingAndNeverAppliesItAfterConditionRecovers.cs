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

    }
}
