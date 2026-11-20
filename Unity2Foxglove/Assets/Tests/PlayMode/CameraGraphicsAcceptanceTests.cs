using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas.Camera;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity2Foxglove.Tests.PlayMode
{
    public sealed class CameraGraphicsAcceptanceTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        [Category("GraphicsAcceptance")]
        public IEnumerator GraphicsAcceptance_ResizeDuringReadbackProducesNoErrors()
        {
            RequireGraphicsReadback();
            var manager = CreateManager();
            var cameraObject = CreateCamera("resize-errors", 1024, 1024, manager, out var publisher);
            var errors = new List<string>();
            Application.logMessageReceived += CaptureErrors;
            try
            {
                publisher.SensorRawImageReady += _ => { };
                yield return null;
                var resources = GetField(publisher, "_captureResources").GetValue(publisher);
                var retiredTexture = (RenderTexture)resources.GetType().GetProperty("CaptureRenderTexture").GetValue(resources);
                var pendingField = GetField(publisher, "_pendingRequests");
                for (var tick = 0; tick < 180 && (int)pendingField.GetValue(publisher) == 0; tick++)
                    yield return null;
                Assert.That((int)pendingField.GetValue(publisher), Is.GreaterThan(0), "Camera publisher did not expose a pending GPU readback.");
                SetField(publisher, "_width", 64);
                SetField(publisher, "_height", 48);
                publisher.GetType().GetMethod("EnsureCaptureResources", PrivateInstance).Invoke(publisher, null);
                Assert.That(retiredTexture, Is.Not.Null);
                Assert.That(retiredTexture.IsCreated(), Is.True, "The retired render texture was released before its readback generation drained.");
                yield return WaitForReleasedTexture(retiredTexture);
                Assert.That(errors, Is.Empty, string.Join(Environment.NewLine, errors));
            }
            finally
            {
                Application.logMessageReceived -= CaptureErrors;
                DestroyObjects(cameraObject, manager.gameObject);
            }

            void CaptureErrors(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception)
                    errors.Add(condition + Environment.NewLine + stackTrace);
            }
        }

        [UnityTest]
        [Category("GraphicsAcceptance")]
        public IEnumerator GraphicsAcceptance_ResizeFramesKeepDimensionsAndPayloadsConsistent()
        {
            RequireGraphicsReadback();
            var manager = CreateManager();
            var cameraObject = CreateCamera("resize-frames", 32, 24, manager, out var publisher);
            var frames = new List<SensorRawImageFrame>();
            try
            {
                publisher.SensorRawImageReady += frames.Add;
                yield return WaitForFrameCount(frames, 1);
                yield return WaitForPendingReadback(publisher);
                SetField(publisher, "_width", 64);
                SetField(publisher, "_height", 48);
                var previousGeneration = (int)GetField(publisher, "_captureGeneration").GetValue(publisher);
                var previousTimestamp = frames[frames.Count - 1].UnixNs;
                var firstPostResizeFrame = frames.Count;
                publisher.enabled = false;
                publisher.enabled = true;
                var currentGeneration = (int)GetField(publisher, "_captureGeneration").GetValue(publisher);
                Assert.That(currentGeneration, Is.GreaterThan(previousGeneration));
                yield return WaitForResizedFrame(frames);

                Assert.That(frames.Exists(frame => frame.Width == 32 && frame.Height == 24), Is.True);
                Assert.That(frames.Exists(frame => frame.Width == 64 && frame.Height == 48), Is.True);
                for (var index = firstPostResizeFrame; index < frames.Count; index++)
                {
                    var frame = frames[index];
                    Assert.That(frame.UnixNs, Is.GreaterThanOrEqualTo(previousTimestamp), "An older readback timestamp was published after the resize generation changed.");
                    Assert.That(frame.Width, Is.EqualTo(64));
                    Assert.That(frame.Height, Is.EqualTo(48));
                    Assert.That(frame.Data.Length, Is.EqualTo(frame.Width * frame.Height * 3));
                    Assert.That(frame.Step, Is.EqualTo(frame.Width * 3));
                }
            }
            finally
            {
                DestroyObjects(cameraObject, manager.gameObject);
            }
        }

        [UnityTest]
        [Category("GraphicsAcceptance")]
        public IEnumerator GraphicsAcceptance_CameraInfoUsesImageSourceCamera()
        {
            RequireGraphicsReadback();
            var manager = CreateManager();
            var imageObject = CreateCamera("image-source", 40, 30, manager, out var imagePublisher);
            var imageSourceCamera = imageObject.GetComponent<Camera>();
            imageSourceCamera.fieldOfView = 60f;
            imageSourceCamera.usePhysicalProperties = true;
            imageSourceCamera.lensShift = new Vector2(0.12f, -0.08f);
            var configuredCameraObject = new GameObject("configured-camera");
            var configuredCamera = configuredCameraObject.AddComponent<Camera>();
            configuredCamera.fieldOfView = 25f;
            var infoObject = new GameObject("camera-info");
            var infoPublisher = infoObject.AddComponent<FoxgloveCameraInfoPublisher>();
            SetField(infoPublisher, "_manager", manager);
            SetField(infoPublisher, "_imagePublisher", imagePublisher);
            SetField(infoPublisher, "_sourceCamera", configuredCamera);
            SetField(infoPublisher, "_autoFromCamera", true);
            var frames = new List<SensorCameraInfoFrame>();
            try
            {
                infoPublisher.SensorCameraInfoReady += frames.Add;
                yield return WaitForFrameCount(frames, 1);
                var frame = frames[0];
                Assert.That(frame.Width, Is.EqualTo(imagePublisher.SensorCameraCaptureWidth));
                Assert.That(frame.Height, Is.EqualTo(imagePublisher.SensorCameraCaptureHeight));
                var projection = imageSourceCamera.projectionMatrix;
                Assert.That(Math.Abs(projection.m02), Is.GreaterThan(0.0001f));
                Assert.That(Math.Abs(projection.m12), Is.GreaterThan(0.0001f));
                AssertCameraInfoProjection(frame, imageSourceCamera);
            }
            finally
            {
                DestroyObjects(imageObject, configuredCameraObject, infoObject, manager.gameObject);
            }
        }

        [UnityTest]
        [Category("ManagerAcceptance")]
        public IEnumerator ManagerDisableAndDestroyInvokeProductionTeardown()
        {
            var disabledManager = CreateManager();
            var destroyedManager = CreateManager();
            try
            {
                SetField(disabledManager, "_port", GetFreeLoopbackPort());
                SetField(destroyedManager, "_port", GetFreeLoopbackPort());
                disabledManager.StartServer();
                Assert.That(disabledManager.IsRunning, Is.True);
                disabledManager.enabled = false;
                Assert.That(disabledManager.IsRunning, Is.False);

                destroyedManager.StartServer();
                Assert.That(destroyedManager.IsRunning, Is.True);
                var destroyedRuntime = destroyedManager.Runtime;
                Assert.That(destroyedRuntime, Is.Not.Null);
                SetField(destroyedManager, "_replayCursorEndpoint", new Unity.FoxgloveSDK.Core.UnityReplayCursorEndpoint());
                var onDestroy = typeof(FoxgloveManager).GetMethod(
                    "OnDestroy",
                    PrivateInstance);
                Assert.That(onDestroy, Is.Not.Null);
                onDestroy.Invoke(destroyedManager, null);
                Assert.That(destroyedManager.IsRunning, Is.False);
                Assert.That(GetField(destroyedManager, "_runtime").GetValue(destroyedManager), Is.Null);
                Assert.Throws<ObjectDisposedException>(
                    () => destroyedRuntime.Start("destroyed-runtime-probe", "127.0.0.1", GetFreeLoopbackPort()));
                Assert.That(GetField(destroyedManager, "_replayCursorEndpoint").GetValue(destroyedManager), Is.Null);
            }
            finally
            {
                DestroyObjects(disabledManager.gameObject, destroyedManager.gameObject);
            }

            yield return null;
        }

        private static int GetFreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static void RequireGraphicsReadback()
        {
            if (!SystemInfo.supportsAsyncGPUReadback)
                Assert.Ignore("graphics device unavailable");
        }

        private static FoxgloveManager CreateManager()
        {
            var managerObject = new GameObject("camera-acceptance-manager");
            managerObject.SetActive(false);
            var manager = managerObject.AddComponent<FoxgloveManager>();
            SetField(manager, "_startOnEnable", false);
            managerObject.SetActive(true);
            return manager;
        }

        private static GameObject CreateCamera(
            string name,
            int width,
            int height,
            FoxgloveManager manager,
            out FoxgloveCameraPublisher publisher)
        {
            var cameraObject = new GameObject(name);
            cameraObject.SetActive(false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            publisher = cameraObject.AddComponent<FoxgloveCameraPublisher>();
            SetField(publisher, "_manager", manager);
            SetField(publisher, "_warnIfManagerMissing", false);
            SetField(publisher, "_width", width);
            SetField(publisher, "_height", height);
            SetField(publisher, "_useAsyncJpeg", false);
            SetField(publisher, "_maxCaptureRateHz", 0f);
            SetField(publisher, "_publishRateSource", PublisherRateSource.OverrideLocal);
            SetField(publisher, "_publishRateHz", 0f);
            SetField(publisher, "_cameraHealthMode", Enum.Parse(GetField(publisher, "_cameraHealthMode").FieldType, "Off"));
            cameraObject.SetActive(true);
            return cameraObject;
        }

        private static IEnumerator WaitForFrameCount(List<SensorRawImageFrame> frames, int count)
        {
            for (var tick = 0; tick < 180 && frames.Count < count; tick++)
                yield return null;
            Assert.That(frames.Count, Is.GreaterThanOrEqualTo(count), "Camera publisher did not produce the expected frame count.");
        }

        private static IEnumerator WaitForResizedFrame(List<SensorRawImageFrame> frames)
        {
            for (var tick = 0; tick < 180; tick++)
            {
                if (frames.Exists(frame => frame.Width == 64 && frame.Height == 48))
                    yield break;
                yield return null;
            }
            Assert.Fail("Camera publisher did not produce the expected resized frame.");
        }

        private static IEnumerator WaitForPendingReadback(FoxgloveCameraPublisher publisher)
        {
            var pendingField = GetField(publisher, "_pendingRequests");
            for (var tick = 0; tick < 180; tick++)
            {
                if ((int)pendingField.GetValue(publisher) > 0)
                    yield break;
                yield return null;
            }

            Assert.Fail("Camera publisher did not expose a pending GPU readback.");
        }

        private static IEnumerator WaitForReleasedTexture(RenderTexture texture)
        {
            for (var tick = 0; tick < 180 && texture != null && texture.IsCreated(); tick++)
                yield return null;
            Assert.That(texture == null || !texture.IsCreated(), Is.True, "The retired render texture remained after its readback callback drained.");
        }

        private static IEnumerator WaitForFrameCount(List<SensorCameraInfoFrame> frames, int count)
        {
            for (var tick = 0; tick < 180 && frames.Count < count; tick++)
                yield return null;
            Assert.That(frames.Count, Is.GreaterThanOrEqualTo(count), "CameraInfo publisher did not produce a frame.");
        }

        private static void DestroyObjects(params GameObject[] objects)
        {
            foreach (var target in objects)
            {
                if (target != null)
                    UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static FieldInfo GetField(object target, string name)
        {
            var field = target.GetType().GetField(name, PrivateInstance);
            Assert.That(field, Is.Not.Null, $"Missing serialized field {name}.");
            return field;
        }

        private static void SetField(object target, string name, object value)
        {
            GetField(target, name).SetValue(target, value);
        }

        private static void AssertCameraInfoProjection(SensorCameraInfoFrame frame, Camera sourceCamera)
        {
            const float sampleDepth = 5f;
            const float sampleSpan = 1f;
            var center = ProjectImagePoint(sourceCamera, new Vector3(0f, 0f, sampleDepth), frame.Width, frame.Height);
            var right = ProjectImagePoint(sourceCamera, new Vector3(sampleSpan, 0f, sampleDepth), frame.Width, frame.Height);
            var left = ProjectImagePoint(sourceCamera, new Vector3(-sampleSpan, 0f, sampleDepth), frame.Width, frame.Height);
            var up = ProjectImagePoint(sourceCamera, new Vector3(0f, sampleSpan, sampleDepth), frame.Width, frame.Height);
            var down = ProjectImagePoint(sourceCamera, new Vector3(0f, -sampleSpan, sampleDepth), frame.Width, frame.Height);
            var fx = (right.x - left.x) * sampleDepth / (2d * sampleSpan);
            var fy = (up.y - down.y) * sampleDepth / (2d * sampleSpan);
            var cx = center.x;
            var cy = frame.Height - center.y;

            AssertCameraIntrinsicMatrix(frame.K, fx, fy, cx, cy);
            AssertCameraProjectionMatrix(frame.P, fx, fy, cx, cy);
        }

        private static Vector2 ProjectImagePoint(Camera camera, Vector3 localPoint, uint width, uint height)
        {
            var screenPoint = camera.WorldToScreenPoint(camera.transform.TransformPoint(localPoint));
            var pixelRect = camera.pixelRect;
            var viewportWidth = Mathf.Max(1f, pixelRect.width);
            var viewportHeight = Mathf.Max(1f, pixelRect.height);
            return new Vector2(
                (screenPoint.x - pixelRect.x) / viewportWidth * width,
                (screenPoint.y - pixelRect.y) / viewportHeight * height);
        }

        private static void AssertCameraIntrinsicMatrix(IReadOnlyList<double> matrix, double fx, double fy, double cx, double cy)
        {
            Assert.That(matrix.Count, Is.EqualTo(9));
            Assert.That(matrix[0], Is.EqualTo(fx).Within(0.1d));
            Assert.That(matrix[1], Is.EqualTo(0d).Within(0.1d));
            Assert.That(matrix[2], Is.EqualTo(cx).Within(0.1d));
            Assert.That(matrix[3], Is.EqualTo(0d).Within(0.1d));
            Assert.That(matrix[4], Is.EqualTo(fy).Within(0.1d));
            Assert.That(matrix[5], Is.EqualTo(cy).Within(0.1d));
            Assert.That(matrix[6], Is.EqualTo(0d).Within(0.1d));
            Assert.That(matrix[7], Is.EqualTo(0d).Within(0.1d));
            Assert.That(matrix[8], Is.EqualTo(1d).Within(0.1d));
        }

        private static void AssertCameraProjectionMatrix(IReadOnlyList<double> matrix, double fx, double fy, double cx, double cy)
        {
            var expected = new[] { fx, 0d, cx, 0d, 0d, fy, cy, 0d, 0d, 0d, 1d, 0d };
            Assert.That(matrix.Count, Is.EqualTo(expected.Length));
            for (var index = 0; index < expected.Length; index++)
                Assert.That(matrix[index], Is.EqualTo(expected[index]).Within(0.1d), $"Unexpected projection matrix element {index}.");
        }
    }
}
