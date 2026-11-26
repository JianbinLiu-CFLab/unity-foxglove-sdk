#if MANAGER_PRODUCTION_BOUNDARY
// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Manager
// Purpose: Reject invalid TCP ports before starting the Manager runtime.

using System.Reflection;
using Unity.FoxgloveSDK.Components;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Manager
{
    /// <summary>Rejects invalid TCP ports before starting the Manager runtime.</summary>
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

        [Theory]
        [InlineData(true, 0, 0, 1)]
        [InlineData(true, 65536, 0, 1)]
        [InlineData(true, 1, 1, 0)]
        [InlineData(true, 65535, 1, 0)]
        [InlineData(false, 0, 1, 0)]
        [InlineData(false, 65536, 1, 0)]
        /// <summary>The Root CA distributor port is validated only while the distributor is enabled.</summary>
        public void StartServerValidatesRootCaDistributorPortOnlyWhenEnabled(
            bool enabled,
            int port,
            int expectedStarts,
            int expectedErrors)
        {
            Debug.Reset();
            var manager = new FoxgloveManager { IsRunning = false };
            SetRootCaDistributor(manager, enabled, port);

            manager.StartServer();

            Assert.Equal(expectedStarts, manager.RuntimeForTest.StartCount);
            Assert.Equal(expectedErrors, Debug.ErrorCount);
        }

        private static void SetPort(FoxgloveManager manager, int port)
        {
            var field = typeof(FoxgloveManager).GetField("_port", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field.SetValue(manager, port);
        }

        private static void SetRootCaDistributor(FoxgloveManager manager, bool enabled, int port)
        {
            var enabledField = typeof(FoxgloveManager).GetField("_rootCaDistributorEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            var portField = typeof(FoxgloveManager).GetField("_rootCaDistributorPort", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(enabledField);
            Assert.NotNull(portField);
            enabledField.SetValue(manager, enabled);
            portField.SetValue(manager, port);
        }
    }
}
#endif
