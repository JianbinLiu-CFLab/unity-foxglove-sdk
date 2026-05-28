// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 137B recording/replay controller decoupling guard.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase137BValidation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 137B Tests ---");
            _passed = 0;

            VerifyIRangePlaybackClock();
            VerifyPlaybackClockImplementsIRangePlaybackClock();
            VerifyIRecordingStateReader();
            VerifyRecordingControllerImplementsIRecordingStateReader();
            VerifyRecordingControllerNewCtor();
            VerifyRecordingControllerAttachToSession();
            VerifyReplayControllerNewCtor();
            VerifyReplayControllerEnable();
            VerifyReplaySchemaGuard();
            VerifyReplayCoordinateModeGuard();
            VerifyFoxgloveRuntimeWiring();

            Console.WriteLine("Phase 137B: " + _passed + " checks passed.\n");
        }

        private static void VerifyIRangePlaybackClock()
        {
            var type = typeof(IRangePlaybackClock);
            Check(type != null && type.IsInterface, "137B-1: IRangePlaybackClock exists as interface");
            Check(typeof(IFoxgloveClock).IsAssignableFrom(type),
                "137B-2: IRangePlaybackClock extends IFoxgloveClock");
            Check(type.GetMethod("EnableRange") != null,
                "137B-3: IRangePlaybackClock declares EnableRange");
        }

        private static void VerifyPlaybackClockImplementsIRangePlaybackClock()
        {
            Check(typeof(IRangePlaybackClock).IsAssignableFrom(typeof(PlaybackClock)),
                "137B-4: PlaybackClock implements IRangePlaybackClock");
        }

        private static void VerifyIRecordingStateReader()
        {
            var type = typeof(IRecordingStateReader);
            Check(type != null && type.IsInterface, "137B-5: IRecordingStateReader exists as interface");
            Check(type.GetProperty("IsEnabled") != null,
                "137B-6: IRecordingStateReader declares IsEnabled");
            Check(type.GetProperty("CoordinateMode") != null,
                "137B-7: IRecordingStateReader declares CoordinateMode");
        }

        private static void VerifyRecordingControllerImplementsIRecordingStateReader()
        {
            Check(typeof(IRecordingStateReader).IsAssignableFrom(typeof(RecordingController)),
                "137B-8: RecordingController implements IRecordingStateReader");
        }

        private static void VerifyRecordingControllerNewCtor()
        {
            var ctor = typeof(RecordingController).GetConstructor(new[] { typeof(IFoxgloveLogger), typeof(IFoxgloveClock) });
            Check(ctor != null, "137B-9: RecordingController has (IFoxgloveLogger, IFoxgloveClock) ctor");
        }

        private static void VerifyRecordingControllerAttachToSession()
        {
            var newMethod = typeof(RecordingController).GetMethod("AttachToSession",
                new[] { typeof(FoxgloveParameterStore), typeof(FoxgloveSession) });
            Check(newMethod != null, "137B-10: AttachToSession(FoxgloveParameterStore, FoxgloveSession) exists");

            var oldMethod = typeof(RecordingController).GetMethod("AttachToSession",
                new[] { typeof(PlaybackClock), typeof(FoxgloveParameterStore), typeof(FoxgloveSession) });
            Check(oldMethod != null, "137B-11: old 3-arg AttachToSession still exists");
            Check(oldMethod.GetCustomAttributes(typeof(ObsoleteAttribute), false).Any(),
                "137B-12: old 3-arg AttachToSession is [Obsolete]");
        }

        private static void VerifyReplayControllerNewCtor()
        {
            var ctor = typeof(ReplayController).GetConstructor(new[]
                { typeof(IFoxgloveLogger), typeof(IRecordingStateReader), typeof(IRangePlaybackClock) });
            Check(ctor != null, "137B-13: ReplayController has (IFoxgloveLogger, IRecordingStateReader, IRangePlaybackClock) ctor");

            var oldCtor = typeof(ReplayController).GetConstructor(new[] { typeof(IFoxgloveLogger) });
            Check(oldCtor != null, "137B-14: old single-arg ReplayController ctor still exists");
            Check(oldCtor.GetCustomAttributes(typeof(ObsoleteAttribute), false).Any(),
                "137B-15: old single-arg ReplayController ctor is [Obsolete]");
        }

        private static void VerifyReplayControllerEnable()
        {
            var newMethod = typeof(ReplayController).GetMethod("Enable",
                new[] { typeof(string), typeof(SchemaIdentityMode) });
            Check(newMethod != null, "137B-16: Enable(string, SchemaIdentityMode) exists");

            var oldMethod = typeof(ReplayController).GetMethod("Enable",
                new[] { typeof(string), typeof(PlaybackClock), typeof(bool), typeof(string), typeof(SchemaIdentityMode) });
            Check(oldMethod != null, "137B-17: old 5-arg Enable still exists");
            Check(oldMethod.GetCustomAttributes(typeof(ObsoleteAttribute), false).Any(),
                "137B-18: old 5-arg Enable is [Obsolete]");
        }

        private static void VerifyReplaySchemaGuard()
        {
            var type = Type.GetType("Unity.FoxgloveSDK.Core.ReplaySchemaGuard");
            Check(type != null, "137B-19: ReplaySchemaGuard class exists");
            Check(type.GetMethod("Evaluate",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) != null,
                "137B-20: ReplaySchemaGuard.Evaluate exists");
        }

        private static void VerifyReplayCoordinateModeGuard()
        {
            var type = Type.GetType("Unity.FoxgloveSDK.Core.ReplayCoordinateModeGuard");
            Check(type != null, "137B-21: ReplayCoordinateModeGuard class exists");
            Check(type.GetMethod("FindMismatch",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public) != null,
                "137B-22: ReplayCoordinateModeGuard.FindMismatch exists");
        }

        private static void VerifyFoxgloveRuntimeWiring()
        {
            var ctor = typeof(FoxgloveRuntime).GetConstructors().FirstOrDefault();
            Check(ctor != null, "137B-23: FoxgloveRuntime has a constructor");

            // Verify new EnableReplay signature: no longer passes PlaybackClock/IsEnabled/CoordinateMode
            var enableReplay = typeof(FoxgloveRuntime).GetMethod("EnableReplay", new[] { typeof(string) });
            Check(enableReplay != null, "137B-24: EnableReplay(string) exists");

            var enableReplayWithMode = typeof(FoxgloveRuntime).GetMethod("EnableReplay",
                new[] { typeof(string), typeof(SchemaIdentityMode) });
            Check(enableReplayWithMode != null, "137B-25: EnableReplay(string, SchemaIdentityMode) exists");
        }

        private static void Check(bool condition, string label)
        {
            if (condition)
            {
                Console.WriteLine("[PASS] " + label);
                _passed++;
            }
            else
            {
                Console.WriteLine("[FAIL] " + label);
            }
        }
    }
}
