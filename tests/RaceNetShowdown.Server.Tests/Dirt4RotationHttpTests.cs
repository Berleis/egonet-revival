using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using RaceNetShowdown.Server.RaceNet;
using Xunit;

namespace RaceNetShowdown.Server.Tests;

public sealed class Dirt4RotationHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpEventsSurviveProcessRestartInSqlite(bool pendingReward)
    {
        var root = Path.Combine(Path.GetTempPath(), "egonet-dirt4-rotation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var connectionString = new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(root, "test.db"), Pooling = false }.ToString();
        var http = FreePort();
        var https = FreePort();
        while (http == https) https = FreePort();
        byte[] request = EgoNetBinary.Dictionary();
        string? original = null;
        long? rewardId = null;
        string? player = null;
        byte[]? issuedResult = null;
        try
        {
            for (var run = 0; run < (pendingReward ? 3 : 2); run++)
            {
                var info = new ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                foreach (var argument in new[] { typeof(RaceNetOptions).Assembly.Location,
                    "--contentRoot", root, "--RaceNet:StoreProvider", "Sqlite",
                    "--ConnectionStrings:RaceNet", connectionString, "--RaceNet:HttpPort", http.ToString(),
                    "--RaceNet:HttpsPort", https.ToString(), "--RaceNet:UseSha1ServerCertificate", "false",
                    "--Logging:LogLevel:Default", "Warning" }) info.ArgumentList.Add(argument);
                using var server = Process.Start(info)!;
                var output = server.StandardOutput.ReadToEndAsync();
                var error = server.StandardError.ReadToEndAsync();
                try
                {
                    using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{http}"),
                        Timeout = TimeSpan.FromSeconds(3) };
                    var ready = false;
                    for (var attempt = 0; attempt < 100 && !server.HasExited; attempt++)
                    {
                        try
                        {
                            using var health = await client.GetAsync("/health");
                            ready = health.IsSuccessStatusCode;
                            if (ready) break;
                        }
                        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
                        await Task.Delay(100);
                    }
                    Assert.True(ready, "HTTP server did not become ready.");
                    using var message = new HttpRequestMessage(HttpMethod.Post, "/dirt4");
                    message.Headers.Add("X-EgoNet-Function", "AsyncChallenge.GetEvents");
                    message.Content = new ByteArrayContent(request);
                    using var response = await client.SendAsync(message);
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    var formatted = EgoNetBinaryFormatter.Format(await response.Content.ReadAsByteArrayAsync());
                    Assert.Contains($"EventDescs: vvtr count={(pendingReward && run == 1 ? 6 : 5)}", formatted);
                    Assert.DoesNotContain("parse-stopped", formatted);
                    await using var db = new SqliteConnection(connectionString);
                    await db.OpenAsync();
                    await using var query = db.CreateCommand();
                    query.CommandText = "SELECT StateJson FROM Dirt4CommunityState WHERE Id = 1";
                    var json = (string)(await query.ExecuteScalarAsync())!;
                    var state = JsonSerializer.Deserialize<Dirt4PersistentState>(json)!;
                    Assert.Equal(rewardId.HasValue ? 6 : 5, state.CommunityEvents!.Count);
                    Assert.All(state.CommunityEvents.Where(r => r.EventId != rewardId), r => Assert.NotNull(r.Definition));
                    if (original is null)
                    {
                        original = json;
                        if (pendingReward)
                        {
                            query.CommandText = "SELECT ExternalId FROM PlayerProfiles LIMIT 1";
                            player = (string)(await query.ExecuteScalarAsync())!;
                            var at = DateTimeOffset.UtcNow.AddDays(-2);
                            var (history, round) = Dirt4RewardTests.CreateRound(at);
                            Dirt4RewardTests.Finish(history, round, at, player);
                            rewardId = round.EventId;
                            state.CommunityEvents.Add(history.Find(round.EventId)!);
                            query.CommandText = "UPDATE Dirt4CommunityState SET StateJson = $json WHERE Id = 1";
                            query.Parameters.AddWithValue("$json", JsonSerializer.Serialize(state));
                            Assert.Equal(1, await query.ExecuteNonQueryAsync());
                        }
                        else request = EgoNetBinary.Dictionary(EgoNetBinary.Vector("EventIds", state.CommunityEvents
                            .Select(r => EgoNetBinary.Si64("", r.EventId).WriteValue).ToArray()));
                    }
                    else if (pendingReward)
                    {
                        var round = state.CommunityEvents.Single(r => r.EventId == rewardId);
                        Assert.Equal(run == 1 ? Dirt4ResultDelivery.Pending : Dirt4ResultDelivery.Issued,
                            round.Runs[player!].ResultDelivery);
                        var eventLabel = $"EventId: si64 value={rewardId}";
                        if (run == 1) Assert.Contains(eventLabel, formatted);
                        else Assert.DoesNotContain(eventLabel, formatted);
                        using var resultsRequest = new HttpRequestMessage(HttpMethod.Post, "/dirt4");
                        resultsRequest.Headers.Add("X-EgoNet-Function", "AsyncChallenge.GetResults");
                        resultsRequest.Content = new ByteArrayContent(EgoNetBinary.Dictionary(
                            EgoNetBinary.Vector("EventIds", EgoNetBinary.Si64("", rewardId!.Value).WriteValue)));
                        using var resultsResponse = await client.SendAsync(resultsRequest);
                        Assert.Equal(HttpStatusCode.OK, resultsResponse.StatusCode);
                        var results = await resultsResponse.Content.ReadAsByteArrayAsync();
                        Assert.Contains(eventLabel, EgoNetBinaryFormatter.Format(results));
                        Assert.Contains("ActCredReward: si32 value=", EgoNetBinaryFormatter.Format(results));
                        if (issuedResult is null) issuedResult = results;
                        else Assert.Equal(issuedResult, results);
                    }
                    else Assert.Equal(original, json);
                }
                finally
                {
                    if (!server.HasExited) server.Kill(entireProcessTree: true);
                    await server.WaitForExitAsync();
                    var diagnostics = (await output) + (await error);
                    Assert.DoesNotContain("Unhandled exception", diagnostics);
                }
            }
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid temporary test directory.");
            Directory.Delete(root, recursive: true);
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
