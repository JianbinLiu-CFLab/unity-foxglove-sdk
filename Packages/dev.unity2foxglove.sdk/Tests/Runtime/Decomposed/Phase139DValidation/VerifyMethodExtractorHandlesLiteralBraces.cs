// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 139D validation for the Unity cursor bridge feasibility surface.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Unity.FoxgloveSDK.Core;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// CI-safe checks for the Phase 139D cursor-bridge scaffold. These checks
    /// validate the internal signal contract and deliberately avoid exposing it
    /// as a product workflow until a Foxglove-side control path exists.
    /// </summary>
    public static partial class Phase139DValidation
    {
        private static void VerifyMethodExtractorHandlesLiteralBraces()
        {
            const string source = @"
class Fixture
{
    private void Target()
    {
        var json = ""{literal}"";
        var format = ""value {0}"";
        AfterLiteralBraces();
    }

    private void Other() { ForbiddenAfterTarget(); }
}";
            var body = ExtractMethodBody(source, "private void Target()");
            Check(body.Contains("AfterLiteralBraces()", StringComparison.Ordinal)
                  && !body.Contains("ForbiddenAfterTarget", StringComparison.Ordinal),
                "139D-7I: validation method-body extraction ignores braces inside string literals");
        }

        private static void VerifyValidationWiring()
        {
            var registry = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs");
            var project = Read("Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj");

            Check(registry.Contains("Ci(\"--phase139d\"", StringComparison.Ordinal),
                "139D-8A: registry wires --phase139d");
            Check(registry.Contains("Phase139DValidation.Validate", StringComparison.Ordinal),
                "139D-8B: registry points Phase139D at the validation entrypoint");
            Check(project.Contains("Phase139DValidation.cs", StringComparison.Ordinal),
                "139D-8C: test project compiles Phase139DValidation");
        }

        /// <summary>Read a repository-relative text file for structural validation checks.</summary>
        private static string ExtractMethodBody(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0)
                return string.Empty;
            var brace = source.IndexOf('{', start);
            if (brace < 0)
                return string.Empty;

            var end = FindMatchingBrace(source, brace);
            return end >= 0 ? source.Substring(brace, end - brace + 1) : string.Empty;
        }

        private static int FindMatchingBrace(string source, int brace)
        {
            var depth = 0;
            var inString = false;
            var inChar = false;
            var inLineComment = false;
            var inBlockComment = false;
            var verbatimString = false;
            for (var i = brace; i < source.Length; i++)
            {
                var ch = source[i];
                var next = i + 1 < source.Length ? source[i + 1] : '\0';

                if (inLineComment)
                {
                    if (ch == '\n')
                        inLineComment = false;
                    continue;
                }

                if (inBlockComment)
                {
                    if (ch == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }
                    continue;
                }

                if (inString)
                {
                    if (verbatimString && ch == '"' && next == '"')
                    {
                        i++;
                        continue;
                    }

                    if (ch == '"' && (verbatimString || !IsEscaped(source, i)))
                    {
                        inString = false;
                        verbatimString = false;
                    }
                    continue;
                }

                if (inChar)
                {
                    if (ch == '\'' && !IsEscaped(source, i))
                        inChar = false;
                    continue;
                }

                if (ch == '/' && next == '/')
                {
                    inLineComment = true;
                    i++;
                    continue;
                }

                if (ch == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    verbatimString = i > 0 && source[i - 1] == '@';
                    continue;
                }

                if (ch == '\'')
                {
                    inChar = true;
                    continue;
                }

                if (ch == '{')
                    depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private static bool IsEscaped(string source, int index)
        {
            var slashCount = 0;
            for (var i = index - 1; i >= 0 && source[i] == '\\'; i--)
                slashCount++;
            return slashCount % 2 == 1;
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot() => CachedRepoRoot;

        private static string ResolveRepoRoot()
        {
            var root = Phase16Validation.FindRepoRoot();
            if (string.IsNullOrEmpty(root))
                throw new DirectoryNotFoundException("Could not find repository root for Phase139D validation.");
            return root;
        }

        private static int ReserveFreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static UnityReplayCursorEndpoint StartCursorEndpointWithRetry(
            string bearerToken,
            Func<ReplayCursorRequest, UnityReplayCursorEndpointQueueResult> queue,
            out int port)
        {
            Exception lastError = null;
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                port = ReserveFreeLoopbackPort();
                var endpoint = new UnityReplayCursorEndpoint();
                try
                {
                    endpoint.Start(
                        new UnityReplayCursorEndpointOptions(
                            enabled: true,
                            host: "127.0.0.1",
                            port: port,
                            path: "/v1/replay-cursor",
                            bearerToken: bearerToken,
                            maxBodyBytes: UnityReplayCursorEndpointOptions.Default.MaxBodyBytes),
                        queue);
                    return endpoint;
                }
                catch (Exception ex) when (IsAddressAlreadyInUse(ex))
                {
                    endpoint.Dispose();
                    lastError = ex;
                }
            }

            port = 0;
            throw new InvalidOperationException(
                "Phase139D could not bind a loopback cursor endpoint after 5 attempts. Last error: "
                + lastError?.GetType().Name + ": " + lastError?.Message,
                lastError);
        }

        private static bool IsAddressAlreadyInUse(Exception error)
        {
            return error is SocketException socket && socket.SocketErrorCode == SocketError.AddressAlreadyInUse
                   || error is HttpListenerException listener
                   && (listener.ErrorCode == 183 || listener.ErrorCode == 10_048);
        }

        private static HttpStatusCode PostJsonStatus(string url, string json, string bearerToken, out string body)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(bearerToken))
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearerToken);
            using var response = client.SendAsync(request).GetAwaiter().GetResult();
            body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return response.StatusCode;
        }

        private static HttpStatusCode PreflightStatus(string url, string origin, out string allowOrigin)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Options, url);
            request.Headers.TryAddWithoutValidation("Origin", origin);
            request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
            request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "Authorization, Content-Type");
            using var response = client.SendAsync(request).GetAwaiter().GetResult();
            allowOrigin = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
                ? string.Join(",", values)
                : string.Empty;
            return response.StatusCode;
        }

        private static string GetText(string url)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = client.GetAsync(url).GetAwaiter().GetResult();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        private static void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("[FAIL] " + label);

            _passed++;
            Console.WriteLine("[PASS] " + label);
        }
    }
}
