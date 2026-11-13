using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Sensors.Lidar;

namespace Unity2Foxglove.Tests.PlayMode
{
    public sealed class VirtualLidarAcceptanceTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        [Category("LidarAcceptance")]
        public IEnumerator LidarAcceptance_ElapsedPhysicsTimeSpansMultipleTicks()
        {
            var target = CreateTarget(new Vector3(0f, 0f, 5f));
            var lidar = CreateLidar("lidar-timing", "timing-frame", target.layer);
            var previousSimulationMode = Physics.simulationMode;
            var previousFixedDeltaTime = Time.fixedDeltaTime;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                var ticks = 0;
                PointCloudFrame firstFrame = null;
                for (; ticks < 30; ticks++)
                {
                    var delta = ticks % 2 == 0 ? 0.06f : 0.04f;
                    Time.fixedDeltaTime = delta;
                    Physics.Simulate(delta);
                    yield return new WaitForFixedUpdate();
                    if (lidar.LastFrame != null && lidar.LastFrame.Points.Count >= 4)
                    {
                        firstFrame = lidar.LastFrame;
                        break;
                    }
                }

                Assert.That(firstFrame, Is.Not.Null, $"The scan did not complete within the elapsed-time budget. LastFrame points={lidar.LastFrame?.Points.Count ?? -1}.");
                Assert.That(ticks, Is.LessThan(30), "Elapsed physics time was not used to advance the scan.");
                var offsets = new List<float>();
                foreach (var point in firstFrame.Points)
                {
                    if (point.HasTimeOffset)
                        offsets.Add(point.TimeOffsetSeconds);
                }

                Assert.That(offsets.Count, Is.GreaterThanOrEqualTo(4));
                for (var index = 1; index < offsets.Count; index++)
                    Assert.That(offsets[index], Is.GreaterThanOrEqualTo(offsets[index - 1]));
                Assert.That(offsets[offsets.Count - 1] - offsets[0], Is.GreaterThan(0.04f));
            }
            finally
            {
                Physics.simulationMode = previousSimulationMode;
                Time.fixedDeltaTime = previousFixedDeltaTime;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(lidar.gameObject);
            }
        }

        [UnityTest]
        [Category("LidarAcceptance")]
        public IEnumerator LidarAcceptance_ConfigurationChangesApplyAfterReenable()
        {
            var target = CreateTarget(new Vector3(0f, 0f, 5f));
            var lidar = CreateLidar("lidar-config", "before-change", target.layer);
            var previousSimulationMode = Physics.simulationMode;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                yield return WaitForFrame(lidar, null);
                var initialFrame = lidar.LastFrame;
                Assert.That(initialFrame.FrameId, Is.EqualTo("before-change"));

                SetField(lidar, "_frameId", "during-scan");
                yield return WaitForFrame(lidar, initialFrame);
                Assert.That(lidar.LastFrame.FrameId, Is.EqualTo("before-change"));

                lidar.enabled = false;
                lidar.enabled = true;
                yield return WaitForFrame(lidar, lidar.LastFrame);
                Assert.That(lidar.LastFrame.FrameId, Is.EqualTo("during-scan"));
            }
            finally
            {
                Physics.simulationMode = previousSimulationMode;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(lidar.gameObject);
            }
        }

        [UnityTest]
        [Category("LidarAcceptance")]
        public IEnumerator LidarAcceptance_SensorTransformDefinesPointFrame()
        {
            var target = CreateTarget(new Vector3(0f, 0f, 5f));
            var lidar = CreateLidar("lidar-transform", "sensor-frame", target.layer);
            var publisherObject = CreatePublisher(lidar);
            var previousSimulationMode = Physics.simulationMode;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                yield return WaitForFrame(lidar, null);
                var firstPoint = FindOffAxisPoint(lidar.LastFrame);

                lidar.transform.position = new Vector3(1f, 0f, 0f);
                Physics.SyncTransforms();
                var previousFrame = lidar.LastFrame;
                yield return WaitForFrame(lidar, previousFrame);
                var movedPoint = FindOffAxisPoint(lidar.LastFrame);

                Assert.That(movedPoint.Y, Is.Not.EqualTo(firstPoint.Y).Within(0.01f));
                Assert.That(movedPoint.Z, Is.EqualTo(firstPoint.Z).Within(0.05f));
                Assert.That(lidar.LastFrame.FrameId, Is.EqualTo("sensor-frame"));
            }
            finally
            {
                Physics.simulationMode = previousSimulationMode;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(lidar.gameObject);
                Object.DestroyImmediate(publisherObject);
            }
        }

        private static GameObject CreateTarget(Vector3 position)
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.transform.position = position;
            target.transform.localScale = new Vector3(20f, 20f, 0.2f);
            Physics.SyncTransforms();
            return target;
        }

        private static VirtualLidar CreateLidar(string name, string frameId, int targetLayer)
        {
            var gameObject = new GameObject(name);
            gameObject.layer = 8;
            gameObject.SetActive(false);
            var lidar = gameObject.AddComponent<VirtualLidar>();
            SetField(lidar, "_profileSource", VirtualLidar.ProfileSource.Custom);
            SetField(lidar, "_customPixelsPerColumn", 1);
            SetField(lidar, "_customColumnsPerFrame", 32);
            SetField(lidar, "_customScanRateHz", 1f);
            SetField(lidar, "_customFovTopDeg", 8f);
            SetField(lidar, "_customFovBottomDeg", -8f);
            SetField(lidar, "_customMinRangeMeters", 0.1f);
            SetField(lidar, "_frameId", frameId);
            SetField(lidar, "_columnStep", 1);
            SetField(lidar, "_maxRangeMeters", 20f);
            SetField(lidar, "_maxRaycastCommandsPerFixedUpdate", 256);
            SetField(lidar, "_publishEmptyFrames", true);
            SetField(lidar, "_layerMask", (LayerMask)(1 << targetLayer));
            gameObject.SetActive(true);
            return lidar;
        }

        private static IEnumerator WaitForFrame(VirtualLidar lidar, PointCloudFrame previous)
        {
            for (var tick = 0; tick < 120; tick++)
            {
                Physics.Simulate(0.02f);
                yield return new WaitForFixedUpdate();
                if (lidar.LastFrame != null && lidar.LastFrame != previous && lidar.LastFrame.Points.Count > 0)
                    yield break;
            }

            Assert.Fail("VirtualLidar did not publish a point frame within 120 simulated ticks.");
        }

        private static GameObject CreatePublisher(VirtualLidar lidar)
        {
            var publisherType = System.Type.GetType("Unity.FoxgloveSDK.Components.FoxglovePointCloudPublisher, Unity.FoxgloveSDK.Proto");
            Assert.That(publisherType, Is.Not.Null, "Point-cloud publisher type is unavailable.");
            var publisherObject = new GameObject("lidar-publisher");
            var publisher = publisherObject.AddComponent(publisherType);
            var field = publisherType.GetField("_outputMode", PrivateInstance);
            Assert.That(field, Is.Not.Null, "Missing point-cloud output mode field.");
            field.SetValue(publisher, System.Enum.Parse(field.FieldType, "Raw"));
            SetObjectField(lidar, "_pointCloudPublisher", publisher);
            return publisherObject;
        }

        private static PointCloudPoint FindOffAxisPoint(PointCloudFrame frame)
        {
            var selected = default(PointCloudPoint);
            var largestOffset = 0f;
            var found = false;
            foreach (var point in frame.Points)
            {
                if (Mathf.Abs(point.Y) <= largestOffset)
                    continue;

                selected = point;
                largestOffset = Mathf.Abs(point.Y);
                found = true;
            }

            Assert.That(found, Is.True, "The scan did not contain an off-axis point.");
            return selected;
        }

        private static void SetField<T>(VirtualLidar lidar, string name, T value)
        {
            var field = typeof(VirtualLidar).GetField(name, PrivateInstance);
            Assert.That(field, Is.Not.Null, $"Missing serialized field {name}.");
            field.SetValue(lidar, value);
        }

        private static void SetObjectField(VirtualLidar lidar, string name, object value)
        {
            var field = typeof(VirtualLidar).GetField(name, PrivateInstance);
            Assert.That(field, Is.Not.Null, $"Missing serialized field {name}.");
            field.SetValue(lidar, value);
        }
    }
}
