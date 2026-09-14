// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;
using Unity.FoxgloveSDK.IO;

/// <summary>Opt-in runtime smoke for the Phase188 IL2CPP player build.</summary>
[Preserve]
public static class Phase188ReplayPlayerAcceptance
{
    private const string FixtureArgument = "-phase188Fixture";
    private const string EnableArgument = "-phase188PlayerAcceptance";

    [Preserve]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RunIfRequested()
    {
        var args = Environment.GetCommandLineArgs();
        if (!HasArgument(args, EnableArgument))
            return;

        try
        {
            var fixture = ValueAfter(args, FixtureArgument);
            if (string.IsNullOrWhiteSpace(fixture))
                throw new InvalidOperationException("-phase188Fixture is required");

            using var engine = new McapReplayEngine();
            engine.Load(fixture);
            var snapshot = engine.Snapshot(engine.EndTimeNs, new List<McapMessage>());
            var metrics = engine.LastSnapshotMetrics;
            if (snapshot.Count == 0 || metrics.ReturnedMessages != snapshot.Count
                || metrics.PayloadCopies < snapshot.Count)
                throw new InvalidOperationException(
                    $"invalid replay result: returned={snapshot.Count}, metricReturned={metrics.ReturnedMessages}, copies={metrics.PayloadCopies}");

            Debug.Log($"PHASE188_PLAYER_PASS returnedMessages={snapshot.Count} "
                + $"decompressedChunks={metrics.DecompressedChunks} headersScanned={metrics.HeadersScanned} "
                + $"payloadBytesCopied={metrics.PayloadBytesCopied}");
            Application.Quit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError("PHASE188_PLAYER_FAIL " + ex);
            Application.Quit(1);
        }
    }

    private static bool HasArgument(string[] args, string value)
    {
        foreach (var arg in args)
            if (string.Equals(arg, value, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static string ValueAfter(string[] args, string name)
    {
        for (var i = 0; i + 1 < args.Length; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        return null;
    }
}
