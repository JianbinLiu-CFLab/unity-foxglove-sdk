// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/FoxRunTriggerTelemetry
// Purpose: Demonstrates [FoxRun("topic", options...)] trigger-driven telemetry,
// including fixed-rate, manual trigger, and grouped topic publishing.

using System.Collections;
using UnityEngine;
using Unity.FoxgloveSDK.Components;
using static Unity.FoxgloveSDK.Components.FoxRunPublishMode;

// Minimal example for FoxRun trigger-driven telemetry.
//
// Think of the attribute as:
//   [FoxRun("your/topic", options...)]
//
// The first argument is always the Foxglove topic path. Use a stable topic
// name that matches your domain, for example:
//   /demo/heartbeat
//   /events/counter
//   /robot/gripper/state
//
// The options are named C# attribute properties:
// - RateHz: maximum scheduled publish rate. Default is 10 Hz.
// - SchemaName: optional Foxglove schema name for this topic.
// - PublishMode: when to publish. Current modes are FixedRate, OnChange,
//   OnChangeOrInterval, and OnTrigger.
// - ChangeEpsilon: numeric tolerance used by change-driven modes.
// - ForceIntervalSeconds: heartbeat interval used by OnChangeOrInterval.
//
// OnTrigger is deliberately part of PublishMode rather than a separate
// TriggerMode because it answers the same question as the other modes:
// "when should this topic publish?"
//
// This file uses `using static FoxRunPublishMode` so examples can write the
// friendly short form:
//   [FoxRun("/events/counter", PublishMode = OnTrigger)]
//
// The explicit long form is equivalent and may be clearer in library code:
//   [FoxRun("/events/counter", PublishMode = FoxRunPublishMode.OnTrigger)]
//
// For OnTrigger fields, generated code adds a method named after the member:
//   triggerCounter -> FoxRun_Trigger_triggerCounter()
// The method returns true when the publish dispatch succeeds.
//
// A class with [FoxRun] members must be partial so the source generator can
// add the hidden IFoxgloveLogSource implementation and trigger methods.
public partial class FoxRunTriggerTelemetrySmoke : MonoBehaviour
{
    // Automatically publishes to /demo/heartbeat at 2 Hz.
    [FoxRun("/demo/heartbeat", RateHz = 2f)]
    public long fixedCounter;

    // Equivalent conceptual form:
    //   [FoxRun("topic", PublishMode = OnTrigger)]
    //
    // This topic publishes only when TriggerCounterEvent calls the generated
    // FoxRun_Trigger_triggerCounter() method.
    [FoxRun("/events/counter", PublishMode = OnTrigger)]
    public int triggerCounter;

    // Multiple members can share one topic. Because this grouped topic has an
    // OnTrigger member, the whole /events/state topic is trigger-only.
    [FoxRun("/events/state", PublishMode = OnTrigger)]
    public string eventName = "idle";

    // This value changes every frame, but it does not auto-publish because it
    // shares /events/state with the OnTrigger member above.
    [FoxRun("/events/state", RateHz = 5f)]
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
        var ok = FoxRun_Trigger_triggerCounter();
        lastTriggerResult = $"single={ok}, count={triggerCounter}";
        Debug.Log($"[FoxRunTriggerSmoke] TriggerCounterEvent returned {ok}");
    }

    [ContextMenu("FoxRun Trigger Grouped State")]
    public void TriggerGroupedState()
    {
        eventName = "group-" + triggerCounter;
        var ok = FoxRun_Trigger_eventName();
        lastTriggerResult = $"grouped={ok}, event={eventName}";
        Debug.Log($"[FoxRunTriggerSmoke] TriggerGroupedState returned {ok}");
    }

    [ContextMenu("FoxRun Trigger All")]
    public void TriggerAllSmoke()
    {
        triggerCounter++;
        eventName = "all-" + triggerCounter;
        var ok = FoxRun_TriggerAll();
        lastTriggerResult = $"all={ok}, count={triggerCounter}";
        Debug.Log($"[FoxRunTriggerSmoke] TriggerAll returned {ok}");
    }
}
