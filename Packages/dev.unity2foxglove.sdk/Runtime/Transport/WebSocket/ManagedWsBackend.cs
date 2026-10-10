// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Transport/WebSocket
// Purpose: Pure C# WebSocket server backend using TcpListener and manual
// RFC 6455 framing. No http.sys dependency - works on all platforms
// without admin rights.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Transport
{
    /// <summary>
    /// Pure C# WebSocket server backend using TcpListener + manual WebSocket protocol.
    /// No http.sys dependency; works on all platforms without admin rights.
    /// </summary>
    /// <remarks>Phase192 source sections: ManagedWsBackend.LifecycleAndMessaging.cs, ManagedWsBackend.AdmissionAndHandshake.cs, ManagedWsBackend.PublicationAndShutdown.cs, ManagedWsBackend.ReceiveAndDisconnect.cs</remarks>
    public partial class ManagedWsBackend : IFoxgloveTransport, IPrioritizedFoxgloveTransport, IReplayResettableFoxgloveTransport, IClientDataQueueResettableFoxgloveTransport, IFoxgloveTransportStatsProvider, IOriginGuardedFoxgloveTransport, IFoxgloveTransportCapacityProvider, IDisposable
    {
        private const int CloseDrainTimeoutMs = 250;
        private const int CloseHandshakeTimeoutMs = 1000;
        private const int StopAcceptLoopWaitMs = 500;
        private const int StopDisconnectWaitMs = 2000;
        private const int StopForcedCloseWaitMs = 1000;
        private const int StopPendingHandshakeWaitMs = 1000;
        private const int StopClientHandlersWaitMs = 5000;
        private const int HandshakeTimeoutMs = 5000;
        private const int CapacityRequestDrainTimeoutMs = 250;
        private const int MaxCapacityRequestBytes = 64 * 1024;
        private const int MaxQueuedCapacityResponses = 64;
        private const int MaxFragmentedMessageBytes = 4 * 1024 * 1024;
        private const ushort GoingAwayCloseCode = 1001;
        private const ushort ProtocolErrorCloseCode = 1002;
        private const ushort InvalidPayloadDataCloseCode = 1007;
        private const ushort MessageTooBigCloseCode = 1009;

        /// <summary>TCP listener bound to the server address and port.</summary>
        private TcpListener _listener;
        /// <summary>Cancellation token source to stop accept/receive loops.</summary>
        private CancellationTokenSource _cts;
        private Task _acceptLoopTask;
        private int _stopping;
        /// <summary>Active WebSocket connections keyed by client ID.</summary>
        private readonly ConcurrentDictionary<uint, WsConnection> _clients = new ConcurrentDictionary<uint, WsConnection>();
        /// <summary>
        /// TCP clients which have been accepted but have not completed the
        /// TLS/HTTP handshake.  The completion source lets Stop wait for the
        /// handler after closing the socket, while the dictionary itself is
        /// the bounded admission reservation.
        /// </summary>
        private readonly ConcurrentDictionary<TcpClient, TaskCompletionSource<bool>> _pendingClients =
            new ConcurrentDictionary<TcpClient, TaskCompletionSource<bool>>();
        private readonly ConcurrentDictionary<TcpClient, ClientHandler> _clientHandlers =
            new ConcurrentDictionary<TcpClient, ClientHandler>();
        /// <summary>Shared managed WebSocket options for queue capacity and token gate.</summary>
        private readonly ManagedWebSocketOptions _options;
        private readonly WsHandshakeHandler _handshakeHandler;
        /// <summary>Logger instance for diagnostic output.</summary>
        private readonly IFoxgloveLogger _logger;
        /// <summary>Monotonically increasing counter for assigning client IDs.</summary>
        private long _nextClientId;
        /// <summary>Allowed browser origins for Cross-Site WebSocket Hijacking protection. Empty collection rejects all browser-origin clients.</summary>
        private readonly HashSet<string> _allowedOrigins = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _allowedOriginsLock = new object();
        private readonly object _clientAdmissionLock = new object();
        // Serializes listener publication/claiming with Stop so a concurrent
        // Start cannot resurrect a listener while the previous shutdown still
        // owns its sockets and callback continuations.
        private readonly object _lifecycleLock = new object();
        private int _stopInProgress;
        private readonly Dictionary<uint, ClientPublication> _clientPublications =
            new Dictionary<uint, ClientPublication>();
        private int _queuedCapacityResponses;
        private readonly ConcurrentDictionary<Task, TcpClient> _capacityResponseWorkers =
            new ConcurrentDictionary<Task, TcpClient>();

        private sealed class ClientPublication
        {
            // Serializes the cancellation check with entering user callback
            // code.  Stop may retire an unstarted publication, but once this
            // gate marks CallbackStarted the callback is logically in flight
            // and disconnect is deferred until it returns.
            internal readonly object CallbackGate = new object();
            internal bool Announced;
            internal bool CallbackStarted;
            internal bool CallbackCompleted;
            internal int CallbackThreadId;
            internal bool Cancelled;
            internal bool StopDisconnectRequested;
            internal readonly List<PendingPublicationFrame> PendingControlFrames =
                new List<PendingPublicationFrame>();
            internal int PendingControlBytes;
            internal bool ControlOverflowed;
            internal readonly TaskCompletionSource<bool> Completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly struct PendingPublicationFrame
        {
            internal PendingPublicationFrame(bool text, byte[] payload)
            {
                Text = text;
                Payload = payload ?? Array.Empty<byte>();
            }

            internal bool Text { get; }
            internal byte[] Payload { get; }
        }

        private sealed class ClientHandler
        {
            internal readonly TcpClient TcpClient;
            internal readonly TaskCompletionSource<bool> Completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal int ThreadId;

            internal ClientHandler(TcpClient tcpClient)
            {
                TcpClient = tcpClient;
            }
        }

        // Aggregate health counters
        private long _totalAcceptedClients;
        private long _totalRejectedClients;
        private long _totalDisconnectedClients;
        private long _totalControlOverflowDisconnects;
        private long _totalDroppedDataFrames;

        public ManagedWsBackend(IFoxgloveLogger logger = null)
            : this(new ManagedWebSocketOptions(), logger) { }

        public ManagedWsBackend(ManagedWebSocketOptions options, IFoxgloveLogger logger = null)
        {
            _options = options ?? new ManagedWebSocketOptions();
            _logger = logger ?? new ConsoleLogger();
            _handshakeHandler = new WsHandshakeHandler(_options, _allowedOrigins, _allowedOriginsLock, _logger);
        }

        /// <summary>Whether the TCP listener is actively accepting connections.</summary>
        public bool IsRunning => Volatile.Read(ref _listener) != null;

        /// <summary>Current admission capacity, reflecting mutable options before Start.</summary>
        public int MaxClients => ManagedWebSocketOptions.NormalizeMaxClients(_options.MaxClients);

        /// <summary>
        /// Whether a capacity rejection can safely write a plaintext HTTP
        /// response before the transport-specific handshake has been created.
        /// TLS transports override this and close the socket instead.
        /// </summary>
        protected virtual bool SupportsPlaintextCapacityResponse => true;

        /// <summary>
        /// Releases resources published by a derived transport after the base
        /// stop has drained its listener, clients, and handshake continuations.
        /// The callback runs while the lifecycle gate is held and before a new
        /// Start is allowed to claim the transport.
        /// </summary>
        protected virtual void OnStopCompletedUnderLifecycleLock() { }

        /// <summary>
        /// Shared lifecycle gate for derived transports that publish additional
        /// resources (for example the WSS certificate) around Start/Stop.
        /// </summary>
        protected object LifecycleLock => _lifecycleLock;

        /// <summary>Fires when a new WebSocket client completes the handshake.</summary>
        public event Action<uint> OnClientConnected;
        /// <summary>Fires when a client disconnects or is forcefully removed.</summary>
        public event Action<uint> OnClientDisconnected;
        /// <summary>Fires when a UTF-8 text message is received from a client.</summary>
        public event Action<uint, string> OnTextReceived;
        /// <summary>Fires when a binary message is received from a client.</summary>
        public event Action<uint, byte[]> OnBinaryReceived;

    }
}
