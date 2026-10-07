// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Unit/FoxRun
// Purpose: RED contract for deterministic generated typed MessagePack publication.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Schemas.MsgPack;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{


public sealed partial class FoxRunMessagePackEmitterTests
    {
        private static string FindRepoRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                    && Directory.Exists(Path.Combine(
                        directory.FullName,
                        "Packages",
                        "dev.unity2foxglove.sdk")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Unity2Foxglove repository root.");
        }

        private static int FindTopicIndex(object source, string topic)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveLogSource");
            var count = Assert.IsType<int>(
                interfaceType.GetProperty("FoxgloveLog_TopicCount")!
                    .GetValue(source));
            var getTopic = interfaceType.GetMethod("FoxgloveLog_GetTopic")
                           ?? throw new InvalidOperationException(
                               "Generated log source is missing FoxgloveLog_GetTopic.");
            for (var index = 0;
                 index < count;
                 index++)
            {
                var info = getTopic.Invoke(source, new object[] { index })
                           ?? throw new InvalidOperationException(
                               "Generated topic metadata is null.");
                var infoType = info.GetType();
                var topicValue =
                    infoType.GetProperty("Topic")?.GetValue(info)
                    ?? infoType.GetField("Topic")?.GetValue(info);
                var actualTopic = Assert.IsType<string>(topicValue);
                if (string.Equals(
                        actualTopic,
                        topic,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }
            throw new InvalidOperationException(
                "Generated topic is missing: " + topic);
        }

        private static bool BeginCapture(object source, int topicIndex)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxglovePublishCaptureSource");
            var method = interfaceType.GetMethod("FoxgloveLog_BeginCapture")
                         ?? throw new InvalidOperationException(
                             "Generated capture source is missing FoxgloveLog_BeginCapture.");
            return Assert.IsType<bool>(
                method.Invoke(source, new object[] { topicIndex }));
        }

        private static void EndCapture(object source, int topicIndex)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxglovePublishCaptureSource");
            var method = interfaceType.GetMethod("FoxgloveLog_EndCapture")
                         ?? throw new InvalidOperationException(
                             "Generated capture source is missing FoxgloveLog_EndCapture.");
            method.Invoke(source, new object[] { topicIndex });
        }

        private static FoxTopicContract GetContract(
            object source,
            int topicIndex)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicContractSource");
            var method = interfaceType.GetMethod("FoxgloveLog_GetContract")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing FoxgloveLog_GetContract.");
            return Assert.IsType<FoxTopicContract>(
                method.Invoke(source, new object[] { topicIndex }));
        }

        private static bool HasObservers(
            object source,
            int topicIndex,
            FoxTopicBus bus)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicObserverSource");
            var method = interfaceType.GetMethod("FoxgloveLog_HasObservers")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing FoxgloveLog_HasObservers.");
            return Assert.IsType<bool>(
                method.Invoke(
                    source,
                    new object[] { topicIndex, bus }));
        }

        private static void PublishCapturedToObservers(
            object source,
            int topicIndex,
            FoxTopicBus bus,
            ulong timestampNs)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicObserverSource");
            var method = interfaceType.GetMethod(
                             "FoxgloveLog_PublishCapturedToObservers")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing observer publication.");
            method.Invoke(
                source,
                new object[] { topicIndex, bus, timestampNs });
        }

        private static void PublishToBus(
            object source,
            int topicIndex,
            FoxTopicBus bus,
            ulong timestampNs)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicBusSource");
            var method = interfaceType.GetMethod("FoxgloveLog_PublishToBus")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing bus publication.");
            method.Invoke(
                source,
                new object[] { topicIndex, bus, timestampNs });
        }

        private static void PublishToSinks(
            object source,
            int topicIndex,
            FoxTopicSinkRouter router,
            ulong timestampNs)
        {
            var interfaceType = FindGeneratedInterface(
                source,
                "IFoxgloveTopicSinkSource");
            var method = interfaceType.GetMethod("FoxgloveLog_PublishToSinks")
                         ?? throw new InvalidOperationException(
                             "Generated source is missing sink publication.");
            method.Invoke(
                source,
                new object[] { topicIndex, router, timestampNs });
        }

        private static void SetCaptureEncoding(
            Type type,
            object instance,
            int topicIndex,
            FoxRunEncoding encoding)
        {
            var field = type.GetField(
                            "__foxRunCaptureEncoding_" + topicIndex,
                            BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException(
                            "Generated capture encoding is missing.");
            field.SetValue(instance, encoding);
        }

        private static Type FindGeneratedInterface(object source, string name)
            => source.GetType()
                   .GetInterfaces()
                   .SingleOrDefault(candidate => candidate.Name == name)
               ?? throw new InvalidOperationException(
                   "Generated source is missing " + name + ".");

        private static FoxgloveMsgPackReader CapturedMessagePack(
            Type type,
            object instance,
            int topicIndex)
        {
            var payload = Assert.IsType<byte[]>(
                type.GetField(
                        "__foxRunLastMessagePack_" + topicIndex,
                        BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(instance));
            return new FoxgloveMsgPackReader(
                payload,
                FoxgloveMsgPackReadLimits.ForPayloadBytes(4096));
        }

        private static FieldInfo CaptureField(Type type, int topicIndex)
            => type.GetField(
                   "__foxRunCapture_" + topicIndex + "_0",
                   BindingFlags.Instance | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException(
                   "Generated capture field is missing for topic " + topicIndex + ".");

        private sealed class GeneratedRecordingSink : IFoxTopicSink
        {
            public string Name => "generated-recording";
            public FoxTopicSinkCapabilities Capabilities =>
                FoxTopicSinkCapabilities.Test;
            public int PublishCalls { get; private set; }
            public FoxTopicContract RegisteredContract { get; private set; }
            public FoxTopicContract LastContract { get; private set; }
            public byte[] LastPayload { get; private set; }

            public void Register(FoxTopicContract contract)
            {
                RegisteredContract = contract;
            }

            public void Publish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin)
            {
                PublishCalls++;
                LastContract = contract;
                LastPayload = payload;
            }

            public void Flush()
            {
            }

            public void Dispose()
            {
            }
        }

        private sealed class GeneratedExternalSink : IFoxTopicSink
        {
            public string Name => "generated-external";
            public FoxTopicSinkCapabilities Capabilities =>
                FoxTopicSinkCapabilities.External;
            public int PublishCalls { get; private set; }

            public void Register(FoxTopicContract contract)
            {
            }

            public void Publish(
                FoxTopicContract contract,
                ulong timestampNs,
                byte[] payload,
                string origin)
            {
                PublishCalls++;
            }

            public void Flush()
            {
            }

            public void Dispose()
            {
            }
        }

    }
}
