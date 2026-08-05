using System.Net;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using RaceNetShowdown.Server;

namespace RaceNetShowdown.Server.Infrastructure;

public sealed record LocalCertificateBundle(
    X509Certificate2 ServerCertificate,
    string RootCertificatePath,
    string ServerCertificatePath);

public static class LocalCertificateStore
{
    private const string Sha1RsaOid = "1.2.840.113549.1.1.5";
    private const string Sha256RsaOid = "1.2.840.113549.1.1.11";

    public static LocalCertificateBundle Ensure(string contentRoot, RaceNetOptions options)
    {
        var certDirectory = Path.Combine(contentRoot, options.CertificateDirectory);
        Directory.CreateDirectory(certDirectory);

        var rootCerPath = Path.Combine(certDirectory, "codemasters-local-root-ca.cer");
        var rootPfxPath = Path.Combine(certDirectory, "codemasters-local-root-ca.pfx");
        var serverPfxPath = Path.Combine(certDirectory, "prod.egonet.codemasters.com.pfx");

        if (!File.Exists(rootPfxPath) || !File.Exists(rootCerPath) || !File.Exists(serverPfxPath))
        {
            GenerateCertificates(rootCerPath, rootPfxPath, serverPfxPath, options);
        }

        using (var rootCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                   rootPfxPath,
                   options.CertificatePassword,
                   X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet))
        {
            if (ServerCertificateNeedsRegeneration(serverPfxPath, options))
            {
                GenerateServerCertificate(rootCertificate, serverPfxPath, certDirectory, options);
            }
        }

        var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
            serverPfxPath,
            options.CertificatePassword,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet);

        return new LocalCertificateBundle(serverCertificate, rootCerPath, serverPfxPath);
    }

    private static void GenerateCertificates(
        string rootCerPath,
        string rootPfxPath,
        string serverPfxPath,
        RaceNetOptions options)
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest(
            "CN=Codemasters Online Root CA, OU=Codemasters Online, O=Codemasters Software Ltd, S=Warwickshire, C=UK",
            rootKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
            true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));

        using var rootCertificate = rootRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));

        using var rootWithPrivateKey = X509CertificateLoader.LoadPkcs12(
            rootCertificate.Export(X509ContentType.Pkcs12, options.CertificatePassword),
            options.CertificatePassword,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);

        File.WriteAllBytes(rootCerPath, rootWithPrivateKey.Export(X509ContentType.Cert));
        File.WriteAllBytes(rootPfxPath, rootWithPrivateKey.Export(X509ContentType.Pkcs12, options.CertificatePassword));

        GenerateServerCertificate(rootWithPrivateKey, serverPfxPath, Path.GetDirectoryName(serverPfxPath)!, options);
    }

    private static bool ServerCertificateNeedsRegeneration(string serverPfxPath, RaceNetOptions options)
    {
        if (!File.Exists(serverPfxPath))
        {
            return true;
        }

        try
        {
            using var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                serverPfxPath,
                options.CertificatePassword,
                X509KeyStorageFlags.EphemeralKeySet);

            var expectedSignatureAlgorithm = options.UseSha1ServerCertificate ? Sha1RsaOid : Sha256RsaOid;
            if (!string.Equals(certificate.SignatureAlgorithm.Value, expectedSignatureAlgorithm, StringComparison.Ordinal))
            {
                return true;
            }

            var commonName = certificate.GetNameInfo(X509NameType.SimpleName, false);
            if (!string.Equals(commonName, options.ServerCertificateCommonName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !certificate.HasPrivateKey;
        }
        catch
        {
            return true;
        }
    }

    private static void GenerateServerCertificate(
        X509Certificate2 rootWithPrivateKey,
        string serverPfxPath,
        string certDirectory,
        RaceNetOptions options)
    {
        if (options.UseSha1ServerCertificate)
        {
            GenerateServerCertificateWithOpenSsl(rootWithPrivateKey, serverPfxPath, certDirectory, options);
            return;
        }

        using var serverKey = RSA.Create(2048);
        var signatureHash = options.UseSha1ServerCertificate ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256;
        var commonName = string.IsNullOrWhiteSpace(options.ServerCertificateCommonName)
            ? "prod.egonet.codemasters.com"
            : options.ServerCertificateCommonName;

        var serverRequest = new CertificateRequest(
            $"CN={commonName}, O=Codemasters Software Ltd, OU=Codemasters Online",
            serverKey,
            signatureHash,
            RSASignaturePadding.Pkcs1);

        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") },
            false));

        var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
        foreach (var hostname in options.Hostnames.Append(commonName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            subjectAlternativeNames.AddDnsName(hostname);
        }

        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        subjectAlternativeNames.AddIpAddress(IPAddress.IPv6Loopback);
        serverRequest.CertificateExtensions.Add(subjectAlternativeNames.Build());
        serverRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(serverRequest.PublicKey, false));

        var serial = RandomNumberGenerator.GetBytes(16);
        var now = DateTimeOffset.UtcNow;
        var rootNotBefore = new DateTimeOffset(rootWithPrivateKey.NotBefore.ToUniversalTime());
        var rootNotAfter = new DateTimeOffset(rootWithPrivateKey.NotAfter.ToUniversalTime());
        var notBefore = rootNotBefore > now.AddDays(-1) ? rootNotBefore : now.AddDays(-1);
        var notAfter = now.AddYears(2) < rootNotAfter.AddDays(-1) ? now.AddYears(2) : rootNotAfter.AddDays(-1);

        using var serverCertificate = serverRequest.Create(
            rootWithPrivateKey,
            notBefore,
            notAfter,
            serial);

        using var serverWithPrivateKey = serverCertificate.CopyWithPrivateKey(serverKey);
        File.WriteAllBytes(serverPfxPath, serverWithPrivateKey.Export(X509ContentType.Pkcs12, options.CertificatePassword));
    }

    private static void GenerateServerCertificateWithOpenSsl(
        X509Certificate2 rootWithPrivateKey,
        string serverPfxPath,
        string certDirectory,
        RaceNetOptions options)
    {
        var commonName = string.IsNullOrWhiteSpace(options.ServerCertificateCommonName)
            ? "prod.egonet.codemasters.com"
            : options.ServerCertificateCommonName;

        var rootPemPath = Path.Combine(certDirectory, "codemasters-local-root-ca.pem");
        var rootKeyPath = Path.Combine(certDirectory, "codemasters-local-root-ca.key");
        var serverConfigPath = Path.Combine(certDirectory, "openssl-compatible-server.cnf");
        var serverKeyPath = Path.Combine(certDirectory, $"{commonName}.key");
        var serverCsrPath = Path.Combine(certDirectory, $"{commonName}.csr");
        var serverPemPath = Path.Combine(certDirectory, $"{commonName}.pem");

        EnsureOpenSslRootFiles(rootWithPrivateKey, rootPemPath, rootKeyPath);
        WriteOpenSslServerConfig(serverConfigPath, commonName, options.Hostnames);

        RunOpenSsl("genrsa", "-out", serverKeyPath, "2048");
        RunOpenSsl("req", "-new", "-key", serverKeyPath, "-out", serverCsrPath, "-config", serverConfigPath);
        RunOpenSsl(
            "x509",
            "-req",
            "-in",
            serverCsrPath,
            "-CA",
            rootPemPath,
            "-CAkey",
            rootKeyPath,
            "-CAcreateserial",
            "-out",
            serverPemPath,
            "-days",
            "825",
            "-sha1",
            "-extensions",
            "v3_server",
            "-extfile",
            serverConfigPath);
        RunOpenSsl(
            "pkcs12",
            "-export",
            "-out",
            serverPfxPath,
            "-inkey",
            serverKeyPath,
            "-in",
            serverPemPath,
            "-certfile",
            rootPemPath,
            "-passout",
            $"pass:{options.CertificatePassword}");
    }

    private static void EnsureOpenSslRootFiles(X509Certificate2 rootWithPrivateKey, string rootPemPath, string rootKeyPath)
    {
        if (!File.Exists(rootPemPath))
        {
            File.WriteAllText(rootPemPath, rootWithPrivateKey.ExportCertificatePem(), Encoding.ASCII);
        }

        if (!File.Exists(rootKeyPath))
        {
            using var rootKey = rootWithPrivateKey.GetRSAPrivateKey()
                ?? throw new InvalidOperationException("The local RaceNet root certificate has no RSA private key.");

            File.WriteAllText(rootKeyPath, rootKey.ExportPkcs8PrivateKeyPem(), Encoding.ASCII);
        }
    }

    private static void WriteOpenSslServerConfig(
        string serverConfigPath,
        string commonName,
        IReadOnlyCollection<string> hostnames)
    {
        var dnsNames = hostnames
            .Append(commonName)
            .Where(hostname => !string.IsNullOrWhiteSpace(hostname))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("[ req ]");
        builder.AppendLine("default_bits = 2048");
        builder.AppendLine("prompt = no");
        builder.AppendLine("default_md = sha1");
        builder.AppendLine("distinguished_name = dn");
        builder.AppendLine("req_extensions = req_ext");
        builder.AppendLine();
        builder.AppendLine("[ dn ]");
        builder.AppendLine($"CN = {commonName}");
        builder.AppendLine("O = Codemasters Software Ltd");
        builder.AppendLine("OU = Codemasters Online");
        builder.AppendLine();
        builder.AppendLine("[ req_ext ]");
        builder.AppendLine("subjectAltName = @alt_names");
        builder.AppendLine();
        builder.AppendLine("[ v3_server ]");
        builder.AppendLine("basicConstraints = CA:false");
        builder.AppendLine("keyUsage = digitalSignature, keyEncipherment");
        builder.AppendLine("extendedKeyUsage = serverAuth");
        builder.AppendLine("subjectAltName = @alt_names");
        builder.AppendLine();
        builder.AppendLine("[ alt_names ]");

        for (var i = 0; i < dnsNames.Length; i++)
        {
            builder.AppendLine($"DNS.{i + 1} = {dnsNames[i]}");
        }

        builder.AppendLine("IP.1 = 127.0.0.1");
        builder.AppendLine("IP.2 = ::1");

        File.WriteAllText(serverConfigPath, builder.ToString(), Encoding.ASCII);
    }

    private static void RunOpenSsl(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("openssl")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start openssl.");

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("openssl did not finish within 30 seconds.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"openssl failed with exit code {process.ExitCode}.{Environment.NewLine}{standardOutput}{standardError}");
        }
    }
}
