// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance
// Purpose: Phase148 per-sink channel filtering manual acceptance component.

using System.Collections;
using UnityEngine;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Components;

/// <summary>
/// Manual Unity/Foxglove acceptance probe for Phase148 per-sink channel filtering.
/// </summary>
/// <remarks>
/// Usage:
/// 1. Add this component to any enabled GameObject in a scene with a
///    <see cref="FoxgloveManager"/>.
/// 2. Assign the Manager field, or leave it empty for auto-discovery.
/// 3. Enable MCAP Recording on that manager.
/// 4. Enter Play Mode and connect Foxglove to ws://127.0.0.1:8765.
/// 5. Confirm live Foxglove shows /phase148/live-only and does not show
///    /phase148/record-only.
/// 6. Stop Play Mode and open the newest MCAP under Recordings/.
/// 7. Confirm the MCAP contains /phase148/record-only and does not contain
///    /phase148/live-only.
///
/// Sink channel filters are start-time configuration. This probe stops the
/// server before changing filters, then starts a fresh session with the fixed
/// live/recording policy; runtime hot-swapping is intentionally unsupported.
/// </remarks>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Manual Acceptance/Phase148 Sink Filter")]
public sealed class Phase148ManualAcceptance : MonoBehaviour
{
    [Header("Manager")]
    [Tooltip("Optional manager under test. When empty, the component finds the first FoxgloveManager in the active scene.")]
    [SerializeField] private FoxgloveManager manager;
    [Tooltip("Automatically find a FoxgloveManager when the explicit Manager field is empty.")]
    [SerializeField] private bool autoFindManager = true;

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
            Debug.LogError("[Phase148] Manual acceptance could not find a FoxgloveManager in the active scene.");
            yield break;
        }

        // Filters are start-time routing policy, not runtime hot-swap state.
        // Stop first so SetSinkChannelFilter is applied to the next session.
        manager.StopServer();

        manager.Runtime.SetSinkChannelFilter(
            FoxgloveSinkKind.LiveWebSocket,
            new TopicFilter(topic => topic != "/phase148/record-only"));

        manager.Runtime.SetSinkChannelFilter(
            FoxgloveSinkKind.McapRecording,
            new TopicFilter(topic => topic != "/phase148/live-only"));

        manager.StartServer();

        yield return new WaitForSeconds(0.5f);

        manager.PublishJson(
            "/phase148/live-only",
            "",
            new { value = "visible-live" },
            manager.NowNs);

        manager.PublishJson(
            "/phase148/record-only",
            "",
            new { value = "recorded-only" },
            manager.NowNs);

        Debug.Log("[Phase148] Published live-only and record-only probe topics. Expected live: /phase148/live-only only. Expected MCAP: /phase148/record-only only.");
    }

    private sealed class TopicFilter : ISinkChannelFilter
    {
        private readonly System.Func<string, bool> allow;

        public TopicFilter(System.Func<string, bool> allow)
        {
            this.allow = allow;
        }

        public bool AllowChannel(SinkChannelFilterContext context)
        {
            return allow(context.Topic);
        }
    }
}
