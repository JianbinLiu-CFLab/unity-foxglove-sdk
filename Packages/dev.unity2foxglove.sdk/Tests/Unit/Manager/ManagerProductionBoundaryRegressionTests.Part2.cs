#if MANAGER_PRODUCTION_BOUNDARY
// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/Manager
// Purpose: Exercise the Manager and publisher production boundaries directly.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Components.Publishing;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.UnitTests.Harness;
using UnityEngine;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Manager
{
    public sealed partial class ManagerProductionBoundaryRegressionTests
    {
        [Fact]
        public void ReplayRestoreDoesNotOverrideExternalPublisherEnable()
        {
            var publisher = new BoundaryPublisher();
            publisher.ExternalEnableForTest();

            Assert.True(publisher.TryDisableForReplayForTest());
            publisher.ExternalEnableForTest();

            publisher.RestoreAfterReplayForTest();
            Assert.True(publisher.enabled);
        }

        [Fact]
        public void RemoteMcapStartupRetriesAfterBoundedFailure()
        {
            var path = Path.GetTempFileName();
            var port = FindFreePort();
            var manager = new FoxgloveManager();
            var attempts = 0;
            manager.ConfigureRemoteForTest(path, port);
            manager.RemoteMcapFileServerStartForTests = options =>
            {
                attempts++;
                if (attempts == 1)
                    throw new InvalidOperationException("one-shot Remote MCAP fault");
                return RemoteMcapHttpServer.Start(options);
            };

            try
            {
                manager.SetTimeForTest(10d);
                manager.StartRemoteForTest();
                Assert.Equal(1, attempts);
                Assert.False(manager.RemoteConfigKnownForTest);
                Assert.False(manager.RemoteRunningForTest);

                manager.SetTimeForTest(10.5d);
                manager.RefreshRemoteForTest();
                Assert.Equal(1, attempts);

                manager.SetTimeForTest(11d);
                manager.RefreshRemoteForTest();
                Assert.Equal(2, attempts);
                Assert.True(manager.RemoteConfigKnownForTest);
                Assert.True(manager.RemoteRunningForTest);
            }
            finally
            {
                manager.StopSidecarsForTest();
                File.Delete(path);
            }
        }

        [Fact]
        public void RemoteMcapManagerPreservesContentIdentityWhenFileIsReplaced()
        {
            var path = Path.GetTempFileName();
            var replacementPath = Path.GetTempFileName();
            WriteRemoteMcapFixture(path, 1);
            WriteRemoteMcapFixture(replacementPath, 2);
            var manager = new FoxgloveManager();
            manager.ConfigureRemoteForTest(path, FindFreePort());

            try
            {
                manager.StartRemoteForTest();

                var options = manager.RemoteOptionsForTest;
                Assert.NotNull(options);
                Assert.Null(options.GenerationVersionProvider);
                var source = new RemoteMcapDataSourcePrototype(
                    options.McapPath,
                    options.SourceId,
                    options.ManifestName,
                    options.RequiredBearerToken,
                    options.MaxInMemoryDataBytes,
                    options.DataRoute,
                    options.DirectFileRoute,
                    options.GenerationVersionProvider);
                var first = source.GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;
                var originalWriteTime = File.GetLastWriteTimeUtc(path);
                var replacement = File.ReadAllBytes(replacementPath);
                Assert.Equal(new FileInfo(path).Length, replacement.LongLength);
                File.WriteAllBytes(path, replacement);
                File.SetLastWriteTimeUtc(path, originalWriteTime);

                manager.RefreshRemoteForTest();
                var second = source.GetManifest(new RemoteMcapRequest()).Manifest.Sources[0].Id;
                Assert.NotEqual(first, second);
            }
            finally
            {
                manager.StopSidecarsForTest();
                File.Delete(path);
                File.Delete(replacementPath);
            }
        }

        private static void WriteRemoteMcapFixture(string path, byte value)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            using (var recorder = new McapRecorder(
                stream,
                null,
                new McapWriterOptions
                {
                    UseChunking = true,
                    ChunkSizeBytes = 128,
                    IndexTypes = McapIndexTypes.Chunk | McapIndexTypes.Message
                },
                leaveOpen: true))
            {
                recorder.AddChannel(1, "/manager/generation", "json", "manager.Generation", "jsonschema", "{}");
                recorder.WriteMessage(1, 1, new byte[] { value });
                recorder.Close();
            }
        }

        [Fact]
        public void ReplayCursorStartupRetriesAfterPersistentFailureWindow()
        {
            var port = FindFreePort();
            var manager = new FoxgloveManager();
            var attempts = 0;
            manager.ConfigureCursorForTest(port);
            manager.ReplayCursorEndpointStartForTests = (endpoint, options, queue, state) =>
            {
                attempts++;
                if (attempts == 1)
                    throw new InvalidOperationException("one-shot replay cursor fault");
                endpoint.Start(options, queue, state);
            };

            try
            {
                manager.SetTimeForTest(20d);
                manager.StartCursorForTest();
                Assert.Equal(1, attempts);
                Assert.False(manager.CursorConfigKnownForTest);
                Assert.False(manager.CursorRunningForTest);

                manager.SetTimeForTest(20.5d);
                manager.RefreshCursorForTest();
                Assert.Equal(1, attempts);

                manager.SetTimeForTest(21d);
                manager.RefreshCursorForTest();
                Assert.Equal(2, attempts);
                Assert.True(manager.CursorConfigKnownForTest);
                Assert.True(manager.CursorRunningForTest);
            }
            finally
            {
                manager.StopSidecarsForTest();
            }
        }

        private static int FindFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private sealed class BoundaryPublisher : FoxglovePublisherBase
        {
            public string TopicForTest
            {
                set => _topic = value;
            }

            public FoxgloveManager ManagerForTest
            {
                set => _manager = value;
            }

            protected override string SchemaName => "manager.boundary";

            public void ExternalEnableForTest()
            {
                SetEnabledLikeUnityForTest(true);
            }

            public void ExternalDisableForTest()
            {
                SetEnabledLikeUnityForTest(false);
            }

            public bool ReplaySuppressedForTest => IsReplaySuppressed;

            public bool PreparePublishForTest()
                => ShouldPreparePublishPayload();

            public FoxRunOrdinaryTransportFanoutResult PublishOrdinaryTransportForTest()
                => PublishOrdinaryTransport(new object(), "manager.boundary", 1UL);

            private void SetEnabledLikeUnityForTest(bool value)
            {
                if (enabled == value)
                    return;

                enabled = value;
                if (value)
                    OnEnable();
                else
                    OnDisable();
            }

            public bool TryDisableForReplayForTest()
                => TryDisableForReplay();

            public void RestoreAfterReplayForTest()
                => RestoreAfterReplay();
        }

        private sealed class BoundaryManagerSerializedObject
        {
            private readonly FoxgloveManager _target;
            private readonly BoundaryManagerSerializedProperty _encoding;
            private readonly BoundaryManagerSerializedProperty _rate;
            private bool _dirty;

            public BoundaryManagerSerializedObject(FoxgloveManager target)
            {
                _target = target;
                _encoding = BoundaryManagerSerializedProperty.Enum(
                    (int)target.DefaultFoxRunPublishEncoding,
                    () => _dirty = true);
                _rate = BoundaryManagerSerializedProperty.Float(
                    target.DefaultPublishRateHz,
                    () => _dirty = true);
            }

            public BoundaryManagerSerializedProperty FindProperty(string propertyName)
            {
                switch (propertyName)
                {
                    case "_defaultFoxRunPublishEncoding": return _encoding;
                    case "_defaultPublishRateHz": return _rate;
                    default: throw new ArgumentException("Unknown serialized property: " + propertyName);
                }
            }

            public bool ApplyModifiedProperties()
            {
                if (!_dirty)
                    return false;

                _target.DefaultFoxRunPublishEncoding =
                    (FoxRunEncoding)_encoding.enumValueIndex;
                _target.DefaultPublishRateHz = _rate.floatValue;
                _dirty = false;
                return true;
            }
        }

        private sealed class BoundaryManagerSerializedProperty
        {
            private readonly Action _onChanged;
            private int _enumValueIndex;
            private float _floatValue;

            private BoundaryManagerSerializedProperty(
                int enumValueIndex,
                float floatValue,
                Action onChanged)
            {
                _enumValueIndex = enumValueIndex;
                _floatValue = floatValue;
                _onChanged = onChanged;
            }

            public int enumValueIndex
            {
                get => _enumValueIndex;
                set
                {
                    _enumValueIndex = value;
                    _onChanged?.Invoke();
                }
            }

            public float floatValue
            {
                get => _floatValue;
                set
                {
                    _floatValue = value;
                    _onChanged?.Invoke();
                }
            }

            public static BoundaryManagerSerializedProperty Enum(int value, Action onChanged)
                => new BoundaryManagerSerializedProperty(value, 0f, onChanged);

            public static BoundaryManagerSerializedProperty Float(float value, Action onChanged)
                => new BoundaryManagerSerializedProperty(0, value, onChanged);
        }
    }
}
#endif
