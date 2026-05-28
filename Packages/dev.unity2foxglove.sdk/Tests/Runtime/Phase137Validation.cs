// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 137 directory-first runtime structure guard.

using System;
using System.IO;
using System.Linq;

namespace Unity.FoxgloveSDK.Tests
{
    public static class Phase137Validation
    {
        private static readonly string[] ExpectedFolders =
        {
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Manager",
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Utilities",
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Logging",
            "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime",
            "Packages/dev.unity2foxglove.sdk/Runtime/Core/Abstractions",
            "Packages/dev.unity2foxglove.sdk/Runtime/Core/Events",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Common",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Writer",
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/Abstractions",
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/Native",
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/Common",
            "Packages/dev.unity2foxglove.sdk/Runtime/Protocol/Messages",
        };

        private static readonly string[] ExpectedFolderMeta =
        {
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Utilities.meta",
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/Logging.meta",
            "Packages/dev.unity2foxglove.sdk/Runtime/Core/Runtime.meta",
            "Packages/dev.unity2foxglove.sdk/Runtime/Core/Abstractions.meta",
            "Packages/dev.unity2foxglove.sdk/Runtime/Core/Events.meta",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Common.meta",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Reader.meta",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/Writer.meta",
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/Abstractions.meta",
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/Native.meta",
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/Common.meta",
            "Packages/dev.unity2foxglove.sdk/Runtime/Protocol/Messages.meta",
        };

        private static readonly string[] OldFlatFiles =
        {
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxgloveManager.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/CoordinateConverter.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Components/FoxgloveLogger.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Core/FoxgloveRuntime.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Core/IRuntimeContext.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Core/BoundedEventQueue.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Core/IFoxgloveLogger.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Core/SchemaIdentityMode.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapCompression.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapRecordTypes.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapConstants.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapBinaryReader.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapIndexedReader.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapReader.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapStreamingReader.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapReadOptions.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapSequentialReadLimits.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapWriter.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/IO/Mcap/McapWriterOptions.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/IFoxgloveTransport.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/IOriginGuardedFoxgloveTransport.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/NativeFoxgloveBackend.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/FoxgloveTransportMode.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/FoxgloveAppUrl.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/TransportStats.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Transport/TransportHostResolver.cs",
            "Packages/dev.unity2foxglove.sdk/Runtime/Protocol/JsonMessages.cs",
        };

        private static readonly string[] ExpectedMessageFiles =
        {
            "ProtocolConstants.cs",
            "ServerInfoMessages.cs",
            "ChannelMessages.cs",
            "StatusMessages.cs",
            "SubscriptionMessages.cs",
            "ParameterMessages.cs",
            "ServiceMessages.cs",
            "ConnectionGraphMessages.cs",
            "AssetMessages.cs",
        };

        private static readonly string[] ExpectedDtoTypes =
        {
            "Unity.FoxgloveSDK.Protocol.ServerInfo",
            "Unity.FoxgloveSDK.Protocol.DataTimestamp",
            "Unity.FoxgloveSDK.Protocol.Advertise",
            "Unity.FoxgloveSDK.Protocol.AdvertiseChannel",
            "Unity.FoxgloveSDK.Protocol.Unadvertise",
            "Unity.FoxgloveSDK.Protocol.StatusMessage",
            "Unity.FoxgloveSDK.Protocol.RemoveStatusMessage",
            "Unity.FoxgloveSDK.Protocol.SubscribeMessage",
            "Unity.FoxgloveSDK.Protocol.Subscription",
            "Unity.FoxgloveSDK.Protocol.UnsubscribeMessage",
            "Unity.FoxgloveSDK.Protocol.ParameterValues",
            "Unity.FoxgloveSDK.Protocol.Parameter",
            "Unity.FoxgloveSDK.Protocol.SetParameters",
            "Unity.FoxgloveSDK.Protocol.SubscribeParameterUpdates",
            "Unity.FoxgloveSDK.Protocol.UnsubscribeParameterUpdates",
            "Unity.FoxgloveSDK.Protocol.GetParameters",
            "Unity.FoxgloveSDK.Protocol.AdvertiseServices",
            "Unity.FoxgloveSDK.Protocol.UnadvertiseServices",
            "Unity.FoxgloveSDK.Protocol.ServiceDescriptor",
            "Unity.FoxgloveSDK.Protocol.ServiceSchemaDescriptor",
            "Unity.FoxgloveSDK.Protocol.ServiceCallFailure",
            "Unity.FoxgloveSDK.Protocol.SubscribeConnectionGraph",
            "Unity.FoxgloveSDK.Protocol.UnsubscribeConnectionGraph",
            "Unity.FoxgloveSDK.Protocol.ConnectionGraphUpdate",
            "Unity.FoxgloveSDK.Protocol.PublishedTopic",
            "Unity.FoxgloveSDK.Protocol.SubscribedTopic",
            "Unity.FoxgloveSDK.Protocol.AdvertisedService",
            "Unity.FoxgloveSDK.Protocol.FetchAsset",
            "Unity.FoxgloveSDK.Protocol.Subprotocol",
            "Unity.FoxgloveSDK.Protocol.Capability",
            "Unity.FoxgloveSDK.Protocol.FoxgloveEnumConverter",
            "Unity.FoxgloveSDK.Protocol.FoxgloveStatusLevel",
        };

        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 137 Tests ---");
            _passed = 0;

            var repoRoot = Phase16Validation.FindRepoRoot();

            VerifyFoldersExist(repoRoot);
            VerifyFolderMetaFiles(repoRoot);
            VerifyOldFilesRemoved(repoRoot);
            VerifyProtocolMessages(repoRoot);
            VerifyDtoTypes();
            VerifyCsprojGlob(repoRoot);

            Console.WriteLine("Phase 137: " + _passed + " checks passed.\n");
        }

        private static void VerifyFoldersExist(string repoRoot)
        {
            foreach (var folder in ExpectedFolders)
            {
                var path = Path.Combine(repoRoot, folder);
                Check(Directory.Exists(path), "137-1: folder exists: " + folder);
            }
        }

        private static void VerifyFolderMetaFiles(string repoRoot)
        {
            foreach (var meta in ExpectedFolderMeta)
            {
                var path = Path.Combine(repoRoot, meta);
                Check(File.Exists(path), "137-2: folder .meta exists: " + meta);
                if (File.Exists(path))
                {
                    var content = File.ReadAllText(path);
                    Check(content.Contains("folderAsset: yes", StringComparison.Ordinal),
                        "137-3: .meta has folderAsset: " + meta);
                }
            }
        }

        private static void VerifyOldFilesRemoved(string repoRoot)
        {
            foreach (var oldFile in OldFlatFiles)
            {
                var path = Path.Combine(repoRoot, oldFile);
                Check(!File.Exists(path), "137-4: old flat file removed: " + oldFile);
            }
        }

        private static void VerifyProtocolMessages(string repoRoot)
        {
            var messagesDir = Path.Combine(repoRoot, "Packages/dev.unity2foxglove.sdk/Runtime/Protocol/Messages");
            var protocolDir = Path.Combine(repoRoot, "Packages/dev.unity2foxglove.sdk/Runtime/Protocol");
            Check(Directory.Exists(messagesDir), "137-5: Messages/ directory exists");

            foreach (var file in ExpectedMessageFiles)
            {
                // ProtocolConstants.cs lives in Protocol/ root; other files live in Messages/
                var dir = file == "ProtocolConstants.cs" ? protocolDir : messagesDir;
                var path = Path.Combine(dir, file);
                Check(File.Exists(path), "137-6: message file exists: " + file);
                if (File.Exists(path))
                {
                    // Check .meta file for each message file
                    var meta = path + ".meta";
                    Check(File.Exists(meta), "137-7: message .meta exists: " + file + ".meta");
                }
            }
        }

        private static void VerifyDtoTypes()
        {
            foreach (var typeName in ExpectedDtoTypes)
            {
                var type = Type.GetType(typeName);
                Check(type != null && type.FullName == typeName,
                    "137-8: DTO type unchanged: " + typeName);
            }
        }

        private static void VerifyCsprojGlob(string repoRoot)
        {
            var csprojPath = Path.Combine(repoRoot, "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            var csproj = File.ReadAllText(csprojPath);
            Check(csproj.Contains("Runtime/Protocol/**/*.cs", StringComparison.Ordinal),
                "137-9: csproj uses recursive Protocol/**/*.cs glob");
        }

        private static void Check(bool condition, string label)
        {
            if (condition)
            {
                Console.WriteLine("[PASS] " + label);
                _passed++;
            }
            else
            {
                Console.WriteLine("[FAIL] " + label);
            }
        }
    }
}
