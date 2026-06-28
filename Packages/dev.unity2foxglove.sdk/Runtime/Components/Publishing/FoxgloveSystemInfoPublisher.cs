// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Publishing
// Purpose: Publishes Unity runtime and device telemetry as structured JSON.

using System;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Util;
using UnityEngine;
using UnityEngine.Profiling;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes Unity runtime and device telemetry on a structured JSON topic.
    /// </summary>
    public sealed class FoxgloveSystemInfoPublisher : FoxglovePublisherBase
    {
        private const string DefaultTopic = "/sysinfo";
        private const float DefaultPublishRateHz = 0.2f;
        private const float MaxPublishRateHz = 5f;

        // SystemInfo applies its own 5 Hz effective-rate cap, so it keeps a
        // dedicated cadence state instead of using the base publisher state.
        private FixedRatePublishState _systemInfoRateState;

        protected override string SchemaName => FoxgloveSchemaDefinitions.SystemInfoSchemaName;

        public override bool SupportsProtobufEncoding => false;
        public override bool SupportsRos2Encoding => false;

        protected override void Reset()
        {
            base.Reset();
            _topic = "/sysinfo";
            _publishRateSource = PublisherRateSource.OverrideLocal;
            _publishRateHz = DefaultPublishRateHz;
        }

        private void Awake()
        {
            ApplySystemInfoDefaults(clampSerializedRate: false);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            ApplySystemInfoDefaults(clampSerializedRate: true);
        }
#endif

        protected override void OnEnable()
        {
            base.OnEnable();
            _systemInfoRateState = default;
            ApplySystemInfoDefaults(clampSerializedRate: false);
        }

        private void Update()
        {
            if (_manager == null)
                ResolveManager();
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;

            var effectiveRateHz = Mathf.Min(EffectivePublishRateHz, MaxPublishRateHz);
            if (!FixedRatePublishScheduler.ShouldPublish(
                    Time.unscaledTimeAsDouble,
                    effectiveRateHz,
                    ref _systemInfoRateState,
                    nonPositivePublishesEveryFrame: true))
            {
                return;
            }

            if (!TryPreparePublishPayload(out var resolution))
                return;

            var nowNs = CurrentLogTimeNs;
            Publish(CreateMessage(nowNs), nowNs, resolution);
        }

        private void ApplySystemInfoDefaults(bool clampSerializedRate)
        {
            if (string.IsNullOrWhiteSpace(_topic))
                _topic = DefaultTopic;
            if (_publishRateHz <= 0f)
                _publishRateHz = DefaultPublishRateHz;
            if (clampSerializedRate)
                _publishRateHz = Mathf.Min(_publishRateHz, MaxPublishRateHz);
        }

        private static FoxgloveSystemInfoMessage CreateMessage(ulong unixNs)
        {
            var frameTimeMs = Math.Max(0.0, Time.unscaledDeltaTime * 1000.0);
            var fps = frameTimeMs > 0.0 ? 1000.0 / frameTimeMs : 0.0;

            return new FoxgloveSystemInfoMessage
            {
                Timestamp = FoxgloveTimeUtil.ToFoxgloveTime(unixNs),
                FrameTimeMs = frameTimeMs,
                Fps = fps,
                GcMemoryMB = BytesToMegabytes(GC.GetTotalMemory(false)),
                MonoUsedMemoryMB = BytesToMegabytes(Profiler.GetMonoUsedSizeLong()),
                TotalAllocatedMemoryMB = BytesToMegabytes(Profiler.GetTotalAllocatedMemoryLong()),
                TotalReservedMemoryMB = BytesToMegabytes(Profiler.GetTotalReservedMemoryLong()),
                SystemMemorySizeMB = SystemInfo.systemMemorySize,
                ProcessorCount = SystemInfo.processorCount,
                ProcessorType = SystemInfo.processorType ?? string.Empty,
                GraphicsDeviceName = SystemInfo.graphicsDeviceName ?? string.Empty,
                GraphicsMemorySizeMB = SystemInfo.graphicsMemorySize,
                Platform = Application.platform.ToString(),
                UnityVersion = Application.unityVersion
            };
        }

        private static double BytesToMegabytes(long bytes)
            => bytes <= 0 ? 0.0 : bytes / (1024.0 * 1024.0);
    }

    /// <summary>
    /// JSON DTO for Unity2Foxglove system telemetry.
    /// </summary>
    [FoxgloveSchema("unity2foxglove.SystemInfo")]
    public sealed class FoxgloveSystemInfoMessage
    {
        [JsonProperty("timestamp")]
        public FoxgloveTime Timestamp { get; set; }

        [JsonProperty("frameTimeMs")]
        public double FrameTimeMs { get; set; }

        [JsonProperty("fps")]
        public double Fps { get; set; }

        [JsonProperty("gcMemoryMB")]
        public double GcMemoryMB { get; set; }

        [JsonProperty("monoUsedMemoryMB")]
        public double MonoUsedMemoryMB { get; set; }

        [JsonProperty("totalAllocatedMemoryMB")]
        public double TotalAllocatedMemoryMB { get; set; }

        [JsonProperty("totalReservedMemoryMB")]
        public double TotalReservedMemoryMB { get; set; }

        [JsonProperty("systemMemorySizeMB")]
        public int SystemMemorySizeMB { get; set; }

        [JsonProperty("processorCount")]
        public int ProcessorCount { get; set; }

        [JsonProperty("processorType")]
        public string ProcessorType { get; set; }

        [JsonProperty("graphicsDeviceName")]
        public string GraphicsDeviceName { get; set; }

        [JsonProperty("graphicsMemorySizeMB")]
        public int GraphicsMemorySizeMB { get; set; }

        [JsonProperty("platform")]
        public string Platform { get; set; }

        [JsonProperty("unityVersion")]
        public string UnityVersion { get; set; }
    }
}
