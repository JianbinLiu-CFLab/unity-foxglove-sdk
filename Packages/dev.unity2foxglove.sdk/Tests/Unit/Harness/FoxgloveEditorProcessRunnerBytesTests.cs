// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Harness
// Purpose: Behavior tests for bounded binary redirected process transfer.

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.FoxgloveSDK.Editor;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Harness
{
    public sealed class FoxgloveEditorProcessRunnerBytesTests
    {
        private const int SlowTimeoutMs = 30000;

        /// <summary>Binary stdin bytes reach the child and come back through bounded stdout unchanged.</summary>
        [Fact]
        public void RunBytesRoundTripsBinaryStdinThroughStdout()
        {
            var input = Enumerable.Range(0, 16 * 1024).Select(index => (byte)index).ToArray();

            var result = FoxgloveEditorProcessRunner.RunBytes(CopyStdin("Output"), input, input.Length, SlowTimeoutMs);

            Assert.False(result.TimedOut);
            Assert.False(result.OutputReadFailed);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(input, result.Stdout);
        }

        /// <summary>Stdout beyond the bound fails the run and stops the child before the timeout.</summary>
        [Fact]
        public void RunBytesStopsChildWhenStdoutExceedsBound()
        {
            var watch = Stopwatch.StartNew();

            var result = FoxgloveEditorProcessRunner.RunBytes(CopyStdin("Output"), new byte[1024 * 1024], 4096, SlowTimeoutMs);

            Assert.True(result.OutputReadFailed);
            Assert.False(result.TimedOut);
            Assert.Empty(result.Stdout);
            Assert.True(watch.ElapsedMilliseconds < SlowTimeoutMs / 3, "elapsed " + watch.ElapsedMilliseconds + " ms");
        }

        /// <summary>Stderr uses the same bound and also stops the child before the timeout.</summary>
        [Fact]
        public void RunBytesStopsChildWhenStderrExceedsBound()
        {
            var watch = Stopwatch.StartNew();

            var result = FoxgloveEditorProcessRunner.RunBytes(CopyStdin("Error"), new byte[1024 * 1024], 4096, SlowTimeoutMs);

            Assert.True(result.OutputReadFailed);
            Assert.False(result.TimedOut);
            Assert.True(watch.ElapsedMilliseconds < SlowTimeoutMs / 3, "elapsed " + watch.ElapsedMilliseconds + " ms");
        }

        /// <summary>A child that outlives the timeout is killed and reported as timed out.</summary>
        [Fact]
        public void RunBytesKillsChildAfterTimeout()
        {
            var watch = Stopwatch.StartNew();

            var result = FoxgloveEditorProcessRunner.RunBytes(Sleep(), Array.Empty<byte>(), 4096, 500);

            Assert.True(result.TimedOut);
            Assert.Equal(-1, result.ExitCode);
            Assert.True(watch.ElapsedMilliseconds < SlowTimeoutMs / 3, "elapsed " + watch.ElapsedMilliseconds + " ms");
        }

        /// <summary>Returns a child that copies stdin to the named standard stream.</summary>
        private static ProcessStartInfo CopyStdin(string stream)
            => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? PowerShell("[Console]::OpenStandardInput().CopyTo([Console]::OpenStandard" + stream + "())")
                : new ProcessStartInfo("/bin/sh", stream == "Output" ? "-c \"cat\"" : "-c \"cat >&2\"");

        /// <summary>Returns a child that sleeps far beyond the test timeout.</summary>
        private static ProcessStartInfo Sleep()
            => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? PowerShell("Start-Sleep -Seconds 60")
                : new ProcessStartInfo("/bin/sh", "-c \"sleep 60\"");

        /// <summary>Returns a non-interactive Windows PowerShell child running one command.</summary>
        private static ProcessStartInfo PowerShell(string command)
            => new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"" + command + "\"");
    }
}
