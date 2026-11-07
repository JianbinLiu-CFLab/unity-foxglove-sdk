// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Performance;
using Xunit;

namespace Unity.FoxgloveSDK.Performance.Tests
{
    public sealed class PerformanceMetricsTests
    {
        [Fact]
        public void CameraBackpressureTransfersAllMeasuredGenerationCounts()
        {
            var invocations = new List<int>();
            try
            {
                PerformanceRunner.AllocationMetricsOverrideForTests = count =>
                {
                    invocations.Add(count);
                    return new PerformanceRunner.AllocationMetricSample(
                        1024,
                        512,
                        0.125,
                        invocations.Count == 1 ? 0 : 1,
                        invocations.Count == 1 ? 0 : 2,
                        invocations.Count == 1 ? 0 : 3,
                        "deterministic camera metric sentinel");
                };

                var thresholds = Thresholds(
                    "CameraBackpressurePolicyMicro",
                    new PerformanceScenarioThreshold
                    {
                        maxGen0Collections = 0,
                        maxGen1Collections = 0,
                        maxGen2Collections = 0
                    });

                var control = InvokeScenario("RunCameraBackpressurePolicyMicro", thresholds);
                var target = InvokeScenario("RunCameraBackpressurePolicyMicro", thresholds);
                var targetJson = JsonConvert.SerializeObject(target);

                Assert.True(control.passed);
                Assert.Equal(2, invocations.Count);
                Assert.Equal(100000, invocations[0]);
                Assert.Equal(100000, invocations[1]);
                Assert.Equal(1, target.gen0Collections);
                Assert.Equal(2, target.gen1Collections);
                Assert.Equal(3, target.gen2Collections);
                Assert.Contains("\"gen0Collections\":1", targetJson);
                Assert.Contains("\"gen1Collections\":2", targetJson);
                Assert.Contains("\"gen2Collections\":3", targetJson);
                Assert.False(target.passed);
                Assert.Contains("gen0Collections 1", target.thresholdNotes);
                Assert.Contains("gen1Collections 2", target.thresholdNotes);
                Assert.Contains("gen2Collections 3", target.thresholdNotes);
            }
            finally
            {
                PerformanceRunner.AllocationMetricsOverrideForTests = null;
            }
        }

        [Fact]
        public void CameraBackpressureUsesRuntimeCollectorWhenNoOverrideIsInstalled()
        {
            PerformanceRunner.AllocationMetricsOverrideForTests = null;
            var result = InvokeScenario(
                "RunCameraBackpressurePolicyMicro",
                new PerformanceThresholdConfig { enabled = false });

            Assert.True(result.passed);
            Assert.True(result.allocatedBytesTotal >= 0);
            Assert.True(result.allocatedBytesCurrentThread >= 0);
            Assert.True(result.allocatedBytesPerMessage >= 0);
            Assert.True(result.gen0Collections >= 0);
            Assert.True(result.gen1Collections >= 0);
            Assert.True(result.gen2Collections >= 0);
        }

        [Fact]
        public void LargeFrameQueueScenarioCoversAllConfiguredFrameSizes()
        {
            var result = InvokeScenario(
                "RunWsSendQueueLargeFrame",
                new PerformanceThresholdConfig { enabled = false });

            Assert.True(result.passed);
            Assert.NotNull(result.frameEvidence);
            Assert.Equal(new[] { 65536, 1048576, 4194304 }, result.frameEvidence.Select(item => item.sizeBytes));
            Assert.All(result.frameEvidence, item =>
            {
                Assert.True(item.droppedDataFrames > 0);
                Assert.True(item.queuedBytes <= item.sizeBytes * 2L);
                Assert.True(item.controlDequeuedFirst);
            });
        }

        [Fact]
        public void LargePayloadThresholdsFailBelowObservedAllocation()
        {
            var thresholds = Thresholds(
                "LargePayloadFanout",
                new PerformanceScenarioThreshold
                {
                    maxAllocatedBytesPerMessage = 1,
                    maxAllocatedBytesTotal = 1,
                    maxGen2Collections = 0
                });

            var result = new PerformanceScenarioResult
            {
                name = "LargePayloadFanout",
                messageCount = 4,
                allocatedBytesPerMessage = 2,
                allocatedBytesTotal = 8,
                gen2Collections = 1,
                passed = true
            };
            typeof(PerformanceRunner)
                .GetMethod("ApplyThresholds", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { result, thresholds });

            Assert.False(result.passed);
            Assert.Contains("allocatedBytesPerMessage", result.thresholdNotes);
            Assert.Contains("allocatedBytesTotal", result.thresholdNotes);
        }

        [Fact]
        public void StreamScenarioTransfersAllocationMetricsToAggregate()
        {
            try
            {
                PerformanceRunner.AllocationMetricsOverrideForTests = count =>
                    new PerformanceRunner.AllocationMetricSample(count * 2L, count, 2.0, 1, 2, 3, "override");
                var thresholds = Thresholds(
                    "FoxRunStreamIngest",
                    new PerformanceScenarioThreshold { maxAllocatedBytesTotal = 1, maxGen2Collections = 0 });
                var runnerType = typeof(PerformanceRunner).Assembly.GetType("Unity.FoxgloveSDK.Performance.HighBandwidthScenarioRunner");
                var method = runnerType.GetMethod("RunFoxRunStreamIngest", BindingFlags.NonPublic | BindingFlags.Static);
                var result = (PerformanceScenarioResult)method.Invoke(null, new object[] { 500, 2000, thresholds });

                Assert.True(result.allocatedBytesTotal > 0);
                Assert.True(result.gen2Collections > 0);
                Assert.False(result.passed);
            }
            finally
            {
                PerformanceRunner.AllocationMetricsOverrideForTests = null;
            }
        }

        [Fact]
        public void TransportQueueTransfersAllMeasuredAllocationMetrics()
        {
            var invocations = new List<int>();
            try
            {
                PerformanceRunner.AllocationMetricsOverrideForTests = count =>
                {
                    invocations.Add(count);
                    return new PerformanceRunner.AllocationMetricSample(
                        invocations.Count == 1 ? 1024 : 4096,
                        invocations.Count == 1 ? 512 : 2048,
                        invocations.Count == 1 ? 0.125 : 4.0,
                        invocations.Count == 1 ? 0 : 2,
                        invocations.Count == 1 ? 0 : 1,
                        invocations.Count == 1 ? 0 : 1,
                        "deterministic transport metric sentinel");
                };

                var thresholds = Thresholds(
                    "TransportQueueMicro",
                    new PerformanceScenarioThreshold
                    {
                        maxAllocatedBytesTotal = 2048,
                        maxAllocatedBytesPerMessage = 1,
                        maxGen0Collections = 1,
                        maxGen1Collections = 0,
                        maxGen2Collections = 0
                    });

                var control = InvokeScenario("RunTransportQueueMicro", thresholds);
                var target = InvokeScenario("RunTransportQueueMicro", thresholds);
                var targetJson = JsonConvert.SerializeObject(target);

                Assert.True(control.passed);
                Assert.Equal(2, invocations.Count);
                Assert.Equal(15, invocations[0]);
                Assert.Equal(15, invocations[1]);
                Assert.Equal(4096, target.allocatedBytesTotal);
                Assert.Equal(2048, target.allocatedBytesCurrentThread);
                Assert.Equal(4.0, target.allocatedBytesPerMessage);
                Assert.Equal(2, target.gen0Collections);
                Assert.Equal(1, target.gen1Collections);
                Assert.Equal(1, target.gen2Collections);
                Assert.Contains("\"allocatedBytesTotal\":4096", targetJson);
                Assert.Contains("\"allocatedBytesCurrentThread\":2048", targetJson);
                Assert.Contains("\"allocatedBytesPerMessage\":4.0", targetJson);
                Assert.Contains("\"gen0Collections\":2", targetJson);
                Assert.Contains("\"gen1Collections\":1", targetJson);
                Assert.Contains("\"gen2Collections\":1", targetJson);
                Assert.False(target.passed);
                Assert.Contains("allocatedBytesTotal 4096", target.thresholdNotes);
                Assert.Contains("allocatedBytesPerMessage", target.thresholdNotes);
                Assert.Contains("gen0Collections 2", target.thresholdNotes);
                Assert.Contains("gen1Collections 1", target.thresholdNotes);
                Assert.Contains("gen2Collections 1", target.thresholdNotes);
            }
            finally
            {
                PerformanceRunner.AllocationMetricsOverrideForTests = null;
            }
        }

        [Fact]
        public void McapAttachmentSummaryTransfersElapsedAndAllocationMetrics()
        {
            var metricInvocations = new List<int>();
            var elapsedInvocations = new List<long>();
            try
            {
                PerformanceRunner.AllocationMetricsOverrideForTests = count =>
                {
                    metricInvocations.Add(count);
                    return new PerformanceRunner.AllocationMetricSample(
                        metricInvocations.Count == 1 ? 1024 : 67_108_865,
                        metricInvocations.Count == 1 ? 512 : 4096,
                        metricInvocations.Count == 1 ? 0.125 : 67_108_865d,
                        metricInvocations.Count == 1 ? 0 : 2,
                        metricInvocations.Count == 1 ? 0 : 1,
                        metricInvocations.Count == 1 ? 0 : 1,
                        "deterministic MCAP attachment metric sentinel");
                };
                PerformanceRunner.ElapsedMillisecondsOverrideForTests = actual =>
                {
                    elapsedInvocations.Add(actual);
                    return elapsedInvocations.Count == 1 ? 10 : 10_001;
                };

                var thresholds = Thresholds(
                    "McapRecordAttachmentSummary",
                    new PerformanceScenarioThreshold
                    {
                        maxElapsedMs = 10_000,
                        maxAllocatedBytesTotal = 67_108_864,
                        maxAllocatedBytesPerMessage = 67_108_864,
                        maxGen2Collections = 0
                    });

                var control = InvokeScenario("RunMcapRecordAttachmentSummary", thresholds);
                var target = InvokeScenario("RunMcapRecordAttachmentSummary", thresholds);
                var targetJson = JsonConvert.SerializeObject(target);

                Assert.True(control.passed);
                Assert.Equal(2, metricInvocations.Count);
                Assert.Equal(1, metricInvocations[0]);
                Assert.Equal(1, metricInvocations[1]);
                Assert.Equal(2, elapsedInvocations.Count);
                Assert.Equal(10, control.elapsedMs);
                Assert.Equal(10_001, target.elapsedMs);
                Assert.True(control.messagesPerSecond > 0);
                Assert.True(target.messagesPerSecond > 0);
                Assert.Equal(67_108_865, target.allocatedBytesTotal);
                Assert.Equal(4096, target.allocatedBytesCurrentThread);
                Assert.Equal(67_108_865d, target.allocatedBytesPerMessage);
                Assert.Equal(2, target.gen0Collections);
                Assert.Equal(1, target.gen1Collections);
                Assert.Equal(1, target.gen2Collections);
                Assert.True(target.outputBytes > 0);
                Assert.Contains("attachment index", target.notes);
                Assert.Contains("\"elapsedMs\":10001", targetJson);
                Assert.Contains("\"allocatedBytesTotal\":67108865", targetJson);
                Assert.Contains("\"gen0Collections\":2", targetJson);
                Assert.Contains("\"gen1Collections\":1", targetJson);
                Assert.Contains("\"gen2Collections\":1", targetJson);
                Assert.False(target.passed);
                Assert.Contains("elapsedMs 10001", target.thresholdNotes);
                Assert.Contains("allocatedBytesTotal 67108865", target.thresholdNotes);
                Assert.Contains("allocatedBytesPerMessage", target.thresholdNotes);
                Assert.Contains("gen2Collections 1", target.thresholdNotes);
            }
            finally
            {
                PerformanceRunner.AllocationMetricsOverrideForTests = null;
                PerformanceRunner.ElapsedMillisecondsOverrideForTests = null;
            }
        }

        private static PerformanceThresholdConfig Thresholds(
            string scenarioName,
            PerformanceScenarioThreshold threshold)
        {
            return new PerformanceThresholdConfig
            {
                enabled = true,
                scenarios = new Dictionary<string, PerformanceScenarioThreshold>
                {
                    [scenarioName] = threshold
                }
            };
        }

        private static PerformanceScenarioResult InvokeScenario(
            string methodName,
            PerformanceThresholdConfig thresholds)
        {
            var method = typeof(PerformanceRunner).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            method ??= typeof(PerformanceRunner).Assembly
                .GetType("Unity.FoxgloveSDK.Performance.HighBandwidthScenarioRunner")
                ?.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            try
            {
                return (PerformanceScenarioResult)method.Invoke(null, new object[] { thresholds });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }
    }
}
