// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Shared/FoxRunEditorRegistry
// Purpose: Domain-reload-scoped ordered/conflicted Editor definition storage.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.FoxgloveSDK.Editor
{
    internal enum FoxRunEditorDefinitionRegistrationResult
    {
        Added = 1,
        AlreadyRegistered = 2,
        Conflict = 3
    }

    internal readonly struct FoxRunEditorDefinition<T>
        where T : class
    {
        internal FoxRunEditorDefinition(
            string id,
            int order,
            T definition)
        {
            Id = id;
            Order = order;
            Definition = definition;
        }

        internal string Id { get; }
        internal int Order { get; }
        internal T Definition { get; }
    }

    /// <summary>
    /// Freezes definition identity/order at registration and excludes every
    /// conflicted ID from selectable snapshots until the next domain reload.
    /// </summary>
    internal sealed class FoxRunEditorDefinitionRegistry<T>
        where T : class
    {
        private readonly object _gate = new object();
        private readonly Func<T, string> _idSelector;
        private readonly Func<T, int> _orderSelector;
        private readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);

        internal FoxRunEditorDefinitionRegistry(
            Func<T, string> idSelector,
            Func<T, int> orderSelector)
        {
            _idSelector = idSelector
                          ?? throw new ArgumentNullException(
                              nameof(idSelector));
            _orderSelector = orderSelector
                             ?? throw new ArgumentNullException(
                                 nameof(orderSelector));
        }

        internal FoxRunEditorDefinitionRegistrationResult Register(
            T definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            var id = _idSelector(definition);
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "Editor definition ID cannot be empty.",
                    nameof(definition));
            }
            var order = _orderSelector(definition);

            lock (_gate)
            {
                if (!_entries.TryGetValue(id, out var entry))
                {
                    entry = new Entry(id, order, definition);
                    _entries.Add(id, entry);
                    return FoxRunEditorDefinitionRegistrationResult.Added;
                }

                if (entry.Definitions.Any(candidate =>
                        ReferenceEquals(candidate, definition)))
                {
                    return FoxRunEditorDefinitionRegistrationResult
                        .AlreadyRegistered;
                }

                entry.Definitions.Add(definition);
                return FoxRunEditorDefinitionRegistrationResult.Conflict;
            }
        }

        internal bool IsConflicted(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;
            lock (_gate)
            {
                return _entries.TryGetValue(id, out var entry)
                       && entry.Definitions.Count != 1;
            }
        }

        internal IReadOnlyList<T> Capture()
            => CaptureEntries()
                .Select(entry => entry.Definition)
                .ToArray();

        internal IReadOnlyList<FoxRunEditorDefinition<T>>
            CaptureEntries()
        {
            lock (_gate)
            {
                return _entries.Values
                    .Where(entry => entry.Definitions.Count == 1)
                    .OrderBy(entry => entry.Order)
                    .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                    .Select(entry =>
                        new FoxRunEditorDefinition<T>(
                            entry.Id,
                            entry.Order,
                            entry.Definitions[0]))
                    .ToArray();
            }
        }

        private sealed class Entry
        {
            internal Entry(string id, int order, T definition)
            {
                Id = id;
                Order = order;
                Definitions = new List<T> { definition };
            }

            internal string Id { get; }
            internal int Order { get; }
            internal List<T> Definitions { get; }
        }
    }
}
