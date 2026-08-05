using RaceNetShowdown.Server.Infrastructure;
using RaceNetShowdown.Server.RaceNet;
using RaceNetShowdown.Server;
using RaceNetShowdown.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Security.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

var raceNetOptions = builder.Configuration.GetSection("RaceNet").Get<RaceNetOptions>() ?? new RaceNetOptions();
var certificateBundle = LocalCertificateStore.Ensure(builder.Environment.ContentRootPath, raceNetOptions);

if (string.Equals(raceNetOptions.StoreProvider, "SqlServer", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("RaceNet");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("RaceNet:StoreProvider is SqlServer, but ConnectionStrings:RaceNet is empty.");
    }

    builder.Services.AddDbContext<RaceNetDbContext>(options =>
    {
        options.UseSqlServer(connectionString);
    });
    builder.Services.AddScoped<IRaceNetStore, EntityFrameworkRaceNetStore>();
}
else if (string.Equals(raceNetOptions.StoreProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
{
    var connectionString = builder.Configuration.GetConnectionString("RaceNet");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("RaceNet:StoreProvider is Sqlite, but ConnectionStrings:RaceNet is empty.");
    }

    EnsureSqliteDirectory(connectionString);
    builder.Services.AddDbContext<RaceNetDbContext>(options =>
    {
        options.UseSqlite(connectionString);
    });
    builder.Services.AddScoped<IRaceNetStore, EntityFrameworkRaceNetStore>();
}
else
{
    builder.Services.AddScoped<IRaceNetStore, LocalRaceNetStore>();
}

if (args.Any(arg => string.Equals(arg, "--regenerate-certs", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine("RaceNet certificates regenerated/verified.");
    Console.WriteLine($"Server certificate: {certificateBundle.ServerCertificate.Subject}");
    Console.WriteLine($"Signature algorithm: {certificateBundle.ServerCertificate.SignatureAlgorithm.FriendlyName}");
    Console.WriteLine($"Server PFX: {certificateBundle.ServerCertificatePath}");
    Console.WriteLine($"Root CA: {certificateBundle.RootCertificatePath}");
    return;
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);

    if (raceNetOptions.ListenAnyIp)
    {
        options.ListenAnyIP(raceNetOptions.HttpPort);
        options.ListenAnyIP(raceNetOptions.HttpsPort, ConfigureHttps);
    }
    else
    {
        options.ListenLocalhost(raceNetOptions.HttpPort);
        options.ListenLocalhost(raceNetOptions.HttpsPort, ConfigureHttps);
    }

    void ConfigureHttps(Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions listenOptions)
    {
        listenOptions.UseHttps(httpsOptions =>
        {
            httpsOptions.ServerCertificate = certificateBundle.ServerCertificate;
            httpsOptions.ServerCertificateSelector = (_, serverName) =>
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} tls: SNI {serverName ?? "<none>"}");
                return certificateBundle.ServerCertificate;
            };
            httpsOptions.OnAuthenticate = (connectionContext, _) =>
            {
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} tls: handshake from {connectionContext.RemoteEndPoint}");
            };
            httpsOptions.SslProtocols =
                SslProtocols.Tls |
                SslProtocols.Tls11 |
                SslProtocols.Tls12 |
                SslProtocols.Tls13;
        });
    }
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var store = scope.ServiceProvider.GetRequiredService<IRaceNetStore>();
    await store.InitializeAsync(CancellationToken.None);
}

RequestCaptureLogger? captureLogger = raceNetOptions.CaptureRequests
    ? new RequestCaptureLogger(
        Path.Combine(app.Environment.ContentRootPath, raceNetOptions.LogDirectory),
        raceNetOptions.BodyPreviewBytes)
    : null;

var responder = new RaceNetResponder(raceNetOptions);

app.MapMethods("/racenet-root-ca.cer", ["GET", "HEAD"], () =>
    Results.File(
        certificateBundle.RootCertificatePath,
        "application/pkix-cert",
        "codemasters-local-root-ca.cer"));

app.MapMethods("/{**path}", RaceNetOptions.AllowedMethods, async context =>
{
    var body = await RequestBodyReader.ReadAsync(context.Request, raceNetOptions.BodyPreviewBytes);
    var store = context.RequestServices.GetRequiredService<IRaceNetStore>();
    var egoNetFunction = context.Request.Headers["X-EgoNet-Function"].ToString();
    var session = string.IsNullOrWhiteSpace(egoNetFunction)
        ? null
        : await store.EnsureSessionAsync(context, body, context.RequestAborted);
    var challengeSnapshot = NeedsChallengeSnapshot(egoNetFunction)
        ? await store.GetChallengeSnapshotAsync(
            session ?? throw new InvalidOperationException("RaceNet session was not created."),
            context.RequestAborted)
        : null;
    var response = await responder.BuildResponseAsync(
        context.Request,
        body,
        session,
        challengeSnapshot,
        store,
        context.RequestAborted);
    if (!string.IsNullOrWhiteSpace(egoNetFunction))
    {
        app.Logger.LogInformation(
            "RaceNet call {Function} -> {StatusCode} (request {RequestBytes} bytes, response {ResponseBytes} bytes)",
            egoNetFunction,
            response.StatusCode,
            body.BodyBytes.Length,
            response.BodyBytes.Length);
    }

    if (captureLogger is not null)
    {
        await captureLogger.WriteAsync(context, body, response);
    }

    if (raceNetOptions.RecordCalls)
    {
        try
        {
            await store.RecordCallAsync(context, body, response, context.RequestAborted);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Failed to persist RaceNet call");
        }
    }

    context.Response.StatusCode = response.StatusCode;
    context.Response.ContentType = response.ContentType;
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Connection = "keep-alive";
    context.Response.Headers.KeepAlive = "timeout=600";
    context.Response.ContentLength = response.BodyBytes.Length;

    if (response.Headers is not null)
    {
        foreach (var header in response.Headers)
        {
            context.Response.Headers[header.Key] = header.Value;
        }
    }

    if (response.BodyBytes.Length > 0)
    {
        await context.Response.Body.WriteAsync(response.BodyBytes, context.RequestAborted);
    }
});

app.Logger.LogInformation("EgoNet Revival server starting");
app.Logger.LogInformation("Active game profile: DiRT Showdown");
app.Logger.LogInformation("HTTP  endpoint: http://127.0.0.1:{Port}", raceNetOptions.HttpPort);
app.Logger.LogInformation("HTTPS endpoint: https://127.0.0.1:{Port}", raceNetOptions.HttpsPort);
app.Logger.LogInformation("Listen any IP: {ListenAnyIp}", raceNetOptions.ListenAnyIp);
app.Logger.LogInformation("RaceNet host should resolve to 127.0.0.1: prod.egonet.codemasters.com");
app.Logger.LogInformation("RaceNet store provider: {Provider}", raceNetOptions.StoreProvider);
app.Logger.LogInformation("Challenge payload format: {Format}", raceNetOptions.ChallengePayloadFormat);
app.Logger.LogInformation("Request capture: {State}", raceNetOptions.CaptureRequests ? "enabled" : "disabled");
app.Logger.LogInformation("Call persistence log: {State}", raceNetOptions.RecordCalls ? "enabled" : "disabled");
app.Logger.LogInformation(
    "Server certificate: {Subject} signed with {Algorithm}",
    certificateBundle.ServerCertificate.Subject,
    certificateBundle.ServerCertificate.SignatureAlgorithm.FriendlyName);
app.Logger.LogInformation("Root CA written to: {Path}", certificateBundle.RootCertificatePath);
if (captureLogger is not null)
{
    app.Logger.LogInformation("Requests log: {Path}", captureLogger.LogPath);
}

app.Run();

static bool NeedsChallengeSnapshot(string egoNetFunction)
{
    return egoNetFunction.Trim() is
        "AsynchronousChallengeService.AcceptChallenge" or
        "AsynchronousChallengeService.DownloadGhost" or
        "AsynchronousChallengeService.GetCompletedIssuedChallenges" or
        "AsynchronousChallengeService.GetFriendChallenges" or
        "AsynchronousChallengeService.GetFriendsOverview" or
        "AsynchronousChallengeService.GetHighestID" or
        "AsynchronousChallengeService.IssueChallenge" or
        "AsynchronousChallengeService.SubmitChallengeResult" or
        "AsynchronousChallengeService.SubmitPersonalRecord" or
        "AsynchronousChallengeService.UploadGhost";
}

static void EnsureSqliteDirectory(string connectionString)
{
    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
    if (string.IsNullOrWhiteSpace(dataSource) || dataSource is ":memory:")
    {
        return;
    }

    var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }
}
