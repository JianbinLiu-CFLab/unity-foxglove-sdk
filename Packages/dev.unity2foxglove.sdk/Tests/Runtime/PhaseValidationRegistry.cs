// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Central registry for CI-safe, local-evidence, and explicit phase validations.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Build-time registry of all phase-level validation entry points used by the
    /// local validation harness and CI pipeline.
    /// </summary>
    internal static class PhaseValidationRegistry
    {
        /// <summary>
        /// All validation definitions, including CI-safe and local evidence suites.
        /// </summary>
        public static IReadOnlyList<PhaseValidationCase> All { get; } = new[]
        {
            DefaultOnly("Skeleton", SkeletonValidation.Validate),
            Ci("--phase1", "Phase 1: validates serverInfo delivery, session identity, real WebSocket connectivity, and subprotocol negotiation", Phase1Validation.Validate),
            Ci("--phase2", "Phase 2: validates protocol DTO serialization, channel registration/unregistration, advertise snapshots, subscribe/unsubscribe parsing, and publish routing", Phase2Validation.Validate),
            Ci("--phase3", "Phase 3: validates core schema registration, typed channel advertising, SceneUpdate DTO serialization, and real WebSocket integration with schemas", Phase3Validation.Validate),
            Ci("--phase4", "Phase 4: validates CompressedImage schema registration, base64 roundtrip, FoxgloveTime utility, and typed channel advertising", Phase4Validation.Validate),
            Ci("--phase5", "Phase 5: validates timestamp utilities, runtime lifecycle (stop/start/dispose), and link.xml code-stripping guard verification", Phase5Validation.Validate),
            Ci("--phase6", "Phase 6: validates serverInfo capabilities, parameter store, parameter subscribe/unsubscribe, service advertise, service call binary codec, and service call timeout/sweep", Phase6Validation.Validate),
            Ci("--phase7", "Phase 7: validates capabilities, logger injection, service call encapsulation, lifecycle survival, handler delegates, and time frame binary encoding", Phase7Validation.Validate),
            Ci("--phase8", "Phase 8: validates connection graph capabilities, graph subscribe/unsubscribe, client publish, per-client channel isolation, and disconnect cleanup", Phase8Validation.Validate),
            Ci("--phase9", "Phase 9: validates asset registry, fetch asset protocol, playback capability, playback control binary codec, and playback clock state machine", Phase9Validation.Validate),
            Ci("--phase10", "Phase 10: validates MCAP magic bytes, record roundtrips (header, schema, channel, message, chunk), full pipeline, recorder operations, dual-write to session, and close idempotency", Phase10Validation.Validate),
            Ci("--phase11", "Phase 11: validates MCAP compression, binary reader helpers, McapReader summary, replay engine load/tick, and replay channel ID mapping", Phase11Validation.Validate),
            Ci("--phase12", "Phase 12: validates LZ4/Zstd compression roundtrips, parameters/services metadata, client publish message, coordinate mode in channel metadata, and MetadataIndex read/parse roundtrip", Phase12Validation.Validate),
            Ci("--phase13", "Phase 13: validates IRuntimeContext indirection, recording/replay controllers, McapBinaryReader bounds checks, client publish auto-increment, seek boundary behavior, coordinate roundtrip, and handler non-accumulation", Phase13Validation.Validate),
            Ci("--phase14", "Phase 14: validates FoxRunAttribute construction, defaults, validation, and usage constraints", Phase14Validation.Validate),
            // Phase 15 had no standalone validation file in the historical
            // sequence; package/repository hygiene coverage continues in Phase 16.
            Ci("--phase16", "Phase 16: validates package metadata (package.json, LICENSE), .gitignore build artifact coverage, CI workflows, and asmdef consistency", Phase16Validation.Validate),
            Ci("--phase17", "Phase 17: validates UPM samples declaration, BasicVisualization and FullDemoVisualization sample integrity, forbidden items, and layout consistency", Phase17Validation.Validate),
            Ci("--phase24d", "Phase 24D: validates MCAP mixed-schema guards, client publish schema dedup, encoding normalization (empty == json), and duplicate topic rejection", Phase24DValidation.Validate),
            Ci("--phase28", "Phase 28", Phase28Validation.Validate),
            Ci("--phase31", "Phase 31", Phase31Validation.Validate,
                evidence: ValidationEvidence.Behavior | ValidationEvidence.Structural | ValidationEvidence.Conformance | ValidationEvidence.FaultInjection),
            Ci("--phase32", "Phase 32: validation for protobuf encoding, schema catalog, publish, and MCAP paths", Phase32Validation.Validate),
            Ci("--phase33", "Phase 33: validate Phase 33 transport backpressure queueing and WebSocket", Phase33Validation.Validate),
            Ci("--phase34", "Phase 34: validate Phase 34 MCAP attachment records and summary CRC behavior", Phase34Validation.Validate),
            Ci("--phase36", "Phase 36", Phase36Validation.Validate),
            Ci("--phase37", "Phase 37: regression tests for direct-write MCAP message records", Phase37Validation.Validate),
            Ci("--phase40", "Phase 40: camera backpressure policy validation", Phase40Validation.Validate),
            Ci("--phase41", "Phase 41: FoxRun event-driven publish policy validation", Phase41Validation.Validate),
            Ci("--phase44", "Phase 44: validation for complete official protobuf schema coverage:", Phase44Validation.Validate),
            Ci("--phase48", "Phase 48: validation for camera CompressedImage protobuf parity", Phase48Validation.Validate),
            Ci("--phase49", "Phase 49: validation for sensor typed publisher builders", Phase49Validation.Validate),
            Ci("--phase50", "Phase 50: regression coverage for critical stability/security fixes", Phase50Validation.Validate),
            Ci("--phase51", "Phase 51: regression coverage for maintainability, lifecycle,", Phase51Validation.Validate),
            Ci("--phase52", "Phase 52: validate Phase 52 Unity-native WSS/TLS transport extension,", Phase52Validation.Validate),
            Ci("--phase53", "Phase 53: FoxRun explicit trigger telemetry validation", Phase53Validation.Validate),
            Ci("--phase54", "Phase 54: regression coverage for session backpressure,", Phase54Validation.Validate),
            Ci("--phase55", "Phase 55: regression coverage for MCAP replay hardening,", Phase55Validation.Validate),
            Ci("--phase56", "Phase 56: regression coverage for FoxRun source-generation", Phase56Validation.Validate),
            Ci("--phase57", "Phase 57: regression coverage for schema/publisher encoding", Phase57Validation.Validate),
            Ci("--phase65", "Phase 65: regression coverage for multi-client PlaybackControl", Phase65Validation.Validate),
            Ci("--phase67", "Phase 67: validation for official Foxglove WebSocket status", Phase67Validation.Validate),
            Ci("--phase68", "Phase 68: validation for the MCAP indexed reader surface", Phase68Validation.Validate),
            Ci("--phase69", "Phase 69: validation for MCAP indexed reader Inspector integration", Phase69Validation.Validate),
            Ci("--phase70", "Phase 70: validation for FoxgloveManager Inspector workflow UX", Phase70Validation.Validate),
            Ci("--phase71", "Phase 71: validation for global default publisher rate policy", Phase71Validation.Validate),
            Ci("--phase72", "Phase 72: validation for stable fixed-rate live publish cadence", Phase72Validation.Validate),
            Ci("--phase73", "Phase 73: validation for subscription-aware heavy topic demand gating", Phase73Validation.Validate),
            Ci("--phase74", "Phase 74: validation for H.264 foxglove.CompressedVideo publishing", Phase74Validation.Validate),
            Ci("--phase75", "Phase 75: validation for unified camera output mode UX and", Phase75Validation.Validate),
            Ci("--phase76", "Phase 76: validation for H.265/HEVC foxglove.CompressedVideo mode", Phase76Validation.Validate),
            Ci("--phase77", "Phase 77: validation for manual-only FFmpeg setup UX", Phase77Validation.Validate),
            Ci("--phase80", "Phase 80: validation for source-only OpenH264 encoder spike", Phase80Validation.Validate),
            Ci("--phase81", "Phase 81: validation for OpenH264 official-binary camera integration", Phase81Validation.Validate),
            Ci("--phase82", "Phase 82: validation for the experimental Windows native H.264 camera backend", Phase82Validation.Validate),
            Manual("--phase82-native-smoke", "Phase 82 native smoke", Phase82Validation.RunNativeSmoke),
            Ci("--phase83", "Phase 83: validation for raw PointCloud QoS budget, LOD,", Phase83Validation.Validate),
            Ci("--phase84", "Phase 84: validation for raw PointCloud voxel-grid LOD", Phase84Validation.Validate),
            Ci("--phase85", "Phase 85: validation for point-cloud Inspector UX and smoke evidence", Phase85Validation.Validate),
            Ci("--phase86", "Phase 86: validation for runtime hardening bugfixes", Phase86Validation.Validate),
            Ci("--phase87", "Phase 87: validation for CompressedPointCloud / Draco spike scaffolding", Phase87Validation.Validate),
            Ci("--phase88", "Phase 88: validation for Draco CompressedPointCloud evidence tooling", Phase88Validation.Validate),
            Ci("--phase89", "Phase 89: validation for Draco CompressedPointCloud productization", Phase89Validation.Validate),
            Ci("--phase90", "Phase 90: validation for ROS 2 .msg schema registry parity", Phase90Validation.Validate),
            Ci("--phase91", "Phase 91: validation for minimal ROS 2 CDR payload writing", Phase91Validation.Validate),
            Ci("--phase92", "Phase 92: validation for productized ROS2 publisher delivery", Phase92Validation.Validate),
            Ci("--phase93", "Phase 93: validation for full ROS 2 .msg CDR serializer parity", Phase93Validation.Validate),
            Ci("--phase94", "Phase 94: validation for the localhost Unity-to-ROS2 bridge spike", Phase94Validation.Validate),
            Ci("--phase95", "Phase 95: validation for ROS2 Bridge productization", Phase95Validation.Validate),
            Ci("--phase96", "Phase 96: validation for ROS2 Bridge topic profiles and QoS metadata", Phase96Validation.Validate),
            Ci("--phase97", "Phase 97: validation for ROS2 Bridge health diagnostics", Phase97Validation.Validate),
            Ci("--phase98", "Phase 98: validation for ROS2 Bridge sample and launch kit evidence", Phase98Validation.Validate),
            Ci("--phase99", "Phase 99: validation for ROS2 release evidence gate reporting", Phase99Validation.Validate),
            Ci("--phase100", "Phase 100: runtime hardening closure validation", Phase100Validation.Validate),
            Ci("--phase105", "Phase 105: Phase100 cleanup and comment governance validation", Phase105Validation.Validate),
            Ci("--phase106", "Phase 106: ROS2 For Unity standalone interop spike validation", Phase106Validation.Validate),
            Ci("--phase107", "Phase 107: ROS2 For Unity optional package distribution gate validation", Phase107Validation.Validate),
            Ci("--phase108", "Phase 108: ROS2 For Unity facade boundary validation", Phase108Validation.Validate),
            Ci("--phase109", "Phase 109: ROS2 For Unity bidirectional string smoke boundary validation", Phase109Validation.Validate),
            Ci("--phase110", "Phase 110: ROS2 For Unity external adapter sample validation", Phase110Validation.Validate),
            Ci("--phase111f", "Phase 111F: R2FU runtime lifecycle and review-fix validation", Phase111FValidation.Validate),
            Ci("--phase112", "Phase 112: FoxRun canonical manifest and fingerprint validation", Phase112Validation.Validate),
            Ci("--phase112b", "Phase 112B: FoxRun debug overlay validation", Phase112BValidation.Validate),
            Ci("--phase113", "Phase 113: FoxRun runtime schema info and drift gate validation", Phase113Validation.Validate),
            Ci("--phase114", "Phase 114: FoxRun MCAP metadata and replay schema guard validation", Phase114Validation.Validate),
            Ci("--phase115", "Phase 115: SDK schema manifest aggregate validation", Phase115Validation.Validate),
            Ci("--phase115b", "Phase 115B: schema evidence identity UX validation", Phase115BValidation.Validate),
            Ci("--phase115c", "Phase 115C: schema evidence hardening validation", Phase115CValidation.Validate),
            Ci("--phase115d", "Phase 115D: validates replay pose ownership arbitration without depending on UnityEngine", Phase115DValidation.Validate),
            Ci("--phase115e", "Phase 115E: validates FoxRun analyzer diagnostics and generation-model equivalence", Phase115EValidation.Validate),
            Ci("--phase115f", "Phase 115F: hardens FoxRun generation-model equivalence after Phase 115E", Phase115FValidation.Validate,
                evidence: ValidationEvidence.Behavior | ValidationEvidence.Structural | ValidationEvidence.Conformance),
            Ci("--phase115g", "Phase 115G: validates review-fix hardening after replay pose and FoxRun generation-model reviews", Phase115GValidation.Validate),
            Ci("--phase115h", "Phase 115H: validates the post-Phase105 comment governance refresh boundary", Phase115HValidation.Validate),
            Ci("--phase116", "Phase 116: validation for the local MCAP DataLoader facade", Phase116Validation.Validate),
            Ci("--phase117", "Phase 117: validation for MCAP spec parity matrix and local direct-message fallback", Phase117Validation.Validate,
                evidence: ValidationEvidence.Behavior | ValidationEvidence.Conformance),
            Ci("--phase118", "Phase 118: validation for MCAP DataLoader hardening and performance harness coverage", Phase118Validation.Validate,
                evidence: ValidationEvidence.Behavior | ValidationEvidence.Performance),
            Ci("--phase119", "Phase 119: validation for the local prototype remote MCAP data-source boundary", Phase119Validation.Validate),
            Local("--phase120", "Phase 120: MCAP official compatibility gate validation and evidence report generation", Phase120Validation.Validate,
                evidence: ValidationEvidence.Conformance),
            Local("--phase120-official", "Phase 120 official compatibility", Phase120Validation.ValidateOfficial,
                evidence: ValidationEvidence.Conformance),
            Local("--phase120b", "Phase 120B: validation for MCAP DataLoader hardening review closure", Phase120BValidation.Validate,
                evidence: ValidationEvidence.Behavior | ValidationEvidence.Conformance),
            Local("--phase121", "Phase 121: validation for the C# MCAP official conformance runner baseline", Phase121Validation.Validate,
                evidence: ValidationEvidence.Conformance),
            Ci("--phase121-conformance", "Phase 121 conformance", Phase121Validation.ValidateConformance, includeInDefault: false,
                evidence: ValidationEvidence.Conformance),
            Ci("--phase122", "Phase 122: validation for MCAP writer option parity", Phase122Validation.Validate, includeInDefault: false,
                evidence: ValidationEvidence.Behavior | ValidationEvidence.Conformance),
            Ci("--phase123", "Phase 123: validation for MCAP streaming reader and query parity", Phase123Validation.Validate, includeInDefault: false,
                evidence: ValidationEvidence.Behavior | ValidationEvidence.Conformance),
            Ci("--phase124", "Phase 124: validation for decoded MCAP DataLoader iteration", Phase124Validation.Validate, includeInDefault: false,
                evidence: ValidationEvidence.Behavior | ValidationEvidence.Conformance),
            Ci("--phase125", "Phase 125: validation for typed ROS2 CDR MCAP decode", Phase125Validation.Validate, includeInDefault: false,
                evidence: ValidationEvidence.Behavior | ValidationEvidence.Conformance),
            Ci("--phase126", "Phase 126: architecture coupling, local-boundary, and validation-registry gate", Phase126Validation.Validate,
                evidence: ValidationEvidence.Structural),
            Ci("--phase128", "Phase 128: RViz2 standard visualization acceptance kit validation", Phase128Validation.Validate, includeInDefault: false),
            Ci("--phase129", "Phase 129: generic PointCloud2 RViz2 acceptance kit validation", Phase129Validation.Validate, includeInDefault: false),
            Ci("--phase130", "Phase 130: MarkerArray RViz2 acceptance kit validation", Phase130Validation.Validate, includeInDefault: false),
            Ci("--phase131", "Phase 131: ROS2 standard visualization productization gate validation", Phase131Validation.Validate, includeInDefault: false),
            Ci("--phase132", "Phase 132: ROS2 standard message expansion validation", Phase132Validation.Validate, includeInDefault: false),
            Ci("--phase134-1", "Phase 134-1: validates Phase 134-1 runtime facade and lifecycle hardening", Phase134_1Validation.Validate),
            Ci("--phase134-2", "Phase 134-2: validates Phase 134-2 session protocol and registry hardening", Phase134_2Validation.Validate),
            Ci("--phase134-3", "Phase 134-3: validates Phase 134-3 recording shutdown and replay callback hardening", Phase134_3Validation.Validate),
            Ci("--phase134-4", "Phase 134-4: validates Phase 134-4 publisher topic guardrails", Phase134_4Validation.Validate),
            Ci("--phase134-5", "Phase 134-5: validates Phase 134-5 replay adapter and FoxRun hub hardening", Phase134_5Validation.Validate),
            Ci("--phase134-6", "Phase 134-6: validates Phase 134-6 managed WebSocket active-client admission bounds", Phase134_6Validation.Validate),
            Ci("--phase134-7", "Phase 134-7: regression coverage for PlaybackControl request id", Phase134_7Validation.Validate),
            Ci("--phase134-8", "Phase 134-8: regression coverage for MCAP length-prefix bounds", Phase134_8Validation.Validate),
            Ci("--phase134-9", "Phase 134-9: regression coverage for MCAP reader/indexing edge cases", Phase134_9Validation.Validate),
            Ci("--phase134-10", "Phase 134-10: regression coverage for MCAP DataLoader query budgets", Phase134_10Validation.Validate),
            Ci("--phase134-11", "Phase 134-11: regression coverage for schema descriptor immutability", Phase134_11Validation.Validate),
            Ci("--phase134-12", "Phase 134-12: regression coverage for protobuf builders and typed publishers", Phase134_12Validation.Validate),
            Ci("--phase134-13", "Phase 134-13: regression coverage for video encoder sidecar frame geometry", Phase134_13Validation.Validate),
            Ci("--phase134-14", "Phase 134-14: regression coverage for native Draco point-cloud input bounds", Phase134_14Validation.Validate),
            Ci("--phase134-15", "Phase 134-15: regression coverage for ROS2 .msg CDR schema helpers", Phase134_15Validation.Validate),
            Ci("--phase134-16", "Phase 134-16: regression coverage for ROS2 bridge frame payload ownership", Phase134_16Validation.Validate),
            Ci("--phase134-17", "Phase 134-17: validation for FoxRun invalid-topic fail-fast behavior", Phase134_17Validation.Validate),
            Ci("--phase134-18", "Phase 134-18: validation for stale FoxRun physical fallback cleanup", Phase134_18Validation.Validate),
            Ci("--phase134-19", "Phase 134-19: validation for OpenH264 installer artifact hash pinning", Phase134_19Validation.Validate),
            Ci("--phase134-20", "Phase 134-20: validation for safe native editor process execution", Phase134_20Validation.Validate),
            Ci("--phase134-21", "Phase 134-21: validation for ROS2 For Unity adapter facade behavior", Phase134_21Validation.Validate),
            Ci("--phase134-22", "Phase 134-22: validation for bundled Jazzy ROS2 For Unity wrapper hardening", Phase134_22Validation.Validate),
            Ci("--phase134-23", "Phase 134-23: validation for core SDK sample package import boundaries", Phase134_23Validation.Validate),
            Ci("--phase134-24", "Phase 134-24: validation for Unity demo runtime script hardening", Phase134_24Validation.Validate),
            Ci("--phase134-25", "Phase 134-25: regression coverage for experimental OpenH264 probe hardening", Phase134_25Validation.Validate),
            Ci("--phase134-26", "Phase 134-26: regression coverage for R2FU adapter sample queue bounds", Phase134_26Validation.Validate),
            Ci("--phase134-27", "Phase 134-27: regression coverage for ROS2 For Unity sample sync and smoke hardening", Phase134_27Validation.Validate),
            Ci("--phase134-28", "Phase 134-28: regression coverage for runtime package builder extraction safety", Phase134_28Validation.Validate),
            Ci("--phase134-29", "Phase 134-29: regression coverage for core smoke script hardening", Phase134_29Validation.Validate),
            Ci("--phase134-30", "Phase 134-30: regression coverage for R2FU smoke/build script path hygiene", Phase134_30Validation.Validate),
            Ci("--phase134-31", "Phase 134-31: regression coverage for generator/build architecture scripts", Phase134_31Validation.Validate,
                evidence: ValidationEvidence.Structural),
            Ci("--phase134-32", "Phase 134-32: regression coverage for optional R2FU adapter test harness isolation", Phase134_32Validation.Validate),
            Ci("--phase134-33", "Phase 134-33: regression coverage for early baseline validation hardening", Phase134_33Validation.Validate),
            Ci("--phase134-34", "Phase 134-34: guard Phase 134-34 mid-baseline validation fixes", Phase134_34Validation.Validate),
            Ci("--phase134-35", "Phase 134-35: validate Phase 134-35 MCAP test helper hardening", Phase134_35Validation.Validate),
            Ci("--phase137b", "Phase 137B: recording/replay controller decoupling guard", Phase137BValidation.Validate,
                evidence: ValidationEvidence.Structural),
            Ci("--phase137c", "Phase 137C: editor codegen refactoring guard", Phase137CValidation.Validate,
                evidence: ValidationEvidence.Structural),
            Ci("--phase137d", "Phase 137D: McapReader decode split guard", Phase137DValidation.Validate,
                evidence: ValidationEvidence.Structural),
            Ci("--phase137e", "Phase 137E: FoxgloveManagerEditor partial-class split guard", Phase137EValidation.Validate,
                evidence: ValidationEvidence.Structural),
            Ci("--phase137f", "Phase 137F: runtime orchestration decoupling guard", Phase137FValidation.Validate,
                evidence: ValidationEvidence.Structural),
            // Phase 137G is an explicit governance audit until the existing documentation baseline is remediated.
            Ci("--phase137g", "Phase 137G", Phase137GValidation.Validate, includeInDefault: false,
                evidence: ValidationEvidence.Structural),
            Ci("--phase137", "Phase 137: directory-first runtime structure guard", Phase137Validation.Validate,
                evidence: ValidationEvidence.Structural),
            Ci("--phase142", "Phase 142: FoxRun type safety hardening validation", Phase142Validation.Validate),
            Local("--phase143", "Phase 143: R2FU standalone distro upgrade ladder strategy validation", Phase143Validation.Validate),
            Ci("--phase144", "Phase 144: protocol-edge validation for WebSocket fragmentation,", ProtocolEdgeHardeningValidation.Validate, includeInDefault: false),
            Ci("--phase145", "Phase 145: validation for the structured System Info publisher", SystemInfoPublisherValidation.Validate, includeInDefault: false),
            Ci("--phase146a", "Phase 146A: validation for the project-level R2FU active runtime selector", R2fuActiveRuntimeSelectorValidation.Validate, includeInDefault: false),
            Ci("--phase146b", "Phase 146B: validation for the R2FU Lyrical Win64 runtime package", R2fuLyricalRuntimePackageValidation.Validate, includeInDefault: false),
            Ci("--phase147", "Phase 147: generated-source literal and determinism validation", Phase147Validation.Validate, includeInDefault: false),
            Ci("--phase148", "Phase 148: per-sink channel filtering validation", Phase148Validation.Validate, includeInDefault: false),
            Ci("--phase149a", "Phase 149A: validation for lazy MCAP file-order iteration", Phase149AValidation.Validate, includeInDefault: false),
            Ci("--phase149b", "Phase 149B: validation for post-recording MCAP metadata and attachment amendment", Phase149BValidation.Validate, includeInDefault: false,
                evidence: ValidationEvidence.Behavior | ValidationEvidence.Structural | ValidationEvidence.FaultInjection),
            Ci("--phase149c", "Phase 149C: validation for MCAP private record writing and enumeration", Phase149CValidation.Validate, includeInDefault: false),
            Ci("--phase150", "Phase 150: validation for SDK-style channel facade API boundaries", Phase150Validation.Validate, includeInDefault: false),
            Ci("--phase151", "Phase 151: validation for profiler infrastructure boundaries", Phase151Validation.Validate, includeInDefault: false),
            Ci("--phase153", "Phase 153: validation for FoxRun topic contracts and local bus boundaries", Phase153Validation.Validate, includeInDefault: false),
            Ci("--phase154", "Phase 154: validation for FoxRun message aggregation and schema inference", Phase154Validation.Validate, includeInDefault: false),
            Ci("--phase155", "Phase 155: validation for additive FoxRun multi-sink fanout", Phase155Validation.Validate, includeInDefault: false),
            Ci("--phase156", "Phase 156: validation for the optional FoxRun ROS2 R2FU sink boundary", Phase156Validation.Validate, includeInDefault: false),
            Ci("--phase157", "Phase 157: repository boundary checks for FoxRun inbound and local services", Phase157Validation.Validate, includeInDefault: false),
            Ci("--phase160", "Phase 160: validation for the R2FU Humble Win64 runtime package", R2fuHumbleRuntimePackageValidation.Validate, includeInDefault: false),
            Ci("--phase161", "Phase 161: validation for the R2FU Jazzy Win64 runtime refresh", R2fuJazzyRuntimeRefreshValidation.Validate, includeInDefault: false),
            Ci("--phase162", "Phase 162: phase 146B validation for the R2FU Lyrical Win64 runtime package", R2fuLyricalRuntimePackageValidation.ValidatePhase162, includeInDefault: false),
            Ci("--phase165", "R2FU native bridge hot path performance", R2fuNativeBridgeHotPathLifecycleValidation.Validate, includeInDefault: false,
                evidence: ValidationEvidence.Behavior | ValidationEvidence.Performance),
            Ci("--phase168", "Phase 168: validation for MessagePack raw channel encoding support", MessagePackRawChannelEncodingValidation.Validate, includeInDefault: false),
            Ci("--phase171", "Phase 171: optional Remote Access Gateway package boundary", RemoteGatewayBoundaryValidation.Validate, includeInDefault: false),
            Ci("--phase172", "Phase 172: camera health-based capture admission", CameraHealthCaptureAdmissionValidation.Validate, includeInDefault: false),
            Ci("--phase175a", "Phase 175A: FoxRun typed Protobuf contract model", Phase175AValidation.Validate, includeInDefault: false),
            Ci("--phase175b", "Phase 175B: FoxRun dual-codec generation and routing", Phase175BValidation.Validate, includeInDefault: false),
            Ci("--phase175c", "Phase 175C: FoxRun Manager wire policy and migration", Phase175CValidation.Validate, includeInDefault: false),
            Ci("--phase176", "Phase 176: FoxRun Subscribe Data and Publish panel", Phase176Validation.Validate, includeInDefault: false),
            Ci("--phase179", "FoxRun native ROS2 subscription boundary", FoxRunRos2NativeSubscriptionValidation.Validate, includeInDefault: false,
                evidence: ValidationEvidence.Structural),
            Ci("--phase163-2", "Phase 163-2: phase163-2 review regression checks for FoxgloveManager lifecycle contracts", Phase163_2Validation.Validate, includeInDefault: false),
            Ci("--phase163-3", "Phase 163-3: phase163-3 review regression checks for session protocol and client routing", Phase163_3Validation.Validate, includeInDefault: false),
            Ci("--phase163-4", "Phase 163-4: phase163-4 review regression checks for registries, assets, parameters, and services", Phase163_4Validation.Validate, includeInDefault: false),
            Ci("--phase163-5", "Phase 163-5: phase163-5 review regression checks for transport, queues, TLS/auth, and backpressure", Phase163_5Validation.Validate, includeInDefault: false),
            Ci("--phase163-6", "Phase 163-6: phase163-6 review regression checks for protocol frames, time, and runtime utilities", Phase163_6Validation.Validate, includeInDefault: false),
            Ci("--phase163-7", "Phase 163-7: phase163-7 review regression checks for recording controller and MCAP recorder orchestration", Phase163_7Validation.Validate, includeInDefault: false),
            Ci("--phase163-8", "Phase 163-8: phase163-8 review regression checks for replay controller, cursor, and timeline ownership", Phase163_8Validation.Validate, includeInDefault: false),
            Ci("--phase163-9", "Phase 163-9: phase163-9 review regression checks for MCAP writer internals", Phase163_9Validation.Validate, includeInDefault: false),
            Ci("--phase163-10", "Phase 163-10: phase163-10 review regression checks for MCAP reader parsing", Phase163_10Validation.Validate, includeInDefault: false),
            Ci("--phase163-11", "Phase 163-11: validation for MCAP DataLoader, Remote File, and Replay Engine review fixes", Phase163_11Validation.Validate, includeInDefault: false),
            Ci("--phase163-12", "Phase 163-12: validation for schema catalog and identity review fixes", Phase163_12Validation.Validate, includeInDefault: false),
            Ci("--phase163-13", "Phase 163-13: validation for protobuf/JSON builder review fixes", Phase163_13Validation.Validate, includeInDefault: false),
            Ci("--phase163-14", "Phase 163-14: validation for publisher base, cadence, and output policy review fixes", Phase163_14Validation.Validate, includeInDefault: false),
            Ci("--phase163-15", "Phase 163-15: validation for camera publisher and editor review fixes", Phase163_15Validation.Validate, includeInDefault: false),
            Ci("--phase163-16", "Phase 163-16: validation for video sidecar and codec review fixes", Phase163_16Validation.Validate, includeInDefault: false),
            Ci("--phase163-17", "Phase 163-17: validation for point-cloud, LaserScan, and geometry payload fixes", Phase163_17Validation.Validate, includeInDefault: false),
            Ci("--phase163-18", "Phase 163-18: validation for virtual LiDAR, IMU, and sensor simulation fixes", Phase163_18Validation.Validate, includeInDefault: false),
            Ci("--phase163-19", "Phase 163-19: validation for ROS2 CDR writer and generator review fixes", Phase163_19Validation.Validate, includeInDefault: false),
            Ci("--phase163-20", "Phase 163-20: validation for ROS2 Bridge and R2FU boundary review fixes", Phase163_20Validation.Validate, includeInDefault: false),
            Ci("--phase163-21", "Phase 163-21: validation for FoxRun runtime bus, sinks, and inbound gates", Phase163_21Validation.Validate, includeInDefault: false),
            Ci("--phase163-22", "Phase 163-22: validation for FoxRun emitter model and descriptor contracts", Phase163_22Validation.Validate, includeInDefault: false),
            Ci("--phase163-23", "Phase 163-23: validation for source generator and analyzer behavior", Phase163_23Validation.Validate, includeInDefault: false),
            Ci("--phase163-24", "Phase 163-24: validation for schema evidence and build tooling guards", Phase163_24Validation.Validate, includeInDefault: false),
            Ci("--phase163-25", "Phase 163-25: validation for Inspector UI lifecycle and state guards", Phase163_25Validation.Validate, includeInDefault: false),
            Ci("--phase163-26", "Phase 163-26: validation for editor native helper, certificate,", Phase163_26Validation.Validate, includeInDefault: false),
            Ci("--phase163-27", "Phase 163-27: validation for R2FU runtime selection and play-mode guards", Phase163_27Validation.Validate, includeInDefault: false),
            Ci("--phase163-28", "Phase 163-28: validation for R2FU native bridge lifecycle hardening", Phase163_28Validation.Validate, includeInDefault: false),
            Ci("--phase163-29", "Phase 163-29: validation for R2FU runtime package governance", Phase163_29Validation.Validate, includeInDefault: false),
            Ci("--phase163-30", "Phase 163-30: validation for Humble runtime import and FastRTPS package path hardening", Phase163_30Validation.Validate, includeInDefault: false),
            Ci("--phase163-31", "Phase 163-31: validation for Jazzy runtime refresh and package path hardening", Phase163_31Validation.Validate, includeInDefault: false),
            Ci("--phase163-32", "Phase 163-32: validation for Lyrical runtime selection and Zenoh package hardening", Phase163_32Validation.Validate, includeInDefault: false),
            Ci("--phase163-33", "Phase 163-33: validation for R2FU sample and RViz acceptance hardening", Phase163_33Validation.Validate, includeInDefault: false),
            Ci("--phase163-35", "Phase 163-35: validation for demo sensor and manual smoke review boundaries", Phase163_35Validation.Validate, includeInDefault: false),
            Ci("--phase163-36", "Phase 163-36: phase163-36 Unity demo scene and project-settings review closure", Phase163_36Validation.Validate, includeInDefault: false),
            Ci("--phase163-37", "Phase 163-37: phase163-37 SDK sample and public package example review closure", Phase163_37Validation.Validate, includeInDefault: false),
            Ci("--phase163-38", "Phase 163-38: phase163-38 Foxglove extension cursor bridge review closure", Phase163_38Validation.Validate, includeInDefault: false),
            Ci("--phase163-39", "Phase 163-39: phase163-39 ROS2 bridge sidecar and launch tooling review closure", Phase163_39Validation.Validate, includeInDefault: false),
            Ci("--phase163-40", "Phase 163-40: phase163-40 release, packaging, and CI script review closure", Phase163_40Validation.Validate, includeInDefault: false),
            Ci("--phase163-44", "Phase 163-44: phase163-44 runtime harness and shared helper review closure", Phase163_44Validation.Validate, includeInDefault: false),
            Ci("--phase163-45", "Phase 163-45: phase163-45 early protocol and runtime validation review closure", Phase163_45Validation.Validate, includeInDefault: false),
            Ci("--phase163-46", "Phase 163-46: phase163-46 mid protocol and session validation review closure", Phase163_46Validation.Validate, includeInDefault: false),
            Ci("--phase163-47", "Phase 163-47: review closure for MCAP/replay validation hardening", Phase163_47Validation.Validate, includeInDefault: false),
            Ci("--phase163-48", "Phase 163-48: review closure for transport/runtime-control validation hardening", Phase163_48Validation.Validate, includeInDefault: false),
            Ci("--phase163-49", "Phase 163-49: review closure for FoxRun/schema validation hardening", Phase163_49Validation.Validate, includeInDefault: false),
            Ci("--phase163-50", "Phase 163-50: review closure for generator/service validation hardening", Phase163_50Validation.Validate, includeInDefault: false),
            Ci("--phase163-51", "Phase 163-51: review closure for DataLoader and R2FU setup validations", Phase163_51Validation.Validate, includeInDefault: false),
            Ci("--phase163-52", "Phase 163-52: review closure for real-project and R2FU smoke validations", Phase163_52Validation.Validate, includeInDefault: false),
            Ci("--phase163-53", "Phase 163-53: review follow-up guard for Phase 134/137 validation robustness", Phase163_53Validation.Validate, includeInDefault: false),
            Ci("--phase163-54", "Phase 163-54: review follow-up guard for Phase 138 sensor validations", Phase163_54Validation.Validate, includeInDefault: false),
            Ci("--phase163-55", "Phase 163-55: review follow-up guard for Phase 139 remote timeline validations", Phase163_55Validation.Validate, includeInDefault: false),
            Ci("--phase163-56", "Phase 163-56: phase163-56 review regression coverage for runtime validation hygiene", Phase163_56Validation.Validate, includeInDefault: false),
            Ci("--phase163-57", "Phase 163-57: phase163-57 review regression coverage for unit, conformance, and performance test hygiene", Phase163_57Validation.Validate, includeInDefault: false,
                evidence: ValidationEvidence.Structural | ValidationEvidence.Conformance | ValidationEvidence.Performance),
            Ci("--phase163-58", "Phase 163-58: phase163-58 regression coverage for isolated local CI dotnet build roots", Phase163_58Validation.Validate, includeInDefault: false),
            Ci("--phase164-1", "Phase 164-1: phase164-1 optimization regression coverage for repository validation paths", Phase164_1Validation.Validate, includeInDefault: false),
            Ci("--phase164-2", "Phase 164-2: phase164-2 optimization regression coverage for runtime lifecycle hot paths", Phase164_2Validation.Validate, includeInDefault: false),
            Ci("--phase164-3", "Phase 164-3: phase164-3 optimization regression coverage for session routing hot paths", Phase164_3Validation.Validate, includeInDefault: false),
            Ci("--phase164-4", "Phase 164-4", Phase164_4Validation.Validate, includeInDefault: false),
            Ci("--phase164-5", "Phase 164-5", Phase164_5Validation.Validate, includeInDefault: false),
            Ci("--phase164-6", "Phase 164-6", Phase164_6Validation.Validate, includeInDefault: false),
            Ci("--phase164-7", "Phase 164-7", Phase164_7Validation.Validate, includeInDefault: false),
            Ci("--phase164-8", "Phase 164-8", Phase164_8Validation.Validate, includeInDefault: false),
            Ci("--phase164-9", "Phase 164-9", Phase164_9Validation.Validate, includeInDefault: false),
            Ci("--phase164-10", "Phase 164-10", Phase164_10Validation.Validate, includeInDefault: false),
            Ci("--phase164-11", "Phase 164-11", Phase164_11Validation.Validate, includeInDefault: false),
            Ci("--phase164-12", "Phase 164-12", Phase164_12Validation.Validate, includeInDefault: false),
            Ci("--phase164-13", "Phase 164-13", Phase164_13Validation.Validate, includeInDefault: false),
            Ci("--phase164-14", "Phase 164-14", Phase164_14Validation.Validate, includeInDefault: false),
            Ci("--phase164-15", "Phase 164-15", Phase164_15Validation.Validate, includeInDefault: false),
            Ci("--phase164-16", "Phase 164-16", Phase164_16Validation.Validate, includeInDefault: false),
            Ci("--phase164-17", "Phase 164-17", Phase164_17Validation.Validate, includeInDefault: false),
            Ci("--phase164-18", "Phase 164-18", Phase164_18Validation.Validate, includeInDefault: false),
            Ci("--phase164-19", "Phase 164-19", Phase164_19Validation.Validate, includeInDefault: false),
            Ci("--phase164-20", "Phase 164-20", Phase164_20Validation.Validate, includeInDefault: false),
            Ci("--phase164-21", "Phase 164-21", Phase164_21Validation.Validate, includeInDefault: false),
            Ci("--phase164-22", "Phase 164-22", Phase164_22Validation.Validate, includeInDefault: false),
            Ci("--phase164-23", "Phase 164-23", Phase164_23Validation.Validate, includeInDefault: false),
            Ci("--phase164-24", "Phase 164-24", Phase164_24Validation.Validate, includeInDefault: false),
            Ci("--phase164-25", "Phase 164-25", Phase164_25Validation.Validate, includeInDefault: false),
            Ci("--phase164-26", "Phase 164-26", Phase164_26Validation.Validate, includeInDefault: false),
            Ci("--phase164-27", "Phase 164-27", Phase164_27Validation.Validate, includeInDefault: false),
            Ci("--phase164-28", "Phase 164-28", Phase164_28Validation.Validate, includeInDefault: false),
            Ci("--phase164-29", "Phase 164-29", Phase164_29Validation.Validate, includeInDefault: false),
            Ci("--phase164-30", "Phase 164-30", Phase164_30Validation.Validate, includeInDefault: false),
            Ci("--phase164-31", "Phase 164-31", Phase164_31Validation.Validate, includeInDefault: false),
            Ci("--phase164-32", "Phase 164-32", Phase164_32Validation.Validate, includeInDefault: false),
            Ci("--phase164-33", "Phase 164-33", Phase164_33Validation.Validate, includeInDefault: false),
            Ci("--phase164-34", "Phase 164-34", Phase164_34Validation.Validate, includeInDefault: false),
            Ci("--phase164-35", "Phase 164-35", Phase164_35Validation.Validate, includeInDefault: false),
            Ci("--phase164-36", "Phase 164-36", Phase164_36Validation.Validate, includeInDefault: false),
            Ci("--phase164-37", "Phase 164-37", Phase164_37Validation.Validate, includeInDefault: false),
            Ci("--phase164-38", "Phase 164-38", Phase164_38Validation.Validate, includeInDefault: false),
            Ci("--phase164-39", "Phase 164-39", Phase164_39Validation.Validate, includeInDefault: false),
            Ci("--phase164-40", "Phase 164-40", Phase164_40Validation.Validate, includeInDefault: false),
            Ci("--phase164-41", "Phase 164-41", Phase164_41Validation.Validate, includeInDefault: false),
            Ci("--phase164-42", "Phase 164-42", Phase164_42Validation.Validate, includeInDefault: false),
            Ci("--phase164-43", "Phase 164-43", Phase164_43Validation.Validate, includeInDefault: false),
            Ci("--phase164-44", "Phase 164-44", Phase164_44Validation.Validate, includeInDefault: false),
            Ci("--phase164-45", "Phase 164-45", Phase164_45Validation.Validate, includeInDefault: false),
            Ci("--phase164-46", "Phase 164-46", Phase164_46Validation.Validate, includeInDefault: false),
            Ci("--phase164-47", "Phase 164-47", Phase164_47Validation.Validate, includeInDefault: false),
            Ci("--phase164-48", "Phase 164-48", Phase164_48Validation.Validate, includeInDefault: false),
            Ci("--phase164-49", "Phase 164-49", Phase164_49Validation.Validate, includeInDefault: false),
            Ci("--phase164-50", "Phase 164-50", Phase164_50Validation.Validate, includeInDefault: false),
            Ci("--phase164-51", "Phase 164-51", Phase164_51Validation.Validate, includeInDefault: false),
            Ci("--phase164-52", "Phase 164-52", Phase164_52Validation.Validate, includeInDefault: false),
            Ci("--phase164-53", "Phase 164-53", Phase164_53Validation.Validate, includeInDefault: false),
            Ci("--phase164-54", "Phase 164-54", Phase164_54Validation.Validate, includeInDefault: false),
            Ci("--phase164-55", "Phase 164-55: optimization guards for Phase 139 remote timeline paths", Phase164_55Validation.Validate, includeInDefault: false),
            Ci("--phase164-56", "Phase 164-56: optimization guards for latest runtime validations", Phase164_56Validation.Validate, includeInDefault: false),
            Ci("--phase164-57", "Phase 164-57: optimization guards for unit, conformance, and performance tests", Phase164_57Validation.Validate, includeInDefault: false,
                evidence: ValidationEvidence.Structural | ValidationEvidence.Conformance | ValidationEvidence.Performance),
            Ci("--phase164-58", "Validation registry descriptive names", ValidationRegistryDescriptiveNamesValidation.Validate, includeInDefault: false,
                evidence: ValidationEvidence.Structural),
            Ci("--phase164-59", "Validation naming guardrails", ValidationNamingGuardsValidation.Validate, includeInDefault: false,
                evidence: ValidationEvidence.Structural),
            Local("--phase138", "Phase 138: Virtual LiDAR Digital Twin validation", Phase138Validation.Validate),
            Local("--phase138b", "Phase 138B: multi-vendor LiDAR middleware validation", Phase138BValidation.Validate),
            Local("--phase138c2", "Phase 138C2: regression checks for shared-channel routing and subscription ids", Phase138C2Validation.Validate),
            Ci("--phase138d", "Phase 138D: virtual IMU schema-registration contract checks", Phase138DValidation.Validate),
            Ci("--phase138f", "Phase 138F: validation for IMU sub-step scheduling math and queue budget", Phase138FValidation.Validate),
            Ci("--phase138h", "Phase 138H: validation for shared timeline and streaming LiDAR scan state", Phase138HValidation.Validate),
            Ci("--phase138i", "Phase 138I: validation for full-fidelity OS-2-128 10Hz point-cloud throughput", Phase138IValidation.Validate, includeInDefault: false),
            Ci("--phase138j", "Phase 138J: validation for async JPEG camera budget and payload rules", Phase138JValidation.Validate, includeInDefault: false),
            // 138K: camera video dimension hardening + diagnostics.
            Ci("--phase138k", "Phase 138K: validation for video camera dimension and diagnostics rules", Phase138KValidation.Validate, includeInDefault: false),
            // 138L: standard sensor_msgs PointCloud2 SLAM pipeline.
            Ci("--phase138l", "Phase 138L: validation for SLAM PointCloud2 native pipeline boundaries", Phase138LValidation.Validate, includeInDefault: false),
            // 138M: cart-mounted camera time sync and standard ROS camera schemas.
            Ci("--phase138m", "Phase 138M: validation for cart-mounted camera time sync and ROS camera schemas", Phase138MValidation.Validate, includeInDefault: false),
            Ci("--phase138p", "Phase 138P: code-review remediation regression coverage", Phase138PValidation.Validate, includeInDefault: false),
            Ci("--phase138q", "Phase 138Q: architecture decomposition regression coverage", Phase138QValidation.Validate, includeInDefault: false,
                evidence: ValidationEvidence.Structural),
            Ci("--phase138t", "Phase 138T: validation for camera raw sensor_msgs/Image native DDS output", Phase138TValidation.Validate, includeInDefault: false),
            Ci("--phase138u", "Phase 138U: validation for LiDAR PointCloud2 visualization deskew contracts", Phase138UValidation.Validate, includeInDefault: false),
            Ci("--phase138s", "Phase 138S: IMU native DDS output contract checks", Phase138SValidation.Validate, includeInDefault: false),
            Ci("--phase139", "Phase 139: validation for end-to-end smoke harness contracts", Phase139Validation.Validate, includeInDefault: false),
            Ci("--phase139b", "Phase 139B: validation for the official Remote Data Loader HTTP backend contract", Phase139BValidation.Validate, includeInDefault: false),
            Ci("--phase139c", "Phase 139C: validation for Remote Data Loader workflow documentation and smoke tooling", Phase139CValidation.Validate, includeInDefault: false),
            Ci("--phase139d", "Phase 139D: validation for the Unity cursor bridge feasibility surface", Phase139DValidation.Validate, includeInDefault: false),
            Ci("--phase140-1", "Phase 140-1: validates Phase 140-1 runtime facade and lifecycle review fixes", Phase140_1Validation.Validate, includeInDefault: false),
            Ci("--phase140-2", "Phase 140-2: validates Phase 140-2 session protocol and registry review fixes", Phase140_2Validation.Validate, includeInDefault: false),
            Ci("--phase140-3", "Phase 140-3: validates Phase 140-3 recording/replay controller review fixes", Phase140_3Validation.Validate, includeInDefault: false),
            Ci("--phase140-4", "Phase 140-4: validates Phase 140-4 publisher base lifecycle review fixes", Phase140_4Validation.Validate, includeInDefault: false),
            Ci("--phase140-5", "Phase 140-5: validates Phase 140-5 replay object adapter review fixes", Phase140_5Validation.Validate, includeInDefault: false),
            Ci("--phase140-6", "Phase 140-6: validates Phase 140-6 transport, clock, and backpressure review fixes", Phase140_6Validation.Validate, includeInDefault: false),
            Ci("--phase140-7", "Phase 140-7: validates Phase 140-7 protocol frame and runtime utility review fixes", Phase140_7Validation.Validate, includeInDefault: false),
            Ci("--phase140-8", "Phase 140-8: validates Phase 140-8 MCAP writer and recording pipeline review fixes", Phase140_8Validation.Validate, includeInDefault: false),
            Ci("--phase140-9", "Phase 140-9: validates Phase 140-9 MCAP reader and indexing review fixes", Phase140_9Validation.Validate, includeInDefault: false),
            Ci("--phase140-10", "Phase 140-10: validates Phase 140-10 MCAP replay engine review fixes", Phase140_10Validation.Validate, includeInDefault: false),
            Ci("--phase140-11", "Phase 140-11: validates Phase 140-11 MCAP DataLoader and remote-file review fixes", Phase140_11Validation.Validate, includeInDefault: false),
            Ci("--phase140-12", "Phase 140-12: validates Phase 140-12 schema registry and message definition review fixes", Phase140_12Validation.Validate, includeInDefault: false),
            Ci("--phase140-13", "Phase 140-13: validates Phase 140-13 protobuf builder and typed publisher review fixes", Phase140_13Validation.Validate, includeInDefault: false),
            Ci("--phase140-14", "Phase 140-14: validates Phase 140-14 camera publisher and async pipeline review fixes", Phase140_14Validation.Validate, includeInDefault: false),
            Ci("--phase140-15", "Phase 140-15: regression coverage for video encoding sidecar review fixes", Phase140_15Validation.Validate, includeInDefault: false),
            Ci("--phase140-16", "Phase 140-16: regression coverage for point-cloud, LaserScan, and Draco review fixes", Phase140_16Validation.Validate, includeInDefault: false),
            Ci("--phase140-17", "Phase 140-17: regression coverage for virtual LiDAR and IMU sensor lifecycle fixes", Phase140_17Validation.Validate, includeInDefault: false),
            Ci("--phase140g", "Phase 140G: IMU covariance and serializer allocation validation", Phase140GValidation.Validate, includeInDefault: false),
            Ci("--phase140h", "Phase 140H: publish cadence diagnostics and fixed-time scheduler boundary validation", Phase140HValidation.Validate, includeInDefault: false),
            Ci("--phase140h2", "Phase 140H2: IMU WebSocket visualization burst boundary validation", Phase140H2Validation.Validate, includeInDefault: false),
            Ci("--phase140j", "Phase 140J: replay enable-failure diagnostics and cursor gate validation", Phase140JValidation.Validate, includeInDefault: false),
            Ci("--phase141a", "FoxRun conditional publish gates", FoxRunConditionalPublishGateValidation.Validate, includeInDefault: false),
            Ci("--phase141b", "FoxService declarative RPC", FoxServiceDeclarativeRpcValidation.Validate, includeInDefault: false),
            Ci("--phase141c", "FoxService DTO serialization analyzer", FoxServiceDtoSerializationAnalyzerValidation.Validate, includeInDefault: false),
            Ci("--phase141e", "FoxService editor schema polish", FoxServiceEditorSchemaPolishValidation.Validate, includeInDefault: false),
            Ci("--phase141f", "FoxService DTO graph walker convergence", FoxServiceDtoGraphWalkerConvergenceValidation.Validate, includeInDefault: false),
        };

        private static readonly IReadOnlyDictionary<string, PhaseValidationCase> FlagIndex;

        private static readonly Regex PhaseOnlyNamePattern = new Regex(
            @"^Phase \d+[A-Za-z]*(?:-\d+)?$",
            RegexOptions.Compiled);

        private static readonly HashSet<string> LegacyPhaseOnlyNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Phase 28",
            "Phase 31",
            "Phase 36",
            "Phase 137G",
            "Phase 164-4",
            "Phase 164-5",
            "Phase 164-6",
            "Phase 164-7",
            "Phase 164-8",
            "Phase 164-9",
            "Phase 164-10",
            "Phase 164-11",
            "Phase 164-12",
            "Phase 164-13",
            "Phase 164-14",
            "Phase 164-15",
            "Phase 164-16",
            "Phase 164-17",
            "Phase 164-18",
            "Phase 164-19",
            "Phase 164-20",
            "Phase 164-21",
            "Phase 164-22",
            "Phase 164-23",
            "Phase 164-24",
            "Phase 164-25",
            "Phase 164-26",
            "Phase 164-27",
            "Phase 164-28",
            "Phase 164-29",
            "Phase 164-30",
            "Phase 164-31",
            "Phase 164-32",
            "Phase 164-33",
            "Phase 164-34",
            "Phase 164-35",
            "Phase 164-36",
            "Phase 164-37",
            "Phase 164-38",
            "Phase 164-39",
            "Phase 164-40",
            "Phase 164-41",
            "Phase 164-42",
            "Phase 164-43",
            "Phase 164-44",
            "Phase 164-45",
            "Phase 164-46",
            "Phase 164-47",
            "Phase 164-48",
            "Phase 164-49",
            "Phase 164-50",
            "Phase 164-51",
            "Phase 164-52",
            "Phase 164-53",
            "Phase 164-54",
        };

        static PhaseValidationRegistry()
        {
            var flagIndex = new Dictionary<string, PhaseValidationCase>(StringComparer.Ordinal);
            foreach (var item in All)
            {
                foreach (var flag in item.AllFlags())
                {
                    if (!flagIndex.TryAdd(flag, item))
                        throw new InvalidOperationException("Duplicate validation flag registered: " + flag);
                }
            }

            FlagIndex = flagIndex;

            var duplicateName = All
                .GroupBy(item => item.Name, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicateName != null)
                throw new InvalidOperationException("Duplicate validation name registered: " + duplicateName.Key);

            var phaseOnlyName = All.FirstOrDefault(item =>
                PhaseOnlyNamePattern.IsMatch(item.Name)
                && !LegacyPhaseOnlyNames.Contains(item.Name));

            if (phaseOnlyName != null)
                throw new InvalidOperationException(
                    "Validation name must be descriptive, not just a phase number: " + phaseOnlyName.Name);

            var unclassified = All.FirstOrDefault(item => item.Evidence == ValidationEvidence.None);
            if (unclassified != null)
                throw new InvalidOperationException("Validation evidence classification is required: " + unclassified.Name);
        }

        /// <summary>
        /// Returns the default validation set for the current runtime.
        /// </summary>
        public static IEnumerable<PhaseValidationCase> DefaultValidations(bool includeLocalEvidence)
        {
            return All.Where(item =>
                item.IncludeInDefault
                && (item.Category == ValidationCategory.CiSafe
                    || (includeLocalEvidence && item.Category == ValidationCategory.LocalEvidence)));
        }

        /// <summary>
        /// Finds the first validation case matching CLI args.
        /// </summary>
        public static PhaseValidationCase Find(IReadOnlyCollection<string> args)
        {
            foreach (var arg in args)
            {
                if (FlagIndex.TryGetValue(arg, out var validation))
                    return validation;
            }

            return null;
        }

        /// <summary>
        /// Finds all validation cases matching CLI args.
        /// </summary>
        public static IEnumerable<PhaseValidationCase> FindAll(IReadOnlyCollection<string> args)
        {
            var emitted = new HashSet<PhaseValidationCase>();
            foreach (var arg in args)
            {
                if (FlagIndex.TryGetValue(arg, out var validation) && emitted.Add(validation))
                    yield return validation;
            }
        }

        /// <summary>
        /// Builds a validation entry with default execution behavior.
        /// </summary>
        private static PhaseValidationCase DefaultOnly(string name, System.Action run)
        {
            return new PhaseValidationCase(
                null,
                name,
                ValidationCategory.CiSafe,
                ValidationEvidence.Behavior,
                run,
                includeInDefault: true);
        }

        /// <summary>
        /// Builds a CI-safe validation entry with a command flag.
        /// </summary>
        private static PhaseValidationCase Ci(
            string flag,
            string name,
            System.Action run,
            bool includeInDefault = true,
            ValidationEvidence evidence = ValidationEvidence.Behavior)
        {
            return new PhaseValidationCase(flag, name, ValidationCategory.CiSafe, evidence, run, includeInDefault);
        }

        /// <summary>
        /// Builds a local-evidence validation entry with optional aliases.
        /// </summary>
        private static PhaseValidationCase Local(
            string flag,
            string name,
            System.Action run,
            ValidationEvidence evidence = ValidationEvidence.Behavior,
            params string[] aliases)
        {
            return new PhaseValidationCase(
                flag,
                name,
                ValidationCategory.LocalEvidence,
                evidence,
                run,
                includeInDefault: true,
                aliases);
        }

        /// <summary>
        /// Builds a manual smoke validation entry.
        /// </summary>
        private static PhaseValidationCase Manual(string flag, string name, System.Action run)
        {
            return new PhaseValidationCase(
                flag,
                name,
                ValidationCategory.ManualSmoke,
                ValidationEvidence.ManualEvidence,
                run,
                includeInDefault: false);
        }
    }
}
