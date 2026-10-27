// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Shared source inspection helpers for runtime validation phases.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.FoxgloveSDK.Tests
{
    /// <remarks>Phase192 source sections: RepositoryReaders.cs, SplitReaders.cs, PythonReaders.cs, SourceMethodContains.cs, ReadMediaFoundationH264EncoderSidecarSources.cs</remarks>
    internal static partial class PhaseValidationSourceHelpers
    {
        private const string FoxgloveRuntimeTrailingGateMarker =
            "    /// <summary>Shared executable generation predicate for main-thread client events.";
        private static readonly string[] FoxgloveRuntimeSourceFileNames =
        {
            "FoxgloveRuntime.cs",
            "FoxgloveRuntime.Lifecycle.cs",
            "FoxgloveRuntime.Publishing.cs",
            "FoxgloveRuntime.RecordingAndReplay.cs",
            "FoxgloveRuntime.RuntimeAndSchema.cs",
            "FoxgloveRuntime.ReplaySuppression.cs",
            "FoxgloveRuntime.SchemaRegistration.cs",
        };
        private static readonly string[] FoxgloveSessionSourceFileNames =
        {
            "FoxgloveSession.cs",
            "Decomposed/FoxgloveSession/FoxgloveSession.Lifecycle.cs",
            "Decomposed/FoxgloveSession/FoxgloveSession.Channels.cs",
            "Decomposed/FoxgloveSession/FoxgloveSession.SchemaChannels.cs",
            "Decomposed/FoxgloveSession/FoxgloveSession.Publishing.cs",
            "Decomposed/FoxgloveSession/FoxgloveSession.ServiceRegistration.cs",
            "Decomposed/FoxgloveSession/FoxgloveSession.TimeAndEncoding.cs",
            "Decomposed/FoxgloveSession/FoxgloveSession.TransportEvents.cs",
        };
        private static readonly string[] ManagedWsBackendSourceFileNames =
        {
            "ManagedWsBackend.cs",
            "Decomposed/ManagedWsBackend/ManagedWsBackend.AdmissionAndHandshake.cs",
            "Decomposed/ManagedWsBackend/ManagedWsBackend.LifecycleAndMessaging.cs",
            "Decomposed/ManagedWsBackend/ManagedWsBackend.PublicationAndShutdown.cs",
            "Decomposed/ManagedWsBackend/ManagedWsBackend.ReceiveAndDisconnect.cs",
        };
        private static readonly string[] Ros2BridgeRuntimeSourceFileNames =
        {
            "Ros2BridgeRuntime.cs",
            "Decomposed/Ros2BridgeWorkerLease/RuntimeControl.cs",
            "Decomposed/Ros2BridgeWorkerLease/WorkerLoop.cs",
            "Decomposed/Ros2BridgeWorkerLease/ConnectionHandshake.cs",
            "Decomposed/Ros2BridgeWorkerLease/PreparationFlow.cs",
            "Decomposed/Ros2BridgeWorkerLease/Retirement.cs",
            "Decomposed/Ros2BridgeWorkerLease/DisposalAndDiagnostics.cs",
            "Decomposed/Ros2BridgeWorkerLease/ValueTypes.cs",
        };
        private static readonly string[] Ros2CustomCdrEmitterSourceFileNames =
        {
            "Ros2CustomCdrEmitter.cs",
            "Decomposed/FoxRunBridgeSourceEmitter/DispatchEmission.cs",
            "Decomposed/FoxRunBridgeSourceEmitter/CdrReaders.cs",
            "Decomposed/FoxRunBridgeSourceEmitter/CdrWriters.cs",
            "Decomposed/FoxRunBridgeSourceEmitter/ShapeProjection.cs",
            "Decomposed/FoxRunBridgeSourceEmitter/NamingAndValidation.cs",
            "Decomposed/FoxRunBridgeSourceEmitter/DataModels.cs",
        };
        private static readonly HashSet<string> PythonSectionKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "False", "None", "True", "and", "as", "assert", "async", "await", "break",
            "class", "continue", "def", "del", "elif", "else", "except", "finally", "for", "from",
            "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass",
            "raise", "return", "try", "while", "with", "yield",
        };

    }
}
