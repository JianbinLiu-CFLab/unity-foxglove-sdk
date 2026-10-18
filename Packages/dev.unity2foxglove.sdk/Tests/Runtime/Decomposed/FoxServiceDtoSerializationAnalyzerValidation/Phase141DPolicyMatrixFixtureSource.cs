// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates FoxService DTO serialization analyzer diagnostics.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.SourceGenerators;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class FoxServiceDtoSerializationAnalyzerValidation
    {
        private static string Phase141DPolicyMatrixFixtureSource()
            => @"
using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace UnityEngine
{
    public class Object {}
    public class GameObject : Object {}
    public class Component : Object {}
    public class MonoBehaviour : Component {}
    public class Transform : Component {}
}

namespace Phase141D
{
    public sealed class CollectionRequest
    {
        public HashSet<string> hashSet { get; set; }
        public ICollection<float> collection { get; set; }
        public Queue<string> queue { get; set; }
        public Stack<string> stack { get; set; }
        public List<string> tags { get; } = new List<string>();
        public IReadOnlyList<string> readOnlyList { get { return tags; } }
        public string readOnlyScalar { get { return ""value""; } }
        public readonly int readonlyValue;
    }

    public sealed class InterfaceOnlyRequest
    {
        public IEnumerable<int> sequence { get; set; }
    }

    public sealed class CollectionResponse
    {
        public IReadOnlyCollection<double> readOnlyNumbers { get; set; }
        public SortedDictionary<string, int> sorted { get; set; }
        public string readOnlyScalar { get { return ""value""; } }
    }

    public sealed class Deep00 { public Deep01 next { get; set; } }
    public sealed class Deep01 { public Deep02 next { get; set; } }
    public sealed class Deep02 { public Deep03 next { get; set; } }
    public sealed class Deep03 { public Deep04 next { get; set; } }
    public sealed class Deep04 { public Deep05 next { get; set; } }
    public sealed class Deep05 { public Deep06 next { get; set; } }
    public sealed class Deep06 { public Deep07 next { get; set; } }
    public sealed class Deep07 { public Deep08 next { get; set; } }
    public sealed class Deep08 { public Deep09 next { get; set; } }
    public sealed class Deep09 { public Deep10 next { get; set; } }
    public sealed class Deep10 { public Deep11 next { get; set; } }
    public sealed class Deep11 { public Deep12 next { get; set; } }
    public sealed class Deep12 { public Deep13 next { get; set; } }
    public sealed class Deep13 { public Deep14 next { get; set; } }
    public sealed class Deep14 { public Deep15 next { get; set; } }
    public sealed class Deep15 { public Deep16 next { get; set; } }
    public sealed class Deep16 { public Deep17 next { get; set; } }
    public sealed class Deep17 { public Deep18 next { get; set; } }
    public sealed class Deep18 { public Deep19 next { get; set; } }
    public sealed class Deep19 { public Deep20 next { get; set; } }
    public sealed class Deep20 { public Deep21 next { get; set; } }
    public sealed class Deep21 { public Deep22 next { get; set; } }
    public sealed class Deep22 { public Deep23 next { get; set; } }
    public sealed class Deep23 { public Deep24 next { get; set; } }
    public sealed class Deep24 { public Deep25 next { get; set; } }
    public sealed class Deep25 { public Deep26 next { get; set; } }
    public sealed class Deep26 { public Deep27 next { get; set; } }
    public sealed class Deep27 { public Deep28 next { get; set; } }
    public sealed class Deep28 { public Deep29 next { get; set; } }
    public sealed class Deep29 { public Deep30 next { get; set; } }
    public sealed class Deep30 { public Deep31 next { get; set; } }
    public sealed class Deep31 { public Deep32 next { get; set; } }
    public sealed class Deep32 { public Deep33 next { get; set; } }
    public sealed class Deep33 { public string value { get; set; } }

    public sealed class OkResponse
    {
        public bool ok { get; set; }
    }

    public partial class PolicyMatrixServices
    {
        [FoxService(""/phase141d/collections"")]
        private CollectionResponse Collections(CollectionRequest request) => new CollectionResponse();

        [FoxService(""/phase141d/interface_only"")]
        private OkResponse InterfaceOnly(InterfaceOnlyRequest request) => new OkResponse();

        [FoxService(""/phase141d/deep"")]
        private OkResponse Deep(Deep00 request) => new OkResponse();
    }
}
";

        private static string InvalidDtoFixtureSource()
            => @"
using System;
using System.Collections.Generic;
using Unity.FoxgloveSDK.Components;

namespace UnityEngine
{
    public class Object {}
    public class GameObject : Object {}
    public class Component : Object {}
    public class MonoBehaviour : Component {}
    public class Transform : Component {}
}

namespace Phase141C
{
    public sealed class BadUnityRequest
    {
        public UnityEngine.Transform transform { get; set; }
    }

    public sealed class BadUnityResponse
    {
        public UnityEngine.MonoBehaviour owner { get; set; }
    }

    public sealed class DelegateRequest
    {
        public Action callback { get; set; }
    }

    public sealed class ObjectRequest
    {
        public object payload { get; set; }
    }

    public sealed class BadDictionaryRequest
    {
        public Dictionary<int, string> lookup { get; set; }
    }

    public class BadInheritedRequestBase
    {
        public UnityEngine.GameObject inheritedObject { get; set; }
    }

    public sealed class BadInheritedRequest : BadInheritedRequestBase
    {
        public string name { get; set; }
    }

    public sealed class BadTaskMemberRequest
    {
        public System.Threading.Tasks.Task<int> delayed { get; set; }
    }

    public sealed class RecursiveNode
    {
        public RecursiveChild next { get; set; }
    }

    public sealed class RecursiveChild
    {
        public RecursiveNode parent { get; set; }
    }

    public sealed class OkResponse
    {
        public bool ok { get; set; }
    }

    public partial class InvalidServices
    {
        [FoxService(""/phase141c/bad_unity_request"")]
        private OkResponse BadUnityRequest(BadUnityRequest request) => new OkResponse();

        [FoxService(""/phase141c/bad_unity_response"")]
        private BadUnityResponse BadUnityResponse(OkResponse request) => new BadUnityResponse();

        [FoxService(""/phase141c/bad_delegate"")]
        private OkResponse BadDelegate(DelegateRequest request) => new OkResponse();

        [FoxService(""/phase141c/bad_object"")]
        private OkResponse BadObject(ObjectRequest request) => new OkResponse();

        [FoxService(""/phase141c/bad_dictionary"")]
        private OkResponse BadDictionary(BadDictionaryRequest request) => new OkResponse();

        [FoxService(""/phase141c/bad_inherited"")]
        private OkResponse BadInherited(BadInheritedRequest request) => new OkResponse();

        [FoxService(""/phase141c/bad_task_member"")]
        private OkResponse BadTaskMember(BadTaskMemberRequest request) => new OkResponse();

        [FoxService(""/phase141c/recursive"")]
        private OkResponse Recursive(RecursiveNode request) => new OkResponse();
    }
}
";

        private static string WarningDtoFixtureSource()
            => @"
using System;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Components;

namespace UnityEngine
{
    public class Object {}
    public class GameObject : Object {}
    public class Component : Object {}
    public class MonoBehaviour : Component {}
    public class Transform : Component {}
}

namespace Phase141C
{
    public sealed class WarningRequest
    {
        public string readOnly { get { return ""value""; } }

        [JsonIgnore]
        public string ignored { get; set; }
    }

    public sealed class WarningResponse
    {
        public bool ok { get; set; }
    }

    public partial class WarningServices
    {
        [FoxService(""/phase141c/warnings"", Type = ""Phase141C.Warning"", RequestSchemaName = ""Phase141C.WarningRequest"", ResponseSchemaName = ""Phase141C.WarningResponse"")]
        private WarningResponse Warnings(WarningRequest request) => new WarningResponse();
    }
}
";

        private sealed class ValidationRun
        {
            public int PassCount { get; private set; }

            public void Check(bool condition, string label)
            {
                if (!condition)
                    throw new InvalidOperationException("[FAIL] " + label);

                Console.WriteLine("[PASS] " + label);
                PassCount++;
            }
        }

        private sealed class Phase141DReflectionAcceptedRequest
        {
            public HashSet<string> hashSet { get; set; }
            public ICollection<float> collection { get; set; }
            public Queue<string> queue { get; set; }
            public Stack<string> stack { get; set; }
            public List<string> tags { get; } = new List<string>();
        }

        private sealed class Phase141DReflectionRejectedRequest
        {
            public IEnumerable<int> sequence { get; set; }
            public string readOnlyScalar { get { return "value"; } }
            public IReadOnlyList<string> readOnlyList { get { return Array.Empty<string>(); } }
        }

        private sealed class Phase141DSharedWarningReferences
        {
            public Phase141DSharedWarningType first { get; set; }
            public Phase141DSharedWarningType second { get; set; }
        }

        private sealed class Phase141DSharedWarningType
        {
            public string value { get { return "value"; } }
        }

        private sealed class Phase187ReflectionResponse
        {
            public string readOnlyScalar { get { return "value"; } }
        }

        private static string ReadRepoText(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static string RepoPath(string relativePath)
            => PhaseValidationSourceHelpers.RepoPath(relativePath);
    }
}
