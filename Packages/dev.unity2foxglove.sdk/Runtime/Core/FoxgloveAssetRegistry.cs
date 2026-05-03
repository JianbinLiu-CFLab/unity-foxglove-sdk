using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace Unity.FoxgloveSDK.Core
{
    /// <summary>
    /// Thread-safe registry mapping URI prefixes to local file system roots for fetchAsset.
    /// No UnityEngine dependency — dotnet-testable.
    /// </summary>
    public class FoxgloveAssetRegistry
    {
        private readonly Dictionary<string, AssetRoot> _roots = new();
        private readonly object _lock = new();

        public bool HasRoots { get { lock (_lock) return _roots.Count > 0; } }

        public void RegisterRoot(string uriPrefix, string localRoot, long maxBytes = 16 * 1024 * 1024)
        {
            if (string.IsNullOrEmpty(uriPrefix)) throw new ArgumentException("uriPrefix is required", nameof(uriPrefix));
            var fullRoot = Path.GetFullPath(localRoot);
            lock (_lock) { _roots[uriPrefix] = new AssetRoot { LocalRoot = fullRoot, MaxBytes = maxBytes }; }
        }

        public bool TryResolve(string uri, out string path, out string error)
        {
            path = null; error = null;
            lock (_lock)
            {
                foreach (var (prefix, root) in _roots)
                {
                    if (!uri.StartsWith(prefix, StringComparison.Ordinal))
                        continue;
                    var relative = uri.Substring(prefix.Length);
                    relative = Uri.UnescapeDataString(relative);
                    relative = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    var resolved = Path.GetFullPath(Path.Combine(root.LocalRoot, relative));
                    if (!resolved.StartsWith(root.LocalRoot + Path.DirectorySeparatorChar) && resolved != root.LocalRoot)
                    { error = $"Path traversal denied: {uri}"; return false; }
                    if (Directory.Exists(resolved))
                    { error = $"Path is a directory: {uri}"; return false; }
                    if (!File.Exists(resolved))
                    { error = $"File not found: {uri}"; return false; }
                    var fi = new FileInfo(resolved);
                    if (fi.Length > root.MaxBytes)
                    { error = $"File exceeds size limit ({root.MaxBytes} bytes): {fi.Length}"; return false; }
                    path = resolved;
                    return true;
                }
            }
            error = $"No asset root registered for URI: {uri}";
            return false;
        }

        public byte[] ReadAsset(string uri, out string error)
        {
            if (!TryResolve(uri, out var path, out error)) return null;
            return File.ReadAllBytes(path);
        }

        public bool TryRead(string uri, out byte[] bytes, out string error)
        {
            bytes = ReadAsset(uri, out error);
            return bytes != null;
        }

        private struct AssetRoot
        {
            public string LocalRoot;
            public long MaxBytes;
        }
    }
}
