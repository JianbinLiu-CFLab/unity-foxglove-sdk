// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Ros2ForUnity
// Purpose: Verify typed native subscription binding lifecycle and ownership.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Unity2Foxglove.Ros2ForUnity.Native;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Ros2ForUnity
{
    [Trait("Phase", "179-C")]
    [Trait("Domain", "Ros2NativeSubscriptionHost")]
    public sealed partial class FoxRunRos2SubscriptionHostTests
    {
        [Fact]
        public void ActiveSessionStateFailClosesBeforeAndAfterCapturedGeneration()
        {
            var state = new FoxRunRos2ActiveSessionState();

            Assert.Equal(-1, state.ReadGeneration());
            state.Activate(12);
            Assert.Equal(12, state.ReadGeneration());

            state.Deactivate();
            Assert.Equal(-1, state.ReadGeneration());
            state.Activate(13);
            Assert.Equal(13, state.ReadGeneration());
        }

        [Fact]
        public void NativeRuntimeAdmissionDoesNotRetainTheUnityHost()
        {
            var fields = typeof(FoxRunRos2NativeRuntimeAdmission).GetFields(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);

            Assert.DoesNotContain(
                fields,
                field => typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(field.FieldType));
            Assert.DoesNotContain(
                fields,
                field => field.FieldType == typeof(FoxRunRos2SubscriptionHub));
        }

        [Fact]
        public void DeniedBootstrapRetriesWithoutCreatingNativeStateAndShutdownStaysInert()
        {
            var retry = new FoxRunRos2BootstrapRetryState();

            Assert.False(retry.ShouldCreateHost(canBootstrap: false, shuttingDown: false));
            Assert.False(retry.HasCreatedHost);
            Assert.True(retry.ShouldCreateHost(canBootstrap: true, shuttingDown: false));
            Assert.True(retry.HasCreatedHost);
            Assert.False(retry.ShouldCreateHost(canBootstrap: true, shuttingDown: false));
            retry.RecordCreateFailed();
            Assert.False(retry.HasCreatedHost);
            Assert.True(retry.ShouldCreateHost(canBootstrap: true, shuttingDown: false));

            var shutdownRetry = new FoxRunRos2BootstrapRetryState();
            Assert.False(shutdownRetry.ShouldCreateHost(canBootstrap: true, shuttingDown: true));
            Assert.False(shutdownRetry.ShouldCreateHost(canBootstrap: false, shuttingDown: true));
            Assert.False(shutdownRetry.HasCreatedHost);
        }

        [Fact]
        public void NativeNodeRetriesAreBurstBoundedButEventuallyResume()
        {
            var retry = new FoxRunRos2BoundedRetryGate(4, 5.0);
            for (var i = 0; i < 4; i++)
            {
                Assert.True(retry.TryBegin(10.0));
                retry.RecordFailure(10.0);
            }
            Assert.False(retry.TryBegin(14.999));
            Assert.True(retry.TryBegin(15.0));
            retry.RecordSuccess();
            Assert.True(retry.TryBegin(15.0));
        }

        [Fact]
        public void ContractActivationRequiresCapturedNativeSubscribeCapability()
        {
            var nativePolicy = new Unity.FoxgloveSDK.Components.FoxRunSubscriptionSessionPolicy(
                12,
                true,
                new Unity.FoxgloveSDK.Components.FoxRunTransportId(
                    FoxRunRos2TransportProvider.IdValue),
                Unity.FoxgloveSDK.Components.FoxRunEncoding.Protobuf,
                Unity.FoxgloveSDK.Components.FoxRunDeliveryPolicy.ProviderDefault,
                120,
                20,
                64 * 1024);
            var inherited = Contract("inherit", "inherit");
            Assert.Equal(
                FoxRunRos2ContractActivationDisposition.Active,
                FoxRunRos2ContractActivation.Resolve(
                inherited,
                nativePolicy,
                FoxRunResolvedQos.SensorData,
                out var qos,
                out _,
                out var diagnostic));
            AssertResolvedQos(
                FoxRunResolvedQos.SensorData,
                qos);
            Assert.Equal(string.Empty, diagnostic);

            Assert.True(FoxRunRos2ContractActivation.TryResolve(
                Contract(
                    "inherit",
                    "inherit",
                    Unity.FoxgloveSDK.Components.FoxRunFlow.PublishAndSubscribe),
                nativePolicy,
                out _,
                out diagnostic));
            Assert.Equal(string.Empty, diagnostic);

            Assert.False(FoxRunRos2ContractActivation.TryResolve(
                Contract(
                    "inherit",
                    "inherit",
                    Unity.FoxgloveSDK.Components.FoxRunFlow.Publish),
                nativePolicy,
                out _,
                out diagnostic));
            Assert.Contains("Subscribe", diagnostic, StringComparison.Ordinal);

            Assert.False(FoxRunRos2ContractActivation.TryResolve(
                Contract("inherit", "inherit", supportsNative: false),
                nativePolicy,
                out _,
                out diagnostic));
            Assert.Contains("capability", diagnostic, StringComparison.OrdinalIgnoreCase);

            var disabled = new Unity.FoxgloveSDK.Components.FoxRunSubscriptionSessionPolicy(
                12,
                false,
                new Unity.FoxgloveSDK.Components.FoxRunTransportId(
                    FoxRunRos2TransportProvider.IdValue),
                Unity.FoxgloveSDK.Components.FoxRunEncoding.Protobuf,
                Unity.FoxgloveSDK.Components.FoxRunDeliveryPolicy.ProviderDefault,
                120,
                20,
                64 * 1024);
            Assert.False(FoxRunRos2ContractActivation.TryResolve(
                inherited,
                disabled,
                out _,
                out diagnostic));
            Assert.Contains("disabled", diagnostic, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LegacyStringContractSurfaceIsAbsent()
        {
            var contractType = typeof(FoxRunRos2GeneratedContract);

            Assert.Null(contractType.GetProperty("DeclaredSource"));
            Assert.Null(contractType.GetProperty("Ros2Qos"));
            foreach (var constructor in contractType.GetConstructors())
            {
                Assert.DoesNotContain(
                    constructor.GetParameters(),
                    parameter => string.Equals(
                        parameter.Name,
                        "ros2Qos",
                        StringComparison.Ordinal));
            }
        }

        [Fact]
        public void ContractActivationPermitsCompleteCustomNativePublishAndSubscribe()
        {
            var nativePolicy = new Unity.FoxgloveSDK.Components.FoxRunSubscriptionSessionPolicy(
                13,
                true,
                new Unity.FoxgloveSDK.Components.FoxRunTransportId(
                    FoxRunRos2TransportProvider.IdValue),
                Unity.FoxgloveSDK.Components.FoxRunEncoding.Protobuf,
                Unity.FoxgloveSDK.Components.FoxRunDeliveryPolicy.ProviderDefault,
                120,
                20,
                64 * 1024);

            var custom = CustomContract(
                FoxRunRos2RouteEndpoint.R2fu,
                Unity.FoxgloveSDK.Components.FoxRunEncoding.JSON);
            Assert.True(custom.HasCompleteCustomMetadata);
            Assert.True(FoxRunRos2ContractActivation.TryResolve(
                custom,
                nativePolicy,
                out var qos,
                out var error,
                out var diagnostic));
            AssertResolvedQos(
                FoxRunResolvedQos.Default,
                qos);
            Assert.Equal(FoxRunRos2RegistrationError.None, error);
            Assert.Equal(string.Empty, diagnostic);

            var withoutNativeProvider = CustomContract(
                FoxRunRos2RouteEndpoint.WebSocket,
                Unity.FoxgloveSDK.Components.FoxRunEncoding.JSON);
            Assert.False(FoxRunRos2ContractActivation.TryResolve(
                withoutNativeProvider,
                nativePolicy,
                out _,
                out _,
                out diagnostic));
            Assert.False(string.IsNullOrWhiteSpace(diagnostic));
        }

        [Fact]
        [Trait("Phase", "184-G")]
        public void NativeHubIgnoresFoxgloveOnlyContractsButStillRejectsInvalidSources()
        {
            var hub = new FoxRunRos2SubscriptionHub();
            var policy = new Unity.FoxgloveSDK.Components.FoxRunSubscriptionSessionPolicy(
                14,
                true,
                new Unity.FoxgloveSDK.Components.FoxRunTransportId(
                    FoxRunRos2TransportProvider.IdValue),
                Unity.FoxgloveSDK.Components.FoxRunEncoding.Protobuf,
                Unity.FoxgloveSDK.Components.FoxRunDeliveryPolicy.ProviderDefault,
                120,
                20,
                64 * 1024);
            var policyField = typeof(FoxRunRos2SubscriptionHub).GetField(
                "_policy",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(policyField);
            policyField.SetValue(hub, policy);

            var source = new NativeOnlySource();
            var sourceCandidateType = typeof(FoxRunRos2SubscriptionHub).GetNestedType(
                "SourceCandidate",
                BindingFlags.NonPublic);
            Assert.NotNull(sourceCandidateType);
            var sourceCandidate = Activator.CreateInstance(
                sourceCandidateType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { source, source, null },
                culture: null);
            var registrarType = typeof(FoxRunRos2SubscriptionHub).GetNestedType(
                "CollectingRegistrar",
                BindingFlags.NonPublic);
            Assert.NotNull(registrarType);
            var registrar = Assert.IsAssignableFrom<IFoxRunRos2SubscriptionRegistrar>(
                Activator.CreateInstance(
                    registrarType,
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    args: new[] { (object)hub, sourceCandidate },
                    culture: null));

            var foxglove = Contract("foxglove", "default", supportsNative: false);
            registrar.Register<FakeHostMessage>(
                foxglove,
                (message, _) => message,
                message => message.Dispose(),
                _ => { },
                _ => false,
                valuesEqual: null,
                consumeTrigger: null,
                canApply: null);
            var foxgloveStream = Contract("foxglove-stream", "default", supportsNative: false);
            registrar.RegisterStream<FakeHostMessage, FakeHostMessage>(
                foxgloveStream,
                tryAdmitInput: () => true,
                materializeOwned: (message, _) => message,
                transferOwned: _ => { },
                clearOwned: () => { });
            var invalid = Contract("invalid", "default");
            registrar.Register<FakeHostMessage>(
                invalid,
                (message, _) => message,
                message => message.Dispose(),
                _ => { },
                _ => false,
                valuesEqual: null,
                consumeTrigger: null,
                canApply: null);

            var seenField = typeof(FoxRunRos2SubscriptionHub).GetField(
                "_seenEndpoints",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(seenField);
            var seen = Assert.IsType<HashSet<string>>(seenField.GetValue(hub));
            Assert.Contains(source.GetInstanceID() + "|" + foxglove.Id, seen);
            Assert.Contains(source.GetInstanceID() + "|" + foxgloveStream.Id, seen);
            Assert.Contains(source.GetInstanceID() + "|" + invalid.Id, seen);

            var diagnosticsField = typeof(FoxRunRos2SubscriptionHub).GetField(
                "_diagnostics",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(diagnosticsField);
            var diagnostics = Assert.IsType<FoxRunRos2SubscriptionDiagnostics>(
                diagnosticsField.GetValue(hub));
            Assert.False(diagnostics.TryGet(
                source.GetInstanceID() + "|" + foxglove.Id,
                out _));
            Assert.False(diagnostics.TryGet(
                source.GetInstanceID() + "|" + foxgloveStream.Id,
                out _));
            Assert.True(diagnostics.TryGet(
                source.GetInstanceID() + "|" + invalid.Id,
                out var invalidSnapshot));
            Assert.Equal(
                FoxRunRos2SubscriptionBindingState.Unsupported,
                invalidSnapshot.State);
            Assert.Equal(
                FoxRunRos2RegistrationError.RegistrationRejected,
                invalidSnapshot.Error);
        }

        [Fact]
        public void ExplicitNativeContractDoesNotDependOnOutputOrManagerDefaultSource()
        {
            var policy = new Unity.FoxgloveSDK.Components.FoxRunSubscriptionSessionPolicy(
                15,
                true,
                Unity.FoxgloveSDK.Components.FoxgloveWebSocketTransport.TransportId,
                Unity.FoxgloveSDK.Components.FoxRunEncoding.JSON,
                Unity.FoxgloveSDK.Components.FoxRunDeliveryPolicy.ProviderDefault,
                120,
                60,
                64 * 1024);

            Assert.True(FoxRunRos2ContractActivation.TryResolve(
                Contract("ros2-native", "reliable"),
                policy,
                out var qos,
                out _));
            AssertResolvedQos(
                FoxRunResolvedQos.Default,
                qos);
        }

    }
}
#endif
