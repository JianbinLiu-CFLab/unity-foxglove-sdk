// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 138M validation for cart-mounted camera time sync and ROS camera schemas.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Schemas;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Phase 138M checks for FAST-LIVO2 camera wiring boundaries.
    /// </summary>
    public static class Phase138MValidation
    {
        private static readonly Dictionary<string, string> SourceCache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static int _passed;

        /// <summary>Runs all Phase 138M validation checks.</summary>
        public static void Validate()
        {
            Console.WriteLine();
            Console.WriteLine("=== Phase 138M: Cart Camera Time Sync for FAST-LIVO2 Prep ===");
            _passed = 0;
            SourceCache.Clear();

            StandardCameraRos2BuildersUseSensorMsgs();
            StandardCameraSchemasAreRegistered();
            SensorUnitProfileExposesCameraContract();
            CameraPublisherHasSensorClockAndStandardImageMode();
            CameraInfoPublisherUsesSensorClockAndStandardCameraInfo();
            OptionalR2fuCameraBridgeStaysInOptionalPackage();
            MazeDemoUsesCartCameraProductPath();
            ValidationRegistryWiresPhase138M();

            Console.WriteLine($"Phase 138M: {_passed} checks passed.");
        }

        private static void StandardCameraRos2BuildersUseSensorMsgs()
        {
            Check(Ros2PublisherSchemaNames.SensorCompressedImage == "sensor_msgs/msg/CompressedImage",
                "138M-1A: standard compressed image schema name is sensor_msgs/msg/CompressedImage");
            Check(Ros2PublisherSchemaNames.SensorCameraInfo == "sensor_msgs/msg/CameraInfo",
                "138M-1B: standard camera-info schema name is sensor_msgs/msg/CameraInfo");

            var compressedPayload = InvokeStaticByteArray(
                "Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrSensorCompressedImageBuilder",
                "Serialize",
                1_700_000_001_234_567_890UL,
                "os_camera",
                new byte[] { 0xff, 0xd8, 0xff },
                "jpeg");
            var compressedReader = new Ros2CdrTestReader(compressedPayload);
            Check(compressedReader.ReadInt32() == 1700000001, "138M-1C: CompressedImage header stamp sec is written");
            Check(compressedReader.ReadUInt32() == 234567890U, "138M-1D: CompressedImage header stamp nanosec is written");
            Check(compressedReader.ReadString() == "os_camera", "138M-1E: CompressedImage header frame_id is written");
            Check(compressedReader.ReadString() == "jpeg", "138M-1F: sensor_msgs/CompressedImage writes format before data");
            Check(compressedReader.ReadByteArray().SequenceEqual(new byte[] { 0xff, 0xd8, 0xff }),
                "138M-1G: sensor_msgs/CompressedImage writes compressed data bytes");

            var k = new[] { 320d, 0d, 160d, 0d, 320d, 120d, 0d, 0d, 1d };
            var r = new[] { 1d, 0d, 0d, 0d, 1d, 0d, 0d, 0d, 1d };
            var p = new[] { 320d, 0d, 160d, 0d, 0d, 320d, 120d, 0d, 0d, 0d, 1d, 0d };
            var cameraInfoPayload = InvokeStaticByteArray(
                "Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg.Ros2CdrSensorCameraInfoBuilder",
                "Serialize",
                1_700_000_002_345_678_901UL,
                "os_camera",
                320U,
                240U,
                "plumb_bob",
                Array.Empty<double>(),
                k,
                r,
                p);
            var cameraInfoReader = new Ros2CdrTestReader(cameraInfoPayload);
            Check(cameraInfoReader.ReadInt32() == 1700000002, "138M-1H: CameraInfo header stamp sec is written");
            Check(cameraInfoReader.ReadUInt32() == 345678901U, "138M-1I: CameraInfo header stamp nanosec is written");
            Check(cameraInfoReader.ReadString() == "os_camera", "138M-1J: CameraInfo header frame_id is written");
            Check(cameraInfoReader.ReadUInt32() == 240U, "138M-1K: CameraInfo height is written before width");
            Check(cameraInfoReader.ReadUInt32() == 320U, "138M-1L: CameraInfo width is written");
            Check(cameraInfoReader.ReadString() == "plumb_bob", "138M-1M: CameraInfo distortion model is written");
            Check(cameraInfoReader.ReadFloat64Sequence().Length == 0, "138M-1N: CameraInfo empty distortion coefficients roundtrip");
            Check(cameraInfoReader.ReadFloat64Fixed(9).SequenceEqual(k), "138M-1O: CameraInfo K matrix roundtrips");
            Check(cameraInfoReader.ReadFloat64Fixed(9).SequenceEqual(r), "138M-1P: CameraInfo R matrix roundtrips");
            Check(cameraInfoReader.ReadFloat64Fixed(12).SequenceEqual(p), "138M-1Q: CameraInfo P matrix roundtrips");
            Check(cameraInfoReader.ReadUInt32() == 0U && cameraInfoReader.ReadUInt32() == 0U,
                "138M-1R: CameraInfo binning defaults to zero");
            Check(cameraInfoReader.ReadUInt32() == 0U
                  && cameraInfoReader.ReadUInt32() == 0U
                  && cameraInfoReader.ReadUInt32() == 0U
                  && cameraInfoReader.ReadUInt32() == 0U
                  && !cameraInfoReader.ReadBool(),
                "138M-1S: CameraInfo ROI defaults to empty/no-rectify");
        }

        private static void StandardCameraSchemasAreRegistered()
        {
            Check(FoxgloveRos2MsgSchemaCatalog.TryGet(Ros2PublisherSchemaNames.SensorCompressedImage, out var compressedEntry)
                  && compressedEntry.Content.Contains("std_msgs/Header header", StringComparison.Ordinal)
                  && compressedEntry.Content.Contains("uint8[] data", StringComparison.Ordinal),
                "138M-2A: standard sensor_msgs/msg/CompressedImage schema resolves for ROS2 publish");
            Check(FoxgloveRos2MsgSchemaCatalog.TryGet(Ros2PublisherSchemaNames.SensorCameraInfo, out var cameraInfoEntry)
                  && cameraInfoEntry.Content.Contains("sensor_msgs/RegionOfInterest roi", StringComparison.Ordinal)
                  && cameraInfoEntry.Content.Contains("float64[9] k", StringComparison.Ordinal)
                  && cameraInfoEntry.Content.Contains("float64[12] p", StringComparison.Ordinal),
                "138M-2B: standard sensor_msgs/msg/CameraInfo schema resolves for ROS2 publish");

            var registry = new DefaultSchemaRegistry();
            Ros2MsgSchemasSetup.RegisterSchemas(registry);
            Check(registry.TryGetSchema(Ros2PublisherSchemaNames.SensorCompressedImage, "ros2msg", out _)
                  && registry.TryGetSchema(Ros2PublisherSchemaNames.SensorCameraInfo, "ros2msg", out _),
                "138M-2C: standard camera schemas register without mutating the Foxglove snapshot");
        }

        private static void SensorUnitProfileExposesCameraContract()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/Lidar/SensorUnitProfile.cs");

            Check(source.Contains("_cameraFrameId = \"os_camera\"", StringComparison.Ordinal)
                  && source.Contains("_cameraImageTopic = \"/unity/sensor/camera/image/compressed\"", StringComparison.Ordinal)
                  && source.Contains("_cameraInfoTopic = \"/unity/sensor/camera/camera_info\"", StringComparison.Ordinal),
                "138M-3A: SensorUnitProfile owns default camera frame and SLAM camera topics");
            Check(source.Contains("public string CameraFrameId", StringComparison.Ordinal)
                  && source.Contains("public string CameraImageTopic", StringComparison.Ordinal)
                  && source.Contains("public string CameraInfoTopic", StringComparison.Ordinal),
                "138M-3B: SensorUnitProfile exposes camera frame/topic properties");
            Check(source.Contains("public LidarTIlExtrinsic ModelCameraToSensor", StringComparison.Ordinal)
                  && source.Contains("public LidarTIlExtrinsic EffectiveCameraToSensor", StringComparison.Ordinal)
                  && source.Contains("public LidarTIlExtrinsic EffectiveCameraToImu", StringComparison.Ordinal)
                  && source.Contains("CopyModelCameraToSensorToOverride", StringComparison.Ordinal),
                "138M-3C: SensorUnitProfile exposes camera extrinsic accessors");
        }

        private static void CameraPublisherHasSensorClockAndStandardImageMode()
        {
            var source = ReadCameraPublisherSources();
            var resolver = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraSensorProfileResolver.cs");
            var editor = Read("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraPublisherEditor.cs");

            Check(source.Contains("_sensorUnitProfile", StringComparison.Ordinal)
                  && source.Contains("_useSharedSensorClock", StringComparison.Ordinal)
                  && source.Contains("SensorCompressedImageReady", StringComparison.Ordinal)
                  && !source.Contains("_publishStandardRos2", StringComparison.Ordinal),
                "138M-4A: core camera publisher exposes a transport-neutral sensor payload without removed ROS toggles");
            Check(source.Contains("ResolveCameraCaptureUnixNs", StringComparison.Ordinal)
                  && source.Contains("GetSharedSensorClockUnixTime(Time.fixedTimeAsDouble)", StringComparison.Ordinal)
                  && source.Contains("ResolveFrameId", StringComparison.Ordinal),
                "138M-4B: camera capture timestamp and frame id resolve through sensor mode");
            Check(source.Contains("new SensorCompressedImageFrame", StringComparison.Ordinal)
                  && source.Contains("SensorCompressedImageReady", StringComparison.Ordinal)
                  && source.Contains("SensorCompressedImageFrame", StringComparison.Ordinal)
                  && !resolver.Contains("Ros2Cdr", StringComparison.Ordinal)
                  && !source.Contains("Ros2Cdr", StringComparison.Ordinal),
                "138M-4C: camera publisher emits a schema-neutral compressed-image handoff frame");
            Check(editor.Contains("Provider Payload", StringComparison.Ordinal)
                  && editor.Contains("Sensor Unit Profile", StringComparison.Ordinal)
                  && editor.Contains("Use Shared Sensor Clock", StringComparison.Ordinal)
                  && !editor.Contains("ROS2 Outputs", StringComparison.Ordinal)
                  && !editor.Contains("Publish CompressedImage DDS", StringComparison.Ordinal),
                "138M-4D: core camera Inspector presents only transport-neutral Provider payload controls");
        }

        private static void CameraInfoPublisherUsesSensorClockAndStandardCameraInfo()
        {
            var publisherBase = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/Publishing/FoxglovePublisherBase.cs");
            var pointCloudPublisher = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.cs");
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraInfoPublisher.cs");
            var editor = Read("Packages/dev.unity2foxglove.sdk/Editor/Publishers/FoxgloveCameraInfoPublisherEditor.cs");

            Check(source.Contains("BuildSensorCameraInfoFrame", StringComparison.Ordinal)
                  && source.Contains("new SensorCameraInfoFrame", StringComparison.Ordinal)
                  && source.Contains("SensorCameraInfoReady", StringComparison.Ordinal)
                  && !source.Contains("Ros2Cdr", StringComparison.Ordinal)
                  && !source.Contains("Ros2PublisherSchemaNames", StringComparison.Ordinal),
                "138M-5A: core CameraInfo publisher emits a schema-neutral Provider handoff frame");
            Check(source.Contains("_sensorUnitProfile", StringComparison.Ordinal)
                  && source.Contains("_imagePublisher", StringComparison.Ordinal)
                  && source.Contains("GetSharedSensorClockUnixTime(Time.fixedTimeAsDouble)", StringComparison.Ordinal)
                  && source.Contains("SensorCameraInfoReady", StringComparison.Ordinal)
                  && source.Contains("SensorCameraInfoFrame", StringComparison.Ordinal),
                "138M-5B: CameraInfo publisher uses sensor clock/profile/image dimensions and emits DDS handoff frames");
            Check(source.Contains("SensorCameraCaptureWidth", StringComparison.Ordinal)
                  && source.Contains("SensorCameraCaptureHeight", StringComparison.Ordinal)
                  && source.Contains("ResolveCameraPoseInParent", StringComparison.Ordinal)
                  && source.Contains("NumericQuaternion.Inverse", StringComparison.Ordinal),
                "138M-5C: CameraInfo matches image capture dimensions and publishes sensor-to-camera TF pose");
            Check(editor.Contains("Standalone CameraInfo", StringComparison.Ordinal)
                  && editor.Contains("Advanced Camera Calibration", StringComparison.Ordinal)
                  && editor.Contains("Optional TF Anchor", StringComparison.Ordinal)
                  && editor.Contains("Image Publisher", StringComparison.Ordinal)
                  && editor.Contains("Use Shared Sensor Clock", StringComparison.Ordinal)
                  && editor.Contains("Publish Camera TF Anchor", StringComparison.Ordinal)
                  && editor.Contains("Advanced Transport", StringComparison.Ordinal),
                "138M-5D: CameraInfo Inspector presents the component as an advanced standalone calibration publisher");
            Check(publisherBase.Contains("SupportsJsonEncoding", StringComparison.Ordinal)
                  && publisherBase.Contains("SupportsProtobufEncoding", StringComparison.Ordinal)
                  && publisherBase.Contains("SupportsMsgPackEncoding", StringComparison.Ordinal)
                  && !publisherBase.Contains("PublisherEffectiveEncoding.Ros2", StringComparison.Ordinal)
                  && source.Contains("SensorCameraInfoReady", StringComparison.Ordinal)
                  && pointCloudPublisher.Contains("PackedPointCloudFrameReady", StringComparison.Ordinal),
                "138M-5E: core encoding stays ROS-free while camera and packed-point-cloud payloads use Provider events");
        }

        private static void OptionalR2fuCameraBridgeStaysInOptionalPackage()
        {
            var bridge = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraNativeBridge.cs");
            var outputPolicy = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2NativeOutputPolicy.cs");
            var nativeSource = ReadDirectory("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native");
            var builder = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Ros2ForUnityCameraMessageBuilder.cs");
            var asmdef = Read("Packages/dev.unity2foxglove.ros2forunity/Runtime/Native/Unity2Foxglove.Ros2ForUnity.Native.asmdef");
            var coreCamera = ReadCameraPublisherSources();
            var coreCameraResolver = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/CameraSensorProfileResolver.cs");
            var coreInfo = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveCameraInfoPublisher.cs");

            Check(asmdef.Contains("\"Unity2Foxglove.Ros2ForUnity.Runtime\"", StringComparison.Ordinal)
                  && asmdef.Contains("\"UNITY2FOXGLOVE_ROS2_FOR_UNITY\"", StringComparison.Ordinal),
                "138M-6A: camera DDS bridge stays behind the optional R2FU runtime boundary");
            Check(bridge.Contains("FindObjectsByType<FoxgloveCameraPublisher>", StringComparison.Ordinal)
                  && bridge.Contains("FindObjectsByType<FoxgloveCameraInfoPublisher>", StringComparison.Ordinal)
                  && bridge.Contains("Ros2NativeOutputPolicy.Enabled", StringComparison.Ordinal)
                  && outputPolicy.Contains("FoxRunRos2TransportProvider", StringComparison.Ordinal)
                  && outputPolicy.Contains("_provider.isActiveAndEnabled", StringComparison.Ordinal)
                  && !outputPolicy.Contains("_ros2NativeEnabled", StringComparison.Ordinal),
                "138M-6B: R2FU camera bridge is automatic and gated by the optional R2FU Provider");
            Check(bridge.Contains("DefaultCompressedImageTopic", StringComparison.Ordinal)
                  && bridge.Contains("DefaultRawImageTopic", StringComparison.Ordinal)
                  && bridge.Contains("DefaultCameraInfoTopic", StringComparison.Ordinal)
                  && bridge.Contains("NormalizeTopic(publisher.SensorCameraImageTopic, DefaultCompressedImageTopic)", StringComparison.Ordinal)
                  && bridge.Contains("NormalizeTopic(publisher.SensorCameraRawImageTopic, DefaultRawImageTopic)", StringComparison.Ordinal)
                  && bridge.Contains("NormalizeTopic(publisher.SensorCameraInfoTopic, DefaultCameraInfoTopic)", StringComparison.Ordinal),
                "138M-6B2: R2FU camera bridge keeps distinct default topics for compressed, raw, and camera-info outputs");
            Check(nativeSource.Contains("CreatePublisher<sensor_msgs.msg.CompressedImage>", StringComparison.Ordinal)
                  && nativeSource.Contains("CreatePublisher<sensor_msgs.msg.CameraInfo>", StringComparison.Ordinal)
                  && nativeSource.Contains("CreatePublisher<tf2_msgs.msg.TFMessage>(TfAnchorTopic)", StringComparison.Ordinal),
                "138M-6C: R2FU camera bridge publishes standard image, CameraInfo, and /tf");
            Check(builder.Contains("BuildCompressedImage(SensorCompressedImageFrame frame", StringComparison.Ordinal)
                  && builder.Contains("BuildCameraInfo(SensorCameraInfoFrame frame", StringComparison.Ordinal)
                  && builder.Contains("Data = frame.Data", StringComparison.Ordinal),
                "138M-6D: R2FU camera message builder maps schema-neutral frames to generated ROS2 messages");
            Check(!coreCamera.Contains("sensor_msgs", StringComparison.Ordinal)
                  && !coreCamera.Contains("tf2_msgs", StringComparison.Ordinal)
                  && !coreCameraResolver.Contains("sensor_msgs", StringComparison.Ordinal)
                  && !coreCameraResolver.Contains("tf2_msgs", StringComparison.Ordinal)
                  && !coreInfo.Contains("sensor_msgs", StringComparison.Ordinal)
                  && !coreInfo.Contains("tf2_msgs", StringComparison.Ordinal),
                "138M-6E: core camera publishers remain ROS-type free");
        }

        private static void MazeDemoUsesCartCameraProductPath()
        {
            var bootstrap = Read("Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Phase138MazeDemoBootstrap.cs");
            var builder = Read("Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Editor/Phase138MazeDemoSceneBuilder.cs");
            var readme = Read("Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/README.md");
            var importedBootstrap = Read("Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.4/Virtual LiDAR Maze Demo/Phase138MazeDemoBootstrap.cs");
            var importedBuilder = Read("Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.4/Virtual LiDAR Maze Demo/Editor/Phase138MazeDemoSceneBuilder.cs");
            var importedReadme = Read("Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.4/Virtual LiDAR Maze Demo/README.md");

            Check(bootstrap.Contains("CartCameraMount", StringComparison.Ordinal)
                  && bootstrap.Contains("FoxgloveCameraInfoPublisher", StringComparison.Ordinal)
                  && bootstrap.Contains("\"_imagePublisher\"", StringComparison.Ordinal)
                  && bootstrap.Contains("_useSharedSensorClock", StringComparison.Ordinal)
                  && bootstrap.Contains("PointCloudOutputMode.Draco", StringComparison.Ordinal)
                  && bootstrap.Contains("/unity/point_cloud_draco", StringComparison.Ordinal)
                  && !bootstrap.Contains("_publishStandardRos2CompressedImage", StringComparison.Ordinal)
                  && !bootstrap.Contains("_publishStandardRos2RawImage", StringComparison.Ordinal)
                  && bootstrap.Contains("/unity/sensor/camera/image/compressed", StringComparison.Ordinal)
                  && bootstrap.Contains("/unity/sensor/camera/camera_info", StringComparison.Ordinal)
                  && bootstrap.Contains("sensorCameraInfoPublisher.enabled = false", StringComparison.Ordinal)
                  && bootstrap.Contains("cartCameraMount.gameObject.SetActive(false)", StringComparison.Ordinal)
                  && bootstrap.Contains("cameraGo.AddComponent<FoxgloveCameraPublisher>()", StringComparison.Ordinal)
                  && bootstrap.Contains("/unity/camera", StringComparison.Ordinal),
                "138M-7A: runtime Maze Demo defaults to a core WebSocket camera/Draco scene without removed ROS fields");
            Check(builder.Contains("CartCameraMount", StringComparison.Ordinal)
                  && builder.Contains("FoxgloveCameraInfoPublisher", StringComparison.Ordinal)
                  && builder.Contains("\"_imagePublisher\"", StringComparison.Ordinal)
                  && builder.Contains("_useSharedSensorClock", StringComparison.Ordinal)
                  && builder.Contains("PointCloudOutputMode.Draco", StringComparison.Ordinal)
                  && builder.Contains("/unity/point_cloud_draco", StringComparison.Ordinal)
                  && !builder.Contains("_publishStandardRos2CompressedImage", StringComparison.Ordinal)
                  && !builder.Contains("_publishStandardRos2RawImage", StringComparison.Ordinal)
                  && builder.Contains("/unity/sensor/camera/image/compressed", StringComparison.Ordinal)
                  && builder.Contains("/unity/sensor/camera/camera_info", StringComparison.Ordinal)
                  && builder.Contains("sensorCamInfoPub.enabled = false", StringComparison.Ordinal)
                  && builder.Contains("cartCameraMount.SetActive(false)", StringComparison.Ordinal)
                  && builder.Contains("camGo.AddComponent<FoxgloveCameraPublisher>()", StringComparison.Ordinal)
                  && builder.Contains("/unity/camera", StringComparison.Ordinal),
                "138M-7B: editor Maze Demo builder defaults to the same WebSocket-friendly scene state");
            Check(bootstrap.Contains("cameraGo.AddComponent<FoxgloveCameraPublisher>()", StringComparison.Ordinal)
                  && !bootstrap.Contains("_publishStandardRos2CompressedImage", StringComparison.Ordinal)
                  && builder.Contains("camGo.AddComponent<FoxgloveCameraPublisher>()", StringComparison.Ordinal)
                  && !builder.Contains("_publishStandardRos2CompressedImage", StringComparison.Ordinal)
                  && !builder.Contains("Phase138VirtualLidarPointCloud2Smoke", StringComparison.Ordinal),
                "138M-7C: overview camera stays on the core path and diagnostic smoke component is absent");
            Check(readme.Contains("/unity/sensor/camera/image/compressed", StringComparison.Ordinal)
                  && readme.Contains("/unity/sensor/camera/camera_info", StringComparison.Ordinal)
                  && readme.Contains("companion transport packages", StringComparison.Ordinal)
                  && !readme.Contains("ROS2 Native (R2FU)", StringComparison.Ordinal)
                  && !readme.Contains("PointCloud2 Native", StringComparison.Ordinal),
                "138M-7D: Maze Demo README keeps core topics and routes optional transports to companion packages");
            Check(importedBootstrap.Contains("CartCameraMount", StringComparison.Ordinal)
                  && importedBootstrap.Contains("FoxgloveCameraInfoPublisher", StringComparison.Ordinal)
                  && importedBootstrap.Contains("\"_imagePublisher\"", StringComparison.Ordinal)
                  && importedBootstrap.Contains("PointCloudOutputMode.Draco", StringComparison.Ordinal)
                  && importedBootstrap.Contains("cartCameraMount.gameObject.SetActive(false)", StringComparison.Ordinal)
                  && importedBootstrap.Contains("cameraGo.AddComponent<FoxgloveCameraPublisher>()", StringComparison.Ordinal)
                  && importedBuilder.Contains("CartCameraMount", StringComparison.Ordinal)
                  && importedBuilder.Contains("FoxgloveCameraInfoPublisher", StringComparison.Ordinal)
                  && importedBuilder.Contains("\"_imagePublisher\"", StringComparison.Ordinal)
                  && importedBuilder.Contains("PointCloudOutputMode.Draco", StringComparison.Ordinal)
                  && importedBuilder.Contains("cartCameraMount.SetActive(false)", StringComparison.Ordinal)
                  && importedBuilder.Contains("camGo.AddComponent<FoxgloveCameraPublisher>()", StringComparison.Ordinal)
                  && !importedBootstrap.Contains("_publishStandardRos2", StringComparison.Ordinal)
                  && !importedBuilder.Contains("_publishStandardRos2", StringComparison.Ordinal)
                  && !importedBuilder.Contains("Phase138VirtualLidarPointCloud2Smoke", StringComparison.Ordinal)
                  && importedReadme.Contains("/unity/sensor/camera/camera_info", StringComparison.Ordinal),
                "138M-7E: imported Unity sample copy matches the opt-in ROS2 camera path and default demo output");
        }

        private static void ValidationRegistryWiresPhase138M()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("--phase138m", StringComparison.Ordinal)
                  && registry.Contains("Phase138MValidation.Validate", StringComparison.Ordinal),
                "138M-8A: validation registry exposes --phase138m");
            Check(project.Contains("Phase138MValidation.cs", StringComparison.Ordinal),
                "138M-8B: test project compiles Phase138M validation");
        }

        private static byte[] InvokeStaticByteArray(string typeName, string methodName, params object[] args)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                           .Select(assembly => assembly.GetType(typeName, throwOnError: false, ignoreCase: false))
                           .FirstOrDefault(foundType => foundType != null)
                       ?? Type.GetType(typeName);
            if (type == null)
                throw new InvalidOperationException("Missing type " + typeName);

            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
                throw new InvalidOperationException("Missing method " + typeName + "." + methodName);

            return (byte[])method.Invoke(null, args);
        }

        private static string Read(string path) => File.ReadAllText(RepoPath(path));

        private static string ReadDirectory(string path)
        {
            var bytes = 0;
            var output = new StringBuilder();
            foreach (var file in Directory.GetFiles(RepoPath(path), "*.cs", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(file);
                output.AppendLine(content);
                bytes += content.Length;
                if (bytes > 8_000_000)
                    throw new InvalidOperationException("Phase138M validation reading too much source in single pass.");
            }
            return output.ToString();
        }

        private static string ReadCameraPublisherSources()
        {
            const string cacheKey = "FoxgloveCameraPublisher*.cs";
            if (SourceCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var dir = RepoPath("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers");
            var output = new StringBuilder();
            foreach (var file in Directory.GetFiles(dir, "FoxgloveCameraPublisher*.cs"))
                output.AppendLine(File.ReadAllText(file));
            var text = output.ToString();
            SourceCache[cacheKey] = text;
            return text;
        }

        private static string RepoPath(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot()
                ?? throw new DirectoryNotFoundException("Could not find repository root for Phase138M validation.");
            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
