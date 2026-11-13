using System;
using System.Collections;
using System.Collections.Generic;
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
                SetField(publisher, "_publishOnEnable", false);
                yield return null;
                var resources = GetField(publisher, "_captureResources").GetValue(publisher);
                var retiredTexture = (RenderTexture)resources.GetType().GetProperty("CaptureRenderTexture").GetValue(resources);
                var generation = (int)GetField(publisher, "_captureGeneration").GetValue(publisher);
                resources.GetType().GetMethod("RegisterReadback").Invoke(resources, new object[] { generation });
                SetField(publisher, "_width", 64);
                SetField(publisher, "_height", 48);
                publisher.GetType().GetMethod("EnsureCaptureResources", PrivateInstance).Invoke(publisher, null);
                Assert.That(retiredTexture, Is.Not.Null);
                Assert.That(retiredTexture.IsCreated(), Is.True, "The retired render texture was released before its readback generation drained.");
                var completeReadback = resources.GetType().GetMethod("CompleteReadback");
                completeReadback.Invoke(resources, new object[] { generation });
                Assert.That(retiredTexture.IsCreated(), Is.False, "The retired render texture remained after its readback generation drained.");
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
                SetField(publisher, "_width", 64);
                SetField(publisher, "_height", 48);
                publisher.enabled = false;
                publisher.enabled = true;
                yield return WaitForResizedFrame(frames);

                Assert.That(frames.Exists(frame => frame.Width == 32 && frame.Height == 24), Is.True);
                Assert.That(frames.Exists(frame => frame.Width == 64 && frame.Height == 48), Is.True);
                foreach (var frame in frames)
                {
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
                var expectedFy = frame.Height / (2d * Math.Tan(imageSourceCamera.fieldOfView * Math.PI / 360d));
                var expectedFx = expectedFy * frame.Width / (double)frame.Height;
                Assert.That(frame.K[0], Is.EqualTo(expectedFx).Within(0.1d));
                Assert.That(frame.K[4], Is.EqualTo(expectedFy).Within(0.1d));
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
                disabledManager.StartServer();
                Assert.That(disabledManager.IsRunning, Is.True);
                disabledManager.enabled = false;
                Assert.That(disabledManager.IsRunning, Is.False);

                destroyedManager.StartServer();
                Assert.That(destroyedManager.IsRunning, Is.True);
                var onDestroy = typeof(FoxgloveManager).GetMethod(
                    "OnDestroy",
                    PrivateInstance);
                Assert.That(onDestroy, Is.Not.Null);
                onDestroy.Invoke(destroyedManager, null);
                Assert.That(destroyedManager.IsRunning, Is.False);
            }
            finally
            {
                DestroyObjects(disabledManager.gameObject, destroyedManager.gameObject);
            }

            yield return null;
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
    }
}
