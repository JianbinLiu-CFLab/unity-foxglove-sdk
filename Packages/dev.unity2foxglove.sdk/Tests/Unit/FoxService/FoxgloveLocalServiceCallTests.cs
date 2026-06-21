// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
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

        [Fact]
        public void ServiceEmitterPreservesWrappersWithoutDuplicatingPartialClassAttribute()
        {
            var generated = FoxServiceSourceEmitter.EmitClass(
                "ManualAcceptance",
                "CombinedFoxRunAndService",
                new[]
                {
                    new FoxServiceSourceEmitter.ServiceMethod(
                        "Apply",
                        "/phase157/apply",
                        "Phase157.Apply",
                        "",
                        "Phase157.ApplyRequest",
                        "Phase157.ApplyResponse",
                        "{}",
                        "{}",
                        "Phase157.ApplyRequest",
                        "Phase157.ApplyResponse",
                        hasRequest: true,
                        hasResponse: true)
                });
            generated = generated.Replace("\r\n", "\n", StringComparison.Ordinal);

            Assert.Equal(
                1,
                generated.Split(
                    "[global::UnityEngine.Scripting.Preserve]",
                    StringSplitOptions.None).Length - 1);
            Assert.Contains("[global::UnityEngine.Scripting.Preserve]", generated, StringComparison.Ordinal);
            Assert.Contains("private global::Newtonsoft.Json.Linq.JToken __FoxService_Apply", generated, StringComparison.Ordinal);
        }

        private static FoxgloveGeneratedServiceDescriptor Descriptor(Func<JToken, JToken> handler) =>
            new("/phase157/reset", "demo.Reset", "", "demo.Request", "demo.Response", handler);
    }
}
