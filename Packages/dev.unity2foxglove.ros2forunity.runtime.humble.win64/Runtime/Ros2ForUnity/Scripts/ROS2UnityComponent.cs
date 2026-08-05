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
    private bool runtimeShutdownRequested = false;
    private bool shutdownRequested = false;
    private bool disposed = false;
    private Thread executorThread;
    private int interval = 2;  // Spinning / executor interval in ms
    private readonly object mutex = new object();
    private double spinTimeout = 0.0001;

    public bool Ok()
    {
        lock (mutex)
        {
            if (shutdownRequested || disposed)
                return false;

            if (initialized && cachedOk && nodes != null && ros2forUnity != null)
                return true;

            if (ros2forUnity == null)
                LazyConstruct();

            if (nodes == null)
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
            if (ros2forUnity != null)
                return;

            ros2forUnity = new ROS2ForUnity();
            runtimeShutdownRequested = false;
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

    public static void StopAllExecutorsForRosShutdown()
    {
        List<ROS2UnityComponent> snapshot;
        lock (instancesMutex)
        {
            snapshot = new List<ROS2UnityComponent>(instances);
        }

        foreach (ROS2UnityComponent component in snapshot)
        {
            if (component != null)
            {
                if (!component.StopExecutor())
                {
                    Debug.LogError(
                        "ROS2UnityComponent executor is still active during ROS shutdown; " +
                        "native ownership remains active.");
                    continue;
                }

                component.MarkRuntimeShutdown();
            }
        }
    }

    void Start()
    {
        LazyConstruct();
    }

    public ROS2Node CreateNode(string name)
    {
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
        RemoveNode(node, true);
    }

    public void DetachNode(ROS2Node node)
    {
        RemoveNode(node, false);
    }

    public void RemoveNode(ROS2Node node, bool dispose)
    {
        if (node == null)
        {
            return;
        }

        bool removed = false;
        bool removedRos2csNode = false;
        lock (mutex)
        {
            if (nodes != null)
            {
                removed = nodes.Remove(node);
                removedRos2csNode = ros2csNodes.Remove(node.NativeNode);
                if (removed || removedRos2csNode)
                {
                    collectionVersion++;
                }
            }
        }

        if (dispose && (removed || removedRos2csNode))
        {
            node.Dispose();
        }
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
        lock (mutex)
        {
            if (initialized || disposed || runtimeShutdownRequested || ros2forUnity == null || nodes == null)
            {
                return;
            }

            quitting = false;
            executorThread = new Thread(() => Tick());
            executorThread.IsBackground = true;
            initialized = true;
            executorStarted = true;
            threadToStart = executorThread;
        }
        threadToStart.Start();
    }

    private void MarkRuntimeShutdown()
    {
        ROS2ForUnity runtimeToDestroy;
        lock (mutex)
        {
            runtimeToDestroy = ros2forUnity;
            ros2forUnity = null;
            cachedOk = false;
            runtimeShutdownRequested = true;
        }

        if (runtimeToDestroy != null)
        {
            runtimeToDestroy.DestroyROS2ForUnity();
        }
    }

    private bool StopExecutor()
    {
        Thread threadToJoin = null;
        lock (mutex)
        {
            quitting = true;
            threadToJoin = executorThread;
        }

        if (threadToJoin != null && threadToJoin != Thread.CurrentThread)
        {
            if (!threadToJoin.Join(TimeSpan.FromSeconds(2)))
            {
                Debug.LogWarning("ROS2UnityComponent executor thread did not stop within 2 seconds");
                return false;
            }
        }

        lock (mutex)
        {
            executorThread = null;
            initialized = false;
            executorStarted = false;
            cachedOk = false;
        }

        return true;
    }

    private void DisposeNodes()
    {
        List<ROS2Node> nodesToDispose = null;
        lock (mutex)
        {
            if (nodes != null)
            {
                nodesToDispose = new List<ROS2Node>(nodes);
                nodes.Clear();
                ros2csNodes.Clear();
                collectionVersion++;
            }
        }

        if (nodesToDispose == null)
        {
            return;
        }

        foreach (ROS2Node node in nodesToDispose)
        {
            try
            {
                node.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    private void Shutdown()
    {
        lock (mutex)
        {
            if (disposed || shutdownRequested)
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
            Debug.LogError(
                "ROS2UnityComponent executor thread timed out during shutdown; " +
                "native ownership remains active until the executor stops.");
            QuarantineNodesAfterExecutorTimeout();
            return;
        }

        DisposeNodes();

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
