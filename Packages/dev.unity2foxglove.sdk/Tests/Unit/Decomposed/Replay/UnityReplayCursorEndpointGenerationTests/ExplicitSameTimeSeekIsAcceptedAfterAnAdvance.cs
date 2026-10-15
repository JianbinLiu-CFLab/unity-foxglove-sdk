// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Replay
// Purpose: Locks replay cursor endpoint worker generations across restart.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.UnitTests.Harness;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Replay
{


public sealed partial class UnityReplayCursorEndpointGenerationTests
    {
        [Fact]
        public void ExplicitSameTimeSeekIsAcceptedAfterAnAdvance()
        {
            var controller = new ExternalReplayCursorController { Enabled = true };
            var advance = ReplayCursorRequest.CreateForTests(
                7_000_000_009UL,
                "phase187",
                sequence: 1,
                didSeek: false);
            var seek = ReplayCursorRequest.CreateForTests(
                7_000_000_009UL,
                "phase187",
                sequence: 2,
                didSeek: true);

            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(advance, replayEnabled: true, startNs: 0, endNs: 10_000_000_000UL, out _));
            Assert.True(controller.TryDrainLatest(out var drainedAdvance));
            Assert.False(drainedAdvance.DidSeek);

            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(seek, replayEnabled: true, startNs: 0, endNs: 10_000_000_000UL, out _));
            Assert.True(controller.TryDrainLatest(out var drainedSeek));
            Assert.True(drainedSeek.DidSeek);
            Assert.Equal(2, drainedSeek.Sequence);
        }

        [Fact]
        public void NewGenerationAcceptsSameTimeAdvanceAfterOldGenerationRevoke()
        {
            var controller = new ExternalReplayCursorController { Enabled = true };
            var oldLease = new ReplayCursorGenerationLease();
            var newLease = new ReplayCursorGenerationLease();
            var oldRequest = ReplayCursorRequest.CreateForTests(
                    7_000_000_009UL,
                    "phase187",
                    sequence: 1,
                    didSeek: false)
                .WithGenerationLease(oldLease);
            var newRequest = ReplayCursorRequest.CreateForTests(
                    7_000_000_009UL,
                    "phase187",
                    sequence: 2,
                    didSeek: false)
                .WithGenerationLease(newLease);

            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(oldRequest, replayEnabled: true, startNs: 0, endNs: 10_000_000_000UL, out _));
            oldLease.Revoke();

            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(newRequest, replayEnabled: true, startNs: 0, endNs: 10_000_000_000UL, out _));
            Assert.True(controller.TryDrainLatest(out var drained));
            Assert.Equal(2, drained.Sequence);
        }

        [Fact]
        public void DisablingControllerClearsAlreadyQueuedRequest()
        {
            var controller = new ExternalReplayCursorController { Enabled = true };
            var request = ReplayCursorRequest.CreateForTests(
                7_000_000_009UL,
                "phase187",
                sequence: 1,
                didSeek: false);

            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(
                    request,
                    replayEnabled: true,
                    startNs: 0,
                    endNs: 10_000_000_000UL,
                    out _));

            controller.Enabled = false;

            Assert.False(controller.TryDrainLatest(out _));
        }

        [Fact]
        public async Task AtomicDrainAppliesBeforeDisableCanClearAuthority()
        {
            var controller = new ExternalReplayCursorController { Enabled = true };
            var request = ReplayCursorRequest.CreateForTests(
                7_000_000_009UL,
                "phase187",
                sequence: 1,
                didSeek: true);
            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(
                    request,
                    replayEnabled: true,
                    startNs: 0,
                    endNs: 10_000_000_000UL,
                    out _));

            using var callbackEntered = new ManualResetEventSlim();
            using var releaseCallback = new ManualResetEventSlim();
            var appliedWhileEnabled = false;
            var drain = Task.Run(() => controller.TryDrainLatest(drained =>
            {
                appliedWhileEnabled = controller.Enabled;
                callbackEntered.Set();
                releaseCallback.Wait(TimeSpan.FromSeconds(5));
                Assert.Equal(1, drained.Sequence);
            }));

            Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));
            var disableStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var disable = Task.Run(() =>
            {
                disableStarted.SetResult(true);
                controller.Enabled = false;
            });
            await disableStarted.Task;
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                Assert.False(disable.IsCompleted);
            }
            finally
            {
                releaseCallback.Set();
            }
            Assert.True(await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(5))) == drain);
            await drain;
            await disable;
            Assert.True(appliedWhileEnabled);
            Assert.False(controller.TryDrainLatest(out _));
        }

        [Fact]
        public async Task AtomicLeasedDrainAppliesBeforeDisableCanRevokeGeneration()
        {
            var controller = new ExternalReplayCursorController { Enabled = true };
            var lease = new ReplayCursorGenerationLease();
            var request = ReplayCursorRequest.CreateForTests(
                    7_000_000_009UL,
                    "phase187",
                    sequence: 1,
                    didSeek: true)
                .WithGenerationLease(lease);
            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(
                    request,
                    replayEnabled: true,
                    startNs: 0,
                    endNs: 10_000_000_000UL,
                    out _));

            using var callbackEntered = new ManualResetEventSlim();
            using var releaseCallback = new ManualResetEventSlim();
            var appliedWhileEnabled = false;
            var drain = Task.Run(() => controller.TryDrainLatest(drained =>
            {
                appliedWhileEnabled = controller.Enabled;
                callbackEntered.Set();
                releaseCallback.Wait(TimeSpan.FromSeconds(5));
                Assert.Equal(1, drained.Sequence);
            }));

            Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));
            var disableStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var disable = Task.Run(() =>
            {
                disableStarted.SetResult(true);
                controller.Enabled = false;
            });
            await disableStarted.Task;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            Assert.False(disable.IsCompleted);
            releaseCallback.Set();
            Assert.True(await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(5))) == drain);
            await drain;
            await disable;
            Assert.True(appliedWhileEnabled);
            Assert.False(controller.TryDrainLatest(out _));
        }

        [Fact]
        public async Task AtomicLeasedDrainHoldsGenerationLeaseUntilApplyCompletes()
        {
            var controller = new ExternalReplayCursorController { Enabled = true };
            var lease = new ReplayCursorGenerationLease();
            var request = ReplayCursorRequest.CreateForTests(
                    7_000_000_009UL,
                    "phase187",
                    sequence: 1,
                    didSeek: true)
                .WithGenerationLease(lease);
            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(
                    request,
                    replayEnabled: true,
                    startNs: 0,
                    endNs: 10_000_000_000UL,
                    out _));

            using var callbackEntered = new ManualResetEventSlim();
            using var releaseCallback = new ManualResetEventSlim();
            var appliedWhileActive = false;
            var drain = Task.Run(() => controller.TryDrainLatest(drained =>
            {
                appliedWhileActive = lease.IsActive;
                callbackEntered.Set();
                releaseCallback.Wait(TimeSpan.FromSeconds(5));
                Assert.Equal(1, drained.Sequence);
            }));

            Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));
            var revokeStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var revoke = Task.Run(() =>
            {
                revokeStarted.SetResult(true);
                lease.Revoke();
            });
            await revokeStarted.Task;
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                Assert.False(revoke.IsCompleted);
            }
            finally
            {
                releaseCallback.Set();
            }
            Assert.True(await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(5))) == drain);
            await drain;
            await revoke;
            Assert.True(appliedWhileActive);
            Assert.False(lease.IsActive);
        }

        [Fact]
        public void LeasedDrainRejectsARevokedGenerationBeforeApplying()
        {
            var controller = new ExternalReplayCursorController { Enabled = true };
            var lease = new ReplayCursorGenerationLease();
            var request = ReplayCursorRequest.CreateForTests(
                    7_000_000_009UL,
                    "phase187",
                    sequence: 1,
                    didSeek: true)
                .WithGenerationLease(lease);
            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(
                    request,
                    replayEnabled: true,
                    startNs: 0,
                    endNs: 10_000_000_000UL,
                    out _));

            lease.Revoke();
            var applied = false;
            Assert.False(controller.TryDrainLatest(_ => applied = true));
            Assert.False(applied);
        }

        [Fact]
        public void FailedCursorApplyIsNotRetainedAfterPartialMutation()
        {
            var controller = new ExternalReplayCursorController { Enabled = true };
            var request = ReplayCursorRequest.CreateForTests(
                7_000_000_009UL,
                "phase187",
                sequence: 1,
                didSeek: true);

            Assert.Equal(
                ExternalReplayCursorEnqueueResult.Accepted,
                controller.TryEnqueue(
                    request,
                    replayEnabled: true,
                    startNs: 0,
                    endNs: 10_000_000_000UL,
                    out _));

            Assert.Throws<InvalidOperationException>(() =>
                controller.TryDrainLatest(_ => throw new InvalidOperationException("simulated apply failure")));

            Assert.False(controller.TryDrainLatest(out _));
        }

        [Fact]
        public void CursorParserRejectsMalformedPayloadsAndLetsRuntimeFailuresEscape()
        {
            const string body = "{\"time\":{\"sec\":1,\"nsec\":2}}";

            Assert.False(ReplayCursorRequest.TryParseJson("{ not json", out _, out var malformedError));
            Assert.Contains("Cursor request JSON is invalid", malformedError, StringComparison.Ordinal);

            Assert.False(ReplayCursorRequest.TryParseJson(
                body,
                _ => throw new Newtonsoft.Json.JsonReaderException("bad token"),
                out _,
                out var readerError));
            Assert.Contains("Cursor request JSON is invalid", readerError, StringComparison.Ordinal);

            // A runtime failure is not a rejected request: it must not be reported to the client as
            // invalid JSON, and it must not be swallowed.
            Assert.Throws<OutOfMemoryException>(() => ReplayCursorRequest.TryParseJson(
                body,
                _ => throw new OutOfMemoryException("parser exhausted the process"),
                out _,
                out _));

            Assert.True(ReplayCursorRequest.TryParseJson(body, out var parsed, out var noError));
            Assert.Equal(string.Empty, noError);
            Assert.Equal(1_000_000_002UL, parsed.TimeNs);
        }

        [Fact]
        public void CursorParserRejectsOversizedNumbersAndKeepsOptionalTokenFailuresLocal()
        {
            // time.sec does not fit in Int64. The token reader raises OverflowException, which is a
            // malformed payload and not a runtime fault, so the client must get a rejected request
            // rather than a server error escaping out of the parser.
            Assert.False(ReplayCursorRequest.TryParseJson(
                "{\"source\":\"phase187\",\"sequence\":1,\"mode\":\"seek\","
                + "\"time\":{\"sec\":99999999999999999999,\"nsec\":3}}",
                out _,
                out var overflowError));
            Assert.Contains("time.sec", overflowError, StringComparison.Ordinal);

            // A float literal far outside Int64 range parses fine as JSON and only fails when the
            // token reader converts it, which is where OverflowException actually comes from.
            Assert.False(ReplayCursorRequest.TryParseJson(
                "{\"source\":\"phase187\",\"mode\":\"seek\",\"time\":{\"sec\":1e300,\"nsec\":3}}",
                out _,
                out var convertError));
            Assert.Contains("time.sec", convertError, StringComparison.Ordinal);

            // sequence and didSeek fail inside their own readers. Those readers own the failure and
            // fall back - sequence to 0, didSeek to the mode default - instead of rejecting a payload
            // whose required fields are all well formed.
            Assert.True(
                ReplayCursorRequest.TryParseJson(
                    "{\"source\":\"phase187\",\"sequence\":\"seven\",\"mode\":\"seek\","
                    + "\"didSeek\":\"maybe\",\"time\":{\"sec\":1,\"nsec\":2}}",
                    out var lenient,
                    out var lenientError),
                lenientError);
            Assert.Equal(string.Empty, lenientError);
            Assert.Equal(0, lenient.Sequence);
            Assert.True(lenient.DidSeek);
            Assert.Equal(1_000_000_002UL, lenient.TimeNs);
        }

    }
}
