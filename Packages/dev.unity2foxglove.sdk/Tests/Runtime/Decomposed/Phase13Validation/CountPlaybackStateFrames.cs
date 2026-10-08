// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Validates IRuntimeContext indirection, recording/replay controllers, McapBinaryReader bounds checks, client publish auto-increment, seek boundary behavior, coordinate roundtrip, and handler non-accumulation.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using Unity.FoxgloveSDK.IO;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Tests
{
    public static partial class Phase13Validation
    {
static int CountPlaybackStateFrames(IReadOnlyList<byte[]> frames)
        {
            var count = 0;
            foreach (var frame in frames)
            {
                if (frame.Length > 0 && frame[0] == ServerOpcode.PlaybackState)
                    count++;
            }
            return count;
        }

        static bool TryDecodePlaybackState(
            byte[] frame,
            out byte status,
            out ulong currentTimeNs,
            out float speed,
            out bool didSeek,
            out string requestId)
        {
            status = 0;
            currentTimeNs = 0;
            speed = 0;
            didSeek = false;
            requestId = string.Empty;

            if (frame == null || frame.Length < 19 || frame[0] != ServerOpcode.PlaybackState)
                return false;

            status = frame[1];
            currentTimeNs = BinaryEncoding.ReadU64LE(frame, 2);
            speed = BinaryEncoding.ReadF32LE(frame, 10);
            didSeek = frame[14] != 0;
            var requestIdLength = BinaryEncoding.ReadU32LE(frame, 15);
            if (requestIdLength > frame.Length - 19)
                return false;

            requestId = Encoding.UTF8.GetString(frame, 19, (int)requestIdLength);
            return true;
        }

        static int CountMessageFrames(IReadOnlyList<byte[]> frames)
        {
            var count = 0;
            foreach (var frame in frames)
            {
                if (BinaryEncoding.TryDecodeServerMessageData(frame, out _, out _, out _))
                    count++;
            }
            return count;
        }

        static List<ulong> MessageLogTimes(IReadOnlyList<byte[]> frames)
        {
            var result = new List<ulong>();
            foreach (var frame in frames)
            {
                if (BinaryEncoding.TryDecodeServerMessageData(frame, out _, out var logTimeNs, out _))
                    result.Add(logTimeNs);
            }
            return result;
        }

        static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var index = 0;
            while (true)
            {
                index = text.IndexOf(value, index, StringComparison.Ordinal);
                if (index < 0)
                    return count;
                count++;
                index += value.Length;
            }
        }

        static bool QuaternionRoundtripEquivalent((double X, double Y, double Z, double W) unity)
        {
            var foxglove = UnityToFoxgloveQuaternion(unity);
            var roundtrip = FoxgloveToUnityQuaternion(foxglove);
            return QuaternionsEquivalent(unity, roundtrip);
        }

        static (double X, double Y, double Z, double W) UnityToFoxgloveQuaternion(
            (double X, double Y, double Z, double W) q)
        {
            return (-q.Z, q.X, -q.Y, q.W);
        }

        static (double X, double Y, double Z, double W) FoxgloveToUnityQuaternion(
            (double X, double Y, double Z, double W) q)
        {
            return (q.Y, -q.Z, -q.X, q.W);
        }

        static bool QuaternionsEquivalent(
            (double X, double Y, double Z, double W) a,
            (double X, double Y, double Z, double W) b)
        {
            var an = NormalizeQuaternion(a);
            var bn = NormalizeQuaternion(b);
            var dot = an.X * bn.X + an.Y * bn.Y + an.Z * bn.Z + an.W * bn.W;
            return Math.Abs(Math.Abs(dot) - 1.0) < 0.000001;
        }

        static (double X, double Y, double Z, double W) NormalizeQuaternion(
            (double X, double Y, double Z, double W) q)
        {
            var length = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            if (length <= double.Epsilon)
                return (0, 0, 0, 1);

            return (q.X / length, q.Y / length, q.Z / length, q.W / length);
        }

        static string ReadRepoText(string relativePath)
        {
            return File.ReadAllText(RepoPath(relativePath));
        }

        static bool TryReadRepoText(string relativePath, out string source)
        {
            source = null;
            var root = FindRepoRoot();
            if (root == null)
            {
                Console.WriteLine("[WARN] repo root unavailable; skipping source inspection for " + relativePath);
                return false;
            }

            var localPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var path = Path.Combine(root, localPath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Required validation source is missing.", path);

            source = File.ReadAllText(path);
            return true;
        }

        static string RepoPath(string relativePath)
        {
            var root = FindRepoRoot();
            if (root == null)
                throw new DirectoryNotFoundException("Could not locate repository root for " + relativePath);
            var localPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(root, localPath);
        }

        static int GetFreeLoopbackPort()
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

        static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Packages", "dev.unity2foxglove.sdk", "package.json")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            return null;
        }
    }



}
