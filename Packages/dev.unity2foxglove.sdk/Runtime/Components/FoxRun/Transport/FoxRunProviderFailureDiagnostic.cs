// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun/Transport
// Purpose: Structured, redacted Provider failure diagnostics.

using System;
using System.Collections.Generic;

namespace Unity.FoxgloveSDK.Components
{
    public sealed class FoxRunProviderFailureDiagnostic
    {
        public string ProviderId { get; }
        public string Operation { get; }
        public string ExceptionType { get; }
        public string Stack { get; }
        public ulong Generation { get; }
        public string Topic { get; }

        public FoxRunProviderFailureDiagnostic(
            string providerId,
            string operation,
            Exception exception,
            ulong generation,
            string topic)
        {
            ProviderId = providerId ?? string.Empty;
            Operation = operation ?? string.Empty;
            ExceptionType = exception?.GetType().FullName ?? typeof(Exception).FullName;
            Stack = exception?.StackTrace ?? string.Empty;
            Generation = generation;
            Topic = topic ?? string.Empty;
        }
    }

    public static class FoxRunProviderDiagnostics
    {
        private static readonly object Gate = new object();
        private static readonly List<FoxRunProviderFailureDiagnostic> Values = new List<FoxRunProviderFailureDiagnostic>();
        private static ulong _currentGeneration;
        private static bool _hasCurrentGeneration;

        public static IReadOnlyList<FoxRunProviderFailureDiagnostic> Snapshot()
        {
            lock (Gate)
                return SnapshotLocked(_hasCurrentGeneration
                    ? _currentGeneration
                    : (ulong?)null);
        }

        public static IReadOnlyList<FoxRunProviderFailureDiagnostic> Snapshot(
            ulong generation)
        {
            lock (Gate)
                return SnapshotLocked(generation);
        }

        public static void BeginGeneration(ulong generation)
        {
            lock (Gate)
            {
                _currentGeneration = generation;
                _hasCurrentGeneration = true;
                for (var index = Values.Count - 1; index >= 0; index--)
                {
                    if (Values[index].Generation != generation)
                        Values.RemoveAt(index);
                }
            }
        }

        public static void Record(
            string providerId,
            string operation,
            Exception exception,
            ulong generation,
            string topic)
        {
            if (exception == null)
                return;
            var diagnostic = new FoxRunProviderFailureDiagnostic(
                providerId,
                operation,
                exception,
                generation,
                topic);
            lock (Gate)
            {
                if (!_hasCurrentGeneration)
                {
                    _currentGeneration = generation;
                    _hasCurrentGeneration = true;
                }
                if (_currentGeneration != generation)
                    return;
                if (Values.Count == 32)
                    Values.RemoveAt(0);
                Values.Add(diagnostic);
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                Values.Clear();
                _currentGeneration = 0;
                _hasCurrentGeneration = false;
            }
        }

        private static IReadOnlyList<FoxRunProviderFailureDiagnostic> SnapshotLocked(
            ulong? generation)
        {
            if (!generation.HasValue)
                return Values.ToArray();

            var snapshot = new List<FoxRunProviderFailureDiagnostic>();
            for (var index = 0; index < Values.Count; index++)
            {
                if (Values[index].Generation == generation.Value)
                    snapshot.Add(Values[index]);
            }
            return snapshot.ToArray();
        }
    }
}
