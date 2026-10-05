namespace RaceNetShowdown.Server.Infrastructure;

public sealed class Dirt4ProTourScoreCapture(string directory)
{
    private const int MaxBodyBytes = 64 * 1024;
    private const int MaxCaptures = 20;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<string?> WriteAsync(byte[] body) => WriteAsync("scores", body);

    public async Task<string?> WriteAsync(string kind, byte[] body)
    {
        if (body.Length is 0 or > MaxBodyBytes)
            throw new ArgumentOutOfRangeException(nameof(body), "Invalid Pro Tour request size.");
        if (string.IsNullOrWhiteSpace(kind) || kind.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("Invalid Pro Tour capture kind.", nameof(kind));

        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(directory);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (Directory.EnumerateFiles(directory, "*.bin").Take(MaxCaptures).Count() == MaxCaptures)
                return null;

            var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fffffff}-{kind}-{Guid.NewGuid():N}.bin";
            var path = Path.Combine(directory, fileName);
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await file.WriteAsync(body);

            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            return fileName;
        }
        finally
        {
            _gate.Release();
        }
    }
}
