// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/IO/Mcap/Remote
// Purpose: Routes embedded Remote Data Loader HTTP requests to MCAP manifest/data operations.

using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.FoxgloveSDK.IO
{
    /// <summary>Routes the small Phase139B HTTP surface without depending on Unity APIs.</summary>
    internal sealed partial class RemoteMcapHttpRouter
    {
        private const string CorsAllowHeaders = "Authorization,Range,Content-Type,Accept";
        private const string CorsAllowMethods = "GET,HEAD,OPTIONS";
        private const string CorsExposeHeaders = "Accept-Ranges,Content-Length,Content-Range";
        private const string CorsMaxAgeSeconds = "86400";
        private readonly RemoteMcapDataSourcePrototype _source;

        /// <summary>Creates a router backed by one local MCAP data source.</summary>
        public RemoteMcapHttpRouter(RemoteMcapDataSourcePrototype source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>Handles one HTTP request and closes its response stream.</summary>
        public Task HandleAsync(HttpListenerContext context)
        {
            return HandleAsync(context, CancellationToken.None);
        }

        /// <summary>Handles one HTTP request and closes its response stream.</summary>
        public Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            ApplyCorsHeaders(context.Response);
            if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                return WritePreflightAsync(context.Response);

            var path = context.Request.Url?.AbsolutePath ?? string.Empty;
            if (string.Equals(path, "/v1/manifest", StringComparison.Ordinal))
                return HandleManifestAsync(context);
            if (string.Equals(path, "/v1/data", StringComparison.Ordinal))
                return HandleDataAsync(context, cancellationToken);
            if (string.Equals(path, _source.DirectFileRoute, StringComparison.Ordinal))
                return HandleDirectFileAsync(context, cancellationToken);

            return WriteTextAsync(context.Response, HttpStatusCode.NotFound, "Unsupported Remote Data Loader route.");
        }

        private Task HandleManifestAsync(HttpListenerContext context)
        {
            if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                return WriteTextAsync(context.Response, HttpStatusCode.MethodNotAllowed, "GET is required for /v1/manifest.");

            var request = BuildRequest(context);
            var bytes = _source.GetManifestBytes(request, out var error);
            if (error != null)
                return WriteTextAsync(context.Response, ToHttpStatus(error.Status), FirstProblem(error.Problems));

            return WriteBytesAsync(context.Response, HttpStatusCode.OK, "application/json", bytes);
        }

        private async Task HandleDataAsync(HttpListenerContext context, CancellationToken cancellationToken)
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

                await CopyAndCloseAsync(data.DataStream, response, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task HandleDirectFileAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var method = context.Request.HttpMethod;
            var isHead = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);
            if (!isHead && !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTextAsync(context.Response, HttpStatusCode.MethodNotAllowed, "GET or HEAD is required for direct MCAP files.").ConfigureAwait(false);
                return;
            }

            var request = BuildRequest(context);
            using (var data = _source.GetDirectFileStream(request))
            {
                if (data.Status != RemoteMcapResponseStatus.Ok)
                {
                    await WriteTextAsync(context.Response, ToHttpStatus(data.Status), FirstProblem(data.Problems)).ConfigureAwait(false);
                    return;
                }

                var response = context.Response;
                response.ContentType = string.IsNullOrEmpty(data.ContentType) ? "application/octet-stream" : data.ContentType;
                response.AddHeader("Accept-Ranges", "bytes");

                if (!TryParseByteRange(context.Request.Headers["Range"], data.Length, out var start, out var end, out var rangeProblem))
                {
                    response.AddHeader("Content-Range", "bytes */" + data.Length.ToString(CultureInfo.InvariantCulture));
                    await WriteTextAsync(response, HttpStatusCode.RequestedRangeNotSatisfiable, rangeProblem).ConfigureAwait(false);
                    return;
                }

                if (start >= 0)
                {
                    var length = end - start + 1;
                    response.StatusCode = (int)HttpStatusCode.PartialContent;
                    response.ContentLength64 = length;
                    response.AddHeader(
                        "Content-Range",
                        "bytes "
                        + start.ToString(CultureInfo.InvariantCulture)
                        + "-"
                        + end.ToString(CultureInfo.InvariantCulture)
                        + "/"
                        + data.Length.ToString(CultureInfo.InvariantCulture));
                    data.DataStream.Seek(start, SeekOrigin.Begin);
                    await CopyAndCloseAsync(data.DataStream, response, isHead ? 0 : length, cancellationToken).ConfigureAwait(false);
                    return;
                }

                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentLength64 = data.Length;
                await CopyAndCloseAsync(data.DataStream, response, isHead ? 0 : data.Length, cancellationToken).ConfigureAwait(false);
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

        private static async Task CopyAndCloseAsync(Stream source, HttpListenerResponse response)
        {
            await CopyAndCloseAsync(source, response, -1, CancellationToken.None).ConfigureAwait(false);
        }

        private static async Task CopyAndCloseAsync(Stream source, HttpListenerResponse response, CancellationToken cancellationToken)
        {
            await CopyAndCloseAsync(source, response, -1, cancellationToken).ConfigureAwait(false);
        }

        private static async Task CopyAndCloseAsync(Stream source, HttpListenerResponse response, long maxBytes)
        {
            await CopyAndCloseAsync(source, response, maxBytes, CancellationToken.None).ConfigureAwait(false);
        }

        private static async Task CopyAndCloseAsync(
            Stream source,
            HttpListenerResponse response,
            long maxBytes,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (source != null && maxBytes != 0)
                {
                    if (maxBytes < 0)
                    {
                        await source.CopyToAsync(response.OutputStream, 81920, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        var buffer = ArrayPool<byte>.Shared.Rent(81920);
                        try
                        {
                            var remaining = maxBytes;
                            while (remaining > 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var readSize = remaining < buffer.Length ? (int)remaining : buffer.Length;
                                var read = await source.ReadAsync(buffer, 0, readSize, cancellationToken).ConfigureAwait(false);
                                if (read <= 0)
                                    break;

                                await response.OutputStream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                                remaining -= read;
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }
                }
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
            var bytes = body ?? Array.Empty<byte>();
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

        private static void ApplyCorsHeaders(HttpListenerResponse response)
        {
            response.AddHeader("Access-Control-Allow-Origin", "*");
            response.AddHeader("Access-Control-Allow-Methods", CorsAllowMethods);
            response.AddHeader("Access-Control-Allow-Headers", CorsAllowHeaders);
            response.AddHeader("Access-Control-Expose-Headers", CorsExposeHeaders);
            response.AddHeader("Access-Control-Allow-Private-Network", "true");
            response.AddHeader("Access-Control-Max-Age", CorsMaxAgeSeconds);
        }

        private static Task WritePreflightAsync(HttpListenerResponse response)
        {
            response.StatusCode = (int)HttpStatusCode.NoContent;
            response.ContentLength64 = 0;
            response.OutputStream.Close();
            return Task.CompletedTask;
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
