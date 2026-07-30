// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: Combined Phase155 multi-sink and Phase156 optional ROS2 sink acceptance probe.

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using Unity.FoxgloveSDK.Components;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using Unity2Foxglove.Ros2ForUnity.Native;
#endif

/// <summary>
/// Manual Unity/Foxglove acceptance probe for Phase155 and Phase156 together.
/// </summary>
/// <remarks>
/// Usage:
/// 1. Add this component to an enabled GameObject in a scene with a
///    <see cref="FoxgloveManager"/>.
/// 2. Enter Play Mode and connect Foxglove to ws://127.0.0.1:8765.
/// 3. In Foxglove Topics, confirm /phase155/vehicle appears as one JSON topic
///    and /phase155/status appears as a legacy single-field topic.
/// 4. In Unity Console, confirm the local Phase155 sink observed both topics.
/// 5. Optional Phase156 shell validation: add
///    <see cref="Phase155and156Ros2UnavailableSinkBootstrap"/> to the same
///    scene and confirm it reports either the same-GameObject R2FU Provider
///    lifecycle or a clear fail-closed unavailable message.
/// </remarks>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase155 and 156")]
[FoxRunMessage("/phase155/vehicle", SchemaName = "ManualAcceptance.Phase155and156.VehicleTelemetry", Hz = 2f)]
public sealed partial class Phase155and156ManualAcceptance : MonoBehaviour
{
    private const string VehicleTopic = "/phase155/vehicle";
    private const string StatusTopic = "/phase155/status";
    private const float StatusMessageUpdateIntervalSeconds = 0.5f;

    [Header("Manager")]
    [Tooltip("Optional manager under test. When empty, the component finds the first FoxgloveManager in the active scene.")]
    [SerializeField] private FoxgloveManager manager;
    [Tooltip("Automatically find a FoxgloveManager when the explicit Manager field is empty.")]
    [SerializeField] private bool autoFindManager = true;
    [Tooltip("Start the Foxglove server when the manager is present but not running.")]
    [SerializeField] private bool autoStartManager = true;

    [Header("Phase155 Local Sink")]
    [Tooltip("Attach a deterministic in-process IFoxTopicSink that records exported FoxRun payloads.")]
    [SerializeField] private bool attachLocalSink = true;
    [Tooltip("Seconds to wait for the local sink to observe both Phase155 topics.")]
    [SerializeField] private float observationTimeoutSeconds = 10f;

    [Header("Telemetry")]
    [Tooltip("Vehicle speed published inside the aggregate JSON topic.")]
    [FoxRunField("speed")]
    [SerializeField] private float speed;
    [Tooltip("Vehicle enabled flag published inside the aggregate JSON topic.")]
    [FoxRunField("enabled")]
    [SerializeField] private bool enabledState = true;
    [Tooltip("Vehicle position published inside the aggregate JSON topic.")]
    [FoxRunField("position")]
    [SerializeField] private Vector3 position;
    [Tooltip("Vehicle rotation published inside the aggregate JSON topic.")]
    [FoxRunField("rotation")]
    [SerializeField] private Quaternion rotation = Quaternion.identity;
    [Tooltip("Legacy single-field status topic published through the same sink router.")]
    [FoxRun("/phase155/status", Hz = 2f)]
    [SerializeField] private string statusMessage = "phase155 fanout alive";

    [Header("Observed State")]
    [Tooltip("True after the local sink is attached to FoxgloveLogHub.TopicSinkRouter.")]
    [SerializeField] private bool localSinkAttached;
    [Tooltip("True after the local sink observes the aggregate /phase155/vehicle payload.")]
    [SerializeField] private bool observedVehiclePayload;
    [Tooltip("True after the local sink observes the legacy /phase155/status payload.")]
    [SerializeField] private bool observedStatusPayload;
    [Tooltip("Total payloads observed by the local sink.")]
    [SerializeField] private int observedPayloads;
    [Tooltip("Most recent aggregate vehicle payload observed by the local sink.")]
    [SerializeField] private string lastVehiclePayload;
    [Tooltip("Most recent legacy status payload observed by the local sink.")]
    [SerializeField] private string lastStatusPayload;
    [Tooltip("Last acceptance status written by this component.")]
    [SerializeField] private string lastStatus;

    private FoxTopicSinkRouter sinkRouter;
    private RecordingSink recordingSink;
    private bool loggedVehiclePayload;
    private bool loggedStatusPayload;
    private float nextStatusMessageUpdateTime;

    private void Awake()
    {
        if (manager == null && autoFindManager)
            manager = UnityEngine.Object.FindFirstObjectByType<FoxgloveManager>();
    }

    private void OnEnable()
    {
        if (this is IFoxgloveLogSource source)
            FoxgloveLogHub.RegisterSource(source);
    }

    private void OnDisable()
    {
        if (recordingSink != null && sinkRouter != null)
            sinkRouter.RemoveSink(recordingSink);

        recordingSink?.Dispose();
        recordingSink = null;
        localSinkAttached = false;

        if (this is IFoxgloveLogSource source)
            FoxgloveLogHub.UnregisterSource(source);
    }

    private IEnumerator Start()
    {
        yield return null;

        if (manager == null && autoFindManager)
            manager = UnityEngine.Object.FindFirstObjectByType<FoxgloveManager>();
        if (manager == null)
        {
            Fail("Manual acceptance could not find a FoxgloveManager in the active scene.");
            yield break;
        }

        if (!manager.IsRunning && autoStartManager)
            manager.StartServer();
        if (!manager.IsRunning)
        {
            Fail("FoxgloveManager is not running. Start the manager before running Phase155/156 acceptance.");
            yield break;
        }

        if (!(this is IFoxgloveLogSource))
        {
            Fail("FoxRun source generator did not attach IFoxgloveLogSource to this component. Wait for Unity compile/import to finish, then re-enter Play Mode.");
            yield break;
        }

        sinkRouter = null;
        var hubDeadline = Time.realtimeSinceStartup + 10f;
        while (sinkRouter == null && Time.realtimeSinceStartup < hubDeadline)
        {
            FoxgloveLogHub.TryGetTopicSinkRouter(out sinkRouter);
            yield return null;
        }

        if (sinkRouter == null)
        {
            Fail("FoxgloveLogHub topic sink router was not created within 10 seconds.");
            yield break;
        }

        if (attachLocalSink)
        {
            recordingSink = new RecordingSink(OnSinkPayload);
            sinkRouter.AddSink(recordingSink);
            localSinkAttached = true;
            Pass("Attached Phase155 recording sink to FoxgloveLogHub.TopicSinkRouter.");
        }

        var observationDeadline = Time.realtimeSinceStartup + Mathf.Max(1f, observationTimeoutSeconds);
        while ((!observedVehiclePayload || !observedStatusPayload) && Time.realtimeSinceStartup < observationDeadline)
            yield return null;

        if (observedVehiclePayload && observedStatusPayload)
            Pass("Phase155 sink observed aggregate and legacy payloads. Confirm the same topics in Foxglove.");
        else
            Fail("Phase155 sink did not observe both /phase155/vehicle and /phase155/status before timeout.");
    }

    private void Update()
    {
        var t = Time.realtimeSinceStartup;
        speed = 2f + Mathf.Sin(t) * 0.75f;
        enabledState = (Time.frameCount / 150) % 2 == 0;
        position = new Vector3(Mathf.Sin(t) * 0.8f, Mathf.Cos(t * 0.6f) * 0.3f, Mathf.PingPong(t * 0.18f, 1f));
        rotation = Quaternion.Euler(0f, t * 20f, Mathf.Sin(t * 0.45f) * 12f);
        if (t >= nextStatusMessageUpdateTime)
        {
            nextStatusMessageUpdateTime = t + StatusMessageUpdateIntervalSeconds;
            statusMessage = "phase155 frame " + Time.frameCount;
        }
    }

    private void OnSinkPayload(FoxTopicContract contract, byte[] payload)
    {
        observedPayloads++;
        var json = payload == null ? "<null>" : Encoding.UTF8.GetString(payload);

        if (contract.Topic == VehicleTopic)
        {
            observedVehiclePayload = true;
            lastVehiclePayload = json;
            if (!loggedVehiclePayload)
            {
                loggedVehiclePayload = true;
                Pass("Local sink observed aggregate payload for " + VehicleTopic + ": " + json);
            }
        }
        else if (contract.Topic == StatusTopic)
        {
            observedStatusPayload = true;
            lastStatusPayload = json;
            if (!loggedStatusPayload)
            {
                loggedStatusPayload = true;
                Pass("Local sink observed legacy payload for " + StatusTopic + ": " + json);
            }
        }
    }

    private void Pass(string message)
    {
        lastStatus = BuildStatus(message);
        Debug.Log("[Phase155/156] " + message);
    }

    private void Fail(string message)
    {
        lastStatus = BuildStatus(message);
        Debug.LogError("[Phase155/156] " + message);
    }

    private string BuildStatus(string message)
    {
        return $"{message} Manager running: {(manager != null && manager.IsRunning)}. Local sink: {localSinkAttached}. Vehicle: {observedVehiclePayload}. Status: {observedStatusPayload}. Payloads: {observedPayloads}.";
    }

    private sealed class RecordingSink : IFoxTopicSink
    {
        private readonly Action<FoxTopicContract, byte[]> onPayload;

        public RecordingSink(Action<FoxTopicContract, byte[]> onPayload)
        {
            this.onPayload = onPayload;
        }

        public string Name => "phase155-manual-recording";

        public FoxTopicSinkCapabilities Capabilities => FoxTopicSinkCapabilities.Test;

        public void Register(FoxTopicContract contract)
        {
            // Registration is intentionally passive. The manual probe verifies
            // payload delivery rather than maintaining its own contract cache.
        }

        public void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin)
        {
            onPayload?.Invoke(contract, payload);
        }

        public void Flush()
        {
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Optional Phase156 Provider acceptance probe. It verifies that the R2FU
/// package contributes its transport through a same-GameObject Provider
/// without restoring the retired ROS-specific topic-sink compatibility API.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase156 R2FU Provider")]
public sealed class Phase155and156Ros2UnavailableSinkBootstrap : MonoBehaviour
{
    [Header("Manager")]
    [Tooltip("Manager whose same-GameObject R2FU Provider is inspected.")]
    [SerializeField] private FoxgloveManager manager;

    [Header("Observed State")]
    [Tooltip("Last Provider status written by this component.")]
    [SerializeField] private string lastBootstrapStatus;

    private void OnEnable()
    {
        if (manager == null)
            manager = UnityEngine.Object.FindFirstObjectByType<FoxgloveManager>();
        InspectProvider();
    }

    private void Update()
    {
        InspectProvider();
    }

    private void InspectProvider()
    {
        if (manager == null)
        {
            SetStatus(
                "Phase156 cannot inspect the R2FU Provider because no FoxgloveManager is assigned.");
            return;
        }

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        var provider = manager.GetComponent<FoxRunRos2TransportProvider>();
        if (provider == null)
        {
            SetStatus(
                "Phase156 fail-closed: add the R2FU Provider from the Manager Transport Providers panel.");
            return;
        }

        SetStatus(
            "Phase156 R2FU Provider state="
            + provider.LifecycleState
            + "; id="
            + provider.Id.Value
            + ".");
#else
        SetStatus(
            "Phase156 fail-closed: no active ROS2 For Unity runtime package.");
#endif
    }

    private void SetStatus(string status)
    {
        if (string.Equals(
                lastBootstrapStatus,
                status,
                StringComparison.Ordinal))
            return;
        lastBootstrapStatus = status;
        Debug.Log("[Phase155/156] " + lastBootstrapStatus);
    }
}
