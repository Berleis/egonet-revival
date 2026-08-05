namespace RaceNetShowdown.Server;

public sealed class RaceNetOptions
{
    public static readonly string[] AllowedMethods =
    [
        "GET",
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
        "HEAD",
        "OPTIONS"
    ];

    public int HttpPort { get; init; } = 80;

    public int HttpsPort { get; init; } = 443;

    public bool ListenAnyIp { get; init; }

    public string CertificateDirectory { get; init; } = "certs";

    public string LogDirectory { get; init; } = "logs";

    public bool CaptureRequests { get; init; }

    public bool RecordCalls { get; init; }

    public string StoreProvider { get; init; } = "Local";

    public string CertificatePassword { get; init; } = "racenet-local";

    public string ServerCertificateCommonName { get; init; } = "prod.egonet.codemasters.com";

    public bool UseSha1ServerCertificate { get; init; } = true;

    public string RevocationListUrl { get; init; } = "http://prod.egonet.codemasters.com/codemasters-local-root-ca.crl";

    public int BodyPreviewBytes { get; init; } = 8192;

    public string ChallengePayloadFormat { get; init; } = "BinaryOverviewResponseOnly";

    public string GhostDownloadPayloadFormat { get; init; } = "RootBlob";

    public string[] Hostnames { get; init; } =
    [
        "prod.egonet.codemasters.com",
        "egonet.codemasters.com",
        "racenet.codemasters.com",
        "api.racenet.codemasters.com",
        "showdown.racenet.codemasters.com",
        "racenet.com",
        "www.racenet.com",
        "api.racenet.com"
    ];
}
