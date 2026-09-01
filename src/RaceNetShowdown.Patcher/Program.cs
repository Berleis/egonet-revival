using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

const string RootCertificateRelativePath = @"src\RaceNetShowdown.Server\certs\codemasters-local-root-ca.cer";
const int ExpectedRootCertificateLength = 1003;

var gameProfiles = new Dictionary<string, (string DisplayName, string DefaultGamePath, string[] Executables, string[] ProcessNames)>(StringComparer.OrdinalIgnoreCase)
{
    ["dirt-showdown"] = (
        "DiRT Showdown",
        @"C:\Program Files (x86)\Steam\steamapps\common\DiRT Showdown",
        ["showdown.exe", "showdown_avx.exe"],
        ["showdown", "showdown_avx"]),
    ["grid-2"] = (
        "GRID 2",
        @"C:\Program Files (x86)\Steam\steamapps\common\grid 2",
        ["grid2.exe", "grid2_avx.exe"],
        ["grid2", "grid2_avx"])
};

var command = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "status";
var remainingArgs = args.Skip(1).Where(arg => !string.IsNullOrWhiteSpace(arg)).ToList();
var gameId = "dirt-showdown";

if (remainingArgs.Count >= 2 && remainingArgs[0] is "--game" or "-g")
{
    gameId = remainingArgs[1];
    remainingArgs.RemoveRange(0, 2);
}
else if (remainingArgs.Count > 0 && remainingArgs[0].StartsWith("--game=", StringComparison.OrdinalIgnoreCase))
{
    gameId = remainingArgs[0]["--game=".Length..];
    remainingArgs.RemoveAt(0);
}
else if (remainingArgs.Count > 0 && gameProfiles.ContainsKey(remainingArgs[0]))
{
    gameId = remainingArgs[0];
    remainingArgs.RemoveAt(0);
}

if (!gameProfiles.TryGetValue(gameId, out var gameProfile))
{
    Fail($"Unknown game profile: {gameId}");
}

var gamePath = remainingArgs.Count > 0
    ? remainingArgs[0]
    : gameProfile.DefaultGamePath;
var rootCertificatePath = remainingArgs.Count > 1
    ? Path.GetFullPath(remainingArgs[1])
    : Path.Combine(FindRepoRoot(), RootCertificateRelativePath);

if (!Directory.Exists(gamePath))
{
    Fail($"{gameProfile.DisplayName} folder not found: {gamePath}");
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
var executablePaths = gameProfile.Executables
    .Select(executable => Path.Combine(gamePath, executable))
    .ToArray();

switch (command)
{
    case "status":
        PrintStatus(gameProfile.DisplayName, executablePaths, localRootCertificateBytes, localRootCertificate);
        return 0;

    case "patch":
        EnsureGameIsClosed(gameProfile.DisplayName, gameProfile.ProcessNames);
        PatchExecutables(executablePaths, localRootCertificateBytes, localRootCertificate);
        return 0;

    case "restore":
        EnsureGameIsClosed(gameProfile.DisplayName, gameProfile.ProcessNames);
        RestoreExecutables(executablePaths);
        return 0;

    default:
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/RaceNetShowdown.Patcher -- status");
        Console.WriteLine("  dotnet run --project src/RaceNetShowdown.Patcher -- patch");
        Console.WriteLine("  dotnet run --project src/RaceNetShowdown.Patcher -- restore");
        Console.WriteLine("  dotnet run --project src/RaceNetShowdown.Patcher -- status --game grid-2");
        Console.WriteLine("  dotnet run --project src/RaceNetShowdown.Patcher -- patch --game grid-2 \"C:\\Path\\To\\GRID 2\"");
        Console.WriteLine("  RaceNetShowdown.Patcher.exe patch --game dirt-showdown \"C:\\Path\\To\\DiRT Showdown\" \"C:\\Path\\To\\root-ca.cer\"");
        Console.WriteLine();
        Console.WriteLine("Game profiles:");
        foreach (var profile in gameProfiles)
        {
            Console.WriteLine($"  {profile.Key}: {profile.Value.DisplayName}");
            Console.WriteLine($"    default path: {profile.Value.DefaultGamePath}");
        }
        return 2;
}

static void PrintStatus(string gameName, string[] executablePaths, byte[] localRootCertificateBytes, X509Certificate2 localRootCertificate)
{
    Console.WriteLine($"Game profile: {gameName}");
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

static void EnsureGameIsClosed(string gameName, string[] processNames)
{
    var running = Process.GetProcesses()
        .Where(process => processNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
        .Select(process => $"{process.ProcessName} ({process.Id})")
        .ToArray();

    if (running.Length > 0)
    {
        Fail($"Close {gameName} before patching: " + string.Join(", ", running));
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

    Fail("Could not find repository root. Run this tool from the EgoNet Revival workspace.");
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
