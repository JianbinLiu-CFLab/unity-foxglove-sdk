using System;
using System.IO;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Tests
{
    internal static class Phase164_6Validation
    {
        private static int _passed;

        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 164-6 Tests ---");
            _passed = 0;

            VerifyReusableFrameEncodersPreserveWireFormat();
            VerifyCommonServiceEncodingsAreCached();
            VerifyEncodePathsUseUncheckedLittleEndianWriters();
            VerifyRegistry();

            Console.WriteLine("Phase 164-6: " + _passed + " checks passed.\n");
        }

        private static void VerifyReusableFrameEncodersPreserveWireFormat()
        {
            var payload = new byte[] { 0xAA, 0xBB, 0xCC };
            var allocatedMessage = BinaryEncoding.EncodeServerMessageData(0x01020304u, 0x0102030405060708ul, payload);
            var pooledMessage = new byte[allocatedMessage.Length + 4];
            BinaryEncoding.EncodeServerMessageData(pooledMessage, 2, 0x01020304u, 0x0102030405060708ul, payload);

            Check(BinaryEncoding.GetServerMessageDataFrameLength(payload.Length) == allocatedMessage.Length,
                "164-6A-1: message data length helper matches the allocated frame length");
            Check(SameSlice(allocatedMessage, pooledMessage, 2, allocatedMessage.Length),
                "164-6A-2: reusable message data encoder preserves the wire format");

            var allocatedTime = BinaryEncoding.EncodeTime(0x0102030405060708ul);
            var pooledTime = new byte[allocatedTime.Length + 3];
            BinaryEncoding.EncodeTime(pooledTime, 1, 0x0102030405060708ul);

            Check(BinaryEncoding.TimeFrameLength == allocatedTime.Length,
                "164-6A-3: time frame length constant matches the allocated frame length");
            Check(SameSlice(allocatedTime, pooledTime, 1, allocatedTime.Length),
                "164-6A-4: reusable time encoder preserves the wire format");
        }

        private static void VerifyCommonServiceEncodingsAreCached()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Protocol/BinaryEncoding.cs");
            var response = SourceMethod(source, "public static byte[] EncodeServerServiceCallResponse");

            Check(source.Contains("JsonEncodingBytes", StringComparison.Ordinal)
                  && source.Contains("ProtobufEncodingBytes", StringComparison.Ordinal)
                  && source.Contains("GetCachedServiceEncodingBytes", StringComparison.Ordinal),
                "164-6B-1: common service-call response encodings are cached");
            Check(response.Contains("GetCachedServiceEncodingBytes(encoding)", StringComparison.Ordinal)
                  && !response.Contains("Encoding.UTF8.GetBytes(encoding ??", StringComparison.Ordinal),
                "164-6B-2: service-call response encoding avoids UTF-8 allocation for common encodings");

            var jsonResponse = BinaryEncoding.EncodeServerServiceCallResponse(1, 2, "json", new byte[] { 3 });
            Check(BinaryEncoding.ReadU32LE(jsonResponse, 9) == 4u,
                "164-6B-3: cached json encoding still writes the correct encoding length");
        }

        private static void VerifyEncodePathsUseUncheckedLittleEndianWriters()
        {
            var source = ReadRepoText("Packages/dev.unity2foxglove.sdk/Runtime/Protocol/BinaryEncoding.cs");
            var message = SourceMethod(source, "public static void EncodeServerMessageData");
            var time = SourceMethod(source, "public static void EncodeTime(byte[] destination");
            var service = SourceMethod(source, "public static byte[] EncodeServerServiceCallResponse");

            Check(source.Contains("WriteU32LEUnchecked", StringComparison.Ordinal)
                  && source.Contains("WriteU64LEUnchecked", StringComparison.Ordinal),
                "164-6C-1: unchecked little-endian writers exist for validated encode paths");
            Check(message.Contains("ValidateBufferRange(destination", StringComparison.Ordinal)
                  && message.Contains("WriteU32LEUnchecked", StringComparison.Ordinal)
                  && message.Contains("WriteU64LEUnchecked", StringComparison.Ordinal),
                "164-6C-2: reusable message encoder validates once then uses unchecked writes");
            Check(time.Contains("ValidateBufferRange(destination", StringComparison.Ordinal)
                  && time.Contains("WriteU64LEUnchecked", StringComparison.Ordinal),
                "164-6C-3: reusable time encoder validates once then uses unchecked writes");
            Check(service.Contains("WriteU32LEUnchecked", StringComparison.Ordinal),
                "164-6C-4: service-call response uses unchecked writes after allocating an exact frame");
        }

        private static void VerifyRegistry()
        {
            var registry = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = ReadRepoText("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");
            Check(registry.Contains("\"--phase164-6\"", StringComparison.Ordinal), "164-6D-1: validation registry exposes Phase164-6");
            Check(project.Contains("Phase164_6Validation.cs", StringComparison.Ordinal), "164-6D-2: runtime validation project compiles Phase164-6");
        }

        private static bool SameSlice(byte[] expected, byte[] actual, int offset, int length)
        {
            for (var i = 0; i < length; i++)
            {
                if (expected[i] != actual[offset + i])
                    return false;
            }

            return true;
        }

        private static string SourceMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                throw new InvalidOperationException("Missing method: " + signature);

            var brace = source.IndexOf('{', start);
            if (brace < 0)
                throw new InvalidOperationException("Missing method body: " + signature);

            var depth = 0;
            for (var i = brace; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            throw new InvalidOperationException("Unterminated method: " + signature);
        }

        private static string ReadRepoText(string relativePath)
        {
            var root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(
                        dir.FullName,
                        "Packages",
                        "dev.unity2foxglove.sdk",
                        "Tests",
                        "Runtime",
                        "FoxgloveSdk.Tests.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate repository root.");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new Exception("[FAIL] " + label);
            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
