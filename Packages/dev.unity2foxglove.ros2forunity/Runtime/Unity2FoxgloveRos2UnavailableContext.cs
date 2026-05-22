// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity
// Purpose: Provides a no-op ROS2 facade implementation when R2FU is unavailable.

using System;

namespace Unity2Foxglove.Ros2ForUnity
{
    /// <summary>
    /// No-op facade used when the ROS2 For Unity runtime is not bundled or active.
    /// </summary>
    public sealed class Unity2FoxgloveRos2UnavailableContext : IUnity2FoxgloveRos2Context
    {
        private const string UnavailableMessage =
            "ROS2 For Unity runtime is not bundled or active; the facade is an API boundary only.";

        public static Unity2FoxgloveRos2UnavailableContext Instance { get; } =
            new Unity2FoxgloveRos2UnavailableContext();

        private Unity2FoxgloveRos2UnavailableContext()
        {
        }

        public bool IsAvailable => false;

        public Unity2FoxgloveRos2Status Status => Unity2FoxgloveRos2Status.Unavailable;

        public string StatusMessage => UnavailableMessage;

        public IUnity2FoxgloveRos2Node CreateNode(string nodeName)
        {
            return new UnavailableNode(NormalizeName(nodeName), UnavailableMessage);
        }

        public void Dispose()
        {
        }

        private static string NormalizeName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unity2foxglove_unavailable" : value;
        }

        private static string NormalizeTopic(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "/unity2foxglove/unavailable" : value;
        }

        private sealed class UnavailableNode : IUnity2FoxgloveRos2Node
        {
            private readonly string _message;

            public UnavailableNode(string name, string message)
            {
                Name = name;
                _message = message;
            }

            public string Name { get; }

            public IUnity2FoxgloveRos2Publisher<T> CreatePublisher<T>(string topic)
            {
                return new UnavailablePublisher<T>(NormalizeTopic(topic), _message);
            }

            public IUnity2FoxgloveRos2Subscription CreateSubscription<T>(string topic, Action<T> callback)
            {
                return new UnavailableSubscription(NormalizeTopic(topic));
            }

            public void Dispose()
            {
            }
        }

        private sealed class UnavailablePublisher<T> : IUnity2FoxgloveRos2Publisher<T>
        {
            private readonly string _message;

            public UnavailablePublisher(string topic, string message)
            {
                Topic = topic;
                _message = message;
            }

            public string Topic { get; }

            public bool TryPublish(T message, out string error)
            {
                error = _message;
                return false;
            }

            public void Dispose()
            {
            }
        }

        private sealed class UnavailableSubscription : IUnity2FoxgloveRos2Subscription
        {
            public UnavailableSubscription(string topic)
            {
                Topic = topic;
            }

            public string Topic { get; }

            public void Dispose()
            {
            }
        }
    }
}
