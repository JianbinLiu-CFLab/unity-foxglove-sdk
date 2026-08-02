// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: Combined Phase153 topic-bus and Phase154 aggregate-message acceptance probe.

using System.Collections;
using System.Text;
using UnityEngine;
using Unity.FoxgloveSDK.Components;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Manual Unity/Foxglove acceptance probe for Phase153 and Phase154 together.
/// </summary>
/// <remarks>
/// Usage:
/// 1. Add this component to an enabled GameObject in a scene with a
///    <see cref="FoxgloveManager"/>.
/// 2. Enter Play Mode and connect Foxglove to ws://127.0.0.1:8765.
/// 3. In Foxglove Topics, confirm /phase154/vehicle appears as one JSON topic
///    with speed, enabled, position, and rotation fields.
/// 4. In Unity Console, confirm the Phase153 bus side-channel log appears.
/// 5. Stop and re-enter Play Mode once to confirm no stale contract or duplicate
///    writer warnings are produced.
/// </remarks>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase153 and 154")]
[FoxRunMessage("/phase154/vehicle", SchemaName = "ManualAcceptance.Phase153and154VehicleTelemetry", Hz = 2f)]
public sealed partial class Phase153and154ManualAcceptance : MonoBehaviour
{
    private const string VehicleTopic = "/phase154/vehicle";

    [Header("Manager")]
    [Tooltip("Optional manager under test. When empty, the component finds the first FoxgloveManager in the active scene.")]
    [SerializeField] private FoxgloveManager manager;
    [Tooltip("Automatically find a FoxgloveManager when the explicit Manager field is empty.")]
    [SerializeField] private bool autoFindManager = true;
    [Tooltip("Start the Foxglove server when the manager is present but not running.")]
    [SerializeField] private bool autoStartManager = true;

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

    [Header("Observed State")]
    [Tooltip("True after the local Phase153 topic bus observes the Phase154 aggregate payload.")]
    [SerializeField] private bool topicBusObserved;
    [Tooltip("Number of aggregate payloads observed through the Phase153 topic bus.")]
    [SerializeField] private int topicBusPayloads;
    [Tooltip("UTF-8 JSON payload from the most recent Phase153 topic bus envelope.")]
    [SerializeField] private string lastBusPayload;
    [Tooltip("Last acceptance status written by this component.")]
    [SerializeField] private string lastStatus;

    private bool subscribedToBus;
    private bool loggedFirstBusPayload;

    private void Awake()
    {
        if (manager == null && autoFindManager)
            manager = Object.FindFirstObjectByType<FoxgloveManager>();
    }

    private void OnEnable()
    {
        if (this is IFoxgloveLogSource source)
            FoxgloveLogHub.RegisterSource(source);
    }

    private void OnDisable()
    {
        if (this is IFoxgloveLogSource source)
            FoxgloveLogHub.UnregisterSource(source);
    }

    private IEnumerator Start()
    {
        yield return null;

        if (manager == null && autoFindManager)
            manager = Object.FindFirstObjectByType<FoxgloveManager>();
        if (manager == null)
        {
            Fail("Manual acceptance could not find a FoxgloveManager in the active scene.");
            yield break;
        }

        if (!manager.IsRunning && autoStartManager)
            manager.StartServer();
        if (!manager.IsRunning)
        {
            Fail("FoxgloveManager is not running. Start the manager before running Phase153/154 acceptance.");
            yield break;
        }

        FoxTopicBus topicBus = null;
        var deadline = Time.realtimeSinceStartup + 10f;
        while (topicBus == null && Time.realtimeSinceStartup < deadline)
        {
            if (FoxgloveLogHub.TryGetTopicBus(out var bus))
                topicBus = bus;
            yield return null;
        }

        if (topicBus == null)
        {
            Fail("FoxgloveLogHub topic bus was not created within 10 seconds.");
            yield break;
        }

        topicBus.Subscribe<byte[]>(VehicleTopic, OnVehicleBusPayload);
        subscribedToBus = true;
        Pass("Subscribed to Phase153 topic bus for " + VehicleTopic + ".");
    }

    private void Update()
    {
        var t = Time.realtimeSinceStartup;
        speed = 1.5f + Mathf.Sin(t) * 0.5f;
        enabledState = (Time.frameCount / 120) % 2 == 0;
        position = new Vector3(Mathf.Sin(t) * 0.75f, Mathf.Cos(t * 0.7f) * 0.25f, Mathf.PingPong(t * 0.2f, 1f));
        rotation = Quaternion.Euler(0f, t * 25f, Mathf.Sin(t * 0.5f) * 10f);
    }

    private void OnVehicleBusPayload(FoxTopicEnvelope<byte[]> envelope)
    {
        topicBusObserved = true;
        topicBusPayloads++;
        lastBusPayload = envelope.Payload == null ? "<null>" : Encoding.UTF8.GetString(envelope.Payload);

        if (!loggedFirstBusPayload)
        {
            loggedFirstBusPayload = true;
            Pass("TopicBus observed aggregate payload for " + envelope.Contract.Topic + ": " + lastBusPayload);
        }
    }

    private void Pass(string message)
    {
        lastStatus = BuildStatus(message);
        Debug.Log("[Phase153/154] " + message);
    }

    private void Fail(string message)
    {
        lastStatus = BuildStatus(message);
        Debug.LogError("[Phase153/154] " + message);
    }

    private string BuildStatus(string message)
    {
        return $"{message} Manager running: {(manager != null && manager.IsRunning)}. Bus subscribed: {subscribedToBus}. Bus observed: {topicBusObserved}. Bus payloads: {topicBusPayloads}.";
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(Phase153and154ManualAcceptance))]
internal sealed class Phase153and154ManualAcceptanceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
    }
}
#endif
