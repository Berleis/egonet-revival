using System.Text;

namespace RaceNetShowdown.Server.RaceNet;

public sealed record RaceNetResponse(
    string ContentType,
    byte[] BodyBytes,
    IReadOnlyDictionary<string, string>? Headers = null,
    int StatusCode = 200)
{
    public RaceNetResponse(
        string contentType,
        string body,
        IReadOnlyDictionary<string, string>? headers = null,
        int statusCode = 200)
        : this(contentType, Encoding.UTF8.GetBytes(body), headers, statusCode)
    {
    }
}
