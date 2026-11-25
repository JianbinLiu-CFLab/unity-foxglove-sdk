// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Receive external IMU publishers through the real bridge in a trusted phase and a negative phase.

using System;
using System.Linq;
using System.Threading;

/// <summary>
/// Receives a trusted phase until stdin sends <c>negative</c>, then a negative phase until stdin sends <c>end</c> or closes.
/// </summary>
/// <remarks>
/// <c>NEGATIVE_NO_FRAMES</c> is only the receive-side evidence. <c>SECURITY_REJECTED</c> also needs the external
/// startup, endpoint and security-denial evidence required by the SROS2 guide.
/// </remarks>
internal sealed class ReceiveScenario
{
    private readonly ImuBridgeHarness _harness;
    private readonly TimeSpan _timeout;
    private readonly string _expectedFrameId;
    private readonly string _forbiddenFrameId;
    private int _negativeRequested;
    private int _endRequested;

    internal ReceiveScenario(ImuBridgeHarness harness, TimeSpan timeout, string expectedFrameId, string forbiddenFrameId)
    {
        _harness = harness;
        _timeout = timeout;
        _expectedFrameId = expectedFrameId;
        _forbiddenFrameId = forbiddenFrameId;
    }

    /// <summary>Returns SECURITY_ACCEPTED, INVALID_TEST, or NEGATIVE_NO_FRAMES.</summary>
    internal string Run()
    {
        new Thread(ReadCommands) { IsBackground = true, Name = "imu-probe-stdin" }.Start();
        _harness.Update();
        Console.WriteLine("RECEIVE_PHASE trusted topic=" + _harness.Bridge.Topic);

        var baseline = -1;
        var ended = FrameIdLimitScenario.RunUntil(_timeout, () =>
        {
            _harness.Update();
            var ending = Volatile.Read(ref _endRequested) == 1;
            if (!ending && baseline < 0 && Volatile.Read(ref _negativeRequested) == 1)
            {
                baseline = _harness.Delivered.Count;
                Console.WriteLine("RECEIVE_PHASE negative baseline=" + baseline);
            }
            return ending;
        });

        var delivered = _harness.Delivered.ToList();
        var trusted = baseline < 0 ? delivered : delivered.Take(baseline).ToList();
        var trustedExpected = trusted.Count(id => id == _expectedFrameId);
        var trustedOther = trusted.Count(id => id != _expectedFrameId && id != _forbiddenFrameId);
        var negativeNew = baseline < 0 ? 0 : delivered.Count - baseline;
        var forbidden = delivered.Count(id => id == _forbiddenFrameId);
        Console.WriteLine(
            "RECEIVE ended=" + ended
            + " negative_started=" + (baseline >= 0)
            + " trusted_expected=" + trustedExpected
            + " trusted_unattributed=" + trustedOther
            + " negative_new_frames=" + negativeNew
            + " forbidden_delivered=" + forbidden
            + " rejected=" + _harness.Bridge.Stats.Rejected);

        if (forbidden > 0)
            return "SECURITY_ACCEPTED";
        if (!ended || baseline < 0 || trustedExpected == 0 || trustedOther > 0 || negativeNew > 0)
            return "INVALID_TEST";
        return "NEGATIVE_NO_FRAMES";
    }

    private void ReadCommands()
    {
        string line;
        while ((line = Console.In.ReadLine()) != null)
        {
            var command = line.Trim();
            if (command == "negative")
                Volatile.Write(ref _negativeRequested, 1);
            else if (command == "end")
                break;
        }

        Volatile.Write(ref _endRequested, 1);
    }
}
