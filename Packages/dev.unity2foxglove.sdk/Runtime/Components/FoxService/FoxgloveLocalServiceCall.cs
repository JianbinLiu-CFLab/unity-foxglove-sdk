// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxService
// Purpose: Deterministic local invocation result for existing generated services.

using System;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace Unity.FoxgloveSDK.Components
{
    public enum FoxgloveLocalServiceCallStatus
    {
        Succeeded,
        MissingService,
        HandlerFailed,
        TimedOut
    }

    public readonly struct FoxgloveLocalServiceCallResult
    {
        public FoxgloveLocalServiceCallResult(
            FoxgloveLocalServiceCallStatus status,
            JToken response,
            string error,
            TimeSpan elapsed)
        {
            Status = status;
            Response = response;
            Error = error ?? string.Empty;
            Elapsed = elapsed;
        }

        public FoxgloveLocalServiceCallStatus Status { get; }
        public JToken Response { get; }
        public string Error { get; }
        public TimeSpan Elapsed { get; }
    }

    public static class FoxgloveLocalServiceCall
    {
        public static FoxgloveLocalServiceCallResult Invoke(
            FoxgloveGeneratedServiceDescriptor descriptor,
            JToken request,
            TimeSpan timeout)
        {
            if (descriptor == null)
            {
                return new FoxgloveLocalServiceCallResult(
                    FoxgloveLocalServiceCallStatus.MissingService,
                    null,
                    "Service is not registered.",
                    TimeSpan.Zero);
            }

            var stopwatch = Stopwatch.StartNew();
            JToken response;
            try
            {
                response = descriptor.Handler(request ?? JValue.CreateNull());
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException)
                                       && !(ex is StackOverflowException)
                                       && !(ex is AccessViolationException))
            {
                stopwatch.Stop();
                return new FoxgloveLocalServiceCallResult(
                    FoxgloveLocalServiceCallStatus.HandlerFailed,
                    null,
                    ex.Message,
                    stopwatch.Elapsed);
            }

            stopwatch.Stop();
            if (timeout > TimeSpan.Zero && stopwatch.Elapsed > timeout)
            {
                return new FoxgloveLocalServiceCallResult(
                    FoxgloveLocalServiceCallStatus.TimedOut,
                    null,
                    "Service handler exceeded the local call timeout.",
                    stopwatch.Elapsed);
            }

            return new FoxgloveLocalServiceCallResult(
                FoxgloveLocalServiceCallStatus.Succeeded,
                response ?? JValue.CreateNull(),
                string.Empty,
                stopwatch.Elapsed);
        }
    }
}
