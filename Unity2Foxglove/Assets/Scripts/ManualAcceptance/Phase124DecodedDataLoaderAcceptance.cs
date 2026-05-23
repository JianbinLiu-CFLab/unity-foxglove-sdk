// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove ManualAcceptance
// Purpose: Unity manual acceptance runner for Phase 124 decoded MCAP DataLoader checks.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[AddComponentMenu("Foxglove/Manual Acceptance/Phase 124 Decoded DataLoader Acceptance")]
public sealed class Phase124DecodedDataLoaderAcceptance : MonoBehaviour
{
    private const string LogPrefix = "[Phase124DecodedDataLoader]";

    [SerializeField] private string _mcapPath = string.Empty;
    [SerializeField] private bool _runOnStart;
    [SerializeField] private bool _runNow;

    private void Start()
    {
        if (_runOnStart)
            Run();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!_runNow)
            return;

        _runNow = false;
        EditorApplication.delayCall += () =>
        {
            if (this != null)
                Run();
        };
    }

    [MenuItem("Foxglove/Manual Acceptance/Run Phase 124 Decoded DataLoader Acceptance")]
    private static void RunFromMenu()
    {
        RunPath(FindLatestRecording());
    }
#endif

    [ContextMenu("Run Phase 124 Decoded DataLoader Acceptance")]
    public void Run()
    {
        RunPath(ResolvePath(_mcapPath));
    }

    private static void RunPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            path = FindLatestRecording();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogWarning(LogPrefix + " No MCAP file found. Set Mcap Path or record phase121_125_chunked_*.mcap first.");
            return;
        }

        Debug.Log(LogPrefix + " START file=" + path);
        using (var loader = new McapDataLoader(path, McapSequentialReadLimits.UnlimitedForTests))
        {
            var init = loader.Initialize();
            var schemaById = init.Schemas.ToDictionary(schema => schema.SchemaId, schema => schema);
            Debug.Log(LogPrefix + " summary channels=" + init.Channels.Count
                      + " schemas=" + init.Schemas.Count
                      + " messages=" + (init.HasTotalMessageCount ? init.TotalMessageCount.ToString() : "unknown")
                      + " initProblems=" + init.Problems.Count);

            var jsonTopic = PickTopicWithMessages(loader, init, "/unity/client_log", "json");
            var protoTopic = PickTopicWithMessages(loader, init, "/scene", "protobuf")
                             ?? PickTopicWithMessages(loader, init, "/tf", "protobuf");
            var topics = new List<string>();
            if (!string.IsNullOrEmpty(jsonTopic))
                topics.Add(jsonTopic);
            if (!string.IsNullOrEmpty(protoTopic) && !topics.Contains(protoTopic))
                topics.Add(protoTopic);

            Debug.Log(LogPrefix + " selectedTopics"
                      + " json=" + (string.IsNullOrEmpty(jsonTopic) ? "(none)" : jsonTopic)
                      + " protobuf=" + (string.IsNullOrEmpty(protoTopic) ? "(none)" : protoTopic));

            var allRawCount = 0;
            var allDecodedCount = 0;
            var allSequencesMatch = true;
            var sawJson = false;
            var sawProtobuf = false;

            foreach (var topic in topics)
            {
                var query = new McapDataLoaderQuery
                {
                    Topics = new List<string> { topic },
                    MaxMessages = 3
                };
                var raw = loader.CreateIterator(query).ToList();
                var decoded = loader.CreateDecodedIterator(query).ToList();
                var sequenceMatch = CompareRawAndDecoded(raw, decoded);
                allRawCount += raw.Count;
                allDecodedCount += decoded.Count;
                allSequencesMatch &= sequenceMatch;

                Debug.Log(LogPrefix + " topicResult topic=" + topic
                          + " rawCount=" + raw.Count
                          + " decodedCount=" + decoded.Count
                          + " sequenceMatch=" + sequenceMatch);

                for (var i = 0; i < decoded.Count; i++)
                {
                    var message = decoded[i];
                    Debug.Log(LogPrefix + " decoded"
                              + " topic=" + message.Raw.Topic
                              + " schema=" + SchemaName(schemaById, message.Raw.SchemaId)
                              + " messageEncoding=" + message.Raw.MessageEncoding
                              + " payloadKind=" + message.Payload.Kind
                              + " rawBytes=" + (message.Raw.Data == null ? 0 : message.Raw.Data.Length)
                              + " payloadRawBytes=" + (message.Payload.RawData == null ? 0 : message.Payload.RawData.Length)
                              + " problems=" + message.Problems.Count
                              + " logTimeNs=" + message.Raw.LogTime);

                    sawJson |= message.Payload.Kind == McapDecodedPayloadKind.Json;
                    sawProtobuf |= message.Payload.Kind == McapDecodedPayloadKind.Protobuf;
                }

                if (raw.Count > 0)
                    LogTryDecode(loader, schemaById, raw[0]);
            }

            var unsupportedRaw = new McapDataLoaderMessage
            {
                ChannelId = ushort.MaxValue,
                SchemaId = 0,
                Topic = "/phase124/manual/unsupported",
                MessageEncoding = "phase124.unsupported",
                LogTime = 0,
                PublishTime = 0,
                Data = new byte[] { 1, 2, 3, 4 }
            };
            var unsupportedOk = loader.TryDecodeMessage(unsupportedRaw, null, out var unsupported);
            Debug.Log(LogPrefix + " unsupported"
                      + " topic=" + unsupported.Raw.Topic
                      + " schema=(none)"
                      + " messageEncoding=" + unsupported.Raw.MessageEncoding
                      + " payloadKind=" + unsupported.Payload.Kind
                      + " problems=" + unsupported.Problems.Count
                      + " ok=" + unsupportedOk
                      + " rawPreserved=" + SameBytes(unsupportedRaw.Data, unsupported.Raw.Data)
                      + " payloadRawPreserved=" + SameBytes(unsupportedRaw.Data, unsupported.Payload.RawData)
                      + " problemCode=" + ProblemCode(unsupported));

            var malformedRaw = new McapDataLoaderMessage
            {
                ChannelId = (ushort)(ushort.MaxValue - 1),
                SchemaId = 0,
                Topic = "/phase124/manual/badjson",
                MessageEncoding = "json",
                LogTime = 0,
                PublishTime = 0,
                Data = System.Text.Encoding.UTF8.GetBytes("{not-json")
            };
            var malformedOk = loader.TryDecodeMessage(malformedRaw, null, out var malformed);
            Debug.Log(LogPrefix + " malformed"
                      + " topic=" + malformed.Raw.Topic
                      + " schema=(none)"
                      + " messageEncoding=" + malformed.Raw.MessageEncoding
                      + " payloadKind=" + malformed.Payload.Kind
                      + " problems=" + malformed.Problems.Count
                      + " ok=" + malformedOk
                      + " rawPreserved=" + SameBytes(malformedRaw.Data, malformed.Raw.Data)
                      + " payloadRawPreserved=" + SameBytes(malformedRaw.Data, malformed.Payload.RawData)
                      + " problemCode=" + ProblemCode(malformed));

            var pass = allRawCount > 0
                       && allRawCount == allDecodedCount
                       && allSequencesMatch
                       && sawJson
                       && sawProtobuf
                       && unsupported.Payload.Kind == McapDecodedPayloadKind.Unsupported
                       && unsupported.Problems.Count > 0
                       && SameBytes(unsupportedRaw.Data, unsupported.Payload.RawData)
                       && malformed.Payload.Kind == McapDecodedPayloadKind.Failed
                       && malformed.Problems.Count > 0
                       && SameBytes(malformedRaw.Data, malformed.Payload.RawData);

            Debug.Log(LogPrefix + " RESULT pass=" + pass
                      + " rawCount=" + allRawCount
                      + " decodedCount=" + allDecodedCount
                      + " sequencesMatch=" + allSequencesMatch
                      + " sawJson=" + sawJson
                      + " sawProtobuf=" + sawProtobuf
                      + " unsupportedKind=" + unsupported.Payload.Kind
                      + " malformedKind=" + malformed.Payload.Kind);
        }
    }

    private static void LogTryDecode(
        McapDataLoader loader,
        Dictionary<ushort, McapDataLoaderSchema> schemas,
        McapDataLoaderMessage raw)
    {
        var tryOk = loader.TryDecodeMessage(raw, null, out var single);
        Debug.Log(LogPrefix + " tryDecode"
                  + " topic=" + single.Raw.Topic
                  + " schema=" + SchemaName(schemas, single.Raw.SchemaId)
                  + " messageEncoding=" + single.Raw.MessageEncoding
                  + " payloadKind=" + single.Payload.Kind
                  + " problems=" + single.Problems.Count
                  + " ok=" + tryOk
                  + " rawPreserved=" + SameBytes(raw.Data, single.Raw.Data)
                  + " payloadRawPreserved=" + SameBytes(raw.Data, single.Payload.RawData));
    }

    private static string FindLatestRecording()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var recordings = Path.Combine(projectRoot, "Recordings");
        if (!Directory.Exists(recordings))
            return string.Empty;

        return Directory.GetFiles(recordings, "phase121_125_chunked_*.mcap")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        if (Path.IsPathRooted(path))
            return path;
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(projectRoot, path));
    }

    private static string PickTopicWithMessages(
        McapDataLoader loader,
        McapDataLoaderInitialization init,
        string preferredTopic,
        string encoding)
    {
        var preferred = init.Channels.FirstOrDefault(channel =>
            string.Equals(channel.Topic, preferredTopic, StringComparison.Ordinal) &&
            string.Equals(channel.MessageEncoding, encoding, StringComparison.OrdinalIgnoreCase));
        if (preferred != null && HasMessages(loader, preferred.Topic))
            return preferred.Topic;

        foreach (var channel in init.Channels
                     .Where(channel => string.Equals(channel.MessageEncoding, encoding, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(channel => channel.Topic.StartsWith("/debug/", StringComparison.Ordinal) ? 1 : 0)
                     .ThenBy(channel => channel.Topic, StringComparer.Ordinal))
        {
            if (HasMessages(loader, channel.Topic))
                return channel.Topic;
        }

        return null;
    }

    private static bool HasMessages(McapDataLoader loader, string topic)
    {
        var query = new McapDataLoaderQuery
        {
            Topics = new List<string> { topic },
            MaxMessages = 1
        };
        return loader.CreateIterator(query).Any();
    }

    private static bool CompareRawAndDecoded(
        IReadOnlyList<McapDataLoaderMessage> raw,
        IReadOnlyList<McapDecodedMessage> decoded)
    {
        if (raw.Count != decoded.Count)
            return false;

        for (var i = 0; i < raw.Count; i++)
        {
            if (raw[i].ChannelId != decoded[i].Raw.ChannelId ||
                raw[i].LogTime != decoded[i].Raw.LogTime ||
                raw[i].PublishTime != decoded[i].Raw.PublishTime ||
                !SameBytes(raw[i].Data, decoded[i].Raw.Data))
            {
                return false;
            }
        }

        return true;
    }

    private static string SchemaName(Dictionary<ushort, McapDataLoaderSchema> schemas, ushort schemaId)
    {
        return schemas.TryGetValue(schemaId, out var schema) ? schema.Name : "(none)";
    }

    private static bool SameBytes(byte[] left, byte[] right)
    {
        return (left ?? Array.Empty<byte>()).SequenceEqual(right ?? Array.Empty<byte>());
    }

    private static string ProblemCode(McapDecodedMessage message)
    {
        return message.Problems.Count > 0 ? message.Problems[0].Code : "(none)";
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(Phase124DecodedDataLoaderAcceptance))]
internal sealed class Phase124DecodedDataLoaderAcceptanceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var runner = (Phase124DecodedDataLoaderAcceptance)target;
        if (GUILayout.Button("Run Decoded DataLoader Acceptance"))
            runner.Run();
    }
}
#endif
