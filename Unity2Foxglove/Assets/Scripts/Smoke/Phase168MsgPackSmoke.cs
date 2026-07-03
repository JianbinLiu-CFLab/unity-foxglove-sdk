// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/MsgPackSmoke
// Purpose: Publishes a schemaless MessagePack raw channel for Phase168 manual smoke acceptance.

using System;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using UnityEngine;

/// <summary>
/// Manual Unity smoke test for the Phase168 MessagePack raw channel path.
/// </summary>
/// <remarks>
/// Usage:
/// 1. Add this component to any enabled GameObject in a scene that also has a
///    running <see cref="FoxgloveManager"/>.
/// 2. Enter Play Mode and watch <c>Last Status</c> in the Inspector.
/// 3. Foxglove Desktop does not currently parse MsgPack live WebSocket channels.
///    Leave unsupported live publish disabled for normal Foxglove sessions.
/// 4. Enable unsupported live WebSocket publish only for protocol, MCAP, or
///    custom-client compatibility checks, then use the context menu action
///    <c>MsgPack Smoke/Publish Once</c> or enable continuous publishing.
/// </remarks>
[DisallowMultipleComponent]
[AddComponentMenu("Foxglove/Smoke/Phase168 MsgPack Smoke")]
public sealed class Phase168MsgPackSmoke : MonoBehaviour
{
    private const string LogPrefix = "[Phase168MsgPackSmoke]";
    private const string DefaultTopic = "/phase168/msgpack_smoke";

    [Header("Manager")]
    [Tooltip("Optional explicit manager. When empty, the component finds the first FoxgloveManager in the active scene.")]
    [SerializeField] private FoxgloveManager _manager;
    [Tooltip("Automatically find a FoxgloveManager when the explicit Manager field is empty.")]
    [SerializeField] private bool _autoFindManager = true;

    [Header("MsgPack Publish")]
    [Tooltip("Schemaless MessagePack topic used for the positive smoke path.")]
    [SerializeField] private string _topic = DefaultTopic;
    [Tooltip("Foxglove Desktop does not currently parse MsgPack live WebSocket channels. Enable unsupported live WebSocket publish only for protocol, MCAP, or custom-client checks.")]
    [SerializeField] private bool _allowUnsupportedLiveWebSocketPublish;
    [Tooltip("How often the smoke sample is published while Play Mode is running.")]
    [SerializeField, Min(0.05f)] private float _publishIntervalSeconds = 0.5f;
    [Tooltip("Publish periodically during Play Mode. Disable this to use the context menu once.")]
    [SerializeField] private bool _publishContinuously = false;
    [Tooltip("Write a Console log when the channel is created and when the first sample publishes.")]
    [SerializeField] private bool _logLifecycle = true;

    [Header("Observed State")]
    [SerializeField] private uint _publishedCount;
    [SerializeField] private uint _channelId;
    [SerializeField] private int _lastPayloadBytes;
    [SerializeField] private string _lastStatus = "Not started.";

    private readonly FoxgloveMsgPackWriter _writer = new FoxgloveMsgPackWriter(256);
    private FoxgloveMsgPackChannel _channel;
    private string _channelTopic;
    private float _nextPublishTime;
    private bool _loggedFirstPublish;
    private bool _loggedUnsupportedLiveWarning;

    private void OnEnable()
    {
        _nextPublishTime = 0f;
        _publishedCount = 0;
        _channelId = 0;
        _lastPayloadBytes = 0;
        _loggedFirstPublish = false;
        _loggedUnsupportedLiveWarning = false;
        _lastStatus = "Idle. Enable unsupported live WebSocket publish before advertising MsgPack.";
    }

    private void OnDisable()
    {
        ResetChannel();
    }

    private void Update()
    {
        if (!_publishContinuously)
            return;

        var now = Time.unscaledTime;
        if (now < _nextPublishTime)
            return;

        _nextPublishTime = now + _publishIntervalSeconds;
        PublishOnce();
    }

    private void OnValidate()
    {
        _topic = NormalizeTopic(_topic);
        _publishIntervalSeconds = Mathf.Max(0.05f, _publishIntervalSeconds);
    }

    /// <summary>
    /// Publishes one MessagePack sample from the Inspector context menu.
    /// </summary>
    [ContextMenu("MsgPack Smoke/Publish Once")]
    public void PublishOnce()
    {
        if (!CanPublishUnsupportedLive())
            return;

        if (!TryEnsureChannel())
            return;

        try
        {
            var payload = BuildPayload(unchecked(_publishedCount + 1u));
            _channel.Log(payload);

            _publishedCount++;
            _lastPayloadBytes = payload.Length;
            _lastStatus = "Published " + _publishedCount + " MsgPack sample(s) on " + _topic + ".";

            if (_logLifecycle && !_loggedFirstPublish)
            {
                _loggedFirstPublish = true;
                Debug.Log(LogPrefix + " First MsgPack sample published on " + _topic
                          + " bytes=" + _lastPayloadBytes + ".");
            }
        }
        catch (InvalidOperationException ex)
        {
            ResetChannel();
            _lastStatus = "Channel became stale. It will be recreated on the next publish: " + ex.Message;
            Debug.LogWarning(LogPrefix + " " + _lastStatus);
        }
    }

    /// <summary>
    /// Drops the cached session-bound channel so the next publish recreates it.
    /// </summary>
    [ContextMenu("MsgPack Smoke/Reset Channel")]
    public void ResetChannel()
    {
        _channel = null;
        _channelTopic = null;
        _channelId = 0;
    }

    private bool TryEnsureChannel()
    {
        if (!CanPublishUnsupportedLive())
            return false;

        if (!TryResolveManager())
            return false;

        if (!_manager.IsRunning)
        {
            _lastStatus = "FoxgloveManager is not running yet.";
            return false;
        }

        var normalizedTopic = NormalizeTopic(_topic);
        if (!string.Equals(_topic, normalizedTopic, StringComparison.Ordinal))
            _topic = normalizedTopic;

        if (_channel != null && string.Equals(_channelTopic, _topic, StringComparison.Ordinal))
            return true;

        try
        {
            _channel = _manager.CreateMsgPackChannel(_topic);
            _channelTopic = _topic;
            _channelId = _channel.ChannelId;
            _lastStatus = "MsgPack channel ready on " + _topic + " id=" + _channelId + ".";

            if (_logLifecycle)
                Debug.Log(LogPrefix + " " + _lastStatus + " encoding=" + _channel.Encoding + ".");

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            ResetChannel();
            _lastStatus = "Could not create MsgPack channel: " + ex.Message;
            Debug.LogWarning(LogPrefix + " " + _lastStatus);
            return false;
        }
    }

    private bool CanPublishUnsupportedLive()
    {
        if (_allowUnsupportedLiveWebSocketPublish)
            return true;

        _lastStatus = "MsgPack publish is disabled. Enable unsupported live WebSocket publish only for protocol, MCAP, or custom-client checks; Foxglove Desktop does not currently parse MsgPack live WebSocket channels.";
        if (_logLifecycle && !_loggedUnsupportedLiveWarning)
        {
            _loggedUnsupportedLiveWarning = true;
            Debug.LogWarning(LogPrefix + " " + _lastStatus);
        }

        return false;
    }

    private bool TryResolveManager()
    {
        if (_manager == null && _autoFindManager)
            _manager = FindFirstObjectByType<FoxgloveManager>();

        if (_manager != null)
            return true;

        _lastStatus = "No FoxgloveManager found in the active scene.";
        return false;
    }

    private byte[] BuildPayload(uint sequence)
    {
        var position = transform.position;

        _writer.Clear();
        _writer.WriteMapHeader(6);
        _writer.WriteString("phase");
        _writer.WriteInt32(168);
        _writer.WriteString("seq");
        _writer.WriteUInt32(sequence);
        _writer.WriteString("timeSec");
        _writer.WriteDouble(Time.realtimeSinceStartupAsDouble);
        _writer.WriteString("source");
        _writer.WriteString(name);
        _writer.WriteString("active");
        _writer.WriteBool(isActiveAndEnabled);
        _writer.WriteString("position");
        _writer.WriteArrayHeader(3);
        _writer.WriteFloat(position.x);
        _writer.WriteFloat(position.y);
        _writer.WriteFloat(position.z);

        return _writer.ToArray();
    }

    private static string NormalizeTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return DefaultTopic;

        topic = topic.Trim();
        return topic[0] == '/' ? topic : "/" + topic;
    }
}
