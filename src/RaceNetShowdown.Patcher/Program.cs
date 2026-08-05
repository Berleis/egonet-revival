using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

const string DefaultGamePath = @"C:\Program Files (x86)\Steam\steamapps\common\DiRT Showdown";
const string RootCertificateRelativePath = @"src\RaceNetShowdown.Server\certs\codemasters-local-root-ca.cer";
const int ExpectedRootCertificateLength = 1003;

var command = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "status";
var gamePath = args.Length > 1 ? args[1] : DefaultGamePath;
var repoRoot = FindRepoRoot();
var rootCertificatePath = Path.Combine(repoRoot, RootCertificateRelativePath);

if (!Directory.Exists(gamePath))
{
    Fail($"Game folder not found: {gamePath}");
}

if (!File.Exists(rootCertificatePath))
{
    Fail($"Local root certificate not found: {rootCertificatePath}");
}

var localRootCertificateBytes = File.ReadAllBytes(rootCertificatePath);
if (localRootCertificateBytes.Length != ExpectedRootCertificateLength)
{
    Fail($"Local root certificate must be {ExpectedRootCertificateLength} bytes for in-place patching, but it is {localRootCertificateBytes.Length} bytes.");
}

var localRootCertificate = X509CertificateLoader.LoadCertificate(localRootCertificateBytes);
var executablePaths = new[]
{
    Path.Combine(gamePath, "showdown.exe"),
    Path.Combine(gamePath, "showdown_avx.exe")
};

switch (command)
{
    case "status":
        PrintStatus(executablePaths, localRootCertificateBytes, localRootCertificate);
        return 0;

    case "patch":
        EnsureGameIsClosed();
        PatchExecutables(executablePaths, localRootCertificateBytes, localRootCertificate);
        return 0;

    case "restore":
        EnsureGameIsClosed();
        RestoreExecutables(executablePaths);
        return 0;

    default:
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/RaceNetShowdown.Patcher -- status");
        Console.WriteLine("  dotnet run --project src/RaceNetShowdown.Patcher -- patch");
        Console.WriteLine("  dotnet run --project src/RaceNetShowdown.Patcher -- restore");
        Console.WriteLine();
        Console.WriteLine($"Default game path: {DefaultGamePath}");
        return 2;
}

static void PrintStatus(string[] executablePaths, byte[] localRootCertificateBytes, X509Certificate2 localRootCertificate)
{
    Console.WriteLine($"Local RaceNet root: {localRootCertificate.Subject}");
    Console.WriteLine($"Local RaceNet root SHA256: {localRootCertificate.GetCertHashString(HashAlgorithmName.SHA256)}");
    Console.WriteLine();

    foreach (var executablePath in executablePaths)
    {
        Console.WriteLine(Path.GetFileName(executablePath));

        if (!File.Exists(executablePath))
        {
            Console.WriteLine("  missing");
            continue;
        }

        var candidates = FindCodemastersRootCertificates(File.ReadAllBytes(executablePath)).ToList();
        if (candidates.Count == 0)
        {
            Console.WriteLine("  root CA not found");
            continue;
        }

        foreach (var candidate in candidates)
        {
            var state = candidate.Bytes.SequenceEqual(localRootCertificateBytes) ? "patched" : "not patched";
            Console.WriteLine($"  offset 0x{candidate.Offset:x}, length {candidate.Bytes.Length}: {state}");
            Console.WriteLine($"  SHA256 {candidate.Certificate.GetCertHashString(HashAlgorithmName.SHA256)}");
        }
    }
}

static void PatchExecutables(string[] executablePaths, byte[] localRootCertificateBytes, X509Certificate2 localRootCertificate)
{
    foreach (var executablePath in executablePaths)
    {
        if (!File.Exists(executablePath))
        {
            Console.WriteLine($"Skipping missing file: {executablePath}");
            continue;
        }

        var bytes = File.ReadAllBytes(executablePath);
        var candidates = FindCodemastersRootCertificates(bytes).ToList();

        if (candidates.Count == 0)
        {
            Console.WriteLine($"{Path.GetFileName(executablePath)}: Codemasters root CA not found.");
            continue;
        }

        var changed = false;
        foreach (var candidate in candidates)
        {
            if (candidate.Bytes.SequenceEqual(localRootCertificateBytes))
            {
                Console.WriteLine($"{Path.GetFileName(executablePath)}: already patched at 0x{candidate.Offset:x}.");
                continue;
            }

            if (candidate.Bytes.Length != localRootCertificateBytes.Length)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(executablePath)}: candidate length {candidate.Bytes.Length} does not match local root length {localRootCertificateBytes.Length}.");
            }

            var backupPath = executablePath + ".racenet-original.bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(executablePath, backupPath);
                Console.WriteLine($"{Path.GetFileName(executablePath)}: backup written to {backupPath}");
            }

            Buffer.BlockCopy(localRootCertificateBytes, 0, bytes, candidate.Offset, localRootCertificateBytes.Length);
            changed = true;
            Console.WriteLine($"{Path.GetFileName(executablePath)}: patched CA at 0x{candidate.Offset:x} -> {localRootCertificate.Subject}");
        }

        if (changed)
        {
            File.WriteAllBytes(executablePath, bytes);
        }
    }
}

static void RestoreExecutables(string[] executablePaths)
{
    foreach (var executablePath in executablePaths)
    {
        var backupPath = executablePath + ".racenet-original.bak";
        if (!File.Exists(backupPath))
        {
            Console.WriteLine($"{Path.GetFileName(executablePath)}: no backup found.");
            continue;
        }

        File.Copy(backupPath, executablePath, overwrite: true);
        Console.WriteLine($"{Path.GetFileName(executablePath)}: restored from backup.");
    }
}

static IEnumerable<CertificateCandidate> FindCodemastersRootCertificates(byte[] bytes)
{
    for (var offset = 0; offset < bytes.Length - 8; offset++)
    {
        var length = TryGetDerSequenceLength(bytes, offset);
        if (length is null or < 256 or > 8192 || offset + length > bytes.Length)
        {
            continue;
        }

        var candidate = bytes.AsSpan(offset, length.Value).ToArray();
        X509Certificate2 certificate;

        try
        {
            certificate = X509CertificateLoader.LoadCertificate(candidate);
        }
        catch (CryptographicException)
        {
            continue;
        }

        if (certificate.Subject.Contains("CN=Codemasters Online Root CA", StringComparison.OrdinalIgnoreCase)
            && certificate.Subject.Contains("OU=Codemasters Online", StringComparison.OrdinalIgnoreCase))
        {
            yield return new CertificateCandidate(offset, candidate, certificate);
            offset += length.Value - 1;
        }
    }
}

static int? TryGetDerSequenceLength(byte[] bytes, int offset)
{
    if (bytes[offset] != 0x30 || offset + 1 >= bytes.Length)
    {
        return null;
    }

    var marker = bytes[offset + 1];
    if ((marker & 0x80) == 0)
    {
        return 2 + marker;
    }

    var lengthBytes = marker & 0x7f;
    if (lengthBytes is <= 0 or > 4 || offset + 2 + lengthBytes > bytes.Length)
    {
        return null;
    }

    var length = 0;
    for (var i = 0; i < lengthBytes; i++)
    {
        length = (length << 8) | bytes[offset + 2 + i];
    }

    return 2 + lengthBytes + length;
}

static void EnsureGameIsClosed()
{
    var running = Process.GetProcesses()
        .Where(process =>
            process.ProcessName.Equals("showdown", StringComparison.OrdinalIgnoreCase)
            || process.ProcessName.Equals("showdown_avx", StringComparison.OrdinalIgnoreCase))
        .Select(process => $"{process.ProcessName} ({process.Id})")
        .ToArray();

    if (running.Length > 0)
    {
        Fail("Close DiRT Showdown before patching: " + string.Join(", ", running));
    }
}

static string FindRepoRoot()
{
    var directory = new DirectoryInfo(Environment.CurrentDirectory);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "EgoNetRevival.sln"))
            || File.Exists(Path.Combine(directory.FullName, "EgoNetRevival.slnx"))
            || File.Exists(Path.Combine(directory.FullName, "DirtShowdownRaceNet.sln"))
            || File.Exists(Path.Combine(directory.FullName, "DirtShowdownRaceNet.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    Fail("Could not find repository root. Run this tool from the Dirt Showdown workspace.");
    return "";
}

static void Fail(string message)
{
    Console.Error.WriteLine(message);
    Environment.Exit(1);
}

sealed record CertificateCandidate(
    int Offset,
    byte[] Bytes,
    X509Certificate2 Certificate);
