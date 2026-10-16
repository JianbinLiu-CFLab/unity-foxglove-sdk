// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validate Phase 52 Unity-native WSS/TLS transport extension,
// shared stream-based WebSocket core, token gating, certificate
// distribution, and secure/plain transport regressions.

using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Reflection;
using System.Threading;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase52Validation.
    /// </summary>
    public static partial class Phase52Validation
    {
        /// <remarks>Phase192 source sections: TransportAndTokenTests.cs, LifecycleAndManagerTests.cs, SocketHelpers.cs, FixturesAndStreams.cs</remarks>
        private const int TestTimeoutMs = 5000;
        private const int CertificateDistributorIdleTimeoutTestMs = 100;
        private const int QuietLogWaitMs = 250;
        private const string ValidToken = "phase52-secret";
        private const string WrongToken = "phase52-wrong";
        private static int _passCount;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 52 Tests ---");
            _passCount = 0;

            TestStatsExposeQueueCapacity();
            TestRuntimeOriginGuardUsesInterface();
            TestTokenGateRedactsAndCompares();
            TestWrongTokenRejectedBeforeUpgrade();
            TestTlsOptionsValidateDeterministicPfx();
            TestWssHandshakeAndServerInfo();
            TestWebSocketHandshakeRejectsUnsupportedVersion();
            TestWssTlsHandshakeAbortIsQuietByDefault();
            TestWssTlsHandshakeAbortCanBeLoggedWhenEnabled();
            TestWssOriginGuardRejectsDisallowedOrigin();
            TestSecureStopStartReleasesPort();
            TestReceiveLoopIgnoresSslStreamDisposalRace();
            TestFrameCodecTreatsRemoteAbortAggregateAsCleanEnd();
            TestWebSocketFrameProtocolDecodesMaskedClientFrame();
            TestWebSocketFrameProtocolRejectsInvalidClientFrames();
            TestSendQueueZeroByteLimitKeepsProtocolFramesUsable();
            TestClientConnectedExceptionDisconnectsRegisteredClient();
            TestManagedBackendStopDisposesCancellationSource();
            TestHostedFoxgloveWebUrlMatchesOfficialSdk();
            TestRemoteFileDesktopUrlMatchesFoxgloveShareableLinks();
            TestManagerDefaultsAllowFoxgloveWebOrigin();
            TestCertificateDistributorServesRootAndFingerprint();
            TestCertificateDistributorIgnoresIdleClientTimeout();
            TestManagerSourceClosesBackendSelectionPath();

            Console.WriteLine($"Phase 52: {_passCount} checks passed.");
        }

    }
}
