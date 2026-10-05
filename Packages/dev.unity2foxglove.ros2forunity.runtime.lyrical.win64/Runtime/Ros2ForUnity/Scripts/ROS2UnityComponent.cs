// Copyright 2019-2021 Robotec.ai.
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
/// The principal MonoBehaviour class for handling ros2 nodes and executables.
/// Use this to create ros2 node, check ros2 status.
/// Spins and executes actions (e. g. clock, sensor publish triggers) in a dedicated thread.
/// Multiple component instances are expected to work because the underlying ROS2 layer reference-counts init/shutdown.
/// </summary>
[DisallowMultipleComponent]
public class ROS2UnityComponent : MonoBehaviour
{
    private static readonly object instancesMutex = new object();
    private static readonly HashSet<ROS2UnityComponent> instances = new HashSet<ROS2UnityComponent>();

    private ROS2ForUnity ros2forUnity;
    private List<ROS2Node> nodes;
    private List<INode> ros2csNodes; // For performance in spinning
    private List<Action> executableActions;
    private HashSet<Action> executableActionSet;
    private readonly List<Action> actionsSnapshot = new List<Action>();
    private readonly List<INode> nodesSnapshot = new List<INode>();
    private int collectionVersion = 0;
    private int snapshotVersion = -1;
    private bool initialized = false;
    private volatile bool executorStarted = false;
    private volatile bool quitting = false;
    private volatile bool cachedOk = false;
    private volatile bool runtimeShutdownRequested = false;
    private volatile bool shutdownRequested = false;
    private int shutdownInProgress = 0;
    private bool disposed = false;
    private Thread executorThread;
    private int lifecycleOwnerThreadId;
    private SynchronizationContext lifecycleSynchronizationContext;
    private int shutdownDispatchScheduled;
    private int interval = 2;  // Spinning / executor interval in ms
    private readonly object mutex = new object();
    private double spinTimeout = 0.0001;

    private void Awake()
    {
        int currentThreadId = Environment.CurrentManagedThreadId;
        int observedOwnerThreadId = Interlocked.CompareExchange(
            ref lifecycleOwnerThreadId,
            currentThreadId,
            0);
        if (observedOwnerThreadId != 0 && observedOwnerThreadId != currentThreadId)
        {
            throw new InvalidOperationException(
                "ROS2UnityComponent lifecycle owner was established on another thread.");
        }
        lifecycleSynchronizationContext = SynchronizationContext.Current;
        ROS2ForUnity.PrewarmUnityPaths();
    }

    /// <summary>
    /// Checks ROS2 availability. The first call must happen on Unity's main thread,
    /// or after Awake has prewarmed Unity API backed package paths.
    /// </summary>
    public bool Ok()
    {
        bool needsConstruct;
        lock (mutex)
        {
            if (shutdownRequested || disposed)
                return false;

            if (initialized && cachedOk && nodes != null && ros2forUnity != null)
                return true;

            needsConstruct = ros2forUnity == null;
        }

        if (needsConstruct)
        {
            try
            {
                EnsureNotExecutorThread();
                LazyConstruct();
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        lock (mutex)
        {
            if (disposed || nodes == null || ros2forUnity == null)
            {
                cachedOk = false;
                return false;
            }

            if (initialized && cachedOk)
                return true;

            cachedOk = ros2forUnity.Ok();
            return cachedOk;
        }
    }

    private void LazyConstruct()
    {
        lock (mutex)
        {
            ThrowIfDisposed();
            if (runtimeShutdownRequested)
            {
                throw new ObjectDisposedException(nameof(ROS2UnityComponent));
            }

            if (ros2forUnity != null)
                return;

            int currentThreadId = Environment.CurrentManagedThreadId;
            int observedOwnerThreadId = Interlocked.CompareExchange(
                ref lifecycleOwnerThreadId,
                currentThreadId,
                0);
            int ownerThreadId = observedOwnerThreadId == 0
                ? currentThreadId
                : observedOwnerThreadId;
            if (ownerThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "ROS2UnityComponent lifecycle construction must run on the lifecycle owner thread.");
            }
            if (lifecycleSynchronizationContext == null)
            {
                lifecycleSynchronizationContext = SynchronizationContext.Current;
            }

            ros2forUnity = new ROS2ForUnity();
            nodes = new List<ROS2Node>();
            ros2csNodes = new List<INode>();
            executableActions = new List<Action>();
            executableActionSet = new HashSet<Action>();

            lock (instancesMutex)
            {
                instances.Add(this);
            }
        }
    }

    public static bool StopAllExecutorsForRosShutdown()
    {
        bool allStopped = ROS2UnityCore.StopAllExecutorsForRosShutdown();
        List<ROS2UnityComponent> snapshot;
        lock (instancesMutex)
        {
            snapshot = new List<ROS2UnityComponent>(instances);
        }

        foreach (ROS2UnityComponent component in snapshot)
        {
            if (component == null)
                continue;

            if (!component.StopForRosShutdown())
                allStopped = false;
        }

        return allStopped;
    }

    void Start()
    {
        LazyConstruct();
        StartExecutor();
    }

    public ROS2Node CreateNode(string name)
    {
        EnsureNotExecutorThread();
        LazyConstruct();

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
                return node.IsDisposed;
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
        LazyConstruct();

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

    void FixedUpdate()
    {
        ROS2UnityCore.RetryPendingShutdownsOnCurrentThread();
        if (runtimeShutdownRequested && IsLifecycleOwnerThread())
        {
            StopForRosShutdown();
            ROS2ForUnity.RetryPendingShutdown();
            return;
        }

        if (executorStarted)
        {
            return;
        }

        // Start on the first fixed-timestep update so executor spin timing follows Unity physics cadence.
        StartExecutor();
    }

    private void StartExecutor()
    {
        Thread threadToStart = null;
        bool previousInitialized;
        bool previousExecutorStarted;
        bool previousQuitting;
        bool previousCachedOk;
        lock (mutex)
        {
            if (initialized || disposed || runtimeShutdownRequested || ros2forUnity == null || nodes == null)
            {
                return;
            }

            previousInitialized = initialized;
            previousExecutorStarted = executorStarted;
            previousQuitting = quitting;
            previousCachedOk = cachedOk;
            quitting = false;
            executorThread = new Thread(() => Tick());
            executorThread.IsBackground = true;
            executorThread.Name = "ROS2UnityComponent.Executor";
            initialized = true;
            executorStarted = true;
            threadToStart = executorThread;
        }
        try
        {
            threadToStart.Start();
        }
        catch
        {
            lock (mutex)
            {
                if (ReferenceEquals(executorThread, threadToStart))
                {
                    executorThread = null;
                    initialized = previousInitialized;
                    executorStarted = previousExecutorStarted;
                    quitting = previousQuitting;
                    cachedOk = previousCachedOk;
                }
            }
            throw;
        }
    }

    private bool StopForRosShutdown()
    {
        if (!IsLifecycleOwnerThread())
        {
            MarkRuntimeShutdownPendingExecutor();
            return false;
        }

        lock (mutex)
        {
            if (disposed)
                return true;
        }

        bool executorStopped = StopExecutor();
        if (!executorStopped)
        {
            MarkRuntimeShutdownPendingExecutor();
            Debug.LogError(
                "ROS2UnityComponent executor is still active during ROS shutdown; " +
                "native ownership remains active.");
            return false;
        }

        if (!DisposeNodes())
        {
            MarkRuntimeShutdownPendingExecutor();
            Debug.LogError(
                "ROS2UnityComponent could not dispose every node during ROS shutdown; " +
                "native ownership remains active for retry.");
            return false;
        }

        ROS2ForUnity instance;
        if (!TryDetachRuntimeState(true, out instance))
            return false;

        if (instance != null)
            instance.DestroyROS2ForUnity();
        return true;
    }

    private void MarkRuntimeShutdown()
    {
        lock (mutex)
        {
            ros2forUnity = null;
            cachedOk = false;
            runtimeShutdownRequested = true;
        }
    }

    private void MarkRuntimeShutdownPendingExecutor()
    {
        shutdownRequested = true;
        runtimeShutdownRequested = true;
        cachedOk = false;
        ScheduleShutdownRetry();
    }

    private void ScheduleShutdownRetry()
    {
        SynchronizationContext context = Volatile.Read(ref lifecycleSynchronizationContext);
        if (context == null || Interlocked.Exchange(ref shutdownDispatchScheduled, 1) != 0)
        {
            return;
        }

        try
        {
            context.Post(_ =>
            {
                Volatile.Write(ref shutdownDispatchScheduled, 0);
                if (IsLifecycleOwnerThread())
                {
                    if (runtimeShutdownRequested)
                        StopForRosShutdown();
                    ROS2ForUnity.RetryPendingShutdown();
                }
            }, null);
        }
        catch
        {
            Volatile.Write(ref shutdownDispatchScheduled, 0);
        }
    }

    private bool StopExecutor()
    {
        quitting = true;
        Thread threadToJoin = Volatile.Read(ref executorThread);

        if (threadToJoin == Thread.CurrentThread)
        {
            Debug.LogError(
                "ROS2UnityComponent cannot stop or dispose from its executor thread.");
            return false;
        }

        if (threadToJoin != null)
        {
            if (!threadToJoin.Join(TimeSpan.FromSeconds(2)))
            {
                Debug.LogWarning("ROS2UnityComponent executor thread did not stop within 2 seconds");
                return false;
            }
        }

        lock (mutex)
        {
            if (ReferenceEquals(executorThread, threadToJoin))
            {
                executorThread = null;
                initialized = false;
                executorStarted = false;
                cachedOk = false;
            }
        }

        return true;
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

    private void Shutdown()
    {
        EnsureNotExecutorThread();
        if (Interlocked.CompareExchange(ref shutdownInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            lock (mutex)
            {
                if (disposed)
                {
                    return;
                }

                shutdownRequested = true;
                executorStarted = false;
                cachedOk = false;
            }

            bool executorStopped = StopExecutor();
            if (!executorStopped)
            {
                MarkRuntimeShutdownPendingExecutor();
                Debug.LogError(
                    "ROS2UnityComponent executor thread timed out during shutdown; " +
                    "native ownership remains active until the executor stops.");
                QuarantineNodesAfterExecutorTimeout();
                return;
            }

            if (!DisposeNodes())
            {
                MarkRuntimeShutdownPendingExecutor();
                Debug.LogError(
                    "ROS2UnityComponent could not dispose every node; native ownership remains active for retry.");
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
        finally
        {
            Volatile.Write(ref shutdownInProgress, 0);
        }
    }

    private bool TryDetachRuntimeState(bool executorStopped, out ROS2ForUnity instance)
    {
        instance = null;
        if (!executorStopped)
        {
            Debug.LogError("ROS2UnityComponent executor is still active; native ownership remains active.");
            return false;
        }

        lock (mutex)
        {
            if (disposed)
            {
                return false;
            }

            disposed = true;
            shutdownRequested = true;
            initialized = false;
            executorStarted = false;
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
            snapshotVersion = -1;
        }

        lock (instancesMutex)
        {
            instances.Remove(this);
        }

        return true;
    }

    private bool IsLifecycleOwnerThread()
    {
        int ownerThread = Volatile.Read(ref lifecycleOwnerThreadId);
        return ownerThread != 0
            && Environment.CurrentManagedThreadId == ownerThread
            && !ReferenceEquals(Volatile.Read(ref executorThread), Thread.CurrentThread);
    }

    private void EnsureNotExecutorThread()
    {
        Thread thread = Volatile.Read(ref executorThread);
        int currentThreadId = Environment.CurrentManagedThreadId;
        int observedOwnerThreadId = Interlocked.CompareExchange(
            ref lifecycleOwnerThreadId,
            currentThreadId,
            0);
        int ownerThread = observedOwnerThreadId == 0
            ? currentThreadId
            : observedOwnerThreadId;
        if (ownerThread != currentThreadId
            || (thread != null && ReferenceEquals(thread, Thread.CurrentThread)))
        {
            throw new InvalidOperationException(
                "ROS2UnityComponent lifecycle operations must be requested from the lifecycle owner thread.");
        }
    }

    private void QuarantineNodesAfterExecutorTimeout()
    {
        if (!Monitor.TryEnter(mutex, TimeSpan.FromMilliseconds(250)))
        {
            Debug.LogError("ROS2UnityComponent could not acquire state lock after executor timeout; native ownership remains active.");
            return;
        }

        try
        {
            cachedOk = false;
        }
        finally
        {
            Monitor.Exit(mutex);
        }
    }

    private void ThrowIfDisposed()
    {
        if (shutdownRequested || disposed)
        {
            throw new ObjectDisposedException(nameof(ROS2UnityComponent));
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

    void OnApplicationQuit()
    {
        Shutdown();
    }

    void OnDestroy()
    {
        Shutdown();
    }
}

}  // namespace ROS2
