// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Unity2Foxglove ManualAcceptance
// Purpose: Unity manual acceptance runner for Phase 125 ROS2 CDR typed decode checks.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[AddComponentMenu("Foxglove/Manual Acceptance/Phase 125 ROS2 CDR Typed Decode Acceptance")]
public sealed class Phase125Ros2CdrTypedDecodeAcceptance : MonoBehaviour
{
    private const string LogPrefix = "[Phase125Ros2CdrTypedDecode]";

    private static readonly string[] SupportedSchemas =
    {
        Ros2PublisherSchemaNames.FrameTransform,
        Ros2PublisherSchemaNames.SceneUpdate,
        Ros2PublisherSchemaNames.PointCloud
    };

    [SerializeField] private string _outputDirectory = "Recordings";
    [SerializeField] private string _filePrefix = "phase125_ros2_cdr_typed_manual";
    [SerializeField] private bool _runNow;

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

    [MenuItem("Foxglove/Manual Acceptance/Run Phase 125 ROS2 CDR Typed Decode Acceptance")]
    private static void RunFromMenu()
    {
        RunAcceptance("Recordings", "phase125_ros2_cdr_typed_manual");
    }
#endif

    [ContextMenu("Run Phase 125 ROS2 CDR Typed Decode Acceptance")]
    public void Run()
    {
        RunAcceptance(_outputDirectory, _filePrefix);
    }

    private static void RunAcceptance(string outputDirectory, string filePrefix)
    {
        var path = CreateFixture(ResolveDirectory(outputDirectory), filePrefix);
        Debug.Log(LogPrefix + " generated=" + path);

        using (var loader = new McapDataLoader(path, McapSequentialReadLimits.UnlimitedForTests))
        {
            var init = loader.Initialize();
            Debug.Log(LogPrefix + " summary channels=" + init.Channels.Count
                      + " schemas=" + init.Schemas.Count
                      + " messages=" + (init.HasTotalMessageCount ? init.TotalMessageCount.ToString() : "unknown")
                      + " initProblems=" + init.Problems.Count);

            var supportedPass = 0;
            foreach (var schemaName in SupportedSchemas)
            {
                if (!Ros2CdrSerializerRegistry.TryGetBySchemaName(schemaName, out var serializer))
                    throw new InvalidOperationException("Missing ROS2 CDR serializer for " + schemaName);

                var topic = TopicForSchema(schemaName);
                var decoded = loader.CreateDecodedIterator(new McapDataLoaderQuery
                {
                    Topics = new List<string> { topic }
                }).Single();

                var value = decoded.Payload.Value as IMessage;
                var pass = decoded.Payload.Kind == McapDecodedPayloadKind.Ros2CdrTyped
                           && value != null
                           && serializer.ClrType.IsInstanceOfType(value)
                           && decoded.Problems.Count == 0
                           && SameBytes(decoded.Raw.Data, decoded.Payload.RawData);
                if (pass)
                    supportedPass++;

                Debug.Log(LogPrefix + " supported"
                          + " topic=" + decoded.Raw.Topic
                          + " schema=" + schemaName
                          + " payloadKind=" + decoded.Payload.Kind
                          + " valueType=" + (value == null ? "(null)" : value.GetType().FullName)
                          + " expectedType=" + serializer.ClrType.FullName
                          + " rawPreserved=" + SameBytes(decoded.Raw.Data, decoded.Payload.RawData)
                          + " problems=" + decoded.Problems.Count
                          + " pass=" + pass);
            }

            var unknown = loader.CreateDecodedIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase125/manual/unknown" }
            }).Single();
            var unknownDiagnostic = unknown.Payload.Value as McapRos2CdrDiagnosticPayload;
            var unknownPass = unknown.Payload.Kind == McapDecodedPayloadKind.Ros2CdrDiagnostic
                              && unknownDiagnostic != null
                              && !unknownDiagnostic.SchemaKnown
                              && SameBytes(unknown.Raw.Data, unknown.Payload.RawData);
            Debug.Log(LogPrefix + " unknown"
                      + " topic=" + unknown.Raw.Topic
                      + " payloadKind=" + unknown.Payload.Kind
                      + " schemaKnown=" + (unknownDiagnostic != null && unknownDiagnostic.SchemaKnown)
                      + " rawPreserved=" + SameBytes(unknown.Raw.Data, unknown.Payload.RawData)
                      + " problems=" + unknown.Problems.Count
                      + " pass=" + unknownPass);

            var malformed = loader.CreateDecodedIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase125/manual/malformed" }
            }).Single();
            var malformedPass = (malformed.Payload.Kind == McapDecodedPayloadKind.Ros2CdrDiagnostic ||
                                 malformed.Payload.Kind == McapDecodedPayloadKind.Failed)
                                && malformed.Problems.Count > 0
                                && SameBytes(malformed.Raw.Data, malformed.Payload.RawData);
            Debug.Log(LogPrefix + " malformed"
                      + " topic=" + malformed.Raw.Topic
                      + " payloadKind=" + malformed.Payload.Kind
                      + " rawPreserved=" + SameBytes(malformed.Raw.Data, malformed.Payload.RawData)
                      + " problems=" + malformed.Problems.Count
                      + " firstProblem=" + FirstProblemCode(malformed)
                      + " pass=" + malformedPass);

            var throwPass = Throws(() =>
                loader.CreateDecodedIterator(
                    new McapDataLoaderQuery { Topics = new List<string> { "/phase125/manual/malformed" } },
                    new McapDecodeOptions { FailurePolicy = McapDecodeFailurePolicy.Throw }).ToList(),
                out var throwException);
            Debug.Log(LogPrefix + " throwPolicy"
                      + " threw=" + throwPass
                      + " exception=" + (throwException == null ? "(none)" : throwException.GetType().Name)
                      + " message=" + (throwException == null ? "(none)" : throwException.Message));

            var fallbackFailed = loader.CreateDecodedIterator(new McapDataLoaderQuery
            {
                Topics = new List<string> { "/phase125/manual/fallback_failed" }
            }).Single();
            var fallbackFailedPass = fallbackFailed.Payload.Kind == McapDecodedPayloadKind.Failed
                                     && fallbackFailed.Problems.Any(problem => problem.Code == "McapDecodeFailed")
                                     && fallbackFailed.Problems.Any(problem => problem.Code == "McapRos2CdrDiagnosticFallbackFailed")
                                     && SameBytes(fallbackFailed.Raw.Data, fallbackFailed.Payload.RawData);
            Debug.Log(LogPrefix + " fallbackFailed"
                      + " topic=" + fallbackFailed.Raw.Topic
                      + " payloadKind=" + fallbackFailed.Payload.Kind
                      + " rawPreserved=" + SameBytes(fallbackFailed.Raw.Data, fallbackFailed.Payload.RawData)
                      + " problems=" + fallbackFailed.Problems.Count
                      + " firstProblem=" + FirstProblemCode(fallbackFailed)
                      + " pass=" + fallbackFailedPass);

            var passAll = supportedPass >= SupportedSchemas.Length
                          && unknownPass
                          && malformedPass
                          && throwPass
                          && fallbackFailedPass;
            Debug.Log(LogPrefix + " RESULT pass=" + passAll
                      + " supported=" + supportedPass + "/" + SupportedSchemas.Length
                      + " unknownKind=" + unknown.Payload.Kind
                      + " malformedKind=" + malformed.Payload.Kind
                      + " throwPolicyThrew=" + throwPass
                      + " fallbackFailedKind=" + fallbackFailed.Payload.Kind
                      + " file=" + path);
        }
    }

    private static string CreateFixture(string outputDirectory, string filePrefix)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(
            outputDirectory,
            (string.IsNullOrEmpty(filePrefix) ? "phase125_ros2_cdr_typed_manual" : filePrefix)
            + "_"
            + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff'Z'")
            + ".mcap");

        using (var stream = File.Create(path))
        using (var recorder = new McapRecorder(stream, null, new McapWriterOptions
               {
                   UseChunking = false,
                   EnableDataCrcs = true
               }, leaveOpen: true))
        {
            uint channelId = 1;
            ulong logTime = 10;
            foreach (var schemaName in SupportedSchemas)
            {
                if (!FoxgloveRos2MsgSchemaCatalog.TryGet(schemaName, out var schema))
                    throw new InvalidOperationException("Missing ROS2 schema catalog entry for " + schemaName);
                if (!Ros2CdrSerializerRegistry.TryGetBySchemaName(schemaName, out var serializer))
                    throw new InvalidOperationException("Missing ROS2 CDR serializer for " + schemaName);

                recorder.AddChannel(
                    channelId,
                    TopicForSchema(schemaName),
                    "cdr",
                    schema.SchemaName,
                    schema.SchemaEncoding,
                    schema.Content);
                recorder.WriteMessage(channelId, logTime, serializer.Serialize(serializer.CreateSample()));
                channelId++;
                logTime += 10;
            }

            recorder.AddChannel(channelId, "/phase125/manual/unknown", "cdr",
                "unknown_msgs/msg/Nope", FoxgloveRos2MsgSchemaCatalog.SchemaEncoding, "uint32 value");
            recorder.WriteMessage(channelId, logTime, new byte[] { 0, 1, 0, 0 });
            channelId++;
            logTime += 10;

            if (!FoxgloveRos2MsgSchemaCatalog.TryGet(Ros2PublisherSchemaNames.FrameTransform, out var malformedSchema))
                throw new InvalidOperationException("Missing ROS2 schema catalog entry for malformed sample.");
            recorder.AddChannel(channelId, "/phase125/manual/malformed", "cdr",
                malformedSchema.SchemaName, malformedSchema.SchemaEncoding, malformedSchema.Content);
            recorder.WriteMessage(channelId, logTime, new byte[] { 0, 1, 0, 0 });
            channelId++;
            logTime += 10;

            recorder.AddChannel(channelId, "/phase125/manual/fallback_failed", "cdr",
                malformedSchema.SchemaName, malformedSchema.SchemaEncoding, malformedSchema.Content);
            recorder.WriteMessage(channelId, logTime, new byte[] { 0 });

            recorder.Close();
        }

        return path;
    }

    private static string TopicForSchema(string schemaName)
    {
        var leaf = schemaName.Substring(schemaName.LastIndexOf('/') + 1);
        return "/phase125/manual/" + ToSnakeCase(leaf);
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var chars = new List<char>(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (i > 0 && char.IsUpper(c))
                chars.Add('_');
            chars.Add(char.ToLowerInvariant(c));
        }
        return new string(chars.ToArray());
    }

    private static string ResolveDirectory(string outputDirectory)
    {
        if (string.IsNullOrEmpty(outputDirectory))
            outputDirectory = "Recordings";
        if (Path.IsPathRooted(outputDirectory))
            return outputDirectory;
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(projectRoot, outputDirectory));
    }

    private static bool SameBytes(byte[] left, byte[] right)
    {
        return (left ?? Array.Empty<byte>()).SequenceEqual(right ?? Array.Empty<byte>());
    }

    private static string FirstProblemCode(McapDecodedMessage message)
    {
        return message.Problems.Count > 0 ? message.Problems[0].Code : "(none)";
    }

    private static bool Throws(Action action, out Exception exception)
    {
        try
        {
            action();
            exception = null;
            return false;
        }
        catch (Exception ex)
        {
            exception = ex;
            return true;
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(Phase125Ros2CdrTypedDecodeAcceptance))]
internal sealed class Phase125Ros2CdrTypedDecodeAcceptanceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var runner = (Phase125Ros2CdrTypedDecodeAcceptance)target;
        if (GUILayout.Button("Run ROS2 CDR Typed Decode Acceptance"))
            runner.Run();
    }
}
#endif
