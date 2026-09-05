// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
using System;
using System.IO;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.Sensors
{
    public sealed class PointCloudEncodeErrorTests
    {
        [Fact]
        public void PointCloudPipelineRoutesWorkerExceptionsToFailureDiagnostic()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/PointCloudEncodePipeline.cs");
            Assert.Contains("onEncodeError: ex => LogFailure(_queueFailureMessagePrefix + ex.Message)", source, StringComparison.Ordinal);
            Assert.Contains("private void LogFailure(string message)", source, StringComparison.Ordinal);
        }

        private static string Read(string relativePath)
            => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../../", relativePath));
    }
}
