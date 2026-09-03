// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 147 generated-source literal and determinism validation.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Editor;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase147Validation.
    /// </summary>
    public static class Phase147Validation
    {
        private static int _passCount;

        /// <summary>
        /// Validation method for Validate.
        /// </summary>
        public static void Validate()
        {
            Console.WriteLine("\n--- Phase 147 Tests ---");
            _passCount = 0;

            VerifyLiteralContract();
            VerifyFoxServiceDescriptorSchemaLiterals();
            VerifyGeneratedTopicOrderDeterministic();
            VerifyInvalidTopicMembersFailEarly();
            VerifyValidationRegistryEntry();

            Console.WriteLine("Phase 147: " + _passCount + " checks passed.\n");
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            Console.WriteLine("[PASS] " + label);
            _passCount++;
        }

        private static void VerifyLiteralContract()
        {
            Check(StringLiteralEmitter.CSharpStringLiteral(null) == string.Empty,
                "147-1: null literal returns empty escaped fragment");
            Check(StringLiteralEmitter.CSharpStringLiteral(string.Empty) == string.Empty,
                "147-2: empty literal returns empty escaped fragment");

            var escaped = StringLiteralEmitter.CSharpStringLiteral("\"\\\r\n\t\0\u0001\u2028\u2029");
            Check(escaped == "\\\"\\\\\\r\\n\\t\\0\\u0001\\u2028\\u2029",
                "147-3: C# literal edge characters are escaped");
            Check(!escaped.StartsWith("\"", StringComparison.Ordinal) && !escaped.EndsWith("\"", StringComparison.Ordinal),
                "147-4: literal emitter does not add surrounding quotes");

            var surrogateEscaped = StringLiteralEmitter.CSharpStringLiteral(char.ConvertFromUtf32(0x1F600));
            Check(surrogateEscaped == "\\uD83D\\uDE00",
                "147-5: surrogate code units are escaped deterministically");
        }

        private static void VerifyGeneratedTopicOrderDeterministic()
        {
            var first = FoxgloveSourceEmitter.EmitClass("Phase147", "OrderedSource",
                new List<FoxgloveSourceEmitter.TopicMember>
                {
                    Member("_z", "/phase147/z"),
                    Member("_a", "/phase147/a"),
                    Member("_m", "/phase147/m"),
                });

            var second = FoxgloveSourceEmitter.EmitClass("Phase147", "OrderedSource",
                new List<FoxgloveSourceEmitter.TopicMember>
                {
                    Member("_m", "/phase147/m"),
                    Member("_z", "/phase147/z"),
                    Member("_a", "/phase147/a"),
                });

            Check(first == second,
                "147-8: equivalent topic sets emit deterministic source independent of input order");
            Check(first.IndexOf("\"/phase147/a\"", StringComparison.Ordinal) <
                  first.IndexOf("\"/phase147/m\"", StringComparison.Ordinal)
                  && first.IndexOf("\"/phase147/m\"", StringComparison.Ordinal) <
                  first.IndexOf("\"/phase147/z\"", StringComparison.Ordinal),
                "147-9: generated topic metadata is ordinal topic ordered");
        }

        private static void VerifyFoxServiceDescriptorSchemaLiterals()
        {
            const string description = "quote \" slash \\ line \\n sep \u2028 pair 😀";
            var requestSchema = "{\"type\":\"object\",\"description\":\"" + description.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
            var responseSchema = "{\"type\":\"object\",\"description\":\"response \\\"ok\\\"\"}";
            var generated = FoxServiceSourceEmitter.EmitClass("Phase147", "ServiceSource",
                new[]
                {
                    new FoxServiceSourceEmitter.ServiceMethod(
                        "Reset",
                        "/phase147/service",
                        "Phase147.Reset",
                        description,
                        "Phase147.Reset.Request",
                        "Phase147.Reset.Response",
                        requestSchema,
                        responseSchema,
                        "Phase147.Request",
                        "Phase147.Response",
                        hasRequest: true,
                        hasResponse: true)
                });

            var literals = ExtractDescriptorStringLiterals(generated, "/phase147/service");
            Check(literals.Length >= 7,
                "147-6: FoxService descriptor includes escaped schema payload constructor arguments");

            var decodedRequest = JObject.Parse(literals[literals.Length - 2]);
            var decodedResponse = JObject.Parse(literals[literals.Length - 1]);
            Check((string)decodedRequest["description"] == description
                  && (string)decodedResponse["description"] == "response \"ok\"",
                "147-7: FoxService generated descriptor schemas parse and round-trip special characters");
        }

        private static void VerifyInvalidTopicMembersFailEarly()
        {
            ExpectArgumentException(
                () => FoxgloveSourceEmitter.EmitClass("Phase147", "InvalidNull", new FoxgloveSourceEmitter.TopicMember[] { null }),
                "147-10: null TopicMember fails before source generation");
            ExpectArgumentException(
                () => FoxgloveSourceEmitter.EmitClass("Phase147", "InvalidMember", new[] { Member("", "/phase147/member") }),
                "147-11: empty MemberName fails before source generation");
            ExpectArgumentException(
                () => FoxgloveSourceEmitter.EmitClass("Phase147", "InvalidType", new[]
                {
                    new FoxgloveSourceEmitter.TopicMember("_value", "", "/phase147/type", 10f, "")
                }),
                "147-12: empty TypeName fails before source generation");
            ExpectArgumentException(
                () => FoxgloveSourceEmitter.EmitClass("Phase147", "InvalidTopic", new[] { Member("_value", "") }),
                "147-13: empty Topic fails before source generation");
        }

        private static void VerifyValidationRegistryEntry()
        {
            var entry = PhaseValidationRegistry.Find(new[] { "--phase147" });
            var inDefaultLane = PhaseValidationRegistry.DefaultValidations(false)
                .Any(item => item.Flag == "--phase147");
            Check(entry != null
                  && entry.Name.StartsWith("Phase 147:", StringComparison.Ordinal)
                  && entry.Name.Contains(
                      "generated-source literal and determinism validation",
                      StringComparison.Ordinal)
                  && inDefaultLane,
                "147-14: PhaseValidationRegistry wires --phase147");
        }

        private static FoxgloveSourceEmitter.TopicMember Member(string name, string topic)
            => new FoxgloveSourceEmitter.TopicMember(name, "System.Single", topic, 10f, "");

        private static void ExpectArgumentException(Action action, string label)
        {
            try
            {
                action();
            }
            catch (ArgumentException)
            {
                Check(true, label);
                return;
            }

            throw new InvalidOperationException("[FAIL] " + label);
        }

        private static string[] ExtractDescriptorStringLiterals(string generated, string serviceName)
        {
            var serviceIndex = generated.IndexOf(serviceName, StringComparison.Ordinal);
            if (serviceIndex < 0)
                return Array.Empty<string>();

            var lineStart = generated.LastIndexOf(
                "new global::Unity.FoxgloveSDK.Components.FoxgloveGeneratedServiceDescriptor",
                serviceIndex,
                StringComparison.Ordinal);
            var lineEnd = generated.IndexOf(")", serviceIndex, StringComparison.Ordinal);
            if (lineStart < 0 || lineEnd < 0)
                return Array.Empty<string>();

            var descriptor = generated.Substring(lineStart, lineEnd - lineStart);
            var values = new List<string>();
            var i = 0;
            while (i < descriptor.Length)
            {
                if (descriptor[i] != '"')
                {
                    i++;
                    continue;
                }

                var start = i;
                i++;
                var escaped = false;
                while (i < descriptor.Length)
                {
                    var ch = descriptor[i++];
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (ch == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (ch == '"')
                        break;
                }

                values.Add(JToken.Parse(descriptor.Substring(start, i - start)).Value<string>());
            }

            return values.ToArray();
        }
    }
}
