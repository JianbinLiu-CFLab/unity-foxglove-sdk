// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/FoxRunTriggerTelemetry
// Purpose: Demonstrates the current direction-specific FoxRun publish trigger
// API alongside the minimum declaration and grouped topic publishing.

using System.Collections;
using UnityEngine;
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunPolicy;

// The minimum publish form is:
//   [FoxRun("/demo/heartbeat")]
//
// The first argument is always the FoxRun topic path. Use a stable topic
// name that matches your domain, for example:
//   /demo/heartbeat
//   /events/counter
//   /robot/gripper/state
//
// The options are named C# attribute properties:
// - Hz: declaration-level cadence override. Default publish cadence is 10 Hz.
// - SchemaName: optional Foxglove schema name for this topic.
// - Policy: when to publish. Current modes are FixedRate, Change, and Trigger.
// - Tolerance: numeric tolerance used by Change.
// - Change + Hz: changes publish immediately and Hz supplies the heartbeat.
//
// Trigger is deliberately part of Policy rather than a separate
// TriggerMode because it answers the same question as the other modes:
// "when should this topic publish?"
//
// This file uses `using static FoxRunPolicy` so examples can write the
// friendly short form:
//   [FoxRun("/events/counter", Policy = Trigger)]
//
// The explicit long form is equivalent and may be clearer in library code:
//   [FoxRun("/events/counter", Policy = FoxRunPolicy.Trigger)]
//
// For Trigger fields, generated code adds a method named after the member:
//   triggerCounter -> FoxRun_Publish_triggerCounter()
// It also adds FoxRun_PublishAll(). Subscribe Trigger declarations instead
// receive FoxRun_Apply_<member>() and FoxRun_ApplyAll(). Each method returns
// true only when its direction-specific dispatch succeeds.
//
// A class with [FoxRun] members must be partial so the source generator can
// add the hidden IFoxgloveLogSource implementation and trigger methods.
public partial class FoxRunTriggerTelemetrySmoke : MonoBehaviour
{
    // Automatically publishes to /demo/heartbeat at 2 Hz.
    [FoxRun("/demo/heartbeat", Hz = 2f)]
    public long fixedCounter;

    // Equivalent conceptual form:
    //   [FoxRun("topic", Policy = Trigger)]
    //
    // This topic publishes only when TriggerCounterEvent calls the generated
    // FoxRun_Publish_triggerCounter() method.
    [FoxRun("/events/counter", Policy = Trigger)]
    public int triggerCounter;

    // Multiple members can share one topic. Because this grouped topic has an
    // Trigger member, the whole /events/state topic is trigger-only.
    [FoxRun("/events/state", Policy = Trigger)]
    public string eventName = "idle";

    // This value changes every frame, but it does not auto-publish because this
    // grouped topic is trigger-only.
    [FoxRun("/events/state", Policy = Trigger)]
    public float groupedTimerValue;

    public string lastTriggerResult = "not triggered";

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(3f);
        TriggerAllSmoke();
    }

    private void Update()
    {
        fixedCounter++;
        groupedTimerValue = Time.time;
    }

    private void OnGUI()
    {
        var panel = new Rect((Screen.width - 260f) * 0.5f, (Screen.height - 150f) * 0.5f, 260f, 150f);
        GUILayout.BeginArea(panel, GUI.skin.box);
        GUILayout.Label("FoxRun Trigger Telemetry");
        GUILayout.Label(lastTriggerResult);
        if (GUILayout.Button("Trigger Counter Event"))
            TriggerCounterEvent();
        if (GUILayout.Button("Trigger Grouped State"))
            TriggerGroupedState();
        if (GUILayout.Button("Trigger All"))
            TriggerAllSmoke();
        GUILayout.EndArea();
    }

    [ContextMenu("FoxRun Trigger Counter Event")]
    public void TriggerCounterEvent()
    {
        triggerCounter++;
        var ok = FoxRun_Publish_triggerCounter();
        lastTriggerResult = $"single={ok}, count={triggerCounter}";
        LogTriggerResult($"[FoxRunTriggerSmoke] TriggerCounterEvent returned {ok}");
    }

    [ContextMenu("FoxRun Trigger Grouped State")]
    public void TriggerGroupedState()
    {
        eventName = "group-" + triggerCounter;
        var ok = FoxRun_Publish_eventName();
        lastTriggerResult = $"grouped={ok}, event={eventName}";
        LogTriggerResult($"[FoxRunTriggerSmoke] TriggerGroupedState returned {ok}");
    }

    [ContextMenu("FoxRun Trigger All")]
    public void TriggerAllSmoke()
    {
        triggerCounter++;
        eventName = "all-" + triggerCounter;
        var ok = FoxRun_PublishAll();
        lastTriggerResult = $"all={ok}, count={triggerCounter}";
        LogTriggerResult($"[FoxRunTriggerSmoke] TriggerAll returned {ok}");
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private static void LogTriggerResult(string message)
    {
        Debug.Log(message);
    }
}
