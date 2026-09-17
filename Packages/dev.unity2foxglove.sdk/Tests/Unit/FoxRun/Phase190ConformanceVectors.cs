// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    /// <summary>
    /// Stable claim-to-test bindings for the Phase190 managed conformance gate.
    /// Transport, Editor and Player evidence is recorded by their own entrypoints.
    /// </summary>
    public static class Phase190ConformanceVectors
    {
        /// <summary>Returns the canonical managed test method for each claim.</summary>
        public static IReadOnlyDictionary<string, string> ByClaim { get; } =
            new Dictionary<string, string>
            {
                ["FR-DECL-001"] = nameof(Phase190ConformanceVectorTests.DeclarationDefaultsVector),
                ["FR-HOST-002"] = nameof(Phase190ConformanceVectorTests.DualHostVector),
                ["FR-EMIT-003"] = nameof(Phase190ConformanceVectorTests.SharedEmitterVector),
                ["FR-OUT-004"] = nameof(Phase190ConformanceVectorTests.OutputFanoutVector),
                ["FR-IN-005"] = nameof(Phase190ConformanceVectorTests.InputAdmissionVector),
                ["FR-OWN-006"] = nameof(Phase190ConformanceVectorTests.OwnershipVector),
                ["FR-LIFE-007"] = nameof(Phase190ConformanceVectorTests.LifecycleVector),
                ["FR-SCHEMA-008"] = nameof(Phase190ConformanceVectorTests.SchemaReplayVector),
                ["FR-ROS-009"] = nameof(Phase190ConformanceVectorTests.Ros2MappingSourceVector),
                ["FR-AOT-010"] = nameof(Phase190ConformanceVectorTests.AotGeneratedSourceVector),
                ["FR-EVID-011"] = nameof(Phase190ConformanceVectorTests.EvidenceToolingVector),
            };
    }
}
