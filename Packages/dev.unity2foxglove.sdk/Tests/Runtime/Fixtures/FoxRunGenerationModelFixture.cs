// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime/Fixtures
// Purpose: Source text fixture for FoxRun generation-model equivalence tests.

using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace UnityEngine
{
    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;
    }

    public struct Vector2
    {
        public float x;
        public float y;
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
    }

    public struct Quaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;
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

        [FoxRun("/debug/value", Hz = 5f)]
        public float _value;

        [FoxRun("/debug/value", Policy = FoxRunPolicy.Change, Tolerance = 0.01f)]
        public float _valueMirror { get; set; }

#if FOXRUN_FIXTURE_EXTRA
        [FoxRun("/debug/extra", Hz = 0f)]
        public string _extra;

        [FoxRun("/debug/trigger", Policy = FoxRunPolicy.Trigger)]
        public int _trigger;

        [FoxRun("/debug/array", Policy = FoxRunPolicy.Change)]
        public float[] _samples;

        [FoxRun("/debug/list", Policy = FoxRunPolicy.Change)]
        public List<float> _sampleList;

        [FoxRun("/debug/nullable", Policy = FoxRunPolicy.Change)]
        public int? _optionalCount;

        // Nested custom payloads are intentionally not a valid FoxRun contract field after the
        // fail-fast diagnostics added for IL2CPP-safe generation.
        public Nested _nested;

        [FoxRun("/debug/vector", Policy = FoxRunPolicy.Change, Tolerance = 0.001f)]
        public UnityEngine.Vector3 _position;
#endif
    }
}
