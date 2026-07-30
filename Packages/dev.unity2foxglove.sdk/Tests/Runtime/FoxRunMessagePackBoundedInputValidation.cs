// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Structural release gate for generated bounded transactional MessagePack input.

using System;

namespace Unity.FoxgloveSDK.Tests
{
    public static class FoxRunMessagePackBoundedInputValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- FoxRun MessagePack bounded-input validation ---");
            _passed = 0;

            var input = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/FoxgloveSourceEmitter/MessagePackInputDispatchEmitter.cs");
            Check(
                ContainsAll(
                    input,
                    "FoxgloveMsgPackReader",
                    "FoxgloveMsgPackReadLimits",
                    "IFoxgloveTransactionalInputSource",
                    "FoxgloveInput_TryStageTransaction",
                    "TryReserveInput",
                    "CommitOwnedInput"),
                "185C-1: generated input uses the one bounded reader and transactional reservation seam");

            var router = Read("Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxRun/FoxRunInputRouter.cs");
            Check(
                ContainsAll(
                    router,
                    "IFoxgloveTransactionalInputSource",
                    "FoxRunEncoding.MessagePack",
                    "FoxgloveInput_TryStageTransaction"),
                "185C-2: input routing dispatches MessagePack only through generated transactions");

            var generated = Read("Unity2Foxglove/Assets/Scripts/Generated/TestLog_FoxRun.g.cs");
            Check(
                generated.Contains("/phase185/messagepack/full-duplex", StringComparison.Ordinal)
                && generated.Contains("FoxgloveInput_TryStageTransaction", StringComparison.Ordinal)
                && generated.Contains("__FoxRunFlushMessagePackTransactions", StringComparison.Ordinal)
                && generated.Contains("FoxgloveMsgPackReader", StringComparison.Ordinal),
                "185C-3: controlled TestLog generated input contains bounded MessagePack apply wiring");

            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(
                ContainsAll(
                    project,
                    "../../Runtime/Schemas/MsgPack/FoxgloveMsgPackReader.cs",
                    "../../Runtime/Schemas/MsgPack/FoxgloveMsgPackReadLimits.cs",
                    "../../Runtime/Components/FoxRun/FoxRun*.cs"),
                "185C-4: runtime validation explicitly compiles reader, limits, and transaction contracts");

            var ros2Input = Read("Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/Ros2InputDispatchEmitter.cs");
            Check(
                !ros2Input.Contains("msgpack", StringComparison.OrdinalIgnoreCase),
                "185C-5: ROS2 native input generation never inspects MessagePack bytes");

            Console.WriteLine("FoxRun MessagePack bounded input: " + _passed + " checks passed.\n");
        }

        private static string Read(string path) => FoxRunMessagePackPublicContractValidation.Read(path);
        private static bool ContainsAll(string source, params string[] values)
            => FoxRunMessagePackPublicContractValidation.ContainsAll(source, values);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);
            Console.WriteLine("[PASS] " + label);
            _passed++;
        }
    }
}
