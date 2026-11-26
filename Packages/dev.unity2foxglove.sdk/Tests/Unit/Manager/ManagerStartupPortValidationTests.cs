#if MANAGER_PRODUCTION_BOUNDARY
// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Manager
// Purpose: Reject invalid TCP ports before the Manager creates or starts its runtime.

using System.Reflection;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Manager
{
    /// <summary>Rejects invalid TCP ports before the Manager creates or starts its runtime.</summary>
    public sealed class ManagerStartupPortValidationTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(65536)]
        /// <summary>An out-of-range server port fails startup without starting the runtime.</summary>
        public void StartServerRejectsOutOfRangePort(int port)
        {
            Debug.Reset();
            var manager = new FoxgloveManager { IsRunning = false };
            SetPort(manager, port);

            manager.StartServer();

            Assert.Equal(0, manager.RuntimeForTest.StartCount);
            Assert.Equal(1, Debug.ErrorCount);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(65535)]
        /// <summary>The inclusive port range boundaries start the runtime.</summary>
        public void StartServerAcceptsBoundaryPort(int port)
        {
            var manager = new FoxgloveManager { IsRunning = false };
            SetPort(manager, port);

            manager.StartServer();

            Assert.Equal(1, manager.RuntimeForTest.StartCount);
        }

        private static void SetPort(FoxgloveManager manager, int port)
        {
            var field = typeof(FoxgloveManager).GetField("_port", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(manager, port);
        }
    }
}
#endif
