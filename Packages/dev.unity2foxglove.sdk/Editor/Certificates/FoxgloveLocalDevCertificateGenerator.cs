// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Editor/Certificates
// Purpose: Generate local-development WSS certificates for the
// FoxgloveManager Inspector without importing trust into the OS.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Unity.FoxgloveSDK.Editor
{
    /// <summary>Certificate generation backend used by the Inspector utility.</summary>
    internal enum FoxgloveLocalDevCertificateBackend
    {
        /// <summary>Use Unity/Mono's built-in certificate APIs. This is the default user path.</summary>
        BuiltIn = 0,
        /// <summary>Use an explicitly selected or already installed OpenSSL executable.</summary>
        OpenSsl = 1
    }

    /// <summary>Failure category for local-development certificate generation.</summary>
    internal enum FoxgloveLocalDevCertificateFailureKind
    {
        BuiltInUnavailable,
        OpenSslNotFound,
        OpenSslFailed,
        Unknown
    }

    /// <summary>Options for the local-development certificate generator.</summary>
    internal readonly struct FoxgloveLocalDevCertificateOptions
    {
        public FoxgloveLocalDevCertificateOptions(
            FoxgloveLocalDevCertificateBackend backend,
            string openSslPath = "")
        {
            Backend = backend;
            OpenSslPath = openSslPath ?? string.Empty;
        }

        public FoxgloveLocalDevCertificateBackend Backend { get; }
        public string OpenSslPath { get; }

        public static FoxgloveLocalDevCertificateOptions BuiltIn =>
            new FoxgloveLocalDevCertificateOptions(FoxgloveLocalDevCertificateBackend.BuiltIn);

        public static FoxgloveLocalDevCertificateOptions OpenSsl(string openSslPath) =>
            new FoxgloveLocalDevCertificateOptions(FoxgloveLocalDevCertificateBackend.OpenSsl, openSslPath);
    }

    /// <summary>Exception type with UI-friendly failure categorization.</summary>
    internal sealed class FoxgloveLocalDevCertificateException : InvalidOperationException
    {
        public FoxgloveLocalDevCertificateException(
            FoxgloveLocalDevCertificateFailureKind kind,
            string message,
            Exception innerException = null)
            : base(message, innerException)
        {
            Kind = kind;
        }

        public FoxgloveLocalDevCertificateFailureKind Kind { get; }
    }

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

        /// <summary>Generate a local-development WSS certificate with the default built-in backend.</summary>
        public static FoxgloveLocalDevCertificateResult Generate(string host)
        {
            return Generate(host, FoxgloveLocalDevCertificateOptions.BuiltIn);
        }

        /// <summary>Generate a local-development WSS certificate with the requested backend.</summary>
        public static FoxgloveLocalDevCertificateResult Generate(
            string host,
            FoxgloveLocalDevCertificateOptions options)
        {
            var context = CreateContext(host);
            switch (options.Backend)
            {
                case FoxgloveLocalDevCertificateBackend.BuiltIn:
                    return GenerateWithBuiltIn(context);
                case FoxgloveLocalDevCertificateBackend.OpenSsl:
                    return new OpenSslLocalDevCertificateBackend(options.OpenSslPath).Generate(context);
                default:
                    throw new FoxgloveLocalDevCertificateException(
                        FoxgloveLocalDevCertificateFailureKind.Unknown,
                        $"Unsupported local certificate backend: {options.Backend}");
            }
        }

        internal static bool IsBuiltInUnavailableException(Exception ex)
        {
            return ex is PlatformNotSupportedException
                   || ex is NotSupportedException
                   || ex is CryptographicException
                   || ex is TypeLoadException
                   || ex is MissingMethodException
                   || (ex is TargetInvocationException
                       && ex.InnerException != null
                       && IsBuiltInUnavailableException(ex.InnerException))
                   || (ex is TypeInitializationException
                       && ex.InnerException != null
                       && IsBuiltInUnavailableException(ex.InnerException));
        }

        internal static FoxgloveLocalDevCertificateException CreateBuiltInUnavailableException(Exception ex)
        {
            return new FoxgloveLocalDevCertificateException(
                FoxgloveLocalDevCertificateFailureKind.BuiltInUnavailable,
                $"Built-in certificate generation is not available in this Unity/Mono profile: {ex.Message}",
                ex);
        }

        private static FoxgloveLocalDevCertificateResult GenerateWithBuiltIn(GenerationContext context)
        {
            try
            {
                return new BuiltInLocalDevCertificateBackend().Generate(context);
            }
            catch (Exception ex) when (IsBuiltInUnavailableException(ex))
            {
                throw CreateBuiltInUnavailableException(ex);
            }
        }

        private static GenerationContext CreateContext(string host)
        {
            var outputDirectory = Path.Combine(ProjectRoot, RelativeCertificateDirectory);
            Directory.CreateDirectory(outputDirectory);

            return new GenerationContext(
                outputDirectory,
                Path.Combine(outputDirectory, PfxFileName),
                Path.Combine(outputDirectory, RootCaFileName),
                BuildDnsNames(host),
                BuildIpAddresses(host));
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

        internal readonly struct GenerationContext
        {
            public GenerationContext(
                string outputDirectory,
                string pfxPath,
                string rootCaPath,
                IReadOnlyList<string> dnsNames,
                IReadOnlyList<string> ipAddresses)
            {
                OutputDirectory = outputDirectory;
                PfxPath = pfxPath;
                RootCaPath = rootCaPath;
                DnsNames = dnsNames;
                IpAddresses = ipAddresses;
            }

            public string OutputDirectory { get; }
            public string PfxPath { get; }
            public string RootCaPath { get; }
            public IReadOnlyList<string> DnsNames { get; }
            public IReadOnlyList<string> IpAddresses { get; }
        }
    }

    /// <summary>Built-in local-development certificate backend using Unity/Mono APIs.</summary>
    internal sealed class BuiltInLocalDevCertificateBackend
    {
        public FoxgloveLocalDevCertificateResult Generate(
            FoxgloveLocalDevCertificateGenerator.GenerationContext context)
        {
            try
            {
                return new MonoSecurityLocalDevCertificateBackend().Generate(context);
            }
            catch (Exception ex) when (FoxgloveLocalDevCertificateGenerator.IsBuiltInUnavailableException(ex))
            {
                throw FoxgloveLocalDevCertificateGenerator.CreateBuiltInUnavailableException(ex);
            }
        }
    }

    /// <summary>
    /// Pure managed certificate/PFX writer backed by Unity's bundled Mono.Security assembly.
    /// Reflection keeps the SDK editor assembly independent from an explicit Mono.Security reference.
    /// </summary>
    internal sealed class MonoSecurityLocalDevCertificateBackend
    {
        private const string MonoSecurityAssemblyName = "Mono.Security";
        private const string SubjectName = "CN=Unity2Foxglove Local Dev";
        private const string X509CertificateBuilderTypeName = "Mono.Security.X509.X509CertificateBuilder";
        private const string X509CertificateTypeName = "Mono.Security.X509.X509Certificate";
        private const string X509ExtensionTypeName = "Mono.Security.X509.X509Extension";
        private const string Asn1TypeName = "Mono.Security.ASN1";
        private const string Asn1ConvertTypeName = "Mono.Security.ASN1Convert";
        private const string Pkcs12TypeName = "Mono.Security.X509.PKCS12";
        private const string BasicConstraintsTypeName = "Mono.Security.X509.Extensions.BasicConstraintsExtension";
        private const string ExtendedKeyUsageTypeName = "Mono.Security.X509.Extensions.ExtendedKeyUsageExtension";
        private const string SubjectAltNameTypeName = "Mono.Security.X509.Extensions.SubjectAltNameExtension";
        private const string KeyUsageOid = "2.5.29.15";
        private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
        private static readonly byte[] LocalDevKeyUsageDer = { 0x03, 0x02, 0x02, 0xA4 };

        public FoxgloveLocalDevCertificateResult Generate(
            FoxgloveLocalDevCertificateGenerator.GenerationContext context)
        {
            using (var rsa = new RSACryptoServiceProvider(FoxgloveLocalDevCertificateGenerator.RsaKeySizeBits))
            {
                rsa.PersistKeyInCsp = false;
                var certificateDer = BuildCertificateDer(context, rsa);
                WritePkcs12(context.PfxPath, certificateDer, rsa);
                File.WriteAllText(context.RootCaPath, BuildPem("CERTIFICATE", certificateDer), Encoding.ASCII);
            }

            return new FoxgloveLocalDevCertificateResult(
                context.OutputDirectory,
                context.PfxPath,
                context.RootCaPath,
                string.Empty);
        }

        private static byte[] BuildCertificateDer(
            FoxgloveLocalDevCertificateGenerator.GenerationContext context,
            RSA rsa)
        {
            var builder = CreateMonoSecurityObject(X509CertificateBuilderTypeName, (byte)3);
            SetProperty(builder, "SerialNumber", CreatePositiveSerialNumber());
            SetProperty(builder, "IssuerName", SubjectName);
            SetProperty(builder, "SubjectName", SubjectName);
            SetProperty(builder, "NotBefore", DateTime.UtcNow.AddDays(-1));
            SetProperty(builder, "NotAfter", DateTime.UtcNow.AddDays(FoxgloveLocalDevCertificateGenerator.ValidityDays));
            SetProperty(builder, "SubjectPublicKey", rsa);
            SetProperty(builder, "Hash", "SHA256");

            var extensions = GetProperty(builder, "Extensions");
            AddExtension(extensions, BuildBasicConstraintsExtension());
            AddExtension(extensions, BuildKeyUsageExtension());
            AddExtension(extensions, BuildExtendedKeyUsageExtension());
            AddExtension(extensions, BuildSubjectAltNameExtension(context));

            return (byte[])Invoke(builder, "Sign", rsa);
        }

        private static object BuildBasicConstraintsExtension()
        {
            var extension = CreateMonoSecurityObject(BasicConstraintsTypeName);
            SetProperty(extension, "CertificateAuthority", true);
            SetProperty(extension, "Critical", true);
            return extension;
        }

        private static object BuildKeyUsageExtension()
        {
            return BuildRawExtension(KeyUsageOid, true, LocalDevKeyUsageDer);
        }

        private static object BuildExtendedKeyUsageExtension()
        {
            var extension = CreateMonoSecurityObject(ExtendedKeyUsageTypeName);
            var keyPurpose = GetProperty(extension, "KeyPurpose");
            Invoke(keyPurpose, "Add", ServerAuthenticationOid);
            return extension;
        }

        private static object BuildSubjectAltNameExtension(
            FoxgloveLocalDevCertificateGenerator.GenerationContext context)
        {
            var type = RequireMonoSecurityType(SubjectAltNameTypeName);
            var constructor = type.GetConstructor(new[]
            {
                typeof(string[]),
                typeof(string[]),
                typeof(string[]),
                typeof(string[])
            });
            if (constructor == null)
                throw new NotSupportedException("Mono.Security SubjectAltNameExtension constructor is unavailable.");

            return constructor.Invoke(new object[]
            {
                Array.Empty<string>(),
                ToArray(context.DnsNames),
                ToArray(context.IpAddresses),
                Array.Empty<string>()
            });
        }

        private static void WritePkcs12(string pfxPath, byte[] certificateDer, RSA rsa)
        {
            var certificate = CreateMonoSecurityObject(X509CertificateTypeName, certificateDer);
            var pkcs12 = CreateMonoSecurityObject(Pkcs12TypeName);
            SetProperty(pkcs12, "Password", string.Empty);
            Invoke(pkcs12, "AddCertificate", certificate);
            Invoke(pkcs12, "AddPkcs8ShroudedKeyBag", rsa);
            File.WriteAllBytes(pfxPath, (byte[])Invoke(pkcs12, "GetBytes"));
        }

        private static object BuildRawExtension(string oid, bool critical, byte[] valueDer)
        {
            var sequence = CreateMonoSecurityObject(Asn1TypeName, (byte)0x30);
            Invoke(sequence, "Add", InvokeStatic(RequireMonoSecurityType(Asn1ConvertTypeName), "FromOid", oid));
            if (critical)
                Invoke(sequence, "Add", CreateMonoSecurityObject(Asn1TypeName, (byte)0x01, new byte[] { 0xFF }));
            Invoke(sequence, "Add", CreateMonoSecurityObject(Asn1TypeName, (byte)0x04, valueDer));
            return CreateMonoSecurityObject(X509ExtensionTypeName, sequence);
        }

        private static Type RequireMonoSecurityType(string fullName)
        {
            var type = Type.GetType(fullName + ", " + MonoSecurityAssemblyName, false);
            if (type != null)
                return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            try
            {
                var assembly = Assembly.Load(MonoSecurityAssemblyName);
                type = assembly.GetType(fullName, false);
            }
            catch (Exception ex)
            {
                throw new NotSupportedException("Unity Mono.Security assembly is not available.", ex);
            }

            if (type == null)
                throw new NotSupportedException($"Unity Mono.Security type is not available: {fullName}");

            return type;
        }

        private static object CreateMonoSecurityObject(string typeName, params object[] args)
        {
            return Activator.CreateInstance(RequireMonoSecurityType(typeName), args);
        }

        private static object GetProperty(object target, string name)
        {
            var property = target.GetType().GetProperty(name);
            if (property == null)
                throw new MissingMethodException(target.GetType().FullName, name);

            return property.GetValue(target, null);
        }

        private static void SetProperty(object target, string name, object value)
        {
            var property = target.GetType().GetProperty(name);
            if (property == null)
                throw new MissingMethodException(target.GetType().FullName, name);

            property.SetValue(target, value, null);
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            var methods = target.GetType().GetMethods();
            foreach (var method in methods)
            {
                if (method.Name == name && method.GetParameters().Length == args.Length)
                    return method.Invoke(target, args);
            }

            throw new MissingMethodException(target.GetType().FullName, name);
        }

        private static object InvokeStatic(Type type, string name, params object[] args)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
            foreach (var method in methods)
            {
                if (method.Name == name && method.GetParameters().Length == args.Length)
                    return method.Invoke(null, args);
            }

            throw new MissingMethodException(type.FullName, name);
        }

        private static void AddExtension(object extensions, object extension)
        {
            Invoke(extensions, "Add", extension);
        }

        private static byte[] CreatePositiveSerialNumber()
        {
            var serial = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(serial);

            serial[0] &= 0x7f;
            var anyNonZero = false;
            for (var i = 0; i < serial.Length; i++)
                anyNonZero |= serial[i] != 0;
            if (!anyNonZero)
                serial[serial.Length - 1] = 1;

            return serial;
        }

        private static string BuildPem(string label, byte[] der)
        {
            var base64 = Convert.ToBase64String(der);
            var sb = new StringBuilder();
            sb.AppendLine($"-----BEGIN {label}-----");
            for (var i = 0; i < base64.Length; i += 64)
                sb.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
            sb.AppendLine($"-----END {label}-----");
            return sb.ToString();
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            var result = new string[values.Count];
            for (var i = 0; i < values.Count; i++)
                result[i] = values[i];
            return result;
        }
    }

    /// <summary>Optional OpenSSL certificate backend for users who explicitly select it.</summary>
    internal sealed class OpenSslLocalDevCertificateBackend
    {
        /// <summary>Temporary RSA private key filename used only while OpenSSL exports the PFX.</summary>
        private const string PrivateKeyFileName = "unity2foxglove-local-dev.key";

        /// <summary>Temporary OpenSSL configuration filename containing SAN and certificate extension settings.</summary>
        private const string OpenSslConfigFileName = "unity2foxglove-local-dev.openssl.cnf";

        private readonly string _openSslPath;

        public OpenSslLocalDevCertificateBackend(string openSslPath)
        {
            _openSslPath = openSslPath ?? string.Empty;
        }

        public FoxgloveLocalDevCertificateResult Generate(
            FoxgloveLocalDevCertificateGenerator.GenerationContext context)
        {
            var openssl = OpenSslResolver.Resolve(_openSslPath);
            if (string.IsNullOrEmpty(openssl))
            {
                throw new FoxgloveLocalDevCertificateException(
                    FoxgloveLocalDevCertificateFailureKind.OpenSslNotFound,
                    "OpenSSL was not found. Install OpenSSL, install Git for Windows, add openssl.exe to PATH, set UNITY2FOXGLOVE_OPENSSL to an OpenSSL executable or bin directory, or choose an OpenSSL executable in the Inspector.");
            }

            try
            {
                GenerateWithOpenSsl(openssl, context);
            }
            catch (Exception ex)
            {
                throw new FoxgloveLocalDevCertificateException(
                    FoxgloveLocalDevCertificateFailureKind.OpenSslFailed,
                    $"OpenSSL generation failed: {ex.Message}",
                    ex);
            }

            return new FoxgloveLocalDevCertificateResult(
                context.OutputDirectory,
                context.PfxPath,
                context.RootCaPath,
                string.Empty);
        }

        /// <summary>Generate PEM and PFX certificate files by invoking an OpenSSL executable.</summary>
        private static void GenerateWithOpenSsl(
            string openssl,
            FoxgloveLocalDevCertificateGenerator.GenerationContext context)
        {
            var keyPath = Path.Combine(context.OutputDirectory, PrivateKeyFileName);
            var configPath = Path.Combine(context.OutputDirectory, OpenSslConfigFileName);
            WriteOpenSslConfig(configPath, context.DnsNames, context.IpAddresses);

            try
            {
                RunTool(
                    openssl,
                    $"req -x509 -newkey rsa:{FoxgloveLocalDevCertificateGenerator.RsaKeySizeBits} -sha256 -days {FoxgloveLocalDevCertificateGenerator.ValidityDays} -nodes -keyout {Quote(keyPath)} -out {Quote(context.RootCaPath)} -config {Quote(configPath)}",
                    context.OutputDirectory);
                RunTool(
                    openssl,
                    $"pkcs12 -export -out {Quote(context.PfxPath)} -inkey {Quote(keyPath)} -in {Quote(context.RootCaPath)} -passout pass: -keypbe PBE-SHA1-3DES -certpbe PBE-SHA1-3DES -macalg sha1",
                    context.OutputDirectory);
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

        private const int OpenSslToolTimeoutMs = 120000;

        /// <summary>Run an external certificate tool and surface stderr/stdout when it fails.</summary>
        private static void RunTool(string executable, string arguments, string workingDirectory)
        {
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = workingDirectory
            };

            var result = FoxgloveEditorProcessRunner.Run(psi, OpenSslToolTimeoutMs);
            if (result.TimedOut)
                throw new InvalidOperationException($"{Path.GetFileName(executable)} timed out after {OpenSslToolTimeoutMs} ms: {result.Stderr}{result.Stdout}");

            if (result.ExitCode != 0)
                throw new InvalidOperationException($"{Path.GetFileName(executable)} exited with code {result.ExitCode}: {result.Stderr}{result.Stdout}");
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
    }

    /// <summary>Resolve an explicitly selected or already installed OpenSSL executable.</summary>
    internal static class OpenSslResolver
    {
        internal const string EnvironmentVariableName = "UNITY2FOXGLOVE_OPENSSL";
        private const string WindowsOpenSslExecutableName = "openssl.exe";
        private const string OpenSslExecutableName = "openssl";

        public static string Resolve(string explicitPath = "")
        {
            foreach (var candidate in EnumerateCandidates(explicitPath))
            {
                var resolved = ResolveCandidate(candidate);
                if (!string.IsNullOrEmpty(resolved))
                    return resolved;
            }

            return null;
        }

        public static bool TryResolve(out string openssl, string explicitPath = "")
        {
            openssl = Resolve(explicitPath);
            return !string.IsNullOrEmpty(openssl);
        }

        private static IEnumerable<string> EnumerateCandidates(string explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
                yield return explicitPath;

            var env = Environment.GetEnvironmentVariable(EnvironmentVariableName);
            if (!string.IsNullOrWhiteSpace(env))
                yield return env;

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var directory in path.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(directory))
                    yield return Path.Combine(directory.Trim(), PlatformExecutableName);
            }

            foreach (var commonPath in CommonInstallPaths())
                yield return commonPath;
        }

        private static string ResolveCandidate(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return null;

            var trimmed = candidate.Trim().Trim('"');
            if (Directory.Exists(trimmed))
            {
                var executable = Path.Combine(trimmed, PlatformExecutableName);
                return File.Exists(executable) ? executable : null;
            }

            return File.Exists(trimmed) ? trimmed : null;
        }

        private static string PlatformExecutableName =>
            Application.platform == RuntimePlatform.WindowsEditor
                ? WindowsOpenSslExecutableName
                : OpenSslExecutableName;

        private static IEnumerable<string> CommonInstallPaths()
        {
            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                yield return @"C:\Program Files\Git\usr\bin\openssl.exe";
                yield return @"C:\Program Files\Git\mingw64\bin\openssl.exe";
                yield return @"C:\Program Files\OpenSSL-Win64\bin\openssl.exe";
                yield return @"C:\Program Files\OpenSSL-Win32\bin\openssl.exe";
                yield return @"C:\Program Files (x86)\OpenSSL-Win32\bin\openssl.exe";
                yield break;
            }

            yield return "/usr/bin/openssl";
            yield return "/usr/local/bin/openssl";
            yield return "/opt/homebrew/bin/openssl";
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
