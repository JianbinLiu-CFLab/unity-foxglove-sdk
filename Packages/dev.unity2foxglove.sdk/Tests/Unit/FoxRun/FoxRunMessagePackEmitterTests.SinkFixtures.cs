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
