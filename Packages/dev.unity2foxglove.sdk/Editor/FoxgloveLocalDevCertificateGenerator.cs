// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor
// Purpose: Generate local-development WSS certificates for the
// FoxgloveManager Inspector without importing trust into the OS.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>
    /// Generates self-signed local-development certificates for WSS smoke tests.
    /// The generated private key stays under the ignored <c>UserSettings</c>
    /// directory and must not be treated as production trust material.
    /// </summary>
    internal static class FoxgloveLocalDevCertificateGenerator
    {
        /// <summary>RSA key size used for local-development certificates.</summary>
        internal const int RsaKeySizeBits = 2048;

        /// <summary>Certificate validity window in days for local-development WSS smoke tests.</summary>
        internal const int ValidityDays = 365;

        /// <summary>Project-relative ignored directory for generated local certificates.</summary>
        internal const string RelativeCertificateDirectory = "UserSettings/Unity2Foxglove/Certificates";

        /// <summary>PFX filename used for the generated server certificate and private key.</summary>
        internal const string PfxFileName = "unity2foxglove-local-dev.pfx";

        /// <summary>Public certificate filename users can import into a trust store after manual fingerprint verification.</summary>
        internal const string RootCaFileName = "unity2foxglove-local-dev-root.crt";

        /// <summary>Temporary RSA private key filename used only while OpenSSL exports the PFX.</summary>
        private const string PrivateKeyFileName = "unity2foxglove-local-dev.key";

        /// <summary>Temporary OpenSSL configuration filename containing SAN and certificate extension settings.</summary>
        private const string OpenSslConfigFileName = "unity2foxglove-local-dev.openssl.cnf";

        /// <summary>Windows OpenSSL executable name used when resolving tools on PATH.</summary>
        private const string WindowsOpenSslExecutableName = "openssl.exe";

        /// <summary>Unix-like OpenSSL executable name used when resolving tools on PATH.</summary>
        private const string OpenSslExecutableName = "openssl";

        /// <summary>Generate a local-development WSS certificate and return its file paths.</summary>
        public static FoxgloveLocalDevCertificateResult Generate(string host)
        {
            var outputDirectory = Path.Combine(ProjectRoot, RelativeCertificateDirectory);
            Directory.CreateDirectory(outputDirectory);

            var pfxPath = Path.Combine(outputDirectory, PfxFileName);
            var rootCaPath = Path.Combine(outputDirectory, RootCaFileName);
            var dnsNames = BuildDnsNames(host);
            var ipAddresses = BuildIpAddresses(host);

            var openssl = FindExecutable(WindowsOpenSslExecutableName, OpenSslExecutableName);
            if (string.IsNullOrEmpty(openssl))
                throw new InvalidOperationException("OpenSSL was not found on PATH. Install OpenSSL or generate a PFX manually.");

            try
            {
                GenerateWithOpenSsl(openssl, outputDirectory, pfxPath, rootCaPath, dnsNames, ipAddresses);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"OpenSSL generation failed: {ex.Message}", ex);
            }

            return new FoxgloveLocalDevCertificateResult(
                outputDirectory,
                pfxPath,
                rootCaPath,
                string.Empty);
        }

        /// <summary>Generate PEM and PFX certificate files by invoking an OpenSSL executable.</summary>
        private static void GenerateWithOpenSsl(
            string openssl,
            string outputDirectory,
            string pfxPath,
            string rootCaPath,
            IReadOnlyList<string> dnsNames,
            IReadOnlyList<string> ipAddresses)
        {
            var keyPath = Path.Combine(outputDirectory, PrivateKeyFileName);
            var configPath = Path.Combine(outputDirectory, OpenSslConfigFileName);
            WriteOpenSslConfig(configPath, dnsNames, ipAddresses);

            try
            {
                RunTool(
                    openssl,
                    $"req -x509 -newkey rsa:{RsaKeySizeBits} -sha256 -days {ValidityDays} -nodes -keyout {Quote(keyPath)} -out {Quote(rootCaPath)} -config {Quote(configPath)}",
                    outputDirectory);
                RunTool(
                    openssl,
                    $"pkcs12 -export -out {Quote(pfxPath)} -inkey {Quote(keyPath)} -in {Quote(rootCaPath)} -passout pass: -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1",
                    outputDirectory);
            }
            finally
            {
                TryDelete(keyPath);
                TryDelete(configPath);
            }
        }

        /// <summary>Write a minimal OpenSSL config with local host names and IP addresses as SAN entries.</summary>
        private static void WriteOpenSslConfig(
            string configPath,
            IReadOnlyList<string> dnsNames,
            IReadOnlyList<string> ipAddresses)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[req]");
            sb.AppendLine("prompt = no");
            sb.AppendLine("distinguished_name = dn");
            sb.AppendLine("x509_extensions = v3_req");
            sb.AppendLine();
            sb.AppendLine("[dn]");
            sb.AppendLine("CN = Unity2Foxglove Local Dev");
            sb.AppendLine();
            sb.AppendLine("[v3_req]");
            sb.AppendLine("basicConstraints = critical,CA:TRUE");
            sb.AppendLine("keyUsage = critical,digitalSignature,keyEncipherment,keyCertSign");
            sb.AppendLine("extendedKeyUsage = serverAuth");
            sb.AppendLine("subjectAltName = @alt_names");
            sb.AppendLine();
            sb.AppendLine("[alt_names]");

            for (var i = 0; i < dnsNames.Count; i++)
                sb.AppendLine($"DNS.{i + 1} = {dnsNames[i]}");
            for (var i = 0; i < ipAddresses.Count; i++)
                sb.AppendLine($"IP.{i + 1} = {ipAddresses[i]}");

            File.WriteAllText(configPath, sb.ToString(), Encoding.ASCII);
        }

        /// <summary>Run an external certificate tool and surface stderr/stdout when it fails.</summary>
        private static void RunTool(string executable, string arguments, string workingDirectory)
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(psi))
            {
                if (process == null)
                    throw new InvalidOperationException($"Failed to start {Path.GetFileName(executable)}.");

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"{Path.GetFileName(executable)} exited with code {process.ExitCode}: {stderr}{stdout}");
            }
        }

        /// <summary>Resolve the first existing executable from explicit paths or entries on PATH.</summary>
        private static string FindExecutable(params string[] executableNames)
        {
            foreach (var name in executableNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (Path.IsPathRooted(name) && File.Exists(name))
                    return name;

                var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                foreach (var directory in path.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(directory))
                        continue;

                    var candidate = Path.Combine(directory.Trim(), name);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }

        /// <summary>Quote a process argument for the simple command lines passed to local certificate tools.</summary>
        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        /// <summary>Delete a temporary certificate generation file while suppressing cleanup-only failures.</summary>
        private static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        /// <summary>Absolute Unity project root derived from <see cref="Application.dataPath"/>.</summary>
        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>Build DNS SAN names for the generated local-development certificate.</summary>
        private static IReadOnlyList<string> BuildDnsNames(string host)
        {
            var names = new List<string> { "localhost" };
            if (!string.IsNullOrWhiteSpace(host)
                && !IPAddress.TryParse(host, out _)
                && !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(host.Trim());
            }

            return names;
        }

        /// <summary>Build IP SAN addresses for the generated local-development certificate.</summary>
        private static IReadOnlyList<string> BuildIpAddresses(string host)
        {
            var addresses = new List<string> { IPAddress.Loopback.ToString() };
            if (!string.IsNullOrWhiteSpace(host)
                && IPAddress.TryParse(host, out var parsed)
                && !addresses.Contains(parsed.ToString()))
            {
                addresses.Add(parsed.ToString());
            }

            return addresses;
        }
    }

    /// <summary>Result paths produced by the local-development certificate generator.</summary>
    internal readonly struct FoxgloveLocalDevCertificateResult
    {
        /// <summary>Absolute output directory containing generated certificate files.</summary>
        public string DirectoryPath { get; }

        /// <summary>Absolute PFX path containing the server certificate and private key.</summary>
        public string PfxPath { get; }

        /// <summary>Absolute public certificate path for manual trust import.</summary>
        public string RootCaPath { get; }

        /// <summary>PFX password. Empty for generated local-development certificates.</summary>
        public string CertificatePassword { get; }

        /// <summary>Create a result object describing the generated certificate files.</summary>
        public FoxgloveLocalDevCertificateResult(
            string directoryPath,
            string pfxPath,
            string rootCaPath,
            string certificatePassword)
        {
            DirectoryPath = directoryPath;
            PfxPath = pfxPath;
            RootCaPath = rootCaPath;
            CertificatePassword = certificatePassword;
        }
    }
}
