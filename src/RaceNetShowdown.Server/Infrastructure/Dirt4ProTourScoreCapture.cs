namespace RaceNetShowdown.Server.Infrastructure;

public sealed class Dirt4ProTourScoreCapture(string directory)
{
    private const int MaxBodyBytes = 64 * 1024;
    private const int MaxCaptures = 20;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string?> WriteAsync(byte[] body)
    {
        if (body.Length is 0 or > MaxBodyBytes)
            throw new ArgumentOutOfRangeException(nameof(body), "Invalid Pro Tour score request size.");

        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(directory);
            if (OperatingSystem.IsLinux())
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (Directory.EnumerateFiles(directory, "*.bin").Take(MaxCaptures).Count() == MaxCaptures)
                return null;

            var fileName = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Guid.NewGuid():N}.bin";
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
