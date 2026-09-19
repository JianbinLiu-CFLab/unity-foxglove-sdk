// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using Unity.FoxgloveSDK.Components;
using Xunit;

namespace Unity.FoxgloveSDK.UnitTests.FoxRun
{
    public sealed class FoxRunProviderDiagnosticTests
    {
        [Fact]
        public void StructuredFailureCapturesFieldsWithoutRetainingException()
        {
            FoxRunProviderDiagnostics.Clear();
            try
            {
                throw new InvalidOperationException("credential=secret payload=bytes");
            }
            catch (Exception exception)
            {
                FoxRunProviderDiagnostics.Record("provider", "Publish", exception, 7, "/topic");
            }

            var diagnostic = Assert.Single(FoxRunProviderDiagnostics.Snapshot());
            Assert.Equal("provider", diagnostic.ProviderId);
            Assert.Equal("Publish", diagnostic.Operation);
            Assert.Equal(typeof(InvalidOperationException).FullName, diagnostic.ExceptionType);
            Assert.NotEmpty(diagnostic.Stack);
            Assert.Equal((ulong)7, diagnostic.Generation);
            Assert.Equal("/topic", diagnostic.Topic);
            Assert.Null(diagnostic.GetType().GetProperty("Exception"));
        }
    }
}
