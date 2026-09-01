using System.Text.Json;
using RaceNetShowdown.Server.RaceNet;

namespace RaceNetShowdown.Server.Infrastructure;

public sealed class RequestCaptureLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _writeLock = new();

    public RequestCaptureLogger(string logDirectory, int bodyPreviewBytes)
    {
        Directory.CreateDirectory(logDirectory);
        LogPath = Path.Combine(logDirectory, "requests.jsonl");
        BodyDirectory = Path.Combine(logDirectory, "bodies");
        Directory.CreateDirectory(BodyDirectory);
        BodyPreviewBytes = bodyPreviewBytes;
    }

    public string LogPath { get; }

    public string BodyDirectory { get; }

    public int BodyPreviewBytes { get; }

    public Task WriteAsync(HttpContext context, CapturedBody body, RaceNetResponse response)
    {
        var request = context.Request;
        var egoNetFunction = request.Headers["X-EgoNet-Function"].ToString();
        var bodyFiles = WriteBodyFiles(body, response, egoNetFunction);
        var record = new
        {
            time = DateTimeOffset.Now,
            protocol = request.IsHttps ? "https" : "http",
            remoteAddress = context.Connection.RemoteIpAddress?.ToString(),
            method = request.Method,
            scheme = request.Scheme,
            host = request.Host.ToString(),
            path = request.Path.ToString(),
            queryString = request.QueryString.ToString(),
            httpVersion = request.Protocol,
            headers = request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToString(),
                StringComparer.OrdinalIgnoreCase),
            bodyLength = body.Length,
            bodyPreview = body.Preview,
            bodyHexPreview = body.HexPreview,
            bodyTruncated = body.Truncated,
            requestBodyPath = bodyFiles.RequestBodyPath,
            requestDecodedPath = bodyFiles.RequestDecodedPath,
            egoNetFunction,
            responseStatus = response.StatusCode,
            responseContentType = response.ContentType,
            responseHeaders = response.Headers,
            responseBodyLength = response.BodyBytes.Length,
            responsePreview = Sanitize(response.BodyBytes, 512),
            responseHexPreview = FormatHex(response.BodyBytes, 512),
            responseBodyPath = bodyFiles.ResponseBodyPath,
            responseDecodedPath = bodyFiles.ResponseDecodedPath
        };

        var line = JsonSerializer.Serialize(record, JsonOptions);

        lock (_writeLock)
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }

        Console.WriteLine(
            "[RaceNet] {0} {1}://{2}{3}{4} {5} UA {6} -> {7} (request {8} bytes, response {9} bytes)",
            request.Method,
            request.Scheme,
            request.Host,
            request.Path,
            request.QueryString,
            egoNetFunction,
            string.IsNullOrWhiteSpace(request.Headers.UserAgent.ToString())
                ? "<none>"
                : request.Headers.UserAgent.ToString(),
            response.StatusCode,
            body.Length,
            response.BodyBytes.Length);

        if (!string.IsNullOrWhiteSpace(body.Preview))
        {
            Console.WriteLine("[RaceNet body] {0}", body.Preview);
        }

        if (!string.IsNullOrWhiteSpace(body.HexPreview))
        {
            Console.WriteLine("[RaceNet body hex] {0}", body.HexPreview);
        }

        if (!string.IsNullOrWhiteSpace(bodyFiles.RequestDecodedPath) ||
            !string.IsNullOrWhiteSpace(bodyFiles.ResponseDecodedPath))
        {
            Console.WriteLine(
                "[RaceNet files] request: {0} response: {1}",
                bodyFiles.RequestDecodedPath ?? "<empty>",
                bodyFiles.ResponseDecodedPath ?? "<empty>");
        }

        return Task.CompletedTask;
    }

    private BodyFileSet WriteBodyFiles(CapturedBody body, RaceNetResponse response, string egoNetFunction)
    {
        if (body.BodyBytes.Length == 0 && response.BodyBytes.Length == 0)
        {
            return BodyFileSet.Empty;
        }

        var prefix = $"{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{SanitizeFileName(egoNetFunction)}";
        string? requestBodyPath = null;
        string? requestDecodedPath = null;
        string? responseBodyPath = null;
        string? responseDecodedPath = null;

        lock (_writeLock)
        {
            if (body.BodyBytes.Length > 0)
            {
                requestBodyPath = Path.Combine(BodyDirectory, $"{prefix}-request.bin");
                File.WriteAllBytes(requestBodyPath, body.BodyBytes);

                requestDecodedPath = Path.Combine(BodyDirectory, $"{prefix}-request.txt");
                File.WriteAllText(requestDecodedPath, EgoNetBinaryFormatter.Format(body.BodyBytes));
            }

            if (response.BodyBytes.Length > 0)
            {
                responseBodyPath = Path.Combine(BodyDirectory, $"{prefix}-response.bin");
                File.WriteAllBytes(responseBodyPath, response.BodyBytes);

                responseDecodedPath = Path.Combine(BodyDirectory, $"{prefix}-response.txt");
                File.WriteAllText(responseDecodedPath, EgoNetBinaryFormatter.Format(response.BodyBytes));
            }
        }

        return new BodyFileSet(requestBodyPath, requestDecodedPath, responseBodyPath, responseDecodedPath);
    }

    private static string SanitizeFileName(string value)
    {
        var fallback = string.IsNullOrWhiteSpace(value) ? "http" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(fallback.Length);

        foreach (var character in fallback)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }

    private static string Sanitize(byte[] bytes, int maxBytes)
    {
        var builder = new System.Text.StringBuilder(Math.Min(bytes.Length, maxBytes));
        var length = Math.Min(bytes.Length, maxBytes);

        for (var i = 0; i < length; i++)
        {
            var value = bytes[i];
            builder.Append(value is 9 or 10 or 13 || value is >= 32 and <= 126
                ? (char)value
                : '.');
        }

        return builder.ToString();
    }

    private static string FormatHex(byte[] bytes, int maxBytes)
    {
        var length = Math.Min(bytes.Length, maxBytes);
        if (length == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(length * 3);
        for (var i = 0; i < length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(bytes[i].ToString("X2"));
        }

        return builder.ToString();
    }

    private sealed record BodyFileSet(
        string? RequestBodyPath,
        string? RequestDecodedPath,
        string? ResponseBodyPath,
        string? ResponseDecodedPath)
    {
        public static BodyFileSet Empty { get; } = new(null, null, null, null);
    }
}
