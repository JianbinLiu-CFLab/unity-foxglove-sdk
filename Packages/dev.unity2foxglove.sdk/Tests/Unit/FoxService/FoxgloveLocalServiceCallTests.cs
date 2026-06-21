// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxService
{
    public sealed class FoxgloveLocalServiceCallTests
    {
        [Fact]
        public void InvokeReturnsHandlerResponse()
        {
            var descriptor = Descriptor(request => new JObject { ["ok"] = request["value"] });

            var result = FoxgloveLocalServiceCall.Invoke(descriptor, new JObject { ["value"] = 7 }, TimeSpan.FromSeconds(1));

            Assert.Equal(FoxgloveLocalServiceCallStatus.Succeeded, result.Status);
            Assert.Equal(7, result.Response["ok"].Value<int>());
        }

        [Fact]
        public void InvokeReportsHandlerException()
        {
            var descriptor = Descriptor(_ => throw new InvalidOperationException("boom"));

            var result = FoxgloveLocalServiceCall.Invoke(descriptor, new JObject(), TimeSpan.FromSeconds(1));

            Assert.Equal(FoxgloveLocalServiceCallStatus.HandlerFailed, result.Status);
            Assert.Contains("boom", result.Error, StringComparison.Ordinal);
        }

        [Fact]
        public void InvokeReportsMissingService()
        {
            var result = FoxgloveLocalServiceCall.Invoke(null, new JObject(), TimeSpan.FromSeconds(1));

            Assert.Equal(FoxgloveLocalServiceCallStatus.MissingService, result.Status);
        }

        [Fact]
        public void InvokeReportsElapsedTimeoutWithoutMovingUnityWorkToWorkerThread()
        {
            var descriptor = Descriptor(_ =>
            {
                Thread.Sleep(20);
                return new JObject();
            });

            var result = FoxgloveLocalServiceCall.Invoke(descriptor, new JObject(), TimeSpan.FromMilliseconds(1));

            Assert.Equal(FoxgloveLocalServiceCallStatus.TimedOut, result.Status);
        }

        private static FoxgloveGeneratedServiceDescriptor Descriptor(Func<JToken, JToken> handler) =>
            new("/phase157/reset", "demo.Reset", "", "demo.Request", "demo.Response", handler);
    }
}
