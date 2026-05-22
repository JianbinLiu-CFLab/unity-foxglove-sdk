// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime/Fixtures
// Purpose: Source text fixture for FoxRun generation-model equivalence tests.

using Unity.FoxgloveSDK.Components;

namespace Unity.FoxgloveSDK.Tests.Fixtures
{
    public partial class FoxRunGenerationModelFixture
    {
        [FoxRun("/debug/value", RateHz = 5f)]
        public float _value;

        [FoxRun("/debug/value", PublishMode = FoxRunPublishMode.OnChange, ChangeEpsilon = 0.01f)]
        public float _valueMirror { get; set; }

#if FOXRUN_FIXTURE_EXTRA
        [FoxRun("/debug/extra", RateHz = 0f)]
        public string _extra;

        [FoxRun("/debug/trigger", PublishMode = FoxRunPublishMode.OnTrigger)]
        public int _trigger;
#endif
    }
}
