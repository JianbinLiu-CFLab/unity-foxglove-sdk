// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Native/FoxRun
// Purpose: Hidden main-thread host for generated native FoxRun subscriptions.

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unity.FoxgloveSDK.Components;
using Unity.Profiling;
using UnityEngine;

namespace Unity2Foxglove.Ros2ForUnity.Native
{
    internal sealed partial class FoxRunRos2SubscriptionHub : MonoBehaviour
    {
        private void Update()
        {
            EnsureHostCleanupQueue();
            DrainPendingHostCleanup();
            _nodeOwner?.RetryPendingNodeReleaseOnCurrentThread();
            if (_stopping)
            {
                BeginShutdown();
                return;
            }

            if (!_providerSessionActive || _manager == null)
            {
                StopBindingsAndNode();
                return;
            }

            // A hierarchy or scene notification deliberately marks the shared
            // gate dirty before its next refresh. Do not turn that recoverable
            // window into a permanent host shutdown: refreshing the bootstrap
            // gate may prove that the active user scene is stable again.
            if (Ros2ForUnityNativeBridgeLifecycleGate.IsShuttingDownForBridge(gameObject.scene)
                && !Ros2ForUnityNativeBridgeLifecycleGate.CanBootstrapBridge)
            {
                PauseForLifecycleWindow();
                return;
            }

            var active = _manager.ActiveFoxRunSubscriptionSessionPolicy;
            if (!ReferenceEquals(active, _policy))
                ApplySessionPolicy(active);
            if (_policy == null || !_policy.SubscriptionsEnabled)
                return;

            _scanCooldown -= Time.deltaTime;
            if (_scanCooldown <= 0f)
            {
                _scanCooldown = ScanIntervalSeconds;
                using (ScanMarker.Auto())
                    ScanAndReconcile();
            }

            using (DrainMarker.Auto())
                DrainBindings(Time.realtimeSinceStartupAsDouble);
        }

        private FoxRunRos2HostCleanupQueue EnsureHostCleanupQueue()
        {
            var queue = _hostCleanupQueue;
            if (queue != null)
                return queue;

            var currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (_hostThreadId != 0 && _hostThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "The deferred ROS2 cleanup queue must be initialized on the Unity host thread.");
            }

            _hostThreadId = currentThreadId;
            queue = new FoxRunRos2HostCleanupQueue(
                currentThreadId,
                SynchronizationContext.Current,
                exception => WarnHostOnce(
                    "deferred-cleanup|" + exception.GetType().Name,
                    "Deferred native ROS2 stream cleanup failed: "
                    + FoxRunRos2PublicDiagnostic.Describe(
                        FoxRunRos2RegistrationError.TeardownFailure)));
            _hostCleanupQueue = queue;
            return queue;
        }

        private void PauseForLifecycleWindow()
        {
            StopBindingsAndNode();
            _scanCooldown = 0f;
        }

        private void SetManager(FoxgloveManager manager)
        {
            if (ReferenceEquals(_manager, manager))
                return;
            _activeSession.Deactivate();
            if (_manager != null)
                _manager.FoxRunSubscriptionSessionChanged -= OnSessionChanged;
            StopBindingsAndNode();
            _manager = manager;
            if (_manager != null)
            {
                _manager.FoxRunSubscriptionSessionChanged += OnSessionChanged;
                ApplySessionPolicy(_manager.ActiveFoxRunSubscriptionSessionPolicy);
            }
            else
            {
                _policy = null;
            }
        }

        private void OnSessionChanged(FoxRunSubscriptionSessionPolicy policy)
            => ApplySessionPolicy(policy);

        private void ApplySessionPolicy(FoxRunSubscriptionSessionPolicy policy)
        {
            if (ReferenceEquals(_policy, policy))
                return;
            _activeSession.Deactivate();
            StopBindingsAndNode();
            _policy = policy;
            if (_providerSessionActive
                && _policy != null
                && _policy.SubscriptionsEnabled)
                _activeSession.Activate(CheckedGeneration(_policy.SessionGeneration));
            _scanCooldown = 0f;
            _nodeRetry.RecordSuccess();
        }

        private void ScanAndReconcile()
        {
            if (_policy == null || !_policy.SubscriptionsEnabled)
                return;
            _sources.Clear();
            var behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                var hasNative = FoxRunRos2SourceDiscovery.TryGet(behaviour, out var nativeSource);
                var hasCustom = FoxRunRos2SourceDiscovery.TryGetCustom(behaviour, out var customSource);
                if (hasNative || hasCustom)
                {
                    _sources.Add(new SourceCandidate(behaviour, nativeSource, customSource));
                }
            }
            var overflow = FoxRunRos2SubscriptionAdmission.RetainDeterministicPrefix(
                _sources,
                MaximumContracts,
                (left, right) => left.Key.CompareTo(right.Key));
            if (overflow > 0)
            {
                WarnHostOnce(
                    "source-capacity",
                    "Native ROS2 subscription discovery exceeded the "
                    + MaximumContracts
                    + "-source limit; ignored "
                    + overflow
                    + " source(s) after deterministic ordering.");
            }

            _seenSources.Clear();
            _seenEndpoints.Clear();
            _existingBindings.Clear();
            for (var i = 0; i < _bindings.Count; i++)
                _existingBindings.Add(_bindings[i].Identity);
            for (var i = 0; i < _sources.Count; i++)
            {
                var source = _sources[i];
                _seenSources.Add(source.InstanceId);
                try
                {
                    var registrar = new CollectingRegistrar(this, source);
                    source.Native?.FoxRunRos2RegisterSubscriptions(registrar);
                    source.Custom?.FoxRunRos2RegisterCustomSubscriptions(registrar);
                }
                catch (Exception exception) when (
                    FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
                {
                    WarnHostOnce(
                        source.Key + ": " + exception.GetType().Name,
                        "Native ROS2 source registration failed for " + source.Key + ": "
                        + FoxRunRos2PublicDiagnostic.Describe(
                            FoxRunRos2RegistrationError.BackendFailure));
                }
            }

            _stale.Clear();
            for (var i = 0; i < _bindings.Count; i++)
            {
                var binding = _bindings[i];
                if (!binding.IsCurrent(_policy.SessionGeneration, _seenSources, _seenEndpoints))
                    _stale.Add(binding);
                else if ((binding.Binding.State == FoxRunRos2SubscriptionBindingState.WaitingForRuntime
                          || binding.Binding.CanRetryRegistration)
                         && Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(
                             gameObject.scene))
                    binding.Binding.TryRegister();
            }
            ExceptionDispatchInfo staleFatal = null;
            var staleCleanupComplete = false;
            try
            {
                StopHostedBindingsAndDrainDeferredCleanup(
                    _stale,
                    _hostCleanupQueue,
                    TimeSpan.FromMilliseconds(DeferredCleanupDrainTimeoutMilliseconds),
                    exception => WarnHostOnce(
                        "stale-binding|" + exception.GetType().Name,
                        "Native ROS2 subscription teardown failed: "
                        + FoxRunRos2PublicDiagnostic.Describe(
                            FoxRunRos2RegistrationError.TeardownFailure)),
                    out staleCleanupComplete);
            }
            catch (Exception exception)
            {
                staleFatal = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                if (staleCleanupComplete)
                {
                    for (var i = 0; i < _stale.Count; i++)
                    {
                        var stale = _stale[i];
                        _bindings.Remove(stale);
                        _existingBindings.Remove(stale.Identity);
                        _diagnostics.Remove(stale.Identity);
                    }
                }
                else
                {
                    WarnHostOnce(
                        "stale-binding|deferred-cleanup-timeout",
                        "Native ROS2 subscription teardown remains pending after the bounded host cleanup window.");
                }
            }
            _diagnostics.RemoveExcept(_seenEndpoints);
            _bindings.Sort((left, right) => left.Key.CompareTo(right.Key));
            SampleDiagnostics();
            staleFatal?.Throw();
        }

        private void DrainBindings(double nowSeconds)
        {
            if (_policy == null)
                return;
            var generation = CheckedGeneration(_policy.SessionGeneration);
            for (var i = 0; i < _bindings.Count; i++)
            {
                _bindings[i].TryDrain(nowSeconds, generation, out _);
            }
            SampleDiagnostics();
        }

        private void SampleDiagnostics()
        {
            if (_policy == null)
                return;
            var generation = CheckedGeneration(_policy.SessionGeneration);
            for (var i = 0; i < _bindings.Count; i++)
            {
                if (!_bindings[i].Binding.TryGetSnapshot(generation, out var snapshot))
                    continue;
                _diagnostics.Update(
                    _bindings[i].Identity,
                    snapshot,
                    _runtimeDiagnosticContext);
                if (_diagnostics.ShouldLog(_bindings[i].Identity, snapshot))
                    Debug.LogWarning("[FoxRun ROS2] " + snapshot.ContractId + ": " + snapshot.Diagnostic);
            }
        }

        private bool TryEnsureNodeOwner(out Ros2ForUnityFoxRunNodeOwner owner)
        {
            if (!Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(gameObject.scene))
            {
                owner = null;
                return false;
            }
            owner = _nodeOwner;
            if (owner != null)
                return true;
            var now = Time.realtimeSinceStartupAsDouble;
            if (!_nodeRetry.TryBegin(now))
                return false;

            try
            {
                _ros2Unity = GetComponent<ROS2.ROS2UnityComponent>()
                             ?? gameObject.AddComponent<ROS2.ROS2UnityComponent>();
                if (!_ros2Unity.Ok())
                {
                    _nodeRetry.RecordFailure(now);
                    return false;
                }
                _runtimeDiagnosticContext =
                    FoxRunRos2RuntimeDiagnosticContext.CaptureAfterRuntimeReady(
                        Environment.GetEnvironmentVariable("ROS_DISTRO"),
                        Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"));
                var node = _ros2Unity.CreateNode(NodeName);
                if (node == null)
                {
                    _nodeRetry.RecordFailure(now);
                    return false;
                }
                var admission = new FoxRunRos2NativeRuntimeAdmission(
                    _activeSession,
                    gameObject.scene);
                owner = new Ros2ForUnityFoxRunNodeOwner(
                    new Ros2ForUnityFoxRunR2fuNodeDriver(_ros2Unity, node),
                    admission.CanUseNativeRuntimeNow,
                    ownerThreadId: Thread.CurrentThread.ManagedThreadId,
                    ownerContext: SynchronizationContext.Current);
                _nodeOwner = owner;
                _nodeRetry.RecordSuccess();
                return true;
            }
            catch (Exception exception) when (
                FoxRunRos2NativeExceptionPolicy.IsRecoverable(exception))
            {
                _nodeRetry.RecordFailure(now);
                WarnHostOnce(
                    "node|" + exception.GetType().Name,
                    "Native ROS2 FoxRun node is unavailable: "
                    + FoxRunRos2PublicDiagnostic.Describe(
                        FoxRunRos2RegistrationError.RuntimeUnavailable));
                return false;
            }
        }

        private void AddBinding<T>(SourceCandidate source, FoxRunRos2GeneratedContract contract,
            Func<T, FoxRunRos2CopyContext, T> copy, Action<T> dispose, Action<T> apply,
            Func<T, bool> clearIfOwned, Func<T, T, bool> valuesEqual = null,
            Func<bool> consumeTrigger = null, Func<bool> canApply = null)
            where T : ROS2.Message, new()
        {
            var identity = source.InstanceId + "|" + contract.Id;
            _seenEndpoints.Add(identity);
            if (_existingBindings.Contains(identity) || _bindings.Count >= MaximumContracts)
                return;

            var activation = FoxRunRos2ContractActivation.Resolve(
                contract,
                _policy,
                _provider?.ActiveSubscribeQos
                ?? FoxRunResolvedQos.Default,
                out var qos,
                out var activationError,
                out var activationDiagnostic);
            if (activation == FoxRunRos2ContractActivationDisposition.NotApplicable)
                return;
            if (activation != FoxRunRos2ContractActivationDisposition.Active)
            {
                RecordUnsupported(identity, contract, activationError, activationDiagnostic);
                return;
            }
            IFoxRunRos2NativeBackend backend;
            if (contract.ContractKind == FoxRunRos2GeneratedContractKind.CustomInterface)
            {
                var readiness = FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(
                    contract.BaseRuntimePackageId,
                    contract.InterfaceDigest,
                    Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"));
                if (!readiness.IsReady)
                {
                    RecordUnsupported(
                        identity,
                        contract,
                        FoxRunRos2RegistrationError.TypesupportUnavailable,
                        FoxRunRos2PublicDiagnostic.Describe(
                            FoxRunRos2RegistrationError.TypesupportUnavailable));
                    return;
                }
                if (!FoxRunRos2CustomNativeTransportHost.TryAcquireSubscriptionBackend(out backend))
                {
                    RecordWaiting(identity, contract, qos, "The selected ROS2 runtime or RMW is not ready.");
                    return;
                }
                _runtimeDiagnosticContext =
                    FoxRunRos2RuntimeDiagnosticContext.CaptureAfterRuntimeReady(
                        Environment.GetEnvironmentVariable("ROS_DISTRO"),
                        Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"));
            }
            else
            {
                if (!TryEnsureNodeOwner(out var owner))
                {
                    RecordWaiting(identity, contract, qos, "The selected ROS2 runtime or RMW is not ready.");
                    return;
                }
                backend = owner.AcquireBackend();
            }

            var generation = CheckedGeneration(_policy.SessionGeneration);
            FoxRunRos2SubscriptionBinding<T> binding = null;
            Func<T, bool> dropBeforeApply = null;
            if (contract.ContractKind == FoxRunRos2GeneratedContractKind.CustomInterface)
            {
                var sourceOrigin =
                    (source.Behaviour as IFoxgloveTopicContractSource)?.FoxgloveLog_Origin;
                // The callback has already deep-copied this envelope when the
                // predicate runs. Do not construct/apply a DTO for a message
                // emitted by this exact active Unity publisher origin.
                dropBeforeApply = owned => contract.TryGetCustomEnvelopeOrigin(
                        owned,
                        out var origin)
                    && IsSelfOrigin(identity, origin, sourceOrigin);
            }
            try
            {
                binding = new FoxRunRos2SubscriptionBinding<T>(
                    contract,
                    generation,
                    _activeSession.ReadGeneration,
                    _provider?.ActiveNativeCopyBudgetBytes
                    ?? FoxRunRos2NativeCopyBudgetPolicy
                        .DefaultBytes,
                    copy,
                    dispose,
                    apply,
                    clearIfOwned,
                    backend,
                    qos,
                    qosFactory: null,
                    dropBeforeApply: dropBeforeApply,
                    valuesEqual: valuesEqual,
                    consumeTrigger: consumeTrigger,
                    canApply: canApply,
                    transportAdmissionRateLimitHz: _policy.TransportAdmissionRateLimitHz);
                binding.WaitForRuntime();
                if (Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(
                        gameObject.scene))
                    binding.TryRegister();
                var hosted = new HostedBinding(
                    source.Behaviour,
                    source.InstanceId,
                    identity,
                    new FoxRunRos2DiscoveryKey(source.TypeName, source.InstanceId, contract.Topic, contract.MemberName),
                    binding,
                    EffectiveSubscribeRateHz(
                        contract,
                        _policy.DefaultSubscribeRateHz,
                        _policy.TransportAdmissionRateLimitHz));
                _bindings.Add(hosted);
                _existingBindings.Add(identity);
            }
            catch (Exception exception)
            {
                var primary = ExceptionDispatchInfo.Capture(exception);
                try
                {
                    if (binding != null)
                        binding.Stop();
                }
                catch (Exception)
                {
                    // Preserve the startup failure while completing cleanup.
                }
                if (binding == null)
                {
                    try
                    {
                        backend.ReleaseNodeOwnership();
                    }
                    catch (Exception)
                    {
                        // Preserve the startup failure.
                    }
                }
                primary.Throw();
                throw;
            }
        }

        private void AddStreamBinding<TTransport, TSample>(
            SourceCandidate source,
            FoxRunRos2GeneratedContract contract,
            Func<bool> tryAdmitInput,
            Func<bool> cancelAdmissionCredit,
            Func<TTransport, FoxRunRos2CopyContext, TSample> materializeOwned,
            Action<TSample> transferOwned,
            Action clearOwned)
            where TTransport : ROS2.Message, new()
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));

            var identity = source.InstanceId + "|" + contract.Id;
            _seenEndpoints.Add(identity);
            if (_existingBindings.Contains(identity) || _bindings.Count >= MaximumContracts)
                return;

            if (tryAdmitInput == null)
            {
                throw new InvalidOperationException(
                    "FoxRunStream field '" + contract.MemberName
                    + "' is null when the native subscription session is captured.");
            }
            if (materializeOwned == null)
                throw new ArgumentNullException(nameof(materializeOwned));
            if (transferOwned == null)
                throw new ArgumentNullException(nameof(transferOwned));
            if (clearOwned == null)
                throw new ArgumentNullException(nameof(clearOwned));

            var activation = FoxRunRos2ContractActivation.Resolve(
                contract,
                _policy,
                _provider?.ActiveSubscribeQos
                ?? FoxRunResolvedQos.Default,
                out var qos,
                out var activationError,
                out var activationDiagnostic);
            if (activation == FoxRunRos2ContractActivationDisposition.NotApplicable)
                return;
            if (activation != FoxRunRos2ContractActivationDisposition.Active)
            {
                RecordUnsupported(identity, contract, activationError, activationDiagnostic);
                return;
            }

            IFoxRunRos2NativeBackend backend;
            if (contract.ContractKind == FoxRunRos2GeneratedContractKind.CustomInterface)
            {
                var readiness = FoxRunRos2CustomTypesupportCatalogRegistry.Evaluate(
                    contract.BaseRuntimePackageId,
                    contract.InterfaceDigest,
                    Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"));
                if (!readiness.IsReady)
                {
                    RecordUnsupported(
                        identity,
                        contract,
                        FoxRunRos2RegistrationError.TypesupportUnavailable,
                        FoxRunRos2PublicDiagnostic.Describe(
                            FoxRunRos2RegistrationError.TypesupportUnavailable));
                    return;
                }
                if (!FoxRunRos2CustomNativeTransportHost.TryAcquireSubscriptionBackend(out backend))
                {
                    RecordWaiting(identity, contract, qos, "The selected ROS2 runtime or RMW is not ready.");
                    return;
                }
                _runtimeDiagnosticContext =
                    FoxRunRos2RuntimeDiagnosticContext.CaptureAfterRuntimeReady(
                        Environment.GetEnvironmentVariable("ROS_DISTRO"),
                        Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION"));
            }
            else
            {
                if (!TryEnsureNodeOwner(out var owner))
                {
                    RecordWaiting(identity, contract, qos, "The selected ROS2 runtime or RMW is not ready.");
                    return;
                }
                backend = owner.AcquireBackend();
            }

            var generation = CheckedGeneration(_policy.SessionGeneration);
            FoxRunRos2StreamSubscriptionBinding<TTransport, TSample> binding = null;
            Func<TTransport, bool> dropBorrowed = null;
            if (contract.ContractKind == FoxRunRos2GeneratedContractKind.CustomInterface)
            {
                var sourceOrigin =
                    (source.Behaviour as IFoxgloveTopicContractSource)?.FoxgloveLog_Origin;
                dropBorrowed = borrowed => contract.TryGetCustomEnvelopeOrigin(
                        borrowed,
                        out var origin)
                    && IsSelfOrigin(identity, origin, sourceOrigin);
            }

            try
            {
                binding = new FoxRunRos2StreamSubscriptionBinding<TTransport, TSample>(
                    contract,
                    generation,
                    _activeSession.ReadGeneration,
                    _provider?.ActiveNativeCopyBudgetBytes
                    ?? FoxRunRos2NativeCopyBudgetPolicy
                        .DefaultBytes,
                    tryAdmitInput,
                    materializeOwned,
                    transferOwned,
                    clearOwned,
                    DispatchCleanupToHostThread,
                    backend,
                    qos,
                    qosFactory: null,
                    dropBorrowed: dropBorrowed,
                    cancelAdmissionCredit: cancelAdmissionCredit);
                binding.WaitForRuntime();
                if (Ros2ForUnityNativeBridgeLifecycleGate.CanInitializeNativeRuntimeForBridge(
                        gameObject.scene))
                    binding.TryRegister();
                _bindings.Add(new HostedBinding(
                    source.Behaviour,
                    source.InstanceId,
                    identity,
                    new FoxRunRos2DiscoveryKey(
                        source.TypeName,
                        source.InstanceId,
                        contract.Topic,
                        contract.MemberName),
                    binding,
                    1d));
                _existingBindings.Add(identity);
            }
            catch (Exception exception)
            {
                var primary = ExceptionDispatchInfo.Capture(exception);
                try
                {
                    if (binding != null)
                        binding.Stop();
                    else
                        backend.ReleaseNodeOwnership();
                }
                catch
                {
                    // Preserve the startup failure.
                }
                primary.Throw();
                throw;
            }
        }

    }
}
#endif
