// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit
// Purpose: Phase 140-74/75/76/78/79/80/81/82 sample and tooling optimization checks.

using System;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    [Trait("Phase", "140-74")]
    [Trait("Domain", "Harness")]
    public sealed class CoreSdkSampleOptimizationTests
    {
        [Fact]
        public void FullDemoAndPointCloudSamplesKeepHotPathCaches()
        {
            var assetsSetup = TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs");
            var assetsMouse = TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/MouseDragCube.cs");
            var mouseUpdate = TestSources.Slice(assetsMouse, "private void Update()", "    private void HandleRotation");
            var controller = TestSources.Text("Packages/dev.unity2foxglove.sdk/Samples~/Virtual LiDAR Maze Demo/Phase138LidarVehicleController.cs");
            var autoWander = TestSources.Slice(controller, "private void ComputeAutoWander", "        /// <summary>True while");
            var smoke = TestSources.Text("Unity2Foxglove/Assets/Scripts/PointCloud/PointCloudSmokeSource.cs");
            var fanout = TestSources.Text("Unity2Foxglove/Assets/Scripts/PointCloud/Phase88PointCloudFanoutSource.cs");

            Assert.Contains("private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);", assetsSetup, StringComparison.Ordinal);
            Assert.Contains("StrictUtf8.GetString(payload, 0, count)", assetsSetup, StringComparison.Ordinal);
            Assert.DoesNotContain("new UTF8Encoding(false, true).GetString", assetsSetup, StringComparison.Ordinal);
            Assert.Contains("private Camera _camera;", assetsMouse, StringComparison.Ordinal);
            Assert.Contains("_camera = Camera.main;", assetsMouse, StringComparison.Ordinal);
            Assert.Contains("var cam = _camera;", mouseUpdate, StringComparison.Ordinal);
            Assert.Contains("_camera = cam;", mouseUpdate, StringComparison.Ordinal);
            Assert.Contains("SetWanderDirection(Vector3.forward);", controller, StringComparison.Ordinal);
            Assert.Contains("SetWanderDirection(Quaternion.Euler(0f, angle, 0f) * _wanderDirection);", controller, StringComparison.Ordinal);
            Assert.Contains("SetWanderDirection(Quaternion.Euler(0f, jitter, 0f) * _wanderDirection);", controller, StringComparison.Ordinal);
            Assert.Contains("worldVelocity = _wanderDirection * _moveSpeed;", autoWander, StringComparison.Ordinal);
            Assert.DoesNotContain("_wanderDirection.normalized", autoWander, StringComparison.Ordinal);
            Assert.Contains("frame.Points.Capacity = count;", smoke, StringComparison.Ordinal);
            Assert.Contains("frame.Points.Capacity = count;", fanout, StringComparison.Ordinal);
        }

        [Fact]
        public void Ros2BridgeSampleStatusFormatsOnlyOnChange()
        {
            var controller = TestSources.Text("Packages/dev.unity2foxglove.sdk/Samples~/Ros2BridgeSample/Scripts/Ros2BridgeSampleController.cs");
            var update = TestSources.Slice(controller, "private void Update()", "    private void UpdateStatusIfChanged");

            Assert.Contains("private bool _lastRos2BridgeEnabled;", controller, StringComparison.Ordinal);
            Assert.Contains("private bool _hasStatusSnapshot;", controller, StringComparison.Ordinal);
            Assert.Contains("private void UpdateStatusIfChanged(Ros2BridgeStatsSnapshot stats, bool ros2BridgeEnabled)", controller, StringComparison.Ordinal);
            Assert.Contains("UpdateStatusIfChanged(stats, _manager.Ros2BridgeEnabled);", update, StringComparison.Ordinal);
            Assert.Contains("_status = $\"ROS2 Bridge", controller, StringComparison.Ordinal);
            Assert.DoesNotContain("_status = $\"ROS2 Bridge", update, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14074MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_74Validation.cs", "--phase140-74", "Phase140_74Validation.Validate");
    }

    [Trait("Phase", "140-75")]
    [Trait("Domain", "Harness")]
    public sealed class DemoScaleParameterOptimizationTests
    {
        [Fact]
        public void AssetsAndPackageDemosUseParameterChangeEventsForScale()
        {
            VerifyScaleEventShape(TestSources.Text("Unity2Foxglove/Assets/Scripts/FullDemoVisualization/FoxgloveDemoSetup.cs"));
            VerifyScaleEventShape(TestSources.Text("Packages/dev.unity2foxglove.sdk/Samples~/FullDemoVisualization/Scripts/FoxgloveDemoSetup.cs"));
        }

        [Fact]
        public void Phase14075MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_75Validation.cs", "--phase140-75", "Phase140_75Validation.Validate");

        private static void VerifyScaleEventShape(string source)
        {
            var initialize = TestSources.Slice(source, "private bool TryInitializeDemo()", "    /// <summary>\r\n    /// Unsubscribes");
            var update = TestSources.Slice(source, "private void Update()", "    private GameObject FindCube()");
            var parameterChanged = TestSources.Slice(source, "private void OnParameterChanged", "    /// <summary>\r\n    /// When the scene cube color changes");

            Assert.Contains("var initialScale = rt.Parameters.GetWireParameter(\"/cube/scale\")?.Value;", initialize, StringComparison.Ordinal);
            Assert.Contains("ApplyScaleFromParameter(initialScale);", initialize, StringComparison.Ordinal);
            Assert.Contains("name == \"/cube/scale\"", parameterChanged, StringComparison.Ordinal);
            Assert.Contains("ApplyScaleFromParameter(scaleValue)", parameterChanged, StringComparison.Ordinal);
            Assert.Contains("private void ApplyScaleFromParameter(JToken value)", source, StringComparison.Ordinal);
            Assert.Contains("private static bool TryReadScale(JToken value, out float clamped, out string reason)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetWireParameter(\"/cube/scale\")", update, StringComparison.Ordinal);
            Assert.DoesNotContain("ApplyScaleFromParameter", update, StringComparison.Ordinal);
        }
    }

    [Trait("Phase", "140-76")]
    [Trait("Domain", "Harness")]
    public sealed class OpenH264ProbeOptimizationTests
    {
        [Fact]
        public void ProbePublisherReusesReadbackBuffersAndCachesLayout()
        {
            var source = TestSources.Text("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbePublisher.cs");
            var readback = TestSources.Slice(source, "private void OnReadbackComplete", "    private bool EnsureSidecarStarted");
            var ensure = TestSources.Slice(source, "private void EnsureCaptureResources", "    private void SyncCaptureCameraIfDirty");
            var sync = TestSources.Slice(source, "private void SyncCaptureCameraIfDirty", "    private void CompletePendingReadback");
            var layout = TestSources.Slice(source, "private bool TryGetProbeFrameLayout", "    private static int PositiveDimension");

            Assert.Contains("private byte[] _rgbBuffer;", source, StringComparison.Ordinal);
            Assert.Contains("private byte[] _i420Buffer;", source, StringComparison.Ordinal);
            Assert.Contains("private void EnsureFrameBuffers(int rgbBytes, int i420Bytes)", source, StringComparison.Ordinal);
            Assert.Contains("var rgbData = request.GetData<byte>();", readback, StringComparison.Ordinal);
            Assert.Contains("EnsureFrameBuffers(rgbData.Length, i420Bytes);", readback, StringComparison.Ordinal);
            Assert.Contains("rgbData.CopyTo(_rgbBuffer);", readback, StringComparison.Ordinal);
            Assert.Contains("TryConvertRgb24ToI420(_rgbBuffer, width, height, _i420Buffer", readback, StringComparison.Ordinal);
            Assert.Contains("sidecar.TrySubmitFrame(_i420Buffer)", readback, StringComparison.Ordinal);
            Assert.DoesNotContain(".ToArray()", readback, StringComparison.Ordinal);
            Assert.DoesNotContain("new byte[i420Bytes]", readback, StringComparison.Ordinal);
            Assert.Contains("private bool _captureCameraDirty;", source, StringComparison.Ordinal);
            Assert.Contains("SyncCaptureCameraIfDirty(width, height);", ensure, StringComparison.Ordinal);
            Assert.DoesNotContain("_captureCamera.CopyFrom(_sourceCamera);", ensure, StringComparison.Ordinal);
            Assert.Contains("if (!_captureCameraDirty", sync, StringComparison.Ordinal);
            Assert.Contains("_captureCamera.CopyFrom(_sourceCamera);", sync, StringComparison.Ordinal);
            Assert.Contains("private bool _cachedProbeLayoutValid;", source, StringComparison.Ordinal);
            Assert.Contains("private int _cachedProbeLayoutSourceWidth;", source, StringComparison.Ordinal);
            Assert.Contains("private int _cachedProbeLayoutSourceHeight;", source, StringComparison.Ordinal);
            Assert.Contains("private int _cachedProbeI420Bytes;", source, StringComparison.Ordinal);
            Assert.Contains("_cachedProbeLayoutSourceWidth == _width", layout, StringComparison.Ordinal);
            Assert.Contains("_cachedProbeLayoutSourceHeight == _height", layout, StringComparison.Ordinal);
            Assert.Contains("return _cachedProbeLayoutValid;", layout, StringComparison.Ordinal);
            Assert.Contains("OpenH264ProbeSidecarOptions.TryComputeFrameByteCount", layout, StringComparison.Ordinal);
        }

        [Fact]
        public void SidecarKeepsDefensiveCopyOwnershipBoundary()
        {
            var sidecar = TestSources.Text("Unity2Foxglove/Assets/Experimental/OpenH264/OpenH264ProbeSidecar.cs");
            var submit = TestSources.Slice(sidecar, "public bool TrySubmitFrame(byte[] i420Frame)", "    public bool TryDequeueAccessUnit");

            Assert.Contains("var copy = new byte[i420Frame.Length];", submit, StringComparison.Ordinal);
            Assert.Contains("Buffer.BlockCopy(i420Frame, 0, copy, 0, i420Frame.Length);", submit, StringComparison.Ordinal);
            Assert.Contains("_inputFrames.Enqueue(copy);", submit, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14076MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_76Validation.cs", "--phase140-76", "Phase140_76Validation.Validate");
    }

    [Trait("Phase", "140-78")]
    [Trait("Domain", "Harness")]
    public sealed class ReleaseToolOptimizationTests
    {
        [Fact]
        public void ReleaseValidatorsAvoidUnboundedReadsAndRepeatedWalks()
        {
            var inspect = TestSources.Text("Scripts/release/inspect_r2fu_runtime_artifact.py");
            var summary = TestSources.Slice(inspect, "def summarize_components", "def inspect_zip");
            var build = TestSources.Text("Scripts/release/build_r2fu_runtime_package.py");
            var metas = TestSources.Slice(build, "def write_generated_metas", "def package_json");
            var validate = TestSources.Text("Scripts/release/validate_package.py");
            var artifacts = TestSources.Slice(validate, "def check_package_build_artifacts", "def check_google_protobuf_collision");
            var runCi = TestSources.Text("Scripts/release/run_ci.py");

            Assert.Contains("def sha256_zip_entry(archive: zipfile.ZipFile, info: zipfile.ZipInfo) -> str:", inspect, StringComparison.Ordinal);
            Assert.Contains("with archive.open(info) as stream:", inspect, StringComparison.Ordinal);
            Assert.Contains("for chunk in iter(lambda: stream.read(1024 * 1024), b\"\"):", inspect, StringComparison.Ordinal);
            Assert.DoesNotContain("archive.read(info.filename)", inspect, StringComparison.Ordinal);
            Assert.DoesNotContain("sha256_bytes(data)", inspect, StringComparison.Ordinal);
            Assert.Contains("lower_names = [(name, name.lower()) for name in names]", summary, StringComparison.Ordinal);
            Assert.Contains("for name, lower in lower_names", summary, StringComparison.Ordinal);
            Assert.DoesNotContain("name.lower() for pattern", summary, StringComparison.Ordinal);
            Assert.Contains("paths = list(package.rglob(\"*\"))", metas, StringComparison.Ordinal);
            Assert.Contains("directories = sorted((path for path in paths if path.is_dir())", metas, StringComparison.Ordinal);
            Assert.Contains("files = sorted((path for path in paths if path.is_file())", metas, StringComparison.Ordinal);
            Assert.Equal(1, TestSources.Count(metas, "package.rglob(\"*\")"));
            Assert.Contains("if path.name in forbidden_dirs and path.is_dir():", artifacts, StringComparison.Ordinal);
            Assert.DoesNotContain("if path.is_dir() and path.name in forbidden_dirs:", artifacts, StringComparison.Ordinal);
            Assert.Contains("for name, ok in results.items():", runCi, StringComparison.Ordinal);
            Assert.Contains("failed = [n for n, ok in results.items() if not ok]", runCi, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14078MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_78Validation.cs", "--phase140-78", "Phase140_78Validation.Validate");
    }

    [Trait("Phase", "140-79")]
    [Trait("Domain", "Harness")]
    public sealed class CoreSmokeScriptOptimizationTests
    {
        [Fact]
        public void CoreSmokeScriptsAvoidHotPathCopies()
        {
            var phase40 = TestSources.Text("Scripts/smoke/phase40_slow_camera_client.py");
            var handshake = TestSources.Slice(phase40, "def read_handshake_response", "def build_websocket_upgrade_request");
            var phase139 = TestSources.Text("Scripts/smoke/phase139_e2e_integration_smoke.py");
            var collectMessages = TestSources.Slice(phase139, "async def collect_messages", "def summarize_observed");
            var collectAdvertisements = TestSources.Slice(phase139, "async def collect_advertisements", "async def collect_messages");
            var phase68 = TestSources.Text("Scripts/smoke/phase68_indexed_reader_smoke.py");
            var topicRate = TestSources.Text("Scripts/smoke/topic_rate_probe.py");

            Assert.Contains("HANDSHAKE_READ_CHUNK_BYTES = 256", phase40, StringComparison.Ordinal);
            Assert.Contains("to_read = min(HANDSHAKE_READ_CHUNK_BYTES, MAX_HANDSHAKE_RESPONSE_BYTES - len(response))", handshake, StringComparison.Ordinal);
            Assert.Contains("chunk = sock.recv(to_read)", handshake, StringComparison.Ordinal);
            Assert.Contains("response.extend(chunk)", handshake, StringComparison.Ordinal);
            Assert.DoesNotContain("sock.recv(HANDSHAKE_READ_BYTES)", handshake, StringComparison.Ordinal);
            Assert.Contains("data = frame if isinstance(frame, bytes) else bytes(frame)", collectMessages, StringComparison.Ordinal);
            Assert.DoesNotContain("data = bytes(frame)", collectMessages, StringComparison.Ordinal);
            Assert.Contains("advertised_topics: set[str] = set()", collectAdvertisements, StringComparison.Ordinal);
            Assert.Contains("advertised_topics.add(channel.get(\"topic\"))", collectAdvertisements, StringComparison.Ordinal);
            Assert.DoesNotContain("advertised_topics = {channel.get(\"topic\") for channel in channels.values()}", collectAdvertisements, StringComparison.Ordinal);
            Assert.Contains("sorted(matches, key=lambda path: path.stat().st_mtime", phase68, StringComparison.Ordinal);
            Assert.Contains("ordered = sorted(values)", topicRate, StringComparison.Ordinal);
            Assert.Contains("p50={percentile(values, 50):.2f}", topicRate, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14079MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_79Validation.cs", "--phase140-79", "Phase140_79Validation.Validate");
    }

    [Trait("Phase", "140-80")]
    [Trait("Domain", "Harness")]
    public sealed class Ros2SmokeBridgeOptimizationTests
    {
        [Fact]
        public void BridgeAndRos2SmokeScriptsAvoidPayloadCopies()
        {
            var bridge = TestSources.Text("Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp");
            var payload = TestSources.Slice(bridge, "PayloadView payload_for_publish", "class BridgeNode");
            var topicRate = TestSources.Text("Scripts/smoke/topic_rate_probe.py");
            var env = TestSources.Text("Scripts/smoke/_ros2_windows_env.py");
            var visibleWindows = TestSources.Slice(env, "def visible_windows_for_pid", "def launch_rviz");
            var phase139 = TestSources.Text("Scripts/smoke/phase139_e2e_integration_smoke.py");

            Assert.Contains("struct PayloadView", bridge, StringComparison.Ordinal);
            Assert.Contains("return PayloadView{frame.payload.data(), frame.payload.size()};", payload, StringComparison.Ordinal);
            Assert.Contains("return PayloadView{frame.payload.data() + 4, frame.payload.size() - 4};", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("return frame.payload;", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("std::vector<uint8_t>(frame.payload.begin() + 4", payload, StringComparison.Ordinal);
            VerifyProbe("Scripts/smoke/topic_rate_probe.py", removePayloadLengthSlice: true);
            VerifyProbe("Scripts/smoke/pointcloud_qos_probe.py", removePayloadLengthSlice: false);
            VerifyProbe("Scripts/smoke/compressed_pointcloud_draco_probe.py", removePayloadLengthSlice: false);
            Assert.Contains("_ENUM_WINDOWS_PROC_TYPE = ctypes.WINFUNCTYPE", env, StringComparison.Ordinal);
            Assert.Contains("_USER32 = ctypes.windll.user32", env, StringComparison.Ordinal);
            Assert.Contains("if _USER32 is None or _ENUM_WINDOWS_PROC_TYPE is None:", visibleWindows, StringComparison.Ordinal);
            Assert.Contains("_USER32.EnumWindows(_ENUM_WINDOWS_PROC_TYPE(callback), 0)", visibleWindows, StringComparison.Ordinal);
            Assert.DoesNotContain("ctypes.WINFUNCTYPE", visibleWindows, StringComparison.Ordinal);
            Assert.DoesNotContain("ctypes.windll.user32", visibleWindows, StringComparison.Ordinal);
            Assert.Contains("data = frame if isinstance(frame, bytes) else bytes(frame)", phase139, StringComparison.Ordinal);
            Assert.Contains("buffer.assign(count, 0);", bridge, StringComparison.Ordinal);
            Assert.Contains("ordered = sorted(values)", topicRate, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14080MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_80Validation.cs", "--phase140-80", "Phase140_80Validation.Validate");

        private static void VerifyProbe(string relativePath, bool removePayloadLengthSlice)
        {
            var source = TestSources.Text(relativePath);
            Assert.Contains("struct.unpack_from(\"<I\", frame, SUBSCRIPTION_ID_START)", source, StringComparison.Ordinal);
            Assert.Contains("struct.unpack_from(\"<Q\", frame, LOG_TIME_START)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("struct.unpack(\"<I\", frame[SUBSCRIPTION_ID_START:SUBSCRIPTION_ID_END])", source, StringComparison.Ordinal);
            Assert.DoesNotContain("struct.unpack(\"<Q\", frame[LOG_TIME_START:LOG_TIME_END])", source, StringComparison.Ordinal);
            if (removePayloadLengthSlice)
            {
                Assert.Contains("total_payload_bytes += max(len(frame) - MESSAGE_PAYLOAD_START, 0)", source, StringComparison.Ordinal);
                Assert.DoesNotContain("payload = frame[MESSAGE_PAYLOAD_START:]", source, StringComparison.Ordinal);
            }
        }
    }

    [Trait("Phase", "140-81")]
    [Trait("Domain", "Harness")]
    public sealed class GeneratorNativeToolOptimizationTests
    {
        [Fact]
        public void GeneratorAndNativeProbesReuseInputBuffers()
        {
            var catalog = TestSources.Text("Scripts/schema/generate_ros2_msg_schema_catalog.py");
            var generate = TestSources.Slice(catalog, "def generate(input_dir: Path, output: Path) -> str:", "    entries = ");
            var sourceTreeSha = TestSources.Slice(catalog, "def source_tree_sha", "def try_source_commit");
            var openh264 = TestSources.Text("Scripts/native/openh264_probe/openh264_probe_encoder.cpp");
            var packageOpenh264 = TestSources.Text("Packages/dev.unity2foxglove.sdk/Editor/Native/OpenH264/openh264_probe_encoder.cpp");
            var writeAccessUnit = TestSources.Slice(openh264, "void WriteAccessUnit", "int main");
            var openh264Main = TestSources.Slice(openh264, "int main(int argc, char** argv)", "    if (exitCode == 0)");
            var draco = TestSources.Text("Scripts/native/draco_probe/draco_probe_encoder.cpp");
            var processOneFrame = TestSources.Slice(draco, "bool ProcessOneFrame", "}  // namespace");
            var dracoMain = TestSources.Slice(draco, "int main()", "  return 0;");
            var coupling = TestSources.Text("Scripts/architecture/analyze_coupling.py");
            var cycles = TestSources.Slice(coupling, "def find_asmdef_cycles", "def find_default_test_private_references");

            Assert.Contains("file_bytes = {path: path.read_bytes() for path in files}", catalog, StringComparison.Ordinal);
            Assert.Contains("def decode_schema_text(data: bytes) -> str:", catalog, StringComparison.Ordinal);
            Assert.Contains("return data.decode(\"utf-8\").replace(\"\\r\\n\", \"\\n\").replace(\"\\r\", \"\\n\")", catalog, StringComparison.Ordinal);
            Assert.Contains("local_sources = {path.stem: decode_schema_text(file_bytes[path]) for path in files}", catalog, StringComparison.Ordinal);
            Assert.Contains("tree_sha = source_tree_sha(files, file_bytes)", catalog, StringComparison.Ordinal);
            Assert.Contains("source_sha = hashlib.sha256(file_bytes[path]).hexdigest()", catalog, StringComparison.Ordinal);
            Assert.Contains("sha.update(file_bytes[path])", sourceTreeSha, StringComparison.Ordinal);
            Assert.DoesNotContain("path.read_text", generate, StringComparison.Ordinal);
            Assert.Equal(1, TestSources.Count(generate, "path.read_bytes()"));
            Assert.Contains("void WriteAccessUnit(const SFrameBSInfo& info, std::vector<uint8_t>& accessUnit)", openh264, StringComparison.Ordinal);
            Assert.Contains("void WriteAccessUnit(const SFrameBSInfo& info, std::vector<uint8_t>& accessUnit)", packageOpenh264, StringComparison.Ordinal);
            AssertWindowsMinMaxMacrosAreDisabledBeforeWindowsHeader(openh264);
            AssertWindowsMinMaxMacrosAreDisabledBeforeWindowsHeader(packageOpenh264);
            Assert.Contains("accessUnit.clear();", writeAccessUnit, StringComparison.Ordinal);
            Assert.DoesNotContain("std::vector<uint8_t> accessUnit;", writeAccessUnit, StringComparison.Ordinal);
            Assert.Contains("std::vector<uint8_t> accessUnit;", openh264Main, StringComparison.Ordinal);
            Assert.Contains("WriteAccessUnit(info, accessUnit);", openh264Main, StringComparison.Ordinal);
            Assert.Contains("bool ProcessOneFrame(std::vector<float>* xyz)", draco, StringComparison.Ordinal);
            Assert.Contains("xyz->resize(float_count);", processOneFrame, StringComparison.Ordinal);
            Assert.Contains("ReadExact(reinterpret_cast<char*>(xyz->data())", processOneFrame, StringComparison.Ordinal);
            Assert.Contains("EncodePointCloud(*xyz, point_count, &buffer)", processOneFrame, StringComparison.Ordinal);
            Assert.DoesNotContain("std::vector<float> xyz(float_count);", processOneFrame, StringComparison.Ordinal);
            Assert.Contains("std::vector<float> xyz;", dracoMain, StringComparison.Ordinal);
            Assert.Contains("ProcessOneFrame(&xyz)", dracoMain, StringComparison.Ordinal);
            Assert.Contains("stack.append(node)", cycles, StringComparison.Ordinal);
            Assert.Contains("stack.pop()", cycles, StringComparison.Ordinal);
            Assert.Contains("visit(child, stack)", cycles, StringComparison.Ordinal);
            Assert.DoesNotContain("path + [current]", cycles, StringComparison.Ordinal);
            Assert.DoesNotContain("stack + [node]", cycles, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14081MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_81Validation.cs", "--phase140-81", "Phase140_81Validation.Validate");

        private static void AssertWindowsMinMaxMacrosAreDisabledBeforeWindowsHeader(string source)
        {
            var nomInMax = source.IndexOf("#define NOMINMAX", StringComparison.Ordinal);
            var windows = source.IndexOf("#include <windows.h>", StringComparison.Ordinal);

            Assert.True(nomInMax >= 0, "OpenH264 helper sources must define NOMINMAX before including windows.h.");
            Assert.True(windows >= 0, "OpenH264 helper sources must include windows.h through the guarded Windows block.");
            Assert.True(nomInMax < windows, "NOMINMAX must be defined before windows.h so std::numeric_limits<T>::min/max compile on MSVC.");
        }
    }

    [Trait("Phase", "140-82")]
    [Trait("Domain", "Harness")]
    public sealed class FoxgloveExtensionOptimizationTests
    {
        [Fact]
        public void CursorBridgeKeepsDomAndFormattingOffRenderHotPath()
        {
            var source = TestSources.Text("Tools/foxglove-extensions/unity-cursor-bridge/src/index.ts");
            var buildPanelDom = TestSources.Slice(source, "function buildPanelDom", "export function initPanel");
            var renderLoop = TestSources.Slice(source, "context.onRender = (renderState, done) =>", "  return () =>");
            var shouldSend = TestSources.Slice(source, "export function shouldSendCursor", "function buildPanelDom");
            var formatter = TestSources.Slice(source, "function formatReplayTimeUtc", "async function sendCursor");

            Assert.Contains("root.innerHTML", buildPanelDom, StringComparison.Ordinal);
            Assert.Contains("querySelector", buildPanelDom, StringComparison.Ordinal);
            Assert.Contains("endpointInput.value = state.endpoint", buildPanelDom, StringComparison.Ordinal);
            Assert.Contains("panel.replayTime.textContent", renderLoop, StringComparison.Ordinal);
            Assert.Contains("panel.unityStatus.textContent", renderLoop, StringComparison.Ordinal);
            Assert.DoesNotContain("innerHTML", renderLoop, StringComparison.Ordinal);
            Assert.DoesNotContain("querySelector", renderLoop, StringComparison.Ordinal);
            Assert.DoesNotContain("addEventListener", renderLoop, StringComparison.Ordinal);
            Assert.DoesNotContain("replaceChildren", renderLoop, StringComparison.Ordinal);
            Assert.DoesNotContain("escapeHtml", renderLoop, StringComparison.Ordinal);
            // Phase 140K Stage 1 promoted the cursor rate to a panel setting; the render loop now
            // derives the interval from state.maxHz (cheap arithmetic, still no DOM/formatting work).
            Assert.Contains("const DEFAULT_MAX_HZ = 60;", source, StringComparison.Ordinal);
            Assert.Contains("const minIntervalMs = 1000 / state.maxHz;", renderLoop, StringComparison.Ordinal);
            Assert.DoesNotContain("1000 / DEFAULT_MAX_HZ", renderLoop, StringComparison.Ordinal);
            Assert.Contains("lastCursorSec", source, StringComparison.Ordinal);
            Assert.Contains("lastCursorNsec", source, StringComparison.Ordinal);
            Assert.Contains("lastSec: number", shouldSend, StringComparison.Ordinal);
            Assert.Contains("lastNsec: number", shouldSend, StringComparison.Ordinal);
            Assert.Contains("currentTime.sec !== lastSec || currentTime.nsec !== lastNsec", shouldSend, StringComparison.Ordinal);
            Assert.DoesNotContain("cursorKey", renderLoop, StringComparison.Ordinal);
            Assert.Contains("type ReplayTimeDisplayCache", source, StringComparison.Ordinal);
            Assert.Contains("const replayTimeCache", source, StringComparison.Ordinal);
            Assert.Contains("cache.lastSec", formatter, StringComparison.Ordinal);
            Assert.Contains("cache.text", formatter, StringComparison.Ordinal);
            Assert.Contains("iso.slice(0, 10)", formatter, StringComparison.Ordinal);
            Assert.Contains("iso.slice(11, iso.length - 1)", formatter, StringComparison.Ordinal);
            Assert.DoesNotContain(".replace(\"T\"", formatter, StringComparison.Ordinal);
            Assert.DoesNotContain(".replace(\"Z\"", formatter, StringComparison.Ordinal);
            Assert.Contains("formatReplayTimeUtc(currentTime, replayTimeCache)", renderLoop, StringComparison.Ordinal);
        }

        [Fact]
        public void Phase14082MigratedConsolePhaseIsRemoved()
            => TestSources.AssertConsolePhaseRemoved("Phase140_82Validation.cs", "--phase140-82", "Phase140_82Validation.Validate");
    }
}
