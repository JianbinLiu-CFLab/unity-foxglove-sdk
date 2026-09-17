// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// Opt-in IL2CPP player check that the physical FoxRun generated capture code
/// survives stripping and transfers concrete values to in-process observers.
/// </summary>
[Preserve]
public static class Phase190FoxRunPlayerAcceptance
{
    private const string EnableArgument = "-phase190FoxRunPlayerAcceptance";
    private const int ConditionalHealthTopic = 0;
    private const int ConditionalPositionTopic = 1;

    [Preserve]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RunIfRequested()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), EnableArgument) < 0)
            return;

        var host = new GameObject("Phase190FoxRunPlayerAcceptance");
        try
        {
            var log = host.AddComponent<TestLog>();
            var capture = (IFoxglovePublishCaptureSource)log;
            var observers = (IFoxgloveTopicObserverSource)log;
            var bus = new FoxTopicBus();
            var healths = new List<object>();
            var positions = new List<object>();
            bus.Subscribe<Dictionary<string, object>>(
                "/debug/conditional_health",
                envelope => healths.Add(envelope.Payload["conditionalHealth"]));
            bus.Subscribe<Dictionary<string, object>>(
                "/debug/conditional_position",
                envelope => positions.Add(envelope.Payload["conditionalPosition"]));

            var healthVectors = new[] { int.MinValue, -1, 0, int.MaxValue };
            foreach (var value in healthVectors)
            {
                log.conditionalHealth = value;
                Transfer(capture, observers, bus, ConditionalHealthTopic);
            }

            var positionVectors = new[]
            {
                new Vector3(-1.5f, 0f, 3.25f),
                new Vector3(float.MaxValue, float.MinValue, float.Epsilon)
            };
            foreach (var value in positionVectors)
            {
                log.conditionalPosition = value;
                Transfer(capture, observers, bus, ConditionalPositionTopic);
            }

            Require(healths.Count == healthVectors.Length, "health observations: " + healths.Count);
            for (var index = 0; index < healthVectors.Length; index++)
            {
                Require(
                    healths[index] is int observed && observed == healthVectors[index],
                    $"health[{index}] expected {healthVectors[index]} observed {healths[index]}");
            }

            Require(positions.Count == positionVectors.Length, "position observations: " + positions.Count);
            for (var index = 0; index < positionVectors.Length; index++)
            {
                var expected = positionVectors[index];
                Require(
                    positions[index] is Dictionary<string, object> observed
                    && observed["x"] is float x && x == expected.x
                    && observed["y"] is float y && y == expected.y
                    && observed["z"] is float z && z == expected.z,
                    $"position[{index}] expected {expected} observed a different value");
            }

            // Negative control: an active capture refuses a second capture until it ends.
            Require(capture.FoxgloveLog_BeginCapture(ConditionalHealthTopic), "begin capture");
            Require(!capture.FoxgloveLog_BeginCapture(ConditionalHealthTopic), "second capture accepted while active");
            capture.FoxgloveLog_EndCapture(ConditionalHealthTopic);
            Require(capture.FoxgloveLog_BeginCapture(ConditionalHealthTopic), "capture refused after end");
            capture.FoxgloveLog_EndCapture(ConditionalHealthTopic);

            Debug.Log($"PHASE190_FOXRUN_PLAYER_PASS health={healths.Count} position={positions.Count}");
            UnityEngine.Object.Destroy(host);
            Application.Quit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError("PHASE190_FOXRUN_PLAYER_FAIL " + ex);
            UnityEngine.Object.Destroy(host);
            Application.Quit(1);
        }
    }

    private static void Transfer(
        IFoxglovePublishCaptureSource capture,
        IFoxgloveTopicObserverSource observers,
        FoxTopicBus bus,
        int topicIndex)
    {
        Require(capture.FoxgloveLog_BeginCapture(topicIndex), "begin capture topic " + topicIndex);
        try
        {
            observers.FoxgloveLog_PublishCapturedToObservers(topicIndex, bus, 1UL);
        }
        finally
        {
            capture.FoxgloveLog_EndCapture(topicIndex);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
