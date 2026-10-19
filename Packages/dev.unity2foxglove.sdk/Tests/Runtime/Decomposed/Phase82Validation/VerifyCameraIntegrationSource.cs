// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 82 validation for the experimental Windows native H.264 camera backend.

using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validates the Windows Media Foundation H.264 backend without requiring
    /// Unity Editor. The optional native smoke path runs only on Windows.
    /// </summary>
    public static partial class Phase82Validation
    {
        private static void VerifyCameraIntegrationSource()
        {
            var publisherSource = ReadCameraPublisherSources();
            var videoPipelineSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraVideoPublishPipeline.cs");
            var factorySource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoSidecarOptionsFactory.cs");
            var sessionSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Video/CameraVideoSidecarSession.cs");
            Check(sessionSource.Contains("ICameraVideoEncoderSidecar _sidecar"),
                "82D-1: camera publisher stores codec-neutral sidecar");
            Check(sessionSource.Contains("CameraOutputMode.H264MediaFoundationExperimental"),
                "82D-2: camera publisher routes native H.264 mode");
            Check(sessionSource.Contains("MediaFoundationH264EncoderSidecar")
                  && sessionSource.Contains("CameraVideoSidecarOptionsFactory.CreateMediaFoundationH264Options(")
                  && factorySource.Contains("MediaFoundationH264EncoderOptions"),
                "82D-3: camera publisher starts the Media Foundation sidecar");
            Check(factorySource.Contains("CreateMediaFoundationH264Options"),
                "82D-4: camera publisher creates native H.264 options");
            CheckOrdered(publisherSource, "ShouldPreparePublishPayload()", "EnsureVideoSidecarStarted(profile)",
                "82D-5: demand preflight remains before native sidecar startup");
            CheckOrdered(publisherSource, "ShouldPreparePublishPayload()", "_captureResources.CaptureCamera.Render()",
                "82D-6: demand preflight remains before camera render");
            var submitIndex = publisherSource.IndexOf("private void SubmitVideoFrame", StringComparison.Ordinal);
            var pipelineSubmitIndex = submitIndex >= 0
                ? publisherSource.IndexOf("_videoPublishPipeline.SubmitVideoFrame(", submitIndex, StringComparison.Ordinal)
                : -1;
            var drainIndex = pipelineSubmitIndex >= 0
                ? publisherSource.IndexOf("DrainEncodedAccessUnits();", pipelineSubmitIndex, StringComparison.Ordinal)
                : -1;
            Check(videoPipelineSource.Contains("_videoSidecarSession.TrySubmitFrame(ownedFrameBytes, identity.TimestampNs)")
                  && submitIndex >= 0
                  && pipelineSubmitIndex > submitIndex
                  && drainIndex > pipelineSubmitIndex,
                "82D-7: camera publisher drains encoded video immediately after sidecar submit");
        }

        private static void VerifyInspectorUxSource()
        {
            var editorSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");
            var profileSource = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraOutputMode.cs");
            Check(editorSource.Contains("BuildCameraOutputModeLabels")
                  && profileSource.Contains("\"H.264 (Windows Native, Experimental)\""),
                "82E-1: camera editor exposes native Windows H.264 label");
            Check(editorSource.Contains("DrawNativeH264Section"),
                "82E-2: camera editor has a dedicated native H.264 section");
            Check(editorSource.Contains("does not use FFmpeg") || editorSource.Contains("no external FFmpeg"),
                "82E-3: camera editor explains native mode does not need FFmpeg");
            Check(editorSource.Contains("Media Foundation"),
                "82E-4: camera editor names Media Foundation as the backend");
        }

        private static void VerifyMediaFoundationGuidTable(Type sidecarType)
        {
            var guidTable = sidecarType.GetNestedType("MfGuids", BindingFlags.NonPublic);
            Check(guidTable != null, "82C-30: Media Foundation GUID table exists");

            try
            {
                var fields = guidTable.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .OrderBy(field => field.Name, StringComparer.Ordinal);
                var count = 0;
                foreach (var field in fields)
                {
                    if (field.FieldType != typeof(Guid))
                        continue;

                    var value = (Guid)field.GetValue(null);
                    Check(value != Guid.Empty,
                        "82C-31-" + count.ToString("D2") + ": Media Foundation GUID " + field.Name + " is valid");
                    count++;
                }

                Check(count >= 10, "82C-32: Media Foundation GUID table covers required attributes");
                CheckGuidField(guidTable, "MF_MT_MPEG2_PROFILE", "AD76A80B-2D5C-4E0B-B375-64E520137036");
                CheckGuidField(guidTable, "MF_MT_MPEG_SEQUENCE_HEADER", "3C036DE7-3AD0-4C9E-9216-EE6D6AC21CB3");
                CheckGuidField(guidTable, "CODECAPI_AVLowLatencyMode", "9C27891A-ED7A-40E1-88E8-B22727A024EE");
                CheckGuidField(guidTable, "CODECAPI_AVEncMPVGOPSize", "95F31B26-95A4-41AA-9303-246A7FC6EEF1");
                CheckGuidField(guidTable, "CODECAPI_AVEncMPVDefaultBPictureCount", "8D390AAC-DC5C-4200-B57F-814D04BABAB2");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "82C-33: Media Foundation GUID table initializes without TypeInitializationException: "
                    + ex.GetType().Name + " " + ex.Message,
                    ex);
            }
        }

        private static void CheckGuidField(Type guidTable, string fieldName, string expected)
        {
            var field = guidTable.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            Check(field != null, "82C-34: Media Foundation GUID " + fieldName + " exists");
            var value = (Guid)field.GetValue(null);
            Check(value == new Guid(expected), "82C-35: Media Foundation GUID " + fieldName + " matches Windows SDK");
        }

        private static string GetStringProperty(Type type, object target, string name)
            => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target) as string ?? "";

        private static bool GetBoolProperty(Type type, object target, string name)
        {
            var value = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            return value is bool b && b;
        }

        private static long GetLongProperty(Type type, object target, string name)
        {
            var value = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            return value is long l ? l : value is int i ? i : 0;
        }

        private static int GetStaticIntProperty(Type type, string name)
        {
            var value = type.GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null);
            return value is int i ? i : 0;
        }

        private static int GetIntProperty(Type type, object target, string name)
        {
            var value = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            return value is int i ? i : 0;
        }

        private static void SetField(Type type, object target, string name, object value)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
                throw new MissingFieldException(type.FullName, name);

            field.SetValue(target, value);
        }

        private static bool InvokeValidate(Type type, object target, out string error)
        {
            var args = new object[] { null };
            var result = (bool)type.GetMethod("Validate").Invoke(target, args);
            error = args[0] as string ?? "";
            return result;
        }

        private static bool InvokeTryNormalize(Type type, object normalizer, byte[] sample, out byte[] accessUnit)
        {
            var args = new object[] { sample, null };
            var result = (bool)type.GetMethod("TryNormalizeSample").Invoke(normalizer, args);
            accessUnit = (byte[])args[1];
            return result;
        }

        private static bool InvokeTryDequeueAccessUnit(MethodInfo method, object sidecar, out byte[] accessUnit)
        {
            var args = new object[] { null };
            var result = (bool)method.Invoke(sidecar, args);
            accessUnit = (byte[])args[0];
            return result;
        }

        private static void InvokeCacheParameterSets(Type type, object normalizer, byte[] data)
            => type.GetMethod("CacheParameterSets").Invoke(normalizer, new object[] { data });

        private static void CheckOrdered(string text, string first, string second, string name)
        {
            var firstIdx = text.IndexOf(first, StringComparison.Ordinal);
            var secondIdx = text.IndexOf(second, StringComparison.Ordinal);
            Check(firstIdx >= 0 && secondIdx >= 0 && firstIdx < secondIdx, name);
        }

        private static byte[] H264AnnexBNal(byte type, params byte[] payload)
        {
            var nal = new byte[5 + payload.Length];
            nal[0] = 0;
            nal[1] = 0;
            nal[2] = 0;
            nal[3] = 1;
            nal[4] = type;
            Buffer.BlockCopy(payload, 0, nal, 5, payload.Length);
            return nal;
        }

        private static byte[] H264RawNal(byte type, params byte[] payload)
        {
            var nal = new byte[1 + payload.Length];
            nal[0] = type;
            Buffer.BlockCopy(payload, 0, nal, 1, payload.Length);
            return nal;
        }

        private static byte[] AvccSample(params byte[][] nals)
        {
            var length = nals.Sum(nal => 4 + (nal?.Length ?? 0));
            var result = new byte[length];
            var offset = 0;
            foreach (var nal in nals)
            {
                var nalLength = nal?.Length ?? 0;
                result[offset] = (byte)((nalLength >> 24) & 0xff);
                result[offset + 1] = (byte)((nalLength >> 16) & 0xff);
                result[offset + 2] = (byte)((nalLength >> 8) & 0xff);
                result[offset + 3] = (byte)(nalLength & 0xff);
                offset += 4;
                if (nalLength > 0)
                {
                    Buffer.BlockCopy(nal, 0, result, offset, nalLength);
                    offset += nalLength;
                }
            }

            return result;
        }

        private static byte[] Concat(params byte[][] chunks)
        {
            var length = chunks.Sum(chunk => chunk?.Length ?? 0);
            var result = new byte[length];
            var offset = 0;
            foreach (var chunk in chunks)
            {
                if (chunk == null)
                    continue;

                Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }

            return result;
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not find repository root.");

            var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
                throw new FileNotFoundException("Required validation fixture is missing: " + relativePath, full);

            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
        }

        private static string ReadCameraPublisherSources()
            => PhaseValidationSourceHelpers.ReadCameraPublisherSources();

        private static string ReadMediaFoundationSidecarSources()
            => PhaseValidationSourceHelpers.ReadMediaFoundationH264EncoderSidecarSources();

        private static string FindRepoRoot()
            => Phase16Validation.FindRepoRoot();

        private static Type FindType(string fullName)
            => PhaseValidationReflectionHelpers.FindType(fullName);

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }
    }
}
