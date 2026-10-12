// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity2Foxglove.Ros2Bridge;
using Unity2Foxglove.Ros2Bridge.Editor;
using Unity2Foxglove.Ros2Bridge.Schemas.Ros2Msg;
using Xunit;

namespace Unity.FoxgloveSDK.Tests.Unit.FoxRun
{
    /// <remarks>Phase192 source sections: GeneratedContractCompilation.cs, GenerationFixtures.cs, OracleAndFixtureModels.cs</remarks>
    public sealed partial class FoxRunRos2CustomCdrBehaviorTests
    {
        private const int MaximumPayloadBytes = 4 * 1024 * 1024;
        private const string Topic = "/phase184/custom-cdr";
        private static readonly Lazy<GeneratedContract> Contract =
            new Lazy<GeneratedContract>(CompileGeneratedContract);
        private static readonly Lazy<GeneratedContract> ArtifactContract =
            new Lazy<GeneratedContract>(
                () => CompileGeneratedContract(
                    CreateArtifactCustomMember(
                        FoxRunFlow.PublishAndSubscribe)));

    }
}
