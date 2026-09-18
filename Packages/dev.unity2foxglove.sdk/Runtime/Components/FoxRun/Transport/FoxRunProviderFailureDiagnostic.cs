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

        public static IReadOnlyList<FoxRunProviderFailureDiagnostic> Snapshot()
        {
            lock (Gate)
                return Values.ToArray();
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
                if (Values.Count == 32)
                    Values.RemoveAt(0);
                Values.Add(diagnostic);
            }
        }

        public static void Clear()
        {
            lock (Gate)
                Values.Clear();
        }
    }
}
