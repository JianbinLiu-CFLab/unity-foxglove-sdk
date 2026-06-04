// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138S IMU native DDS output contract checks.

using System;
using System.IO;
using System.Numerics;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Regression checks for VirtualImu native sensor_msgs/msg/Imu DDS output.
    /// </summary>
    public static class Phase138SValidation
    {
        private static int _passed;

        /// <summary>Runs all Phase 138S checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138S: IMU Native DDS Output ===");
            _passed = 0;

            ImuNativeFrameShape();
            VirtualImuApiSurface();
            UpdatePublishesNativeFrameFromDequeuedSample();
            CoreContainsNoRos2References();
            Ros2BridgeDiscoveryAndBinding();
            ImuMessageBuilderMapping();
            RegistryIncludesPhase138s();

            Console.WriteLine($"Phase 138S: {_passed} checks passed.");
            Console.WriteLine();
        }

        private static void ImuNativeFrameShape()
        {
            var dtoType = FindType("Unity.FoxgloveSDK.Schemas.Imu.ImuNativeFrame");
            Check(dtoType != null, "138S-1A: ImuNativeFrame DTO type exists in runtime");
            Check(dtoType != null && dtoType.GetConstructor(new[]
                {
                    typeof(ulong), typeof(string), typeof(Vector3), typeof(Vector3), typeof(Quaternion), typeof(bool)
                }) != null,
                "138S-1B: ImuNativeFrame exposes ROS-converted value payload and HasOrientation");
            Check(dtoType != null && dtoType.GetProperty("UnixNs") != null
                  && dtoType.GetProperty("FrameId") != null
                  && dtoType.GetProperty("LinearAcceleration") != null
                  && dtoType.GetProperty("AngularVelocity") != null
                  && dtoType.GetProperty("Orientation") != null
                  && dtoType.GetProperty("HasOrientation") != null,
                "138S-1C: ImuNativeFrame exposes frame fields required by native bridge");
            Check(File.Exists("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Imu.meta"),
                "138S-1D: new Runtime/Schemas/Imu folder has a tracked Unity meta file");
        }

        private static void VirtualImuApiSurface()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            Check(!string.IsNullOrWhiteSpace(source), "138S-2A: VirtualImu source exists in runtime");
            if (string.IsNullOrWhiteSpace(source))
                return;

            Check(ContainsSignature(source, "public bool IsImuNativeOutput"),
                "138S-2A: VirtualImu exposes IMU native output eligibility accessor");
            Check(ContainsSignature(source, "public string ImuNativeTopic"),
                "138S-2B: VirtualImu exposes native topic property");
            Check(ContainsSignature(source, "public IReadOnlyList<double> ImuOrientationCovariance")
                && ContainsSignature(source, "public IReadOnlyList<double> ImuAngularVelocityCovariance")
                && ContainsSignature(source, "public IReadOnlyList<double> ImuLinearAccelerationCovariance"),
                "138S-2C: VirtualImu exposes IMU native covariance accessors");
            Check(
                ContainsSignature(source, "public event Action<ImuNativeFrame> ImuNativeFrameReady"),
                "138S-2D: VirtualImu exposes ImuNativeFrameReady event");
            Check(source.Contains("ImuNativeFrameReady", StringComparison.Ordinal),
                "138S-2E: VirtualImu source code includes IMU native frame-ready event usage");
            Check(ExtractMethod(source, "private void Start()").Contains("NormalizeSerializedConfiguration();", StringComparison.Ordinal)
                  && ExtractMethod(source, "private void OnValidate()").Contains("NormalizeSerializedConfiguration();", StringComparison.Ordinal),
                "138S-2F: VirtualImu normalizes native DDS serialized config in runtime and editor validation paths");
            Check(source.Contains("[SerializeField, HideInInspector] private bool _publishImuNative", StringComparison.Ordinal)
                  && source.Contains("[SerializeField, HideInInspector] private string _imuNativeTopic", StringComparison.Ordinal),
                "138S-2G: legacy IMU native override fields are hidden from the default Inspector");
            Check(ExtractProperty(source, "public bool IsImuNativeOutput").Contains("=> isActiveAndEnabled", StringComparison.Ordinal)
                  && !ExtractProperty(source, "public bool IsImuNativeOutput").Contains("_publishImuNative", StringComparison.Ordinal),
                "138S-2H: IMU native output follows component eligibility instead of a second per-IMU toggle");
            Check(ExtractProperty(source, "public string ImuNativeTopic").Contains("_topic", StringComparison.Ordinal)
                  && !ExtractProperty(source, "public string ImuNativeTopic").Contains("_imuNativeTopic", StringComparison.Ordinal),
                "138S-2I: IMU native topic follows the main IMU topic");
        }

        private static void UpdatePublishesNativeFrameFromDequeuedSample()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Imu/VirtualImu.cs");
            var update = ExtractMethod(source, "private void Update()");
            Check(update.Contains("while (_queue.Count > 0)", StringComparison.Ordinal),
                "138S-3A: VirtualImu still drains the IMU queue on Update");
            Check(update.Contains("var sample = _queue.Dequeue();", StringComparison.Ordinal),
                "138S-3B: VirtualImu dequeue path remains single source sample");
            Check(update.Contains("ImuNativeFrame nativeFrame = null;", StringComparison.Ordinal)
                  && update.Contains("CreateNativeFrame(", StringComparison.Ordinal)
                  && update.Contains("ImuNativeFrameReady?.Invoke(nativeFrame);", StringComparison.Ordinal),
                "138S-3C: VirtualImu creates and emits native frame from dequeued sample");
            Check(!update.Contains("_publishImuNative", StringComparison.Ordinal),
                "138S-3C2: VirtualImu native frame emission is subscriber-driven, not gated by a second Inspector toggle");
            Check(update.IndexOf("ImuNativeFrame nativeFrame", StringComparison.Ordinal)
                      < update.IndexOf("ImuNativeFrameReady?.Invoke(nativeFrame);", StringComparison.Ordinal),
                "138S-3D: IMU native frame is created before publish invocation");
            Check(!update.Contains("return; //", StringComparison.Ordinal),
                "138S-3E: VirtualImu update path contains no accidental early native-frame bypass");
        }

        private static void CoreContainsNoRos2References()
        {
            var runtime = ReadDirectory("Packages/dev.unity2foxglove.sdk/Runtime", includeMd: false);
            Check(!runtime.Contains("using ROS2;", StringComparison.Ordinal),
                "138S-4A: runtime source has no ROS2 using directives");
            Check(!runtime.Contains("ROS2UnityComponent", StringComparison.Ordinal)
                  && !runtime.Contains("ROS2Node", StringComparison.Ordinal)
                  && !runtime.Contains("IPublisher<", StringComparison.Ordinal),
                "138S-4B: runtime source has no direct ROS2 For Unity API symbols");
            Check(!runtime.Contains("namespace ROS2", StringComparison.Ordinal),
                "138S-4C: runtime source has no ROS2 namespace declarations");
            Check(!runtime.Contains("sensor_msgs.msg.", StringComparison.Ordinal)
                  && !runtime.Contains("std_msgs.msg.", StringComparison.Ordinal)
                  && !runtime.Contains("builtin_interfaces.msg.", StringComparison.Ordinal)
                  && !runtime.Contains("tf2_msgs.msg.", StringComparison.Ordinal)
                  && !runtime.Contains("geometry_msgs.msg.", StringComparison.Ordinal),
                "138S-4D: runtime source has no ROS2 C# type namespaces");
        }

        private static void Ros2BridgeDiscoveryAndBinding()
        {
            var bridge = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityImuNativeBridge.cs");
            var asmdef = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Unity2Foxglove.Ros2ForUnity.Native.asmdef");
            Check(bridge.Contains("#if UNITY2FOXGLOVE_ROS2_FOR_UNITY", StringComparison.Ordinal),
                "138S-5A: IMU bridge is wrapped by ROS2 package symbol");
            Check(bridge.Contains("FindObjectsByType<VirtualImu>", StringComparison.Ordinal),
                "138S-5B: IMU bridge scans VirtualImu instances");
            Check(bridge.Contains("ImuNativeFrameReady", StringComparison.Ordinal),
                "138S-5C: IMU bridge subscribes to native frame event");
            Check(bridge.Contains("using Unity.FoxgloveSDK.Schemas.Imu;", StringComparison.Ordinal),
                "138S-5C2: IMU bridge imports the schema-neutral IMU DTO namespace");
            Check(bridge.Contains("Ros2NativeOutputPolicy.Enabled", StringComparison.Ordinal),
                "138S-5D: IMU bridge observes global native output policy");
            Check(bridge.Contains("IPublisher<sensor_msgs.msg.Imu>", StringComparison.Ordinal),
                "138S-5E: IMU bridge creates IMU DDS publishers");
            Check(asmdef.Contains("\"Unity.FoxgloveSDK.Sensors\"", StringComparison.Ordinal),
                "138S-5F: R2FU native asmdef references the Sensors assembly that contains VirtualImu");
        }

        private static void ImuMessageBuilderMapping()
        {
            var builder = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityImuMessageBuilder.cs");
            Check(builder.Contains("class Ros2ForUnityImuMessageBuilder", StringComparison.Ordinal),
                "138S-6A: IMU ROS2 builder exists");
            Check(builder.Contains("Build(", StringComparison.Ordinal)
                  && builder.Contains("frame.UnixNs", StringComparison.Ordinal)
                  && builder.Contains("frame.FrameId", StringComparison.Ordinal),
                "138S-6B: IMU ROS2 builder maps timestamp and frame id");
            Check(builder.Contains("Angular_velocity", StringComparison.Ordinal)
                  && builder.Contains("Linear_acceleration", StringComparison.Ordinal),
                "138S-6C: IMU ROS2 builder maps angular velocity and linear acceleration");
            Check(builder.Contains("Orientation_covariance", StringComparison.Ordinal),
                "138S-6D: IMU ROS2 builder maps orientation covariance");
            Check(builder.Contains("Orientation_covariance", StringComparison.Ordinal)
                  && builder.Contains("Orientation_covariance[0] = -1", StringComparison.Ordinal),
                "138S-6E: IMU ROS2 builder applies orientation disabled convention");
            Check(builder.Contains("CopyInto(orientationCovariance, message.Orientation_covariance)", StringComparison.Ordinal)
                  && builder.Contains("CopyInto(angularVelocityCovariance, message.Angular_velocity_covariance)", StringComparison.Ordinal)
                  && builder.Contains("CopyInto(linearAccelerationCovariance, message.Linear_acceleration_covariance)", StringComparison.Ordinal),
                "138S-6F: IMU ROS2 builder copies covariance values into generated fixed arrays");
            Check(!builder.Contains("Orientation_covariance = ", StringComparison.Ordinal)
                  && !builder.Contains("Angular_velocity_covariance = ", StringComparison.Ordinal)
                  && !builder.Contains("Linear_acceleration_covariance = ", StringComparison.Ordinal),
                "138S-6F2: IMU ROS2 builder does not assign read-only generated covariance arrays");
            Check(builder.Contains("ValidateCovariance", StringComparison.Ordinal),
                "138S-6G: IMU ROS2 builder validates covariance length");
        }

        private static void RegistryIncludesPhase138s()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            Check(registry.Contains("Ci(\"--phase138s\"", StringComparison.Ordinal),
                "138S-7A: phase 138s is registered in validation registry");
            Check(registry.Contains("includeInDefault: false", StringComparison.Ordinal)
                  && registry.Contains("--phase138s", StringComparison.Ordinal),
                "138S-7B: phase 138s is available as explicit CI phase and not in default");
        }

        private static string ReadDirectory(string path, bool includeMd)
        {
            var bytes = 0;
            var sb = new System.Text.StringBuilder();
            if (!Directory.Exists(path))
                return "";

            var extFilter = includeMd ? new[] { ".cs", ".md", ".txt" } : new[] { ".cs" };
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

                sb.AppendLine(File.ReadAllText(file));
                bytes += File.ReadAllText(file).Length;
                if (bytes > 8_000_000)
                    throw new InvalidOperationException("Phase138S validation reading too much source in single pass.");
            }

            return sb.ToString();
        }

        private static string Read(string relativePath)
            => File.ReadAllText(relativePath);

        private static bool ContainsSignature(string source, string signature)
            => source.Contains(signature, StringComparison.Ordinal);

        private static string ExtractMethod(string source, string signature)
        {
            var index = source.IndexOf(signature, StringComparison.Ordinal);
            if (index < 0)
                return "";

            var brace = source.IndexOf('{', index);
            if (brace < 0)
                return "";

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(index, i - index + 1);
                }
            }

            return "";
        }

        private static string ExtractProperty(string source, string signature)
        {
            var index = source.IndexOf(signature, StringComparison.Ordinal);
            if (index < 0)
                return "";

            var nextPublic = source.IndexOf("\n        public ", index + signature.Length, StringComparison.Ordinal);
            var nextEvent = source.IndexOf("\n        public event ", index + signature.Length, StringComparison.Ordinal);
            var end = source.Length;
            if (nextPublic >= 0)
                end = Math.Min(end, nextPublic);
            if (nextEvent >= 0)
                end = Math.Min(end, nextEvent);

            return source.Substring(index, end - index);
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
