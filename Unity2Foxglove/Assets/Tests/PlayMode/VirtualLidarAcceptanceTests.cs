using Math = System.Math;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
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
        private static readonly Regex FixedUpdateTimingPattern = new Regex(
            @"\[LidarDiag\] fixed-update timing: columnsToEmit=(?<columns>\d+).*?cursor=(?<cursor>\d+)",
            RegexOptions.CultureInvariant);

        private readonly struct LidarBatchObservation
        {
            public LidarBatchObservation(int columnsToEmit, int cursor, double acquisitionPhysSeconds, float fixedDeltaTimeSeconds)
            {
                ColumnsToEmit = columnsToEmit;
                Cursor = cursor;
                AcquisitionPhysSeconds = acquisitionPhysSeconds;
                FixedDeltaTimeSeconds = fixedDeltaTimeSeconds;
            }

            public int ColumnsToEmit { get; }
            public int Cursor { get; }
            public double AcquisitionPhysSeconds { get; }
            public float FixedDeltaTimeSeconds { get; }
        }

        [UnityTest]
        [Category("LidarAcceptance")]
        public IEnumerator LidarAcceptance_ElapsedPhysicsTimeSpansMultipleTicks()
        {
            var target = CreateEnclosingTarget();
            var stepperObject = new GameObject("lidar-physics-steps");
            var stepper = stepperObject.AddComponent<PhysicsStepAlternator>();
            var previousSimulationMode = Physics.simulationMode;
            var previousFixedDeltaTime = Time.fixedDeltaTime;
            VirtualLidar lidar = null;
            var batches = new List<LidarBatchObservation>();
            var captureDiagnostics = false;

            void CaptureLidarDiagnostics(string condition, string stackTrace, LogType type)
            {
                if (!captureDiagnostics || type != LogType.Log || lidar == null || lidar.LastFrame != null)
                    return;

                var match = FixedUpdateTimingPattern.Match(condition);
                if (!match.Success)
                    return;

                batches.Add(new LidarBatchObservation(
                    int.Parse(match.Groups["columns"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["cursor"].Value, CultureInfo.InvariantCulture),
                    Time.fixedTimeAsDouble,
                    Time.fixedDeltaTime));
            }

            try
            {
                Physics.simulationMode = SimulationMode.Script;
                var scanStartPhysSeconds = Time.fixedTimeAsDouble;
                lidar = CreateLidar(
                    "lidar-timing",
                    "timing-frame",
                    target.layer,
                    pixelsPerColumn: 4,
                    columnsPerFrame: 256,
                    activate: false,
                    logPerformanceDiagnostics: true);
                Application.logMessageReceived += CaptureLidarDiagnostics;
                captureDiagnostics = true;
                lidar.gameObject.SetActive(true);
                var ticks = 0;
                PointCloudFrame firstFrame = null;
                for (; ticks < 120; ticks++)
                {
                    Physics.Simulate(Time.fixedDeltaTime);
                    yield return new WaitForFixedUpdate();
                    if (lidar.LastFrame != null && lidar.LastFrame.Points.Count >= 4)
                    {
                        firstFrame = lidar.LastFrame;
                        break;
                    }
                }

                Assert.That(firstFrame, Is.Not.Null, $"The scan did not complete within the elapsed-time budget. LastFrame points={lidar.LastFrame?.Points.Count ?? -1}.");
                Assert.That(ticks, Is.LessThan(120));
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
                Assert.That(ticks, Is.GreaterThan(3), "The acceptance scan did not span multiple physics ticks.");
                AssertElapsedDiffersFromNominalStep(stepper, scanStartPhysSeconds);
                const int scanColumns = 256;
                const int budgetColumns = 64;
                var expectedBatches = BuildExpectedBatches(
                    stepper.Times,
                    stepper.NominalSteps,
                    scanStartPhysSeconds,
                    scanColumns,
                    budgetColumns,
                    scanPeriodSeconds: 1d);
                Assert.That(expectedBatches.Count, Is.EqualTo(batches.Count), "The observed batch count differed from the elapsed-time model.");
                for (var index = 0; index < expectedBatches.Count; index++)
                {
                    var expectedBatch = expectedBatches[index];
                    var observedBatch = batches[index];
                    Assert.That(observedBatch.ColumnsToEmit, Is.EqualTo(expectedBatch.ColumnsToEmit), $"Unexpected emitted column count for batch {index}.");
                    Assert.That(observedBatch.Cursor, Is.EqualTo(expectedBatch.Cursor), $"Unexpected scan cursor for batch {index}.");
                    Assert.That(observedBatch.AcquisitionPhysSeconds, Is.EqualTo(expectedBatch.AcquisitionPhysSeconds).Within(0.000001d), $"Unexpected acquisition time for batch {index}.");
                    Assert.That(observedBatch.FixedDeltaTimeSeconds, Is.EqualTo(expectedBatch.FixedDeltaTimeSeconds).Within(0.00001f), $"Unexpected fixed step for batch {index}.");
                }

                var expectedByColumn = BuildExpectedAcquisitionByColumn(expectedBatches, scanStartPhysSeconds, scanColumns);
                Assert.That(expectedByColumn.Count, Is.EqualTo(scanColumns), "The elapsed-time model did not cover one complete scan.");

                var observedColumns = new HashSet<int>();
                foreach (var point in firstFrame.Points)
                {
                    var azimuth = Math.Atan2(-point.Y, point.X);
                    if (azimuth < 0d)
                        azimuth += 2d * Math.PI;
                    var column = (int)Math.Round(azimuth / (2d * Math.PI) * scanColumns) % scanColumns;
                    observedColumns.Add(column);
                    var normalizedOffset = column / (float)scanColumns;
                    Assert.That(expectedByColumn.TryGetValue(column, out var expected), Is.True, $"No acquisition time was recorded for column {column}.");
                    var expectedOffset = ExpectedAcquisitionOffset(
                        expected.ElapsedSeconds,
                        normalizedOffset,
                        expected.FixedDeltaTimeSeconds);
                    Assert.That(point.TimeOffsetSeconds, Is.EqualTo(expectedOffset).Within(0.003f), $"Unexpected acquisition offset for column {column}.");
                }

                Assert.That(observedColumns.Count, Is.EqualTo(scanColumns), "The first frame did not contain points from every scan column.");
                Assert.That(observedColumns.SetEquals(expectedByColumn.Keys), Is.True, "Point columns did not match the recorded batch columns.");
            }
            finally
            {
                captureDiagnostics = false;
                Application.logMessageReceived -= CaptureLidarDiagnostics;
                Object.DestroyImmediate(stepperObject);
                Physics.simulationMode = previousSimulationMode;
                Time.fixedDeltaTime = previousFixedDeltaTime;
                Object.DestroyImmediate(target);
                if (lidar != null)
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

        private static GameObject CreateEnclosingTarget()
        {
            const float extent = 10f;
            const float thickness = 0.2f;
            var root = new GameObject("lidar-enclosing-target");
            var diameter = extent * 2f + thickness;
            AddEnclosingWall(root, new Vector3(-extent, 0f, 0f), new Vector3(thickness, diameter, diameter));
            AddEnclosingWall(root, new Vector3(extent, 0f, 0f), new Vector3(thickness, diameter, diameter));
            AddEnclosingWall(root, new Vector3(0f, -extent, 0f), new Vector3(diameter, thickness, diameter));
            AddEnclosingWall(root, new Vector3(0f, extent, 0f), new Vector3(diameter, thickness, diameter));
            AddEnclosingWall(root, new Vector3(0f, 0f, -extent), new Vector3(diameter, diameter, thickness));
            AddEnclosingWall(root, new Vector3(0f, 0f, extent), new Vector3(diameter, diameter, thickness));
            Physics.SyncTransforms();
            return root;
        }

        private static void AddEnclosingWall(GameObject root, Vector3 position, Vector3 scale)
        {
            var wall = new GameObject("lidar-enclosing-wall");
            wall.transform.SetParent(root.transform);
            wall.transform.localPosition = position;
            wall.transform.localScale = scale;
            wall.layer = root.layer;
            wall.AddComponent<BoxCollider>();
        }

        private static void AssertElapsedDiffersFromNominalStep(PhysicsStepAlternator stepper, double scanStartPhysSeconds)
        {
            var distinguishingSteps = 0;
            for (var index = 1; index < stepper.Times.Count; index++)
            {
                if (stepper.Times[index] <= scanStartPhysSeconds)
                    continue;
                var elapsed = stepper.Times[index] - stepper.Times[index - 1];
                if (Math.Abs(elapsed - stepper.NominalSteps[index]) > 0.01d)
                    distinguishingSteps++;
            }

            Assert.That(distinguishingSteps, Is.GreaterThan(3), "Elapsed physics time did not differ from the nominal step seen by VirtualLidar.");
        }

        private static List<LidarBatchObservation> BuildExpectedBatches(
            IReadOnlyList<double> fixedTimes,
            IReadOnlyList<float> nominalSteps,
            double scanStartPhysSeconds,
            int columns,
            int budgetColumns,
            double scanPeriodSeconds)
        {
            var expected = new List<LidarBatchObservation>();
            var progress = 0d;
            var cursor = 0;
            var columnsScheduled = 0;
            var previousPhysSeconds = scanStartPhysSeconds;
            var primed = false;
            for (var index = 0; index < fixedTimes.Count && columnsScheduled < columns; index++)
            {
                var acquisitionPhysSeconds = fixedTimes[index];
                if (acquisitionPhysSeconds <= scanStartPhysSeconds)
                    continue;
                if (!primed)
                {
                    previousPhysSeconds = acquisitionPhysSeconds;
                    primed = true;
                    continue;
                }

                var elapsedPhysicsSeconds = acquisitionPhysSeconds - previousPhysSeconds;
                previousPhysSeconds = acquisitionPhysSeconds;
                progress += elapsedPhysicsSeconds * columns / scanPeriodSeconds;
                progress = Math.Min(progress, columns + budgetColumns);

                var remainingColumns = columns - columnsScheduled;
                var boundedBudgetColumns = Math.Min(budgetColumns, remainingColumns);
                var columnsToEmit = Math.Min((int)Math.Floor(progress), boundedBudgetColumns);
                if (columnsToEmit <= 0)
                    continue;

                cursor = (cursor + columnsToEmit) % columns;
                expected.Add(new LidarBatchObservation(
                    columnsToEmit,
                    cursor,
                    acquisitionPhysSeconds,
                    nominalSteps[index]));
                progress -= columnsToEmit;
                columnsScheduled += columnsToEmit;
            }

            Assert.That(columnsScheduled, Is.EqualTo(columns), "The elapsed-time model did not schedule one complete scan.");
            return expected;
        }

        private static Dictionary<int, (double ElapsedSeconds, float FixedDeltaTimeSeconds)> BuildExpectedAcquisitionByColumn(
            IReadOnlyList<LidarBatchObservation> batches,
            double scanStartPhysSeconds,
            int columns)
        {
            var expected = new Dictionary<int, (double ElapsedSeconds, float FixedDeltaTimeSeconds)>();
            foreach (var batch in batches)
            {
                Assert.That(batch.ColumnsToEmit, Is.GreaterThan(0));
                Assert.That(batch.Cursor, Is.InRange(0, columns - 1));
                var firstColumn = (batch.Cursor - batch.ColumnsToEmit + columns) % columns;
                for (var offset = 0; offset < batch.ColumnsToEmit; offset++)
                {
                    var column = (firstColumn + offset) % columns;
                    Assert.That(expected.ContainsKey(column), Is.False, $"Column {column} was recorded by multiple batches.");
                    expected[column] = (
                        batch.AcquisitionPhysSeconds - scanStartPhysSeconds,
                        batch.FixedDeltaTimeSeconds);
                }
            }

            return expected;
        }

        private static float ExpectedAcquisitionOffset(
            double elapsedSeconds,
            float normalizedOffset,
            float fixedDeltaTimeSeconds)
        {
            var elapsed = double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds)
                ? 0d
                : Math.Max(0d, elapsedSeconds);
            var phase = float.IsNaN(normalizedOffset) || float.IsInfinity(normalizedOffset)
                ? 0f
                : Mathf.Clamp01(normalizedOffset);
            var tick = float.IsNaN(fixedDeltaTimeSeconds) || float.IsInfinity(fixedDeltaTimeSeconds)
                ? 0f
                : Mathf.Max(0f, fixedDeltaTimeSeconds);
            return (float)elapsed + phase * tick;
        }

        private static VirtualLidar CreateLidar(
            string name,
            string frameId,
            int targetLayer,
            int pixelsPerColumn = 1,
            int columnsPerFrame = 32,
            bool activate = true,
            bool logPerformanceDiagnostics = false)
        {
            var gameObject = new GameObject(name);
            gameObject.layer = 8;
            gameObject.SetActive(false);
            var lidar = gameObject.AddComponent<VirtualLidar>();
            SetField(lidar, "_profileSource", VirtualLidar.ProfileSource.Custom);
            SetField(lidar, "_customPixelsPerColumn", pixelsPerColumn);
            SetField(lidar, "_customColumnsPerFrame", columnsPerFrame);
            SetField(lidar, "_customScanRateHz", 1f);
            SetField(lidar, "_customFovTopDeg", 8f);
            SetField(lidar, "_customFovBottomDeg", -8f);
            SetField(lidar, "_customMinRangeMeters", 0.1f);
            SetField(lidar, "_frameId", frameId);
            SetField(lidar, "_columnStep", 1);
            SetField(lidar, "_maxRangeMeters", 20f);
            SetField(lidar, "_logPerformanceDiagnostics", logPerformanceDiagnostics);
            SetField(lidar, "_maxRaycastCommandsPerFixedUpdate", 256);
            SetField(lidar, "_publishEmptyFrames", true);
            SetField(lidar, "_layerMask", (LayerMask)(1 << targetLayer));
            if (activate)
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
