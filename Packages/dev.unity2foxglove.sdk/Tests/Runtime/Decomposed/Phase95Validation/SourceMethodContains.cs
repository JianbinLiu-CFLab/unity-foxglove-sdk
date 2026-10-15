// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 95 validation for ROS2 Bridge productization.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Foxglove;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase95Validation.
    /// </summary>
    public static partial class Phase95Validation
    {
        private static bool SourceMethodContains(string source, string methodName, string needle)
            => PhaseValidationSourceHelpers.SourceMethodContains(source, methodName, needle);

        private static bool WaitUntil(Func<bool> condition, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        private static bool Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = Phase16Validation.FindRepoRoot();
            if (root == null)
                throw new InvalidOperationException("Could not find repository root.");

            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source file was not found.", path);

            return PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);
        }

        private static string ReadProductPublisherText(string relativePath)
        {
            if (relativePath.EndsWith("FoxgloveCameraPublisher.cs", StringComparison.Ordinal))
                return ReadCameraPublisherSources();

            if (relativePath.EndsWith("FoxglovePointCloudPublisher.cs", StringComparison.Ordinal))
            {
                return ReadRepoText(relativePath)
                    + "\n" + ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Raw.cs")
                    + "\n" + ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.Draco.cs")
                    + "\n" + ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxglovePointCloudPublisher.PackedPointCloud.cs");
            }

            return ReadRepoText(relativePath);
        }

        private static string ReadCameraPublisherSources()
            => PhaseValidationSourceHelpers.ReadCameraPublisherSources();

        private sealed class FakeSinkFactory
        {
            private readonly object _gate = new object();
            private readonly List<Ros2BridgeFrame> _allSentFrames = new List<Ros2BridgeFrame>();
            public int ConnectFailuresRemaining;
            public int DisconnectCalls;
            public int DisposeCalls;
            public bool DisconnectedBeforeDispose;

            public int SentFrameCount
            {
                get
                {
                    lock (_gate)
                    {
                        return _allSentFrames.Count;
                    }
                }
            }

            public Ros2BridgeFrame GetSentFrame(int index)
            {
                lock (_gate)
                {
                    return _allSentFrames[index];
                }
            }

            /// <summary>
            /// Validation method for Create.
            /// </summary>
            /// <returns>The value produced by the validation helper.</returns>
            public IRos2BridgeSink Create()
            {
                return new FakeSink(this);
            }

            private sealed class FakeSink : IRos2BridgeSink
            {
                private readonly FakeSinkFactory _owner;
                private bool _disconnectCalled;

                /// <summary>
                /// Validation constructor for FakeSink.
                /// </summary>
                /// <param name="owner">Owning factory.</param>
                public FakeSink(FakeSinkFactory owner) => _owner = owner;
                public bool IsConnected { get; private set; }

                /// <summary>
                /// Validation method for Connect.
                /// </summary>
                /// <param name="host">Host address used by the validation client or listener.</param>
                /// <param name="port">TCP port used by the validation client or listener.</param>
                /// <param name="timeoutMs">Timeout in milliseconds for the validation operation.</param>
                public void Connect(string host, int port, int timeoutMs)
                {
                    lock (_owner._gate)
                    {
                        if (_owner.ConnectFailuresRemaining > 0)
                        {
                            _owner.ConnectFailuresRemaining--;
                            throw new InvalidOperationException("planned connect failure");
                        }
                    }

                    IsConnected = true;
                }

                /// <summary>
                /// Validation method for Send.
                /// </summary>
                /// <param name="frame">ROS 2 bridge frame sent by the validation client.</param>
                /// <param name="timeoutMs">Timeout in milliseconds for the validation operation.</param>
                public void Send(Ros2BridgeFrame frame, int timeoutMs)
                {
                    if (!IsConnected)
                        throw new InvalidOperationException("not connected");
                    lock (_owner._gate)
                    {
                        _owner._allSentFrames.Add(frame);
                    }
                }

                /// <summary>
                /// Validation method for Disconnect.
                /// </summary>
                public void Disconnect()
                {
                    IsConnected = false;
                    lock (_owner._gate)
                    {
                        _disconnectCalled = true;
                        _owner.DisconnectCalls++;
                    }
                }

                /// <summary>
                /// Validation method for Dispose.
                /// </summary>
                public void Dispose()
                {
                    lock (_owner._gate)
                    {
                        _owner.DisposeCalls++;
                        if (_disconnectCalled)
                            _owner.DisconnectedBeforeDispose = true;
                    }
                }
            }
        }

        private sealed class BlockingConnectSinkFactory
        {
            private readonly object _gate = new object();
            public readonly ManualResetEventSlim ConnectStarted = new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim ReleaseConnect = new ManualResetEventSlim(false);
            public readonly ManualResetEventSlim StopStarted = new ManualResetEventSlim(false);
            public int DisposeCalls;

            /// <summary>
            /// Validation method for Create.
            /// </summary>
            /// <returns>The value produced by the validation helper.</returns>
            public IRos2BridgeSink Create()
            {
                return new BlockingSink(this);
            }

            private sealed class BlockingSink : IRos2BridgeSink
            {
                private readonly BlockingConnectSinkFactory _owner;
                /// <summary>
                /// Validation constructor for BlockingSink.
                /// </summary>
                /// <param name="owner">Owning factory.</param>
                public BlockingSink(BlockingConnectSinkFactory owner) => _owner = owner;
                public bool IsConnected { get; private set; }

                /// <summary>
                /// Validation method for Connect.
                /// </summary>
                /// <param name="host">Host address used by the validation client or listener.</param>
                /// <param name="port">TCP port used by the validation client or listener.</param>
                /// <param name="timeoutMs">Timeout in milliseconds for the validation operation.</param>
                public void Connect(string host, int port, int timeoutMs)
                {
                    _owner.ConnectStarted.Set();
                    if (!_owner.ReleaseConnect.Wait(Math.Max(timeoutMs, 3000)))
                        throw new TimeoutException("Timed out waiting for Phase95 stop-race test to release blocked connect.");
                    IsConnected = true;
                }

                /// <summary>
                /// Validation method for Send.
                /// </summary>
                /// <param name="frame">ROS 2 bridge frame sent by the validation client.</param>
                /// <param name="timeoutMs">Timeout in milliseconds for the validation operation.</param>
                public void Send(Ros2BridgeFrame frame, int timeoutMs)
                {
                }

                /// <summary>
                /// Validation method for Disconnect.
                /// </summary>
                public void Disconnect()
                {
                    IsConnected = false;
                }

                /// <summary>
                /// Validation method for Dispose.
                /// </summary>
                public void Dispose()
                {
                    Disconnect();
                    lock (_owner._gate)
                    {
                        _owner.DisposeCalls++;
                    }
                }
            }
        }
    }
}
