// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Ros2ForUnity.Editor
// Purpose: Atomically select at most one validated Phase181 custom ROS2 typesupport add-on.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.Ros2ForUnity.Native;

namespace Unity2Foxglove.Ros2ForUnity.Editor
{
    internal static partial class Ros2ForUnityCustomTypesupportSelectionTransaction
    {
        private static void RemoveOwnedDependencies(JObject dependencies)
        {
            foreach (var property in dependencies.Properties()
                         .Where(property => property.Name.StartsWith(RuntimePackagePrefix, StringComparison.Ordinal)
                                            || property.Name.StartsWith(CustomTypesupportPackagePrefix, StringComparison.Ordinal)
                                            || property.Name == StaticInterfacePackageId)
                         .ToArray())
            {
                property.Remove();
            }
        }

        private static void AddDependency(JObject dependencies, string packageId, string reference)
        {
            var anchor = dependencies.Property(OptionalFacadePackageId)
                         ?? dependencies.Property("dev.unity2foxglove.sdk")
                         ?? dependencies.Properties().FirstOrDefault();
            if (anchor == null)
            {
                dependencies.Add(new JProperty(packageId, reference));
            }
            else
            {
                anchor.AddAfterSelf(new JProperty(packageId, reference));
            }
        }

        private static string RepositoryPackagesDirectory(string projectDirectory)
        {
            var project = new DirectoryInfo(projectDirectory);
            return Path.Combine(project.Parent?.FullName ?? string.Empty, "Packages");
        }

        private static string BuildPackageReference(string projectDirectory, string packagesDirectory, string packageId)
        {
            var from = new Uri(AppendDirectorySeparator(Path.GetFullPath(Path.Combine(projectDirectory, "Packages"))));
            var target = new Uri(Path.GetFullPath(Path.Combine(packagesDirectory, packageId)));
            return "file:" + Uri.UnescapeDataString(from.MakeRelativeUri(target).ToString()).Replace('\\', '/');
        }

        private static bool TryReadObject(string path, out JObject value)
        {
            value = null;
            try
            {
                value = JObject.Parse(File.ReadAllText(path));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static JObject ParseManifestJson(string text)
        {
            var manifest = JObject.Parse(text);
            if (!(manifest["dependencies"] is JObject))
                throw new InvalidDataException("Unity package manifest has no dependencies object.");
            return manifest;
        }

        private static void WriteAtomically(string path, string content)
        {
            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            var temporary = Path.Combine(directory, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temporary, content);
            try
            {
                File.Replace(temporary, path, null);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(temporary, path, true);
                File.Delete(temporary);
            }
            catch (IOException) when (!File.Exists(path))
            {
                File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        private static string SerializeManifest(JObject manifest, string lineEnding)
            => manifest.ToString(Formatting.Indented)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", lineEnding)
                .TrimEnd() + lineEnding;

        private static string DetectLineEnding(string text)
            => text?.Contains("\r\n", StringComparison.Ordinal) == true ? "\r\n" : "\n";

        private static string AppendDirectorySeparator(string value)
            => value.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
               || value.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? value
                : value + Path.DirectorySeparatorChar;

        private static bool IsChildPath(string candidate, string root)
        {
            try
            {
                var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsSafePackageRelativePath(string value)
            => !string.IsNullOrWhiteSpace(value)
               && value.IndexOf('\\') < 0
               && !Path.IsPathRooted(value)
               && !value.Split('/').Any(part => part == "." || part == "..");

        private static string Text(JToken token)
            => token?.Type == JTokenType.String ? token.Value<string>() ?? string.Empty : string.Empty;

        private static bool IsSha256(string value)
            => value?.Length == 64 && value.All(character => (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'));

        internal static bool HasMatchingRosPackageRevision(string value, int interfaceRevision)
        {
            return FoxRunRos2InterfaceIdentity.TryParseRosPackageRevision(value, out var parsedRevision)
                   && parsedRevision == interfaceRevision;
        }

        private static bool IsGuid(string value)
            => Guid.TryParseExact(value, "D", out _);

        private static bool IsVersion(string value)
            => Version.TryParse(value, out var version) && version.Revision >= 0;

        private static bool StringEquals(string left, string right)
            => string.Equals(left, right, StringComparison.Ordinal);

        private static string FileSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = OpenForVerificationRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        // Unity can retain an already loaded plugin with read/write access while
        // permitting readers. Verification must therefore share the read handle
        // without loading the assembly or weakening the MVID/SHA-256 comparison.
        private static FileStream OpenForVerificationRead(string path)
            => new FileStream(
                NormalizeWindowsLongPathForRead(path),
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

        internal static bool FileExistsForVerification(string path)
        {
            try
            {
                using (OpenForVerificationRead(path))
                    return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // A generated rosidl typesupport DLL name can legitimately push the
        // complete package path past legacy MAX_PATH when Unity runs from a
        // deeply nested workspace. Unity's Mono FileStream needs the Win32
        // extended-path form. Keep this read-only adaptation at the verification
        // seam; package metadata and manifest paths retain their normal form.
        internal static string NormalizeWindowsLongPathForRead(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (Path.DirectorySeparatorChar != '\\'
                || fullPath.Length < 248
                || fullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return fullPath;
            }

            return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
                ? @"\\?\UNC\" + fullPath.Substring(2)
                : @"\\?\" + fullPath;
        }

        private static byte[] ReadAllBytesForVerification(string path)
        {
            using (var stream = OpenForVerificationRead(path))
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        private static string NormalizedJsonSha256(JToken token)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(CanonicalJson(token));
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string CanonicalJson(JToken token)
        {
            if (token is JObject obj)
            {
                return "{" + string.Join(",", obj.Properties()
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => CanonicalJson(new JValue(property.Name)) + ":" + CanonicalJson(property.Value))) + "}";
            }

            if (token is JArray array)
                return "[" + string.Join(",", array.Select(CanonicalJson)) + "]";

            return token.ToString(Formatting.None);
        }

    }
}
