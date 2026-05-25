// Copyright 2019-2021 Robotec.ai.
// Modifications Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
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
/// Spins and executes actions (e. g. clock, sensor publish triggers) in a dedicated thread
/// TODO: this is meant to be used as a one-of (a singleton). Enforce. However, things should work
/// anyway with more than one since the underlying library can handle multiple init and shutdown calls,
/// and does node name uniqueness check independently.
/// </summary>
public class ROS2UnityComponent : MonoBehaviour
{
    private ROS2ForUnity ros2forUnity;
    private List<ROS2Node> nodes;
    private List<INode> ros2csNodes; // For performance in spinning
    private List<Action> executableActions;
    private HashSet<Action> executableActionSet;
    private bool initialized = false;
    private volatile bool quitting = false;
    private bool disposed = false;
    private Thread executorThread;
    private int interval = 2;  // Spinning / executor interval in ms
    private object mutex = new object();
    private double spinTimeout = 0.0001;

    public bool Ok()
    {
        lock (mutex)
        {
            if (disposed)
                return false;
            if (ros2forUnity == null)
                LazyConstruct();
            return (nodes != null && ros2forUnity.Ok());
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
            nodes = new List<ROS2Node>();
            ros2csNodes = new List<INode>();
            executableActions = new List<Action>();
            executableActionSet = new HashSet<Action>();
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
            ros2csNodes.Add(node.node);
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
        lock (mutex)
        {
            if (nodes != null)
            {
                ros2csNodes.Remove(node.node);
                removed = nodes.Remove(node);
            }
        }

        if (dispose && removed)
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
            }
        }
    }

    public void UnregisterExecutable(Action executable)
    {
        lock (mutex)
        {
            if (executableActions != null)
            {
                if (executableActionSet != null)
                {
                    executableActionSet.Remove(executable);
                }
                executableActions.Remove(executable);
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
            lock (mutex)
            {
                if (!quitting && ros2forUnity != null && nodes != null && ros2forUnity.Ok())
                {
                    foreach (Action action in executableActions)
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

                    if (ros2csNodes.Count > 0)
                    {
                        try
                        {
                            Ros2cs.SpinOnce(ros2csNodes, spinTimeout);
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
            }
            Thread.Sleep(interval);
        }
    }

    void FixedUpdate()
    {
        StartExecutor();
    }

    private void StartExecutor()
    {
        lock (mutex)
        {
            if (initialized || disposed)
            {
                return;
            }

            quitting = false;
            executorThread = new Thread(() => Tick());
            executorThread.IsBackground = true;
            initialized = true;
            executorThread.Start();
        }
    }

    private bool StopExecutor()
    {
        quitting = true;
        Thread threadToJoin = Volatile.Read(ref executorThread);

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
            if (ReferenceEquals(executorThread, threadToJoin))
            {
                executorThread = null;
                initialized = false;
            }
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
        bool executorStopped = StopExecutor();
        if (executorStopped)
        {
            DisposeNodes();
        }
        else
        {
            Debug.LogError(
                "ROS2UnityComponent executor thread timed out during shutdown; " +
                "continuing best-effort lifecycle cleanup with nodes quarantined.");
            QuarantineNodesAfterExecutorTimeout();
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

    private bool TryDetachRuntimeState(bool executorStopped, out ROS2ForUnity instance)
    {
        instance = null;
        if (!executorStopped && !Monitor.TryEnter(mutex, TimeSpan.FromMilliseconds(250)))
        {
            Debug.LogError("ROS2UnityComponent could not acquire state lock after executor timeout; ROS2 lifecycle owner remains active.");
            return false;
        }

        try
        {
            if (executorStopped)
            {
                Monitor.Enter(mutex);
            }

            if (disposed)
            {
                return false;
            }

            disposed = true;
            initialized = false;
            instance = ros2forUnity;
            ros2forUnity = null;
            executableActions = null;
            executableActionSet = null;
            nodes = null;
            ros2csNodes = null;
            return true;
        }
        finally
        {
            Monitor.Exit(mutex);
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(ROS2UnityComponent));
        }
    }

    private void QuarantineNodesAfterExecutorTimeout()
    {
        if (!Monitor.TryEnter(mutex, TimeSpan.FromMilliseconds(250)))
        {
            Debug.LogError("ROS2UnityComponent could not acquire node lock after executor timeout; nodes remain quarantined by the stuck executor.");
            return;
        }

        try
        {
            if (nodes != null)
            {
                nodes.Clear();
                ros2csNodes.Clear();
            }
        }
        finally
        {
            Monitor.Exit(mutex);
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
