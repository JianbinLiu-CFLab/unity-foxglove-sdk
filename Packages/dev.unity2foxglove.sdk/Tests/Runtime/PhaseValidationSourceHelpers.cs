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
    /// <remarks>Phase192 source sections: RepositoryReaders.cs, SplitReaders.cs, SyntaxQueries.cs</remarks>
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
            "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.QueueAndStats.cs",
            "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.WorkerLifecycle.cs",
            "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.ConnectionAndPreparation.cs",
            "Decomposed/Ros2BridgeRuntime/Ros2BridgeRuntime.RetirementAndNested.cs",
        };
        private static readonly string[] Ros2CustomCdrEmitterSourceFileNames =
        {
            "Ros2CustomCdrEmitter.cs",
            "Decomposed/Ros2CustomCdrEmitter/Generation.cs",
            "Decomposed/Ros2CustomCdrEmitter/Readers.cs",
            "Decomposed/Ros2CustomCdrEmitter/Writers.cs",
            "Decomposed/Ros2CustomCdrEmitter/ValidationAndProjection.cs",
            "Decomposed/Ros2CustomCdrEmitter/NamingAndUtilities.cs",
            "Decomposed/Ros2CustomCdrEmitter/Shapes.cs",
        };
        private static readonly HashSet<string> PythonSectionKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "False", "None", "True", "and", "as", "assert", "async", "await", "break",
            "class", "continue", "def", "del", "elif", "else", "except", "finally", "for", "from",
            "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or", "pass",
            "raise", "return", "try", "while", "with", "yield",
        };



















































        /// <summary>
        /// Resolves one method by identifier and a stable body marker.  This is
        /// useful for overloads whose tuple-heavy signatures are legitimately
        /// reformatted by different C# writers; the marker still anchors the
        /// intended implementation body and the Roslyn span keeps extraction
        /// syntax-aware. Resolution is fail-closed: callers receive an
        /// exception unless exactly one declaration matches.
        /// </summary>












    }
}
