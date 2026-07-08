using System;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_12Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-12 Tests ---");
            _passed = 0;

            VerifyDescriptorBlobIsCached();
            VerifyProtobufRegistryAvoidsIntermediateRawContentClone();
            VerifyDescriptorMapAvoidsLinqOrdering();
            VerifyManifestUsesPrecomputedDescriptorHash();
            VerifyRegistry();

            Console.WriteLine("Phase 164-12: " + _passed + " checks passed.\n");
        }

        private static void VerifyDescriptorBlobIsCached()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Generated/Descriptors/FoxgloveSchemas.cs");

            Check(source.Contains("public const string FileDescriptorSetSha256 = \"39fe4da31ce3f19ad84e5bb05270df6c710dd2bb16376590372609f6d9008521\"", StringComparison.Ordinal),
                "164-12A-1: generated protobuf descriptor exposes the precomputed SHA-256");
            Check(source.Contains("public static readonly byte[] FileDescriptorSetData = System.Convert.FromBase64String", StringComparison.Ordinal)
                  && !source.Contains("public static byte[] FileDescriptorSetData =>", StringComparison.Ordinal),
                "164-12A-2: descriptor base64 is decoded once into a static readonly field");
        }

        private static void VerifyProtobufRegistryAvoidsIntermediateRawContentClone()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Registry/ProtobufSchemaRegistry.cs");
            var registerAll = PhaseValidationSourceHelpers.SourceMethod(source, "public void RegisterAll");

            Check(registerAll.Contains("RawContent = kv.Value.Bytes", StringComparison.Ordinal)
                  && registerAll.Contains("Content = kv.Value.Base64", StringComparison.Ordinal)
                  && !registerAll.Contains("RawContent = (byte[])kv.Value.Clone()", StringComparison.Ordinal),
                "164-12B-1: RegisterAll lets DefaultSchemaRegistry perform the single defensive RawContent clone while reusing cached base64");
            Check(source.Contains("public byte[] GetFileDescriptorSet(string schemaName)", StringComparison.Ordinal)
                  && source.Contains("(byte[])entry.Bytes.Clone()", StringComparison.Ordinal),
                "164-12B-2: public descriptor lookup keeps ownership-safe byte[] clone semantics");
        }

        private static void VerifyDescriptorMapAvoidsLinqOrdering()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Proto/Registry/ProtobufSchemaRegistry.cs");
            var build = PhaseValidationSourceHelpers.SourceMethod(source, "private void BuildDescriptorMap");

            Check(build.Contains("var neededFiles = new List<string>()", StringComparison.Ordinal)
                  && build.Contains("CollectDependencies(file.Name, fileMap, new HashSet<string>(), neededFiles)", StringComparison.Ordinal)
                  && !build.Contains("OrderBy(", StringComparison.Ordinal),
                "164-12C-1: descriptor map keeps allocation-light dependency ordering without LINQ OrderBy");
        }

        private static void VerifyManifestUsesPrecomputedDescriptorHash()
        {
            var source = Read("Packages/dev.unity2foxglove.sdk/Editor/Shared/SchemaManifest/Unity2FoxgloveSchemaManifestBuilder.cs");
            var build = PhaseValidationSourceHelpers.SourceMethod(source, "private static Unity2FoxgloveProtobufRegistrySection BuildProtobufRegistrySection");

            Check(build.Contains("FoxgloveSchemas.FileDescriptorSetSha256", StringComparison.Ordinal)
                  && !build.Contains("Sha256Hex(descriptorData)", StringComparison.Ordinal),
                "164-12D-1: schema manifest uses the precomputed protobuf descriptor hash");
            Check(build.Contains("var descriptorData = FoxgloveSchemas.FileDescriptorSetData", StringComparison.Ordinal)
                  && build.Contains("descriptorData == null || descriptorData.Length == 0", StringComparison.Ordinal),
                "164-12D-2: manifest builder still validates descriptor data availability");
        }

        private static void VerifyRegistry()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-12\"", StringComparison.Ordinal), "164-12E-1: validation registry exposes Phase164-12");
            Check(project.Contains("Phase164_12Validation.cs", StringComparison.Ordinal), "164-12E-2: runtime validation project compiles Phase164-12");
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
