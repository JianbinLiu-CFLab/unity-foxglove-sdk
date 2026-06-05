// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: Routes embedded Remote Data Loader HTTP requests to MCAP manifest/data operations.

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Routes the small Phase139B HTTP surface without depending on Unity APIs.</summary>
    internal sealed class RemoteMcapHttpRouter
    {
        private readonly RemoteMcapDataSourcePrototype _source;

        public RemoteMcapHttpRouter(RemoteMcapDataSourcePrototype source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public Task HandleAsync(HttpListenerContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (string.Equals(path, "/v1/manifest", StringComparison.Ordinal))
                return HandleManifestAsync(context);
            if (string.Equals(path, "/v1/data", StringComparison.Ordinal))
                return HandleDataAsync(context);

            return WriteTextAsync(context.Response, HttpStatusCode.NotFound, "Unsupported Remote Data Loader route.");
        }

        private Task HandleManifestAsync(HttpListenerContext context)
        {
            if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                return WriteTextAsync(context.Response, HttpStatusCode.MethodNotAllowed, "GET is required for /v1/manifest.");

            var request = BuildRequest(context);
            var manifest = _source.GetManifest(request);
            if (manifest.Status != RemoteMcapResponseStatus.Ok)
                return WriteTextAsync(context.Response, ToHttpStatus(manifest.Status), FirstProblem(manifest.Problems));

            var json = RemoteMcapOfficialManifestSerializer.Serialize(manifest.Manifest);
            return WriteBytesAsync(context.Response, HttpStatusCode.OK, "application/json", Encoding.UTF8.GetBytes(json));
        }

        private async Task HandleDataAsync(HttpListenerContext context)
        {
            if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTextAsync(context.Response, HttpStatusCode.MethodNotAllowed, "GET is required for /v1/data.").ConfigureAwait(false);
                return;
            }

            var request = BuildRequest(context);
            if (!TryApplyTimeRange(context, request, out var problem))
            {
                await WriteTextAsync(context.Response, HttpStatusCode.BadRequest, problem).ConfigureAwait(false);
                return;
            }

            var recordingId = context.Request.QueryString["recordingId"];
            if (!string.IsNullOrEmpty(recordingId))
                request.SourceId = recordingId;

            using (var data = _source.GetDataStream(request))
            {
                if (data.Status != RemoteMcapResponseStatus.Ok)
                {
                    await WriteTextAsync(context.Response, ToHttpStatus(data.Status), FirstProblem(data.Problems)).ConfigureAwait(false);
                    return;
                }

                var response = context.Response;
                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentType = string.IsNullOrEmpty(data.ContentType) ? "application/octet-stream" : data.ContentType;
                if (data.Length >= 0)
                    response.ContentLength64 = data.Length;

                await CopyAndCloseAsync(data.DataStream, response).ConfigureAwait(false);
            }
        }

        private static RemoteMcapRequest BuildRequest(HttpListenerContext context)
        {
            return new RemoteMcapRequest
            {
                BearerToken = context.Request.Headers["Authorization"] ?? string.Empty,
                SourceId = context.Request.QueryString["recordingId"] ?? context.Request.QueryString["sourceId"] ?? string.Empty
            };
        }

        private static bool TryApplyTimeRange(HttpListenerContext context, RemoteMcapRequest request, out string problem)
        {
            problem = string.Empty;
            var startTime = context.Request.QueryString["startTime"];
            var endTime = context.Request.QueryString["endTime"];

            if (!string.IsNullOrEmpty(startTime) && !TryParseIsoUtcNs(startTime, out request.StartTimeNs))
            {
                problem = "Invalid startTime. Use an ISO 8601 UTC timestamp such as 2026-06-05T12:00:00Z.";
                return false;
            }

            if (!string.IsNullOrEmpty(endTime) && !TryParseIsoUtcNs(endTime, out request.EndTimeNs))
            {
                problem = "Invalid endTime. Use an ISO 8601 UTC timestamp such as 2026-06-05T12:00:00Z.";
                return false;
            }

            if (request.StartTimeNs > request.EndTimeNs)
            {
                problem = "startTime must be less than or equal to endTime.";
                return false;
            }

            return true;
        }

        private static bool TryParseIsoUtcNs(string value, out ulong nanoseconds)
        {
            nanoseconds = 0;
            if (string.IsNullOrEmpty(value) || !value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
                return false;

            var withoutZone = value.Substring(0, value.Length - 1);
            var dot = withoutZone.IndexOf('.');
            var secondsPart = dot >= 0 ? withoutZone.Substring(0, dot) : withoutZone;
            var fractionPart = dot >= 0 ? withoutZone.Substring(dot + 1) : string.Empty;
            if (fractionPart.Length > 9)
                return false;
            for (var i = 0; i < fractionPart.Length; i++)
                if (!char.IsDigit(fractionPart[i]))
                    return false;

            if (!DateTimeOffset.TryParseExact(
                    secondsPart,
                    "yyyy-MM-dd'T'HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return false;
            }

            var unixSeconds = parsed.ToUnixTimeSeconds();
            if (unixSeconds < 0)
                return false;

            try
            {
                var fractionalNanoseconds = 0UL;
                if (fractionPart.Length > 0)
                {
                    var padded = fractionPart.PadRight(9, '0');
                    fractionalNanoseconds = ulong.Parse(padded, CultureInfo.InvariantCulture);
                }

                nanoseconds = checked((ulong)unixSeconds * 1000000000UL + fractionalNanoseconds);
                return true;
            }
            catch (OverflowException)
            {
                nanoseconds = 0;
                return false;
            }
        }

        private static async Task CopyAndCloseAsync(Stream source, HttpListenerResponse response)
        {
            try
            {
                if (source != null)
                    await source.CopyToAsync(response.OutputStream).ConfigureAwait(false);
            }
            finally
            {
                response.OutputStream.Close();
            }
        }

        private static Task WriteTextAsync(HttpListenerResponse response, HttpStatusCode status, string message)
        {
            return WriteBytesAsync(response, status, "text/plain", Encoding.UTF8.GetBytes(message ?? string.Empty));
        }

        private static async Task WriteBytesAsync(HttpListenerResponse response, HttpStatusCode status, string contentType, byte[] body)
        {
            var bytes = body ?? new byte[0];
            response.StatusCode = (int)status;
            response.ContentType = contentType;
            response.ContentLength64 = bytes.Length;
            try
            {
                await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
            finally
            {
                response.OutputStream.Close();
            }
        }

        private static HttpStatusCode ToHttpStatus(RemoteMcapResponseStatus status)
        {
            switch (status)
            {
                case RemoteMcapResponseStatus.Unauthorized:
                    return HttpStatusCode.Unauthorized;
                case RemoteMcapResponseStatus.NotFound:
                    return HttpStatusCode.NotFound;
                case RemoteMcapResponseStatus.Unsupported:
                    return HttpStatusCode.BadRequest;
                case RemoteMcapResponseStatus.Error:
                    return HttpStatusCode.InternalServerError;
                default:
                    return HttpStatusCode.OK;
            }
        }

        private static string FirstProblem(System.Collections.Generic.List<RemoteMcapProblem> problems)
        {
            return problems != null && problems.Count > 0
                ? problems[0].Message
                : "Remote MCAP request failed.";
        }
    }
}
