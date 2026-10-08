// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Tests/Runtime
// Purpose: Phase 116 validation for the local MCAP DataLoader facade.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.IO;

namespace Unity.FoxgloveSDK.Tests
{
    /// <summary>
    /// Validation type for Phase116Validation.
    /// </summary>
    public static partial class Phase116Validation
    {
        private static bool ParametersAssignable(ParameterInfo[] parameters, object[] args)
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameterType = parameters[i].ParameterType;
                var arg = args[i];
                if (arg == null)
                {
                    if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                        return false;
                    continue;
                }

                if (!parameterType.IsInstanceOfType(arg))
                    return false;
            }

            return true;
        }

        private static bool TypeReferencesUnityEngine(Type type)
        {
            return type.GetFields(BindingFlags.Instance | BindingFlags.Public)
                       .Select(field => field.FieldType)
                       .Concat(type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                   .Where(property => property.GetIndexParameters().Length == 0)
                                   .Select(property => property.PropertyType))
                       .Any(ReferencesUnityEngineType);
        }

        private static bool ReferencesUnityEngineType(Type type)
        {
            if (type == null)
                return false;
            if ((type.Namespace ?? string.Empty).StartsWith("UnityEngine", StringComparison.Ordinal))
                return true;
            if (type.HasElementType && ReferencesUnityEngineType(type.GetElementType()))
                return true;
            return type.IsGenericType && type.GetGenericArguments().Any(ReferencesUnityEngineType);
        }

        private static List<object> InvokeMessages(object loader, string method, object query)
            => ((IEnumerable)Invoke(loader, method, query)).Cast<object>().ToList();

        private static List<object> Problems(object initialization)
            => ((IEnumerable)Member(initialization, "Problems")).Cast<object>().ToList();
        private static void Set(object target, string name, object value)
        {
            var type = target.GetType();
            var field = type.GetField(name);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            var property = type.GetProperty(name);
            if (property != null)
            {
                property.SetValue(target, value);
                return;
            }

            throw new MissingMemberException(type.FullName, name);
        }

        private static int Count(object enumerable)
            => ((IEnumerable)enumerable).Cast<object>().Count();

        private static bool BoolMember(object target, string name)
            => (bool)Member(target, name);

        private static uint UIntMember(object target, string name)
            => (uint)Member(target, name);

        private static ushort UShortMember(object target, string name)
            => (ushort)Member(target, name);

        private static ulong ULongMember(object target, string name)
            => (ulong)Member(target, name);

        private static string StringMember(object target, string name)
        {
            var value = Member(target, name);
            return value == null ? string.Empty : value.ToString();
        }

        private static void CheckTimes(List<object> messages, ulong[] expected, string name)
        {
            var actual = messages.Select(m => ULongMember(m, "LogTime")).ToArray();
            Check(actual.SequenceEqual(expected), name);
        }

        private static IEnumerable<string> ChannelTimes(List<object> messages)
            => messages.Select(m => UShortMember(m, "ChannelId") + ":" + ULongMember(m, "LogTime"));

        private static bool Throws<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is TException)
            {
                return true;
            }
            catch (TException)
            {
                return true;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup for validation temp files.
            }
        }

        private static string ReadRepoText(string relativePath)
        {
            var path = RepoPath(relativePath);
            if (!File.Exists(path))
                throw new FileNotFoundException("Missing required Phase116 file: " + relativePath, path);
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string RepoPath(string relativePath)
            => Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        private static string RepoRoot()
        {
            if (_repoRoot != null)
                return _repoRoot;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
                    || File.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    _repoRoot = dir.FullName;
                    return _repoRoot;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
        }

        private static void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);

            _passed++;
            Console.WriteLine("[PASS] " + name);
        }

        private sealed class NonSeekableStream : Stream
        {
            private readonly Stream _inner;

            public NonSeekableStream(Stream inner)
            {
                _inner = inner;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            /// <summary>
            /// Validation method for Flush.
            /// </summary>
            public override void Flush() => _inner.Flush();
            /// <summary>
            /// Validation method for Read.
            /// </summary>
            /// <param name="buffer">Buffer used by the stream operation.</param>
            /// <param name="offset">Zero-based offset used by the stream operation.</param>
            /// <param name="count">Number of bytes to read or write.</param>
            /// <returns>The value produced by the validation helper.</returns>
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            /// <summary>
            /// Validation method for Seek.
            /// </summary>
            /// <param name="offset">Zero-based offset used by the stream operation.</param>
            /// <param name="origin">Reference point for the seek operation.</param>
            /// <returns>The value produced by the validation helper.</returns>
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            /// <summary>
            /// Validation method for SetLength.
            /// </summary>
            /// <param name="value">Requested value for the stream operation.</param>
            public override void SetLength(long value) => throw new NotSupportedException();
            /// <summary>
            /// Validation method for Write.
            /// </summary>
            /// <param name="buffer">Buffer used by the stream operation.</param>
            /// <param name="offset">Zero-based offset used by the stream operation.</param>
            /// <param name="count">Number of bytes to read or write.</param>
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
