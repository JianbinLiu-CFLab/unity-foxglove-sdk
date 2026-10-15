// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Editor;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.SourceGenerators;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    [CollectionDefinition("FoxRunSchemaRegistry")]
    public sealed class FoxRunSchemaRegistryCollectionDefinition
    {
    }

}
