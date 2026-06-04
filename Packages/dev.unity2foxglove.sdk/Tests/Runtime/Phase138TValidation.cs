// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138T validation for camera raw sensor_msgs/Image native DDS output.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Regression checks for Camera raw sensor_msgs/image native DDS output contracts.
    /// </summary>
    public static class Phase138TValidation
    {
        private static int _passed;

        /// <summary>Runs all Phase 138T checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138T: Camera Raw Native DDS Output ===");
            _passed = 0;

            RawFrameDtoAndBuilderShape();
            RawBuilderFlipContract();
            CameraPublisherApiSurface();
            RawOnlyWiringDoesNotForceJpegOrCompressedDefaults();
            CoreContainsNoRos2References();
            Ros2BridgeDiscoveryAndBinding();
            RegistryIncludesPhase138t();

            Console.WriteLine($"Phase 138T: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void RawFrameDtoAndBuilderShape()
        {
            var dtoType = FindType("Unity.FoxgloveSDK.Schemas.Camera.SensorRawImageFrame");
            Check(dtoType != null, "138T-1A: SensorRawImageFrame DTO type exists");
            if (dtoType == null)
                return;

            Check(dtoType.GetConstructor(new[]
                {
                    typeof(ulong),
                    typeof(string),
                    typeof(int),
                    typeof(int),
                    typeof(byte[]),
                    typeof(string),
                }) != null,
                "138T-1B: SensorRawImageFrame constructor accepts unix ns/frame id/size/bytes/encoding");

            Check(dtoType.GetConstructor(new[]
                {
                    typeof(ulong),
                    typeof(string),
                    typeof(int),
                    typeof(int),
                    typeof(byte[]),
                    typeof(string),
                    typeof(int?),
                }) != null,
                "138T-1C: SensorRawImageFrame constructor also accepts optional isBigendian");

            Check(dtoType.GetProperty("Step") != null
                  && dtoType.GetProperty("Width") != null
                  && dtoType.GetProperty("Height") != null
                  && dtoType.GetProperty("Encoding") != null
                  && dtoType.GetProperty("Data") != null,
                "138T-1D: SensorRawImageFrame exposes fields needed by native bridge");
            Check(dtoType.GetProperty("IsBigendian") != null, "138T-1E: SensorRawImageFrame exposes IsBigendian");

            var builderType = FindType("Unity.FoxgloveSDK.Components.CameraRawImageFrameBuilder");
            Check(builderType != null, "138T-1F: CameraRawImageFrameBuilder exists in runtime");
            if (builderType == null)
                return;

            var buildMethod = builderType.GetMethod(
                "BuildRgb8",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Check(buildMethod != null,
                "138T-1G: CameraRawImageFrameBuilder.BuildRgb8 exists");
        }

        private static void RawBuilderFlipContract()
        {
            var frameBuilderType = FindType("Unity.FoxgloveSDK.Components.CameraRawImageFrameBuilder");
            Check(frameBuilderType != null, "138T-2A: CameraRawImageFrameBuilder type is discoverable");
            if (frameBuilderType == null)
                return;

            var copyMethod = frameBuilderType.GetMethod(
                "CopyRgb24Rows",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(byte[]), typeof(byte[]), typeof(int), typeof(int), typeof(bool) },
                null);
            Check(copyMethod != null, "138T-2B: CameraRawImageFrameBuilder.CopyRgb24Rows overload exists");
            if (copyMethod == null)
                return;

            var source = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
            var destination = new byte[12];
            try
            {
                copyMethod.Invoke(null, new object[] { source, destination, 2, 2, true });
                Check(destination[0] == 6 && destination[1] == 7 && destination[2] == 8,
                    "138T-2C: Raw frame builder flips rows vertically");
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private static void CameraPublisherApiSurface()
        {
            var source = ReadCameraPublisherSources();
            Check(!string.IsNullOrWhiteSpace(source), "138T-3A: Camera publisher source file exists");
            if (string.IsNullOrWhiteSpace(source))
                return;

            Check(source.Contains("public event Action<SensorRawImageFrame> SensorRawImageReady;", StringComparison.Ordinal),
                "138T-3B: FoxgloveCameraPublisher exposes SensorRawImageReady event");
            Check(source.Contains("public bool IsStandardRos2RawImageOutput", StringComparison.Ordinal),
                "138T-3C: FoxgloveCameraPublisher exposes raw output gate");
            Check(source.Contains("private bool HasSensorRawImageDemand()", StringComparison.Ordinal),
                "138T-3D: FoxgloveCameraPublisher checks raw demand with event presence");
            Check(source.Contains("private string ResolveSensorCameraRawImageTopic()", StringComparison.Ordinal),
                "138T-3E: FoxgloveCameraPublisher resolves raw topic");
            Check(source.Contains("_publishStandardRos2RawImage", StringComparison.Ordinal),
                "138T-3F: FoxgloveCameraPublisher stores raw output flag");
            Check(source.Contains("_sensorCameraRawImageTopic", StringComparison.Ordinal),
                "138T-3G: FoxgloveCameraPublisher stores raw topic override");
            Check(source.Contains("PublishRawFrame(", StringComparison.Ordinal),
                "138T-3H: FoxgloveCameraPublisher builds and emits raw DTO");
            Check(source.Contains("LogRawBandwidthWarningIfNeeded();", StringComparison.Ordinal),
                "138T-3I: FoxgloveCameraPublisher logs raw bandwidth warning when raw output is active");

            var resolverSource = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraSensorProfileResolver.cs");
            Check(resolverSource.Contains("public static string ResolveRawImageTopic", StringComparison.Ordinal),
                "138T-3J: Camera sensor profile resolver handles raw topic derivation");
            Check(resolverSource.Contains("public static bool HasRawImageDemand(", StringComparison.Ordinal),
                "138T-3K: Camera sensor profile resolver handles raw-demand gate");
            Check(resolverSource.Contains("public static void ApplyDefaults(", StringComparison.Ordinal),
                "138T-3L: Camera sensor profile resolver applies raw defaults");
        }

        private static void RawOnlyWiringDoesNotForceJpegOrCompressedDefaults()
        {
            var source = ReadCameraPublisherSources();
            var resolverSource = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraSensorProfileResolver.cs");

            Check(source.Contains("var publishJpegFrame = publishWebSocket || publishBridge || publishNativeFrame;", StringComparison.Ordinal),
                "138T-4A: camera publisher tracks JPEG demand separately from raw demand");
            Check(source.Contains("if (!publishJpegFrame)", StringComparison.Ordinal)
                  && source.Contains("PublishRawFrame(frameBytes, renderUnixNs, captureWidth, captureHeight);", StringComparison.Ordinal),
                "138T-4B: raw-only camera readbacks publish raw frames without forcing JPEG encode");
            Check(resolverSource.Contains("if (publishStandardRos2CompressedImage", StringComparison.Ordinal)
                  && resolverSource.Contains("topic = ResolveCompressedImageTopic", StringComparison.Ordinal),
                "138T-4C: raw-only profile defaults do not rewrite the compressed/WebSocket topic");
            Check(resolverSource.Contains("if (publishStandardRos2RawImage", StringComparison.Ordinal)
                  && resolverSource.Contains("rawTopic = ResolveRawImageTopic", StringComparison.Ordinal),
                "138T-4D: raw profile defaults are applied only to the raw topic");
        }

        private static void CoreContainsNoRos2References()
        {
            var runtimeSource = ReadDirectory("Packages/dev.unity2foxglove.sdk/Runtime", includeMd: false);
            Check(!runtimeSource.Contains("using ROS2;", StringComparison.Ordinal),
                "138T-5A: runtime source contains no direct ROS2 using directives");
            Check(!runtimeSource.Contains("namespace ROS2", StringComparison.Ordinal),
                "138T-5B: runtime source contains no ROS2 namespace declaration");
            Check(!runtimeSource.Contains("sensor_msgs.msg.", StringComparison.Ordinal)
                  && !runtimeSource.Contains("std_msgs.msg.", StringComparison.Ordinal)
                  && !runtimeSource.Contains("builtin_interfaces.msg.", StringComparison.Ordinal)
                  && !runtimeSource.Contains("tf2_msgs.msg.", StringComparison.Ordinal),
                "138T-5C: runtime source contains no generated ROS2 namespaces");
        }

        private static void Ros2BridgeDiscoveryAndBinding()
        {
            var bridge = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs");
            var nativeSource = ReadDirectory("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native", includeMd: false);
            var asmdef = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Unity2Foxglove.Ros2ForUnity.Native.asmdef");
            Check(bridge.Contains("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal),
                "138T-6A: Camera native bridge remains ROS2 package symbol gated");
            Check(bridge.Contains("RefreshRawImageBindings", StringComparison.Ordinal),
                "138T-6B: Camera native bridge refreshes raw image bindings");
            Check(nativeSource.Contains("RawImageBinding", StringComparison.Ordinal),
                "138T-6C: Camera native bridge includes raw binding type");
            Check(nativeSource.Contains("SensorRawImageReady", StringComparison.Ordinal),
                "138T-6D: Camera raw binding subscribes raw frame event");
            Check(nativeSource.Contains("CreatePublisher<sensor_msgs.msg.Image>", StringComparison.Ordinal),
                "138T-6E: Camera native bridge creates sensor_msgs.msg.Image publishers");
            Check(nativeSource.Contains("Ros2ForUnityCameraMessageBuilder.BuildImage", StringComparison.Ordinal),
                "138T-6F: Camera raw binding uses ROS2 image builder");
            Check(bridge.Contains("IsRawEligible(", StringComparison.Ordinal),
                "138T-6G: Camera native bridge exposes dedicated raw eligibility helper");
            Check(asmdef.Contains("\"Unity.FoxgloveSDK.Runtime\"", StringComparison.Ordinal),
                "138T-6H: R2FU native asmdef references runtime package");
        }

        private static void RegistryIncludesPhase138t()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("Ci(\"--phase138t\"", StringComparison.Ordinal),
                "138T-7A: phase 138t is registered");
            Check(registry.Contains("--phase138t") && registry.Contains("includeInDefault: false", StringComparison.Ordinal),
                "138T-7B: phase 138t is explicit CI-only");
            Check(registry.Contains("Phase138TValidation.Validate", StringComparison.Ordinal),
                "138T-7C: phase 138t points at the right validation entrypoint");
        }

        private static string ReadDirectory(string path, bool includeMd)
        {
            var bytes = 0;
            var sb = new StringBuilder();
            if (!Directory.Exists(path))
                return "";

            var extFilter = includeMd
                ? new[] { ".cs", ".md", ".txt" }
                : new[] { ".cs" };
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                var allowed = false;
                foreach (var candidate in extFilter)
                {
                    if (string.Equals(ext, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                    continue;

                var content = File.ReadAllText(file);
                sb.AppendLine(content);
                bytes += content.Length;
                if (bytes > 8_000_000)
                    throw new InvalidOperationException("Phase138T validation reading too much source in single pass.");
            }

            return sb.ToString();
        }

        private static string Read(string relativePath) => File.ReadAllText(relativePath);

        private static string ReadCameraPublisherSources()
        {
            const string dir = "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers";
            var output = "";
            foreach (var file in Directory.GetFiles(dir, "FoxgloveCameraPublisher*.cs"))
                output += File.ReadAllText(file) + "\n";
            return output;
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }

        private static Type FindType(string typeName)
            => Type.GetType(typeName + ", FoxgloveSdk.Runtime")
               ?? Type.GetType(typeName + ", FoxgloveSdk.Tests")
               ?? Type.GetType(typeName);
    }
}
