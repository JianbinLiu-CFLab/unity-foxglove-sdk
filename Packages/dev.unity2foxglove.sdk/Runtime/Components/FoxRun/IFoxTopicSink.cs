// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Byte-oriented sink boundary for FoxRun multi-sink topic fanout.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Coarse capability flags describing what kind of destination a sink is.
    /// Used only for diagnostics and capability-aware routing; it does not change
    /// delivery semantics, which remain sink-specific.
    /// </summary>
    [Flags]
    public enum FoxTopicSinkCapabilities
    {
        None = 0,
        /// <summary>Live visualization output such as the Foxglove WebSocket.</summary>
        Live = 1 << 0,
        /// <summary>Durable recording output such as MCAP.</summary>
        Recording = 1 << 1,
        /// <summary>Replay or playback output.</summary>
        Replay = 1 << 2,
        /// <summary>Optional external middleware output such as ROS2.</summary>
        External = 1 << 3,
        /// <summary>Deterministic test/observation output.</summary>
        Test = 1 << 4
    }

    /// <summary>
    /// A FoxRun topic destination that receives already-serialized payload bytes.
    /// </summary>
    /// <remarks>
    /// Sinks are byte-oriented on purpose: payloads are serialized once at the
    /// producer boundary and the same bytes are shared across every sink, so a
    /// fanout with N sinks does not re-serialize or box the payload N times.
    /// Live Foxglove and MCAP recording remain their existing primary paths;
    /// this boundary fans an envelope out to additional sinks only.
    /// All methods run on the FoxRun publishing thread (Unity main thread by
    /// default) and must not block indefinitely.
    /// </remarks>
    public interface IFoxTopicSink : IDisposable
    {
        /// <summary>Stable diagnostic name for this sink.</summary>
        string Name { get; }

        /// <summary>Coarse capability flags for diagnostics and routing.</summary>
        FoxTopicSinkCapabilities Capabilities { get; }

        /// <summary>
        /// Register a topic contract with this sink. Called once per exported
        /// contract before any <see cref="Publish"/> for that topic.
        /// </summary>
        void Register(FoxTopicContract contract);

        /// <summary>
        /// Deliver one already-serialized payload for the contract's topic.
        /// The same <paramref name="payload"/> instance may be shared with other
        /// sinks; implementations must not mutate it.
        /// </summary>
        void Publish(FoxTopicContract contract, ulong timestampNs, byte[] payload, string origin);

        /// <summary>Flush any buffered output. Best-effort.</summary>
        void Flush();
    }

    /// <summary>
    /// Optional sink lifecycle surface for releasing resources owned by one
    /// exported topic contract before that contract is removed or replaced.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="IFoxTopicSink"/> so existing additive
    /// sinks remain source- and binary-compatible. Sinks that create external
    /// endpoints per topic should implement this interface.
    /// </remarks>
    public interface IFoxTopicSinkContractLifecycle
    {
        /// <summary>Release resources owned by the exported <paramref name="topic"/>.</summary>
        void Unregister(string topic);
    }
}
