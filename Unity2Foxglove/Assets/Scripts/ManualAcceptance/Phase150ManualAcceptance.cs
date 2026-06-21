// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: Phase150 SDK-style channel API manual acceptance component.

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using Unity.FoxgloveSDK.Components;

/// <summary>
/// Manual Unity/Foxglove acceptance probe for Phase150 SDK-style channel wrappers.
/// </summary>
/// <remarks>
/// Usage:
/// 1. Add this component to any enabled GameObject in a scene with a
///    <see cref="FoxgloveManager"/>.
/// 2. Assign the Manager field, or leave it empty for auto-discovery.
/// 3. Enter Play Mode and connect Foxglove to ws://127.0.0.1:8765.
/// 4. Confirm Foxglove shows /phase150/json, /phase150/raw, and
///    /phase150/proto.
/// 5. Wait for the scripted StopServer/StartServer restart and Foxglove
///    reconnect window.
/// 6. Confirm Foxglove shows /phase150/recycled after the restart, then watch
///    the Unity Console for the stale wrapper rejection.
///
/// The restart step demonstrates the Phase150 hard boundary: channel wrappers
/// are bound to the server session that created them. Re-create wrappers after
/// restarting the Foxglove server instead of reusing old channel ids.
/// </remarks>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase150 SDK Channel API")]
public sealed class Phase150ManualAcceptance : MonoBehaviour
{
    [Header("Manager")]
    [Tooltip("Optional manager under test. When empty, the component finds the first FoxgloveManager in the active scene.")]
    [SerializeField] private FoxgloveManager manager;
    [Tooltip("Automatically find a FoxgloveManager when the explicit Manager field is empty.")]
    [SerializeField] private bool autoFindManager = true;

    [Header("Topics")]
    [Tooltip("JSON channel topic created through FoxgloveManager.CreateJsonChannel.")]
    [SerializeField] private string jsonTopic = "/phase150/json";
    [Tooltip("Raw byte channel topic created through FoxgloveManager.CreateRawChannel.")]
    [SerializeField] private string rawTopic = "/phase150/raw";
    [Tooltip("Protobuf channel topic created through FoxgloveManager.CreateProtoChannel<T>. Uses foxglove.KeyValuePair to avoid colliding with demo log schemas.")]
    [SerializeField] private string protoTopic = "/phase150/proto";
    [Tooltip("Topic registered after the server restart to prove channel ids can be recycled safely.")]
    [SerializeField] private string recycledTopic = "/phase150/recycled";

    [Header("Timing")]
    [Tooltip("Delay after Play Mode starts before creating the first channel wrappers. Increase this when recording Foxglove Desktop connection setup.")]
    [SerializeField] private float initialDelaySeconds = 0.5f;
    [Tooltip("Delay between StopServer and StartServer during stale wrapper validation.")]
    [SerializeField] private float restartGapSeconds = 10f;
    [Tooltip("Delay after StartServer before publishing /phase150/recycled, giving Foxglove Desktop time to reconnect.")]
    [SerializeField] private float reconnectDelaySeconds = 20f;

    [Header("Observed State")]
    [Tooltip("True after the initial JSON, raw, and protobuf channel samples have been published.")]
    [SerializeField] private bool publishedInitialSamples;
    [Tooltip("True after an old channel wrapper is rejected following StopServer/StartServer.")]
    [SerializeField] private bool staleWrapperRejected;
    [Tooltip("Last acceptance status written by this component.")]
    [SerializeField] private string lastStatus;

    private FoxgloveRawChannel staleRawChannel;

    private void Awake()
    {
        if (manager == null && autoFindManager)
            manager = UnityEngine.Object.FindFirstObjectByType<FoxgloveManager>();
    }

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(initialDelaySeconds);

        if (manager == null)
        {
            Fail("Manual acceptance could not find a FoxgloveManager in the active scene.");
            yield break;
        }

        EnsureServerRunning();
        if (!manager.IsRunning)
        {
            Fail("FoxgloveManager did not start. Enable Foxglove output on the manager before running Phase150 acceptance.");
            yield break;
        }

        PublishInitialChannelSamples();

        yield return new WaitForSeconds(reconnectDelaySeconds);

        // The stale wrapper test intentionally restarts the server so channel
        // ids can be recycled. Old wrappers must reject instead of publishing
        // into whatever new topic later receives the same numeric id.
        manager.StopServer();
        yield return new WaitForSeconds(restartGapSeconds);
        manager.StartServer();
        yield return new WaitForSeconds(reconnectDelaySeconds);

        if (!manager.IsRunning)
        {
            Fail("FoxgloveManager did not restart for stale wrapper validation.");
            yield break;
        }

        ValidateStaleWrapperRejection();
    }

    private void PublishInitialChannelSamples()
    {
        var jsonChannel = manager.CreateJsonChannel(jsonTopic);
        var rawChannel = manager.CreateRawChannel(rawTopic, "json");
        var protoChannel = manager.CreateProtoChannel<Foxglove.KeyValuePair>(protoTopic);

        var nowNs = manager.NowNs;
        jsonChannel.Log(
            new
            {
                phase = 150,
                channel = "json",
                frame = Time.frameCount,
                note = "created with FoxgloveManager.CreateJsonChannel"
            },
            nowNs);

        rawChannel.Log(
            Encoding.UTF8.GetBytes("{\"phase\":150,\"channel\":\"raw\",\"note\":\"created with FoxgloveManager.CreateRawChannel\"}"),
            nowNs);

        protoChannel.Log(
            new Foxglove.KeyValuePair
            {
                Key = "phase",
                Value = "150 protobuf channel sample created with FoxgloveManager.CreateProtoChannel<T>"
            },
            nowNs);

        staleRawChannel = rawChannel;
        publishedInitialSamples = true;
        Pass("Published Phase150 JSON, raw, and protobuf channel samples.");
    }

    private void ValidateStaleWrapperRejection()
    {
        var recycledChannel = manager.CreateRawChannel(recycledTopic, "json");
        recycledChannel.Log(Encoding.UTF8.GetBytes("{\"phase\":150,\"channel\":\"recycled\",\"note\":\"fresh wrapper after restart\"}"));

        try
        {
            staleRawChannel.Log(Encoding.UTF8.GetBytes("{\"phase\":150,\"unexpected\":\"old wrapper should not publish\"}"));
        }
        catch (InvalidOperationException)
        {
            staleWrapperRejected = true;
            Pass("Stale raw channel wrapper rejected after StopServer/StartServer. Re-create wrappers after restart.");
            return;
        }

        staleWrapperRejected = false;
        Fail("Stale raw channel wrapper was not rejected after StopServer/StartServer.");
    }

    private void EnsureServerRunning()
    {
        if (!manager.IsRunning)
            manager.StartServer();
    }

    private void Pass(string message)
    {
        lastStatus = BuildStatus(message);
        Debug.Log("[Phase150] " + message);
    }

    private void Fail(string message)
    {
        lastStatus = BuildStatus(message);
        Debug.LogError("[Phase150] " + message);
    }

    private string BuildStatus(string message)
    {
        return $"{message} Initial samples: {publishedInitialSamples}. Stale wrapper rejected: {staleWrapperRejected}.";
    }
}
