// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime/Fixtures
// Purpose: Source text fixture for FoxRun generation-model equivalence tests.

using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
    }
}

namespace Unity.FoxgloveSDK.Tests.Fixtures
{
    public partial class FoxRunGenerationModelFixture
    {
        public sealed class Nested
        {
            public int Value;
        }

        [FoxRun("/debug/value", RateHz = 5f)]
        public float _value;

        [FoxRun("/debug/value", PublishMode = FoxRunPublishMode.OnChange, ChangeEpsilon = 0.01f)]
        public float _valueMirror { get; set; }

#if FOXRUN_FIXTURE_EXTRA
        [FoxRun("/debug/extra", RateHz = 0f)]
        public string _extra;

        [FoxRun("/debug/trigger", PublishMode = FoxRunPublishMode.OnTrigger)]
        public int _trigger;

        [FoxRun("/debug/array", PublishMode = FoxRunPublishMode.OnChange)]
        public float[] _samples;

        [FoxRun("/debug/list", PublishMode = FoxRunPublishMode.OnChange)]
        public List<float> _sampleList;

        [FoxRun("/debug/nullable", PublishMode = FoxRunPublishMode.OnChange)]
        public int? _optionalCount;

        [FoxRun("/debug/nested", PublishMode = FoxRunPublishMode.OnChange)]
        public Nested _nested;

        [FoxRun("/debug/vector", PublishMode = FoxRunPublishMode.OnChange, ChangeEpsilon = 0.001f)]
        public UnityEngine.Vector3 _position;
#endif
    }
}
