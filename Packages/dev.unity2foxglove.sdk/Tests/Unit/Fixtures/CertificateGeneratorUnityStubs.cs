// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Fixtures
// Purpose: Minimal Unity surface needed to compile the production certificate
// generator in the .NET unit-test assembly.

using System;

namespace UnityEngine
{
    public static class Application
    {
        public static string dataPath => "Project/Assets";
        public static RuntimePlatform platform => RuntimePlatform.WindowsEditor;
    }

    public enum RuntimePlatform
    {
        WindowsEditor
    }

    public static class Debug
    {
        public static void Log(string message) { }
        public static void LogWarning(string message) { }
        public static void LogError(string message) { }
    }

    // The focused unit lane compiles the Editor FoxRun reflection boundary.
    // These minimal inheritance stubs let dynamic probe assemblies exercise
    // the same MonoBehaviour assignability filter without a Unity runtime.
    public class Object { }
    public class Component : Object { }
    public class MonoBehaviour : Component { }
}
