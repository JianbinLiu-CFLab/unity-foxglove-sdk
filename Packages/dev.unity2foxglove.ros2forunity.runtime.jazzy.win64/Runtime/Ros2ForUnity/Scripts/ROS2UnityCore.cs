// Copyright 2019-2022 Robotec.ai.
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
    /// The principal class for handling ros2 nodes and executables.
    /// Use this to create ros2 node, check ros2 status.
    /// Spins and executes actions (e. g. clock, sensor publish triggers) in a dedicated thread
    /// TODO: this is meant to be used as a one-of (a singleton). Enforce. However, things should work
    /// anyway with more than one since the underlying library can handle multiple init and shutdown calls,
    /// and does node name uniqueness check independently.
    /// </summary>
    public class ROS2UnityCore : IDisposable
    {
        private ROS2ForUnity ros2forUnity;
        private List<ROS2Node> nodes;
        private List<INode> ros2csNodes; // For performance in spinning
        private List<Action> executableActions;
        private volatile bool quitting = false;
        private Thread spinThread;
        private int interval = 2;  // Spinning / executor interval in ms
        private object mutex = new object();
        private double spinTimeout = 0.0001;

        public bool Ok()
        {
            lock (mutex)
            {
                if (quitting || ros2forUnity == null)
                    return false;
                return (nodes != null && ros2forUnity.Ok());
            }
        }

        public ROS2UnityCore()
        {
            lock (mutex)
            {
                ros2forUnity = new ROS2ForUnity();
                nodes = new List<ROS2Node>();
                ros2csNodes = new List<INode>();
                executableActions = new List<Action>();

                spinThread = new Thread(() => Tick())
                {
                    IsBackground = true,
                    Name = "ROS2 For Unity core spin"
                };
                spinThread.Start();
            }
        }

        public ROS2Node CreateNode(string name)
        {
            lock (mutex)
            {
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
            lock (mutex)
            {
                ros2csNodes.Remove(node.node);
                nodes.Remove(node);
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
            lock (mutex)
            {
                executableActions.Add(executable);
            }
        }

        public void UnregisterExecutable(Action executable)
        {
            lock (mutex)
            {
                executableActions.Remove(executable);
            }
        }

        /// <summary>
        /// "Executor" thread will tick all clocks and spin the node
        /// </summary>
        private void Tick()
        {
            while (!quitting)
            {
                if (Ok())
                {
                    lock (mutex)
                    {
                        foreach (Action action in executableActions)
                        {
                            try
                            {
                                action();
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning("[ROS2UnityCore] executable action failed: " + ex.Message);
                            }
                        }
                        Ros2cs.SpinOnce(ros2csNodes, spinTimeout);
                    }
                }
                Thread.Sleep(interval);
            }
        }

        public void DestroyNow()
        {
            Dispose();
        }

        public void Dispose()
        {
            Thread threadToJoin;
            List<ROS2Node> nodesToDispose;
            ROS2ForUnity ros2ToDestroy;
            lock (mutex)
            {
                if (quitting)
                    return;
                quitting = true;
                threadToJoin = spinThread;
                spinThread = null;
                nodesToDispose = nodes != null ? new List<ROS2Node>(nodes) : new List<ROS2Node>();
                nodes?.Clear();
                ros2csNodes?.Clear();
                executableActions?.Clear();
                ros2ToDestroy = ros2forUnity;
                ros2forUnity = null;
            }

            if (threadToJoin != null
                && threadToJoin.IsAlive
                && Thread.CurrentThread != threadToJoin
                && !threadToJoin.Join(1000))
            {
                Debug.LogWarning("[ROS2UnityCore] spin thread did not stop within 1s.");
            }

            foreach (var node in nodesToDispose)
                node.Dispose();

            ros2ToDestroy?.DestroyROS2ForUnity();
        }

        ~ROS2UnityCore()
        {
            quitting = true;
        }
    }

}  // namespace ROS2
