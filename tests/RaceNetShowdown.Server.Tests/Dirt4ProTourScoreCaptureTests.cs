using RaceNetShowdown.Server.Infrastructure;
using Xunit;

namespace RaceNetShowdown.Server.Tests;

public sealed class Dirt4ProTourScoreCaptureTests
{
    [Fact]
    public async Task StoresExactBodyInPersistentDirectoryWithBoundedCount()
    {
        var directory = Path.Combine(Path.GetTempPath(), "egonet-protour-scores-" + Guid.NewGuid().ToString("N"));
        try
        {
            var capture = new Dirt4ProTourScoreCapture(directory);
            var body = new byte[] { 0, 1, 2, 255, 0, 42 };
            for (var i = 0; i < 20; i++)
            {
                var fileName = await capture.WriteAsync(body);
                Assert.NotNull(fileName);
                Assert.Equal(body, await File.ReadAllBytesAsync(Path.Combine(directory, fileName)));
            }

            Assert.Null(await capture.WriteAsync(body));
            Assert.Equal(20, Directory.EnumerateFiles(directory, "*.bin").Count());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65537)]
    public async Task RejectsEmptyOrOversizedBody(int size)
    {
        var directory = Path.Combine(Path.GetTempPath(), "egonet-protour-scores-" + Guid.NewGuid().ToString("N"));
        var capture = new Dirt4ProTourScoreCapture(directory);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => capture.WriteAsync(new byte[size]));
        Assert.False(Directory.Exists(directory));
    }
}
