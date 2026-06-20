// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: Phase151 profiler marker manual acceptance component.

using System.Collections;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using Unity.FoxgloveSDK.Components;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Manual Unity Profiler acceptance probe for Phase151 profiler markers.
/// </summary>
/// <remarks>
/// Usage:
/// 1. Add this component to any enabled GameObject in a scene with a
///    <see cref="FoxgloveManager"/>.
/// 2. Select the manager, expand Diagnostics, and confirm Unity Profiler
///    Markers is visible.
/// 3. Run once with Unity Profiler Markers disabled and confirm no
///    profiler-related Console errors.
/// 4. Enable Unity Profiler Markers, open Unity Profiler, record Play Mode,
///    and confirm Phase151.Acceptance.PublishSamples, FoxgloveManager.*, and
///    WsSendQueue.Enqueue appear while this probe publishes samples.
/// 5. WsFrameCodec.* requires an active Foxglove client and a frame with socket
///    traffic. Ros2CdrWriter/CdrBuild/Virtual* markers require the matching
///    ROS2/CDR, LiDAR, or IMU publisher path to be active.
/// </remarks>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase151 Profiler")]
public sealed class Phase151ManualAcceptance : MonoBehaviour
{
    private static readonly ProfilerMarker AcceptancePublishMarker = new ProfilerMarker("Phase151.Acceptance.PublishSamples");

    [Header("Manager")]
    [Tooltip("Optional manager under test. When empty, the component finds the first FoxgloveManager in the active scene.")]
    [SerializeField] private FoxgloveManager manager;
    [Tooltip("Automatically find a FoxgloveManager when the explicit Manager field is empty.")]
    [SerializeField] private bool autoFindManager = true;

    [Header("Timing")]
    [Tooltip("Keep publishing lightweight samples until Play Mode stops. This makes the Phase151 marker easy to find while recording.")]
    [SerializeField] private bool runContinuously = true;
    [Tooltip("Seconds to keep publishing lightweight samples when Run Continuously is disabled.")]
    [SerializeField, Min(0.5f)] private float runDurationSeconds = 60f;

    [Header("Observed State")]
    [Tooltip("True after this probe publishes its initial JSON and raw samples.")]
    [SerializeField] private bool initialSamplesPublished;
    [Tooltip("True after this probe reads the manager's Unity Profiler Markers toggle.")]
    [SerializeField] private bool profilerToggleObserved;
    [Tooltip("Number of lightweight sample batches published by this probe.")]
    [SerializeField] private int samplesPublished;
    [Tooltip("Last acceptance status written by this component.")]
    [SerializeField] private string lastStatus;

    private bool publishing;

    private void Awake()
    {
        if (manager == null && autoFindManager)
            manager = Object.FindFirstObjectByType<FoxgloveManager>();
    }

    private IEnumerator Start()
    {
        yield return null;

        if (manager == null)
        {
            Fail("Manual acceptance could not find a FoxgloveManager in the active scene.");
            yield break;
        }

        profilerToggleObserved = true;
        Pass("Unity Profiler Markers observed as " + (manager.ProfilingEnabled ? "enabled" : "disabled") + ".");

        if (!manager.IsRunning)
            manager.StartServer();

        if (!manager.IsRunning)
        {
            Fail("FoxgloveManager did not start. Enable Foxglove output on the manager before running Phase151 acceptance.");
            yield break;
        }

        var rawChannel = manager.CreateRawChannel("/phase151/raw", "json");
        var endTime = Time.realtimeSinceStartup + Mathf.Max(0.5f, runDurationSeconds);
        var sequence = 0;
        publishing = true;
        Pass(runContinuously
            ? "Publishing Phase151 profiler acceptance samples until Play Mode stops."
            : "Publishing Phase151 profiler acceptance samples for " + runDurationSeconds.ToString("0.##") + " seconds.");

        while (publishing && (runContinuously || Time.realtimeSinceStartup < endTime))
        {
            if (manager.ProfilingEnabled)
            {
                using (AcceptancePublishMarker.Auto())
                    PublishAcceptanceSamples(rawChannel, sequence);
            }
            else
            {
                PublishAcceptanceSamples(rawChannel, sequence);
            }

            initialSamplesPublished = true;
            samplesPublished = sequence + 1;
            sequence++;
            yield return new WaitForSeconds(0.25f);
        }

        publishing = false;
        Pass("Published Phase151 profiler acceptance samples.");
    }

    private void OnDisable()
    {
        if (!publishing)
            return;

        publishing = false;
        Pass("Stopped Phase151 profiler acceptance samples.");
    }

    private void PublishAcceptanceSamples(FoxgloveRawChannel rawChannel, int sequence)
    {
        var timestampNs = manager.NowNs;
        manager.PublishJson(
            "/phase151/json",
            "",
            new
            {
                phase = 151,
                profilingEnabled = manager.ProfilingEnabled,
                sequence,
                frame = Time.frameCount
            },
            timestampNs);

        rawChannel.Log(
            Encoding.UTF8.GetBytes("{\"phase\":151,\"channel\":\"raw\",\"sequence\":" + sequence + "}"),
            timestampNs);
    }

    private void Pass(string message)
    {
        lastStatus = BuildStatus(message);
        Debug.Log("[Phase151] " + message);
    }

    private void Fail(string message)
    {
        lastStatus = BuildStatus(message);
        Debug.LogError("[Phase151] " + message);
    }

    private string BuildStatus(string message)
    {
        return $"{message} Initial samples: {initialSamplesPublished}. Profiler toggle observed: {profilerToggleObserved}. Samples: {samplesPublished}. Continuous: {runContinuously}.";
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(Phase151ManualAcceptance))]
internal sealed class Phase151ManualAcceptanceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
    }
}
#endif
