// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Receive external IMU publishers through the real bridge and classify trusted and untrusted frame ids.

using System;
using System.Linq;

/// <summary>Runs the bridge for a fixed window; the expected frame id is a liveness control for the forbidden one.</summary>
internal sealed class ReceiveScenario
{
    private readonly ImuBridgeHarness _harness;
    private readonly TimeSpan _window;
    private readonly string _expectedFrameId;
    private readonly string _forbiddenFrameId;

    internal ReceiveScenario(ImuBridgeHarness harness, TimeSpan window, string expectedFrameId, string forbiddenFrameId)
    {
        _harness = harness;
        _window = window;
        _expectedFrameId = expectedFrameId;
        _forbiddenFrameId = forbiddenFrameId;
    }

    /// <summary>Returns the classification: PASS, SECURITY_ACCEPTED, or INVALID_TEST.</summary>
    internal string Run()
    {
        FrameIdLimitScenario.RunUntil(_window, () =>
        {
            _harness.Update();
            return false;
        });

        var expected = _harness.Delivered.Count(id => id == _expectedFrameId);
        var forbidden = _harness.Delivered.Count(id => id == _forbiddenFrameId);
        var other = _harness.Delivered.Count - expected - forbidden;
        var stats = _harness.Bridge.Stats;
        Console.WriteLine(
            "RECEIVE topic=" + _harness.Bridge.Topic
            + " expected=\"" + _expectedFrameId + "\" expected_delivered=" + expected
            + " forbidden=\"" + _forbiddenFrameId + "\" forbidden_delivered=" + forbidden
            + " other_delivered=" + other
            + " rejected=" + stats.Rejected);

        if (forbidden > 0)
            return "SECURITY_ACCEPTED";
        return expected > 0 && other == 0 ? "PASS" : "INVALID_TEST";
    }
}
