using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using RaceNetShowdown.Server.RaceNet;
using Xunit;
using static RaceNetShowdown.Server.Tests.Dirt4ProTourTests;

namespace RaceNetShowdown.Server.Tests;

public sealed class Dirt4ProTourHttpTests
{
    [Fact]
    public async Task FourHttpClientsDiscoverTheHostAndRestartDoesNotRestoreStaleRooms()
    {
        var root = Path.Combine(Path.GetTempPath(), "egonet-dirt4-protour-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var http = FreePort();
        var https = FreePort();
        while (https == http) https = FreePort();
        try
        {
            for (var run = 0; run < 2; run++)
            {
                var info = new ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                foreach (var argument in new[] { typeof(RaceNetOptions).Assembly.Location,
                    "--contentRoot", root, "--RaceNet:StoreProvider", "Sqlite",
                    "--ConnectionStrings:RaceNet", $"Data Source={Path.Combine(root, "test.db")};Pooling=False",
                    "--RaceNet:HttpPort", http.ToString(), "--RaceNet:HttpsPort", https.ToString(),
                    "--RaceNet:UseSha1ServerCertificate", "false", "--Logging:LogLevel:Default", "Warning" })
                    info.ArgumentList.Add(argument);
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
                    var players = new List<string>();
                    foreach (var name in new[] { "ProTourHost", "ProTourGuest1", "ProTourGuest2", "ProTourGuest3" })
                    {
                        var login = await Send(client, null, "LoginService.Login",
                            EgoNetBinary.Dictionary(EgoNetBinary.Dstr("Name", name)));
                        Assert.DoesNotContain(login.Session, players);
                        players.Add(login.Session);
                    }
                    var host = players[0];
                    AssertEmpty((await Send(client, players[1], "LiveLadder.GetSessionList", Search().BodyBytes)).Bytes);
                    if (run != 0) continue;

                    await Send(client, host, "LiveLadder.SubmitSession", Advertisement().BodyBytes);
                    foreach (var guest in players.Skip(1))
                    {
                        AssertOne((await Send(client, guest, "LiveLadder.GetSessionList", Search().BodyBytes)).Bytes);
                        await Send(client, guest, "LiveLadder.QuitSession", Connection().BodyBytes);
                    }
                    AssertEmpty((await Send(client, players[1], "LiveLadder.GetSessionList", Search(false).BodyBytes)).Bytes);
                    await Send(client, host, "LoginService.Tick", EgoNetBinary.Dictionary());
                    AssertOne((await Send(client, players[1], "LiveLadder.GetSessionList", Search().BodyBytes)).Bytes);
                    await Send(client, host, "LiveLadder.SessionStart", Connection().BodyBytes);
                    AssertEmpty((await Send(client, players[1], "LiveLadder.GetSessionList", Search().BodyBytes)).Bytes);
                    await Send(client, host, "LiveLadder.SubmitSession", Advertisement().BodyBytes);
                    await Send(client, host, "LiveLadder.QuitSession", Connection().BodyBytes);
                    AssertEmpty((await Send(client, players[1], "LiveLadder.GetSessionList", Search().BodyBytes)).Bytes);
                    await Send(client, host, "LiveLadder.SubmitSession", Advertisement().BodyBytes);
                    AssertOne((await Send(client, players[1], "LiveLadder.GetSessionList", Search().BodyBytes)).Bytes);
                }
                finally
                {
                    if (!server.HasExited) server.Kill(entireProcessTree: true);
                    await server.WaitForExitAsync();
                    Assert.DoesNotContain("Unhandled exception", (await output) + (await error));
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

    private static async Task<(string Session, byte[] Bytes)> Send(HttpClient client, string? session,
        string function, byte[] bytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/dirt4");
        request.Headers.Add("X-EgoNet-Function", function);
        if (session is not null) request.Headers.Add("X-EgoNet-SessionID", session);
        request.Content = new ByteArrayContent(bytes);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("0", Assert.Single(response.Headers.GetValues("X-EgoNet-Result")));
        var returnedSession = Assert.Single(response.Headers.GetValues("X-EgoNet-SessionID"));
        if (session is not null) Assert.Equal(session, returnedSession);
        var result = await response.Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain("parse-stopped", Format(result));
        return (returnedSession, result);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
