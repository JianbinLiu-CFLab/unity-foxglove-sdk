using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_34Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-34 Tests ---");
            _passed = 0;

            VerifyTransformPublisherResolvesPoseOncePerTick();
            VerifyRigidWorldToLocalUsesClosedFormInverse();
            VerifyMazeCameraSkipsStationaryTargetFrames();
            VerifyMazeBuilderReusesNeighbourList();
            VerifyRegistry();

            Console.WriteLine("Phase 164-34: " + _passed + " checks passed.\n");
        }

        private static void VerifyTransformPublisherResolvesPoseOncePerTick()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Publishers/FoxgloveTransformPublisher.cs");
            var update = MethodBody(source, "protected override void Update()");

            Check(update.Contains("ResolveTransform(out var pos, out var rot);", StringComparison.Ordinal),
                "164-34A-1: transform publisher resolves pose once in Update");
            Check(!update.Contains("var message = CreateMessage(unixNs);", StringComparison.Ordinal),
                "164-34A-2: transform publisher no longer creates FrameTransformMessage before knowing the output path");
            Check(update.Contains("PublishProtobufTransform(unixNs, encodingResolution, pos, rot)", StringComparison.Ordinal),
                "164-34A-3: protobuf transform publishing reuses the pre-resolved pose");

            var createMessage = MethodBody(source, "private FrameTransformMessage CreateMessage(ulong unixNs, UVector3 pos, UQuaternion rot)");
            var publishProto = MethodBody(source, "private void PublishProtobufTransform(ulong unixNs, PublisherEncodingResolution resolution, UVector3 pos, UQuaternion rot)");
            Check(!createMessage.Contains("ResolveTransform(", StringComparison.Ordinal)
                  && !publishProto.Contains("ResolveTransform(", StringComparison.Ordinal),
                "164-34A-4: transform payload builders do not resolve the transform a second time");
            Check(source.Contains("private string ResolveChildFrameId()", StringComparison.Ordinal)
                  && source.Contains("_childFrameIdCacheValid", StringComparison.Ordinal)
                  && source.Contains("private string ResolveParentFrameId()", StringComparison.Ordinal),
                "164-34A-5: transform frame-id sanitization is cached between frame-id changes");
            Check(source.Contains("private string _cachedGameObjectName;", StringComparison.Ordinal)
                  && MethodBody(source, "private void Awake()").Contains("RefreshGameObjectNameCache();", StringComparison.Ordinal)
                  && MethodBody(source, "protected override void OnEnable()").Contains("RefreshGameObjectNameCache();", StringComparison.Ordinal)
                  && MethodBody(source, "protected override void OnValidate()").Contains("RefreshGameObjectNameCache();", StringComparison.Ordinal),
                "173-077A: transform publisher refreshes fallback object-name cache at explicit lifecycle points");
            Check(MethodBody(source, "private string ResolveChildFrameId()").Contains("_cachedGameObjectName", StringComparison.Ordinal)
                  && !MethodBody(source, "private string ResolveChildFrameId()").Contains("gameObject.name", StringComparison.Ordinal)
                  && !update.Contains("gameObject.name", StringComparison.Ordinal),
                "173-077B: transform publisher hot path uses cached object name instead of gameObject.name");
        }

        private static void VerifyRigidWorldToLocalUsesClosedFormInverse()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Sensors/CoordinateConverterFloat3.cs");
            var method = MethodBody(source, "public static float4x4 RigidWorldToLocal(Vector3 position, Quaternion rotation)");

            Check(method.Contains("Quaternion.Inverse(rotation)", StringComparison.Ordinal)
                  && method.Contains("inverseRotation * -position", StringComparison.Ordinal),
                "164-34B-1: rigid world-to-local uses closed-form unit-scale inverse");
            Check(!method.Contains("math.inverse", StringComparison.Ordinal),
                "164-34B-2: rigid world-to-local avoids full matrix inversion");
        }

        private static void VerifyMazeCameraSkipsStationaryTargetFrames()
        {
            var source = Read("Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.4/Virtual LiDAR Maze Demo/Phase138MazeCameraFollow.cs");
            var update = MethodBody(source, "private void LateUpdate()");

            Check(source.Contains("_hasLastTargetPose", StringComparison.Ordinal)
                  && update.Contains("_target.position == _lastTargetPosition", StringComparison.Ordinal)
                  && update.Contains("_target.rotation == _lastTargetRotation", StringComparison.Ordinal)
                  && update.Contains("return;", StringComparison.Ordinal),
                "164-34C-1: camera follow skips unchanged target pose frames");
            Check(update.IndexOf("return;", StringComparison.Ordinal) < update.IndexOf("transform.position", StringComparison.Ordinal),
                "164-34C-2: camera follow exits before writing transform on unchanged frames");
        }

        private static void VerifyMazeBuilderReusesNeighbourList()
        {
            var source = Read("Unity2Foxglove/Assets/Samples/Unity2Foxglove SDK/1.9.4/Virtual LiDAR Maze Demo/Phase138MazeBuilder.cs");
            var build = MethodBody(source, "public static GameObject Build(int cellsX, int cellsZ, float cellSize,");

            Check(build.Contains("var neighbours = new List<(int, int, int)>(4);", StringComparison.Ordinal)
                  && build.Contains("neighbours.Clear();", StringComparison.Ordinal),
                "164-34D-1: maze DFS reuses one bounded neighbour list");
            Check(!build.Contains("while (stack.Count > 0)\r\n            {\r\n                var (cx, cz) = stack.Peek();\r\n\r\n                // Collect unvisited neighbours\r\n                var neighbours = new List", StringComparison.Ordinal)
                  && !build.Contains("while (stack.Count > 0)\n            {\n                var (cx, cz) = stack.Peek();\n\n                // Collect unvisited neighbours\n                var neighbours = new List", StringComparison.Ordinal),
                "164-34D-2: maze DFS does not allocate the neighbour list inside the loop");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-34\"", StringComparison.Ordinal), "164-34E-1: validation registry exposes Phase164-34");
            Check(project.Contains("Phase164_34Validation.cs", StringComparison.Ordinal), "164-34E-2: runtime validation project compiles Phase164-34");
        }

        private static string MethodBody(string source, string signature)
        {
            var signatureStart = source.IndexOf(signature, StringComparison.Ordinal);
            if (signatureStart < 0)
                throw new Exception("[FAIL] missing method signature: " + signature);

            var bodyStart = source.IndexOf('{', signatureStart);
            if (bodyStart < 0)
                throw new Exception("[FAIL] missing method body: " + signature);

            var depth = 0;
            for (var i = bodyStart; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(bodyStart, i - bodyStart + 1);
                }
            }

            throw new Exception("[FAIL] unterminated method body: " + signature);
        }

        private static string Read(string relativePath)
            => PhaseValidationSourceHelpers.ReadRequiredRepoText(relativePath);

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
