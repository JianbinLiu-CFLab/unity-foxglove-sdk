// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Video
// Purpose: Experimental Windows Media Foundation H.264 encoder sidecar.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.FoxgloveSDK.Components;

namespace Foxglove.Schemas.Video
{
    /// <summary>
    /// Experimental Media Foundation H.264 encoder sidecar. RGB frames enter a
    /// bounded worker queue so TrySubmitFrame remains non-blocking like the
    /// other video sidecars.
    /// </summary>
    public sealed partial class MediaFoundationH264EncoderSidecar : ICameraVideoEncoderSidecar, ITimestampedCameraVideoEncoderSidecar, ICameraVideoFrameSourceSidecar, ICameraVideoSidecarDeferredCleanup, ICameraVideoSidecarDeferredCleanupScheduler
    {
        private const int SOk = 0;
        private const int SFalse = 1;
        private const int MfVersion = 0x00020070;
        private const int ClsctxInprocServer = 0x1;
        private const int CoinitMultithreaded = 0x0;
        private const int RpcEChangedMode = unchecked((int)0x80010106);
        private const int MfENotAccepting = unchecked((int)0xC00D36B5);
        private const int MfETransformNeedMoreInput = unchecked((int)0xC00D6D72);
        private const int MfETransformStreamChange = unchecked((int)0xC00D6D61);
        private const int MftOutputStreamProvidesSamples = 0x00000100;
        private const int VtBool = 11;
        private const int VtUI4 = 19;
        private const int VariantTrue = -1;
        private const int RateControlModeCbr = 0;
        private const int MftMessageCommandFlush = 0x00000000;
        private const int MftMessageNotifyBeginStreaming = 0x10000000;
        private const int MftMessageNotifyEndStreaming = 0x10000001;
        private const int MftMessageNotifyStartOfStream = 0x10000002;
        private const int MftMessageNotifyEndOfStream = 0x10000003;
        private const int MfVideoInterlaceProgressive = 2;
        private const int H264BaselineProfile = 66;
        private const int MaxTrackedSampleTimestamps = 256;
        private const int MaxConsecutiveOutputStreamChanges = 3;
        private static readonly int s_mftOutputDataBufferSize = Marshal.SizeOf(typeof(MftOutputDataBuffer));

        private readonly ConcurrentQueue<EncodedVideoAccessUnit> _outputAccessUnits = new ConcurrentQueue<EncodedVideoAccessUnit>();
        private readonly ConcurrentQueue<QueuedInputFrame> _inputFrames = new ConcurrentQueue<QueuedInputFrame>();
        private readonly AutoResetEvent _inputSignal = new AutoResetEvent(false);
        private readonly Dictionary<long, ulong> _sampleTimestampNsByTime = new Dictionary<long, ulong>();
        private readonly Dictionary<long, LinkedListNode<long>> _sampleTimestampNodesByTime = new Dictionary<long, LinkedListNode<long>>();
        private readonly LinkedList<long> _sampleTimestampOrder = new LinkedList<long>();
        private readonly object _outputLock = new object();
        private readonly object _inputLock = new object();
        private readonly H264AccessUnitNormalizer _normalizer = new H264AccessUnitNormalizer();
        private MediaFoundationH264EncoderOptions _options;
        private IMFTransform _transform;
        private byte[] _nv12Scratch;
        private MftOutputStreamInfo _outputStreamInfo;
        private long _nextSampleTime;
        private long _sampleDuration;
        private long _evictedTimestampCount;
        private long _droppedInputFrames;
        private int _outputCount;
        private int _inputCount;
        private Thread _encoderWorker;
        private readonly ManualResetEvent _workerExited = new ManualResetEvent(false);
        private readonly ManualResetEventSlim _workerInitialized = new ManualResetEventSlim(false);
        private RegisteredWaitHandle _deferredCleanupRegistration;
        private int _deferredCleanupScheduled;
        private int _stopRequested;
        private const int MaxInputQueueCapacity = 2;
        private const int StartupTimeoutMs = 10000;
        private const int ShutdownTimeoutMs = 500;
        private int _maxOutputQueue = 4;
        private bool _mfStarted;
        private bool _comInitialized;
        private bool _hasOutputStreamInfo;
        private bool _isRunning;
        private string _lastDiagnosticLine;
        private string _lastError;

        public bool IsRunning
        {
            get => Volatile.Read(ref _isRunning);
            private set => Volatile.Write(ref _isRunning, value);
        }
        public int OutputQueueDepth => Volatile.Read(ref _outputCount);
        public int MaxOutputQueue => Volatile.Read(ref _maxOutputQueue);
        public int InputQueueDepth => Volatile.Read(ref _inputCount);
        public int MaxInputQueue => MaxInputQueueCapacity;
        public string LastDiagnosticLine
        {
            get => Volatile.Read(ref _lastDiagnosticLine);
            private set => Volatile.Write(ref _lastDiagnosticLine, value);
        }
        public string LastError
        {
            get => Volatile.Read(ref _lastError);
            private set => Volatile.Write(ref _lastError, value);
        }
        public long EvictedTimestampCount => Interlocked.Read(ref _evictedTimestampCount);
        public long DroppedInputFrames => Interlocked.Read(ref _droppedInputFrames);

        internal static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    }
}
