// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core/Replay
// Purpose: Resolves optional Foxglove protobuf message parsers for replay payloads.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Unity.FoxgloveSDK.Core
{
    internal static class ReplayProtobufParser
    {
        private static readonly object ReflectionCacheGate = new();
        private static readonly Dictionary<string, ProtobufParserBinding> ProtobufParserCache = new();
        private static readonly string[] PreferredAssemblyNames =
        {
            "Unity.FoxgloveSDK.Proto",
            "Unity.FoxgloveSDK.Proto.Generated"
        };

        public static object Parse(string typeName, byte[] payload)
        {
            var binding = ResolveParser(typeName);

            try
            {
                return binding.Parse(payload);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static ProtobufParserBinding ResolveParser(string typeName)
        {
            lock (ReflectionCacheGate)
            {
                if (ProtobufParserCache.TryGetValue(typeName, out var binding))
                    return binding;

                var type = ResolveType(typeName);
                if (type == null)
                    throw new InvalidOperationException($"Optional protobuf type '{typeName}' is not available.");

                var parser = ReplayPropertyCache.Resolve(type, "Parser", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (parser == null)
                    throw new InvalidOperationException($"Optional protobuf type '{typeName}' does not expose a Parser.");

                var parseFrom = parser.GetType().GetMethod(
                    "ParseFrom",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(byte[]) },
                    null);
                if (parseFrom == null)
                    throw new InvalidOperationException($"Optional protobuf parser for '{typeName}' does not support ParseFrom(byte[]).");

                binding = new ProtobufParserBinding(parser, parseFrom);
                ProtobufParserCache[typeName] = binding;
                return binding;
            }
        }

        private static Type ResolveType(string typeName)
        {
            for (var i = 0; i < PreferredAssemblyNames.Length; i++)
            {
                var type = Type.GetType(typeName + ", " + PreferredAssemblyNames[i], throwOnError: false);
                if (type != null)
                    return type;
            }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                var type = assemblies[i].GetType(typeName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private sealed class ProtobufParserBinding
        {
            public ProtobufParserBinding(object parser, MethodInfo parseFrom)
            {
                Parser = parser;
                ParseFrom = parseFrom;
                ParseFromArguments = new object[1];
            }

            public object Parser { get; }
            public MethodInfo ParseFrom { get; }
            private object[] ParseFromArguments { get; }

            public object Parse(byte[] payload)
            {
                lock (ParseFromArguments)
                {
                    try
                    {
                        ParseFromArguments[0] = payload;
                        return ParseFrom.Invoke(Parser, ParseFromArguments);
                    }
                    finally
                    {
                        ParseFromArguments[0] = null;
                    }
                }
            }
        }
    }
}
