// Copyright 2019-2022 Robotec.ai.
// Modifications Copyright (c) 2026 Jianbin Liu.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading;
using ROS2;

namespace ROS2
{

    /// <summary>
    /// The principal class for handling ros2 nodes and executables.
    /// Use this to create ros2 node, check ros2 status.
    /// Spins and executes actions (e. g. clock, sensor publish triggers) in a dedicated thread.
    /// Multiple core instances are expected to work because the underlying ROS2 layer reference-counts init/shutdown.
    /// </summary>
    public class ROS2UnityCore : IDisposable
    {
        private static readonly object instancesMutex = new object();
        private static readonly HashSet<ROS2UnityCore> instances = new HashSet<ROS2UnityCore>();
        private ROS2ForUnity ros2forUnity;
        private List<ROS2Node> nodes;
        private List<INode> ros2csNodes; // For performance in spinning
        private List<Action> executableActions;
        private HashSet<Action> executableActionSet;
        private readonly List<Action> actionsSnapshot = new List<Action>();
        private readonly List<INode> nodesSnapshot = new List<INode>();
        private int collectionVersion = 0;
        private int snapshotVersion = -1;
        private volatile bool quitting = false;
        private volatile bool disposeRequested = false;
        private bool cachedOk = false;
        private bool disposed = false;
        private Thread executorThread;
        private int interval = 2;  // Spinning / executor interval in ms
        private readonly object mutex = new object();
        private readonly int lifecycleOwnerThreadId;
        private readonly SynchronizationContext lifecycleSynchronizationContext;
        private int shutdownDispatchScheduled;
        private int shutdownRetryPending;
        private double spinTimeout = 0.0001;

        public bool Ok()
        {
            lock (mutex)
            {
                if (disposeRequested || disposed || nodes == null || ros2forUnity == null)
                {
                    cachedOk = false;
                    return false;
                }

                if (cachedOk)
                    return true;

                cachedOk = ros2forUnity.Ok();
                return cachedOk;
            }
        }

        public ROS2UnityCore()
        {
            lifecycleOwnerThreadId = Environment.CurrentManagedThreadId;
            lifecycleSynchronizationContext = SynchronizationContext.Current;
            Thread threadToStart = null;
            lock (mutex)
            {
                ros2forUnity = new ROS2ForUnity();
                nodes = new List<ROS2Node>();
                ros2csNodes = new List<INode>();
                executableActions = new List<Action>();
                executableActionSet = new HashSet<Action>();

                executorThread = new Thread(() => Tick());
                executorThread.IsBackground = true;
                threadToStart = executorThread;
            }
            lock (instancesMutex)
            {
                instances.Add(this);
            }

            try
            {
                threadToStart.Start();
            }
            catch
            {
                ROS2ForUnity failedInstance;
                lock (mutex)
                {
                    failedInstance = ros2forUnity;
                    ros2forUnity = null;
                    cachedOk = false;
                }
                lock (instancesMutex)
                {
                    instances.Remove(this);
                }
                try
                {
                    failedInstance?.DestroyROS2ForUnity();
                }
                catch (Exception cleanupException)
                {
                    Debug.LogException(cleanupException);
                }
                throw;
            }
        }

        internal static bool StopAllExecutorsForRosShutdown()
        {
            List<ROS2UnityCore> snapshot;
            lock (instancesMutex)
            {
                snapshot = new List<ROS2UnityCore>(instances);
            }

            bool allStopped = true;
            foreach (ROS2UnityCore core in snapshot)
            {
                if (core == null || !core.RequestStopForRosShutdown())
                {
                    allStopped = false;
                }
            }

            return allStopped;
        }

        internal static bool RetryPendingShutdownsOnCurrentThread()
        {
            List<ROS2UnityCore> snapshot;
            lock (instancesMutex)
            {
                snapshot = new List<ROS2UnityCore>(instances);
            }

            bool allCompleted = true;
            foreach (ROS2UnityCore core in snapshot)
            {
                if (core == null || !core.HasPendingShutdownRetry)
                    continue;

                if (!core.RetryPendingShutdown())
                    allCompleted = false;
            }

            return allCompleted;
        }

        public ROS2Node CreateNode(string name)
        {
            EnsureNotExecutorThread();
            lock (mutex)
            {
                ThrowIfDisposed();
                foreach (ROS2Node n in nodes)
                {  // Assumed to be a rare operation on rather small (<1k) list
                    if (n.name == name)
                    {
                        throw new InvalidOperationException("Cannot create node " + name + ". A node with this name already exists!");
                    }
                }
                ROS2Node node = new ROS2Node(name);
                nodes.Add(node);
                ros2csNodes.Add(node.NativeNode);
                collectionVersion++;
                return node;
            }
        }

        public void RemoveNode(ROS2Node node)
        {
            TryRemoveNode(node, true);
        }

        public void DetachNode(ROS2Node node)
        {
            TryRemoveNode(node, false);
        }

        public bool TryRemoveNode(ROS2Node node, bool dispose = true)
        {
            if (dispose)
                EnsureNotExecutorThread();

            if (node == null)
            {
                return false;
            }

            int nodeIndex = -1;
            int nativeIndex = -1;
            lock (mutex)
            {
                if (nodes == null || ros2csNodes == null)
                {
                    return false;
                }

                nodeIndex = nodes.IndexOf(node);
                nativeIndex = ros2csNodes.IndexOf(node.NativeNode);
                if (nodeIndex < 0 && nativeIndex < 0)
                {
                    return node.IsDisposed;
                }

                nodes.Remove(node);
                ros2csNodes.Remove(node.NativeNode);
                collectionVersion++;
            }

            if (!dispose)
            {
                return true;
            }

            if (node.TryDispose())
            {
                return true;
            }

            lock (mutex)
            {
                if (nodes != null && !nodes.Contains(node))
                {
                    int insertAt = Math.Min(nodeIndex < 0 ? nodes.Count : nodeIndex, nodes.Count);
                    nodes.Insert(insertAt, node);
                }

                if (ros2csNodes != null && !ros2csNodes.Contains(node.NativeNode))
                {
                    int insertAt = Math.Min(nativeIndex < 0 ? ros2csNodes.Count : nativeIndex, ros2csNodes.Count);
                    ros2csNodes.Insert(insertAt, node.NativeNode);
                }

                collectionVersion++;
            }
            return false;
        }

        public void RemoveNode(ROS2Node node, bool dispose)
        {
            TryRemoveNode(node, dispose);
        }

        /// <summary>
        /// Works as a simple executor registration analogue. These functions will be called with each Tick()
        /// Actions need to take care of correct call resolution by checking in their body (TODO)
        /// Make sure actions are lightweight (TODO - separate out threads for spinning and executables?)
        /// </summary>
        public void RegisterExecutable(Action executable)
        {
            lock (mutex)
            {
                ThrowIfDisposed();
                if (executableActionSet.Add(executable))
                {
                    executableActions.Add(executable);
                    collectionVersion++;
                }
            }
        }

        public void UnregisterExecutable(Action executable)
        {
            lock (mutex)
            {
                if (executableActions != null)
                {
                    if (executableActionSet.Remove(executable))
                    {
                        executableActions.Remove(executable);
                        collectionVersion++;
                    }
                }
            }
        }

        /// <summary>
        /// "Executor" thread will tick all clocks and spin the node
        /// </summary>
        private void Tick()
        {
            while (!quitting)
            {
                bool hasSnapshot = false;

                lock (mutex)
                {
                    PruneDisposedNodesLocked();
                    if (!quitting && !disposed && ros2forUnity != null && nodes != null && ros2forUnity.Ok())
                    {
                        cachedOk = true;
                        if (snapshotVersion != collectionVersion)
                        {
                            actionsSnapshot.Clear();
                            actionsSnapshot.AddRange(executableActions);
                            nodesSnapshot.Clear();
                            nodesSnapshot.AddRange(ros2csNodes);
                            snapshotVersion = collectionVersion;
                        }
                        hasSnapshot = true;
                    }
                    else
                    {
                        cachedOk = false;
                    }
                }

                if (hasSnapshot)
                {
                    foreach (Action action in actionsSnapshot)
                    {
                        try
                        {
                            action();
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }

                    if (nodesSnapshot.Count > 0)
                    {
                        try
                        {
                            Ros2cs.SpinOnce(nodesSnapshot, spinTimeout);
                        }
                        catch (Exception e)
                        {
                            if (!quitting)
                            {
                                Debug.LogException(e);
                            }
                        }
                    }
                }
                Thread.Sleep(interval);
            }
        }

        private bool RequestStopForRosShutdown()
        {
            lock (mutex)
            {
                if (disposed)
                {
                    return true;
                }

                disposeRequested = true;
            }

            if (IsLifecycleOwnerThread())
            {
                return RetryPendingShutdown();
            }

            SynchronizationContext context = lifecycleSynchronizationContext;
            if (context == null)
            {
                Volatile.Write(ref shutdownRetryPending, 1);
                return false;
            }

            Volatile.Write(ref shutdownRetryPending, 1);
            if (Interlocked.Exchange(ref shutdownDispatchScheduled, 1) != 0)
            {
                return false;
            }

            try
            {
                context.Post(_ =>
                {
                    Volatile.Write(ref shutdownDispatchScheduled, 0);
                    if (IsLifecycleOwnerThread())
                    {
                        RetryPendingShutdown();
                        ROS2ForUnity.RetryPendingShutdown();
                    }
                    else
                    {
                        Volatile.Write(ref shutdownRetryPending, 1);
                    }
                }, null);
            }
            catch
            {
                Volatile.Write(ref shutdownDispatchScheduled, 0);
                Volatile.Write(ref shutdownRetryPending, 1);
                return false;
            }

            return false;
        }

        internal bool RetryPendingShutdown()
        {
            if (!IsLifecycleOwnerThread())
            {
                Volatile.Write(ref shutdownRetryPending, 1);
                return false;
            }

            bool completed = StopForRosShutdown();
            Volatile.Write(ref shutdownRetryPending, completed ? 0 : 1);
            return completed;
        }

        private bool HasPendingShutdownRetry =>
            Volatile.Read(ref shutdownRetryPending) != 0;

        private void ScheduleShutdownRetry()
        {
            Volatile.Write(ref shutdownRetryPending, 1);
            SynchronizationContext context = lifecycleSynchronizationContext;
            if (context == null || Interlocked.Exchange(ref shutdownDispatchScheduled, 1) != 0)
                return;

            try
            {
                context.Post(
                    _ =>
                    {
                        Volatile.Write(ref shutdownDispatchScheduled, 0);
                        if (disposeRequested && IsLifecycleOwnerThread())
                        {
                            RetryPendingShutdown();
                            ROS2ForUnity.RetryPendingShutdown();
                        }
                        else if (disposeRequested)
                        {
                            Volatile.Write(ref shutdownRetryPending, 1);
                        }
                    },
                    null);
            }
            catch
            {
                Volatile.Write(ref shutdownDispatchScheduled, 0);
                Volatile.Write(ref shutdownRetryPending, 1);
            }
        }

        private bool StopForRosShutdown()
        {
            if (!IsLifecycleOwnerThread())
            {
                return false;
            }

            lock (mutex)
            {
                if (disposed)
                {
                    Volatile.Write(ref shutdownRetryPending, 0);
                    return true;
                }
                disposeRequested = true;
            }

            bool executorStopped = StopExecutor();
            if (!executorStopped || !DisposeNodes())
            {
                ScheduleShutdownRetry();
                return false;
            }

            ROS2ForUnity instance = null;
            if (!TryDetachRuntimeState(executorStopped, out instance))
            {
                ScheduleShutdownRetry();
                return false;
            }

            if (instance != null)
            {
                instance.DestroyROS2ForUnity();
            }

            Volatile.Write(ref shutdownRetryPending, 0);
            return true;
        }

        /// <summary>
        /// Compatibility alias for older callers; new code should call Dispose().
        /// </summary>
        public void DestroyNow()
        {
            Dispose();
        }

        public void Dispose()
        {
            EnsureNotExecutorThread();
            disposeRequested = true;
            bool executorStopped = StopExecutor();
            if (!executorStopped)
            {
                Debug.LogError(
                    "ROS2UnityCore executor thread timed out during dispose; " +
                    "native ownership remains active until the executor stops.");
                QuarantineNodesAfterExecutorTimeout();
                ScheduleShutdownRetry();
                return;
            }

            if (!DisposeNodes())
            {
                Debug.LogError(
                    "ROS2UnityCore could not dispose every node; native ownership remains active for retry.");
                ScheduleShutdownRetry();
                return;
            }

            ROS2ForUnity instance = null;
            if (!TryDetachRuntimeState(executorStopped, out instance))
            {
                return;
            }

            if (instance != null)
            {
                instance.DestroyROS2ForUnity();
            }
        }

        private bool StopExecutor()
        {
            quitting = true;
            Thread threadToJoin = Volatile.Read(ref executorThread);

            if (threadToJoin == Thread.CurrentThread)
            {
                Debug.LogError(
                    "ROS2UnityCore cannot stop or dispose from its executor thread.");
                return false;
            }

            if (threadToJoin != null)
            {
                if (!threadToJoin.Join(TimeSpan.FromSeconds(2)))
                {
                    Debug.LogWarning("ROS2UnityCore executor thread did not stop within 2 seconds");
                    return false;
                }
            }

            lock (mutex)
            {
                if (ReferenceEquals(executorThread, threadToJoin))
                {
                    executorThread = null;
                }
            }

            return true;
        }

        private bool TryDetachRuntimeState(bool executorStopped, out ROS2ForUnity instance)
        {
            instance = null;
            var lockTaken = false;
            if (!executorStopped && !Monitor.TryEnter(mutex, TimeSpan.FromMilliseconds(250)))
            {
                Debug.LogError("ROS2UnityCore could not acquire state lock after executor timeout; ROS2 lifecycle owner remains active.");
                return false;
            }

            try
            {
                if (executorStopped)
                {
                    Monitor.Enter(mutex, ref lockTaken);
                }
                else
                {
                    lockTaken = true;
                }

                if (disposed)
                {
                    return false;
                }

                disposed = true;
                cachedOk = false;
                instance = ros2forUnity;
                ros2forUnity = null;
                executableActions = null;
                executableActionSet = null;
                nodes = null;
                ros2csNodes = null;
                actionsSnapshot.Clear();
                nodesSnapshot.Clear();
                collectionVersion++;
                snapshotVersion = collectionVersion;
                lock (instancesMutex)
                {
                    instances.Remove(this);
                }

                return true;
            }
            finally
            {
                if (lockTaken)
                    Monitor.Exit(mutex);
            }
        }

        private bool DisposeNodes()
        {
            List<ROS2Node> nodesToDispose = null;
            lock (mutex)
            {
                if (nodes != null)
                {
                    nodesToDispose = new List<ROS2Node>(nodes);
                }
            }

            if (nodesToDispose == null)
            {
                return true;
            }

            bool allDisposed = true;
            foreach (ROS2Node node in nodesToDispose)
            {
                var nativeNode = node.NativeNode;
                bool disposedNode;
                try
                {
                    disposedNode = node.TryDispose();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    disposedNode = false;
                }

                if (!disposedNode)
                {
                    allDisposed = false;
                    continue;
                }

                lock (mutex)
                {
                    if (nodes != null && nodes.Remove(node))
                    {
                        ros2csNodes.Remove(nativeNode);
                        collectionVersion++;
                    }
                }
            }

            return allDisposed;
        }

        private bool IsLifecycleOwnerThread()
        {
            Thread thread = Volatile.Read(ref executorThread);
            return Environment.CurrentManagedThreadId == lifecycleOwnerThreadId
                && (thread == null || !ReferenceEquals(thread, Thread.CurrentThread));
        }

        private void EnsureNotExecutorThread()
        {
            Thread thread = Volatile.Read(ref executorThread);
            if (Environment.CurrentManagedThreadId != lifecycleOwnerThreadId
                || (thread != null && ReferenceEquals(thread, Thread.CurrentThread)))
            {
                throw new InvalidOperationException(
                    "ROS2UnityCore lifecycle operations must be requested from the lifecycle owner thread.");
            }
        }

        private void QuarantineNodesAfterExecutorTimeout()
        {
            if (!Monitor.TryEnter(mutex, TimeSpan.FromMilliseconds(250)))
            {
                Debug.LogError("ROS2UnityCore could not acquire node lock after executor timeout; nodes remain quarantined by the stuck executor.");
                return;
            }

            try
            {
                if (nodes != null)
                {
                    cachedOk = false;
                }
            }
            finally
            {
                Monitor.Exit(mutex);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposeRequested || disposed)
            {
                throw new ObjectDisposedException(nameof(ROS2UnityCore));
            }
        }

        private void PruneDisposedNodesLocked()
        {
            if (nodes == null || ros2csNodes == null)
            {
                return;
            }

            for (int index = nodes.Count - 1; index >= 0; index--)
            {
                ROS2Node candidate = nodes[index];
                if (!candidate.IsDisposed)
                {
                    continue;
                }

                nodes.RemoveAt(index);
                if (index < ros2csNodes.Count && ReferenceEquals(ros2csNodes[index], candidate.NativeNode))
                {
                    ros2csNodes.RemoveAt(index);
                }
                else
                {
                    ros2csNodes.Remove(candidate.NativeNode);
                }
                collectionVersion++;
            }
        }
    }

}  // namespace ROS2
