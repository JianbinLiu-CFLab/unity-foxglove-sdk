// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Purpose: Replay subscriber fanout policy with failure isolation.

using System;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>Maintains an immutable subscriber snapshot and isolates handler failures.</summary>
    internal sealed class ReplaySubscriberFanoutState<TDelegate>
        where TDelegate : Delegate
    {
        private readonly object _gate = new object();
        private TDelegate _handlers;
        private TDelegate[] _snapshot = Array.Empty<TDelegate>();

        internal int Count
        {
            get
            {
                lock (_gate)
                    return _snapshot.Length;
            }
        }

        internal void Add(TDelegate subscriber)
        {
            if (subscriber == null)
                return;

            lock (_gate)
            {
                _handlers = (TDelegate)Delegate.Combine(_handlers, subscriber);
                _snapshot = CreateSnapshot(_handlers);
            }
        }

        internal void Remove(TDelegate subscriber)
        {
            if (subscriber == null)
                return;

            lock (_gate)
            {
                _handlers = (TDelegate)Delegate.Remove(_handlers, subscriber);
                _snapshot = CreateSnapshot(_handlers);
            }
        }

        private static TDelegate[] CreateSnapshot(TDelegate handlers)
        {
            if (handlers == null)
                return Array.Empty<TDelegate>();

            var invocationList = handlers.GetInvocationList();
            var snapshot = new TDelegate[invocationList.Length];
            for (var index = 0; index < invocationList.Length; index++)
                snapshot[index] = (TDelegate)invocationList[index];
            return snapshot;
        }

        internal void Invoke(Action<TDelegate> invoke, Action<Exception> onError)
        {
            if (invoke == null) throw new ArgumentNullException(nameof(invoke));
            if (onError == null) throw new ArgumentNullException(nameof(onError));

            TDelegate[] snapshot;
            lock (_gate)
                snapshot = _snapshot;

            foreach (var subscriber in snapshot)
            {
                try
                {
                    invoke(subscriber);
                }
                catch (Exception exception)
                {
                    onError(exception);
                }
            }
        }
    }
}
