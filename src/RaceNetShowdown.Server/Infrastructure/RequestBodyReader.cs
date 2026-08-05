using System.Buffers;
using System.Text;

namespace RaceNetShowdown.Server.Infrastructure;

public sealed record CapturedBody(
    long Length,
    string Preview,
    string HexPreview,
    bool Truncated,
    byte[] PreviewBytes,
    byte[] BodyBytes);

public static class RequestBodyReader
{
    public static async Task<CapturedBody> ReadAsync(HttpRequest request, int previewBytes)
    {
        if (!MayHaveBody(request))
        {
            return new CapturedBody(0, string.Empty, string.Empty, false, [], []);
        }

        request.EnableBuffering();

        var rented = ArrayPool<byte>.Shared.Rent(8192);
        using var bodyBuffer = new MemoryStream();
        long totalBytes = 0;

        try
        {
            while (true)
            {
                var read = await request.Body.ReadAsync(rented);
                if (read == 0)
                {
                    break;
                }

                bodyBuffer.Write(rented, 0, read);
                totalBytes += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        var bodyBytes = bodyBuffer.ToArray();
        var previewLength = Math.Min(bodyBytes.Length, previewBytes);
        var previewBytesValue = new byte[previewLength];
        Array.Copy(bodyBytes, previewBytesValue, previewLength);

        var preview = Sanitize(Encoding.UTF8.GetString(previewBytesValue));
        var hexPreview = FormatHex(previewBytesValue);

        return new CapturedBody(totalBytes, preview, hexPreview, totalBytes > previewBytes, previewBytesValue, bodyBytes);
    }

    private static bool MayHaveBody(HttpRequest request)
    {
        if (request.ContentLength == 0)
        {
            return false;
        }

        if (request.ContentLength > 0)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(request.Headers.TransferEncoding.ToString()))
        {
            return true;
        }

        return HttpMethods.IsPost(request.Method) ||
               HttpMethods.IsPut(request.Method) ||
               HttpMethods.IsPatch(request.Method);
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (character is '\t' or '\r' or '\n' || character is >= ' ' and <= '~')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('.');
            }
        }

        return builder.ToString();
    }

    private static string FormatHex(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(bytes.Length * 3);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(bytes[i].ToString("X2"));
        }

        return builder.ToString();
    }
}
