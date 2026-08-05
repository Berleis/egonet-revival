using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

var ports = ParsePorts(args);
var listeners = new List<TcpListener>();
using var shutdown = new CancellationTokenSource();

Console.WriteLine("DiRT Showdown RaceNet TCP/TLS probe");
Console.WriteLine("Close RaceNetShowdown.Server before running this, because both use ports 80/443.");
Console.WriteLine("Press Ctrl+C to stop.");
Console.WriteLine();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();

    foreach (var listener in listeners)
    {
        listener.Stop();
    }
};

foreach (var port in ports)
{
    TryStartListener(IPAddress.Loopback, port, listeners);
    TryStartListener(IPAddress.IPv6Loopback, port, listeners);
}

if (listeners.Count == 0)
{
    Console.WriteLine("No listener started. The ports are probably already in use.");
    return 1;
}

var acceptLoops = listeners
    .Select(listener => AcceptLoopAsync(listener, shutdown.Token))
    .ToArray();

await Task.WhenAll(acceptLoops);
return 0;

static IReadOnlyList<int> ParsePorts(string[] args)
{
    var ports = new List<int>();

    foreach (var arg in args)
    {
        var value = arg;
        if (value.StartsWith("--ports=", StringComparison.OrdinalIgnoreCase))
        {
            value = value["--ports=".Length..];
        }
        else if (value.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
        {
            value = value["--port=".Length..];
        }

        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(item, out var port) && port is > 0 and <= 65535 && !ports.Contains(port))
            {
                ports.Add(port);
            }
        }
    }

    return ports.Count > 0 ? ports : [443, 80];
}

static void TryStartListener(IPAddress address, int port, List<TcpListener> listeners)
{
    var listener = new TcpListener(address, port);

    try
    {
        listener.Start();
        listeners.Add(listener);
        Console.WriteLine($"Listening on {address}:{port}");
    }
    catch (SocketException ex)
    {
        Console.WriteLine($"Could not listen on {address}:{port}: {ex.SocketErrorCode} - {ex.Message}");
    }
}

static async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
{
    var local = listener.LocalEndpoint?.ToString() ?? "unknown";

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken);
            _ = Task.Run(() => HandleClientAsync(client, local, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (ObjectDisposedException)
        {
            break;
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{Timestamp()} accept error on {local}: {ex.Message}");
        }
    }
}

static async Task HandleClientAsync(TcpClient client, string local, CancellationToken cancellationToken)
{
    using var _ = client;

    var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
    Console.WriteLine();
    Console.WriteLine($"{Timestamp()} connection {remote} -> {local}");

    try
    {
        using var firstReadTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        firstReadTimeout.CancelAfter(TimeSpan.FromSeconds(5));

        var data = await ReadAvailableBytesAsync(client, firstReadTimeout.Token);
        if (data.Length == 0)
        {
            Console.WriteLine("  no bytes received before timeout/close");
            return;
        }

        DescribeTraffic(data);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("  read timed out");
    }
    catch (IOException ex)
    {
        Console.WriteLine($"  socket closed while reading: {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  probe error: {ex}");
    }
}

static async Task<byte[]> ReadAvailableBytesAsync(TcpClient client, CancellationToken cancellationToken)
{
    const int maxBytes = 16 * 1024;

    var buffer = new byte[maxBytes];
    var total = 0;
    var stream = client.GetStream();

    var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
    total += read;

    await Task.Delay(100, cancellationToken);

    while (client.Available > 0 && total < buffer.Length)
    {
        read = await stream.ReadAsync(
            buffer.AsMemory(total, Math.Min(client.Available, buffer.Length - total)),
            cancellationToken);

        if (read == 0)
        {
            break;
        }

        total += read;
    }

    return buffer[..total];
}

static void DescribeTraffic(ReadOnlySpan<byte> data)
{
    Console.WriteLine($"  captured bytes: {data.Length}");
    Console.WriteLine($"  first bytes: {FormatHex(data, 96)}");

    if (LooksLikeHttp(data))
    {
        DescribeHttp(data);
        return;
    }

    if (LooksLikeTlsClientHello(data))
    {
        DescribeTlsClientHello(data);
        return;
    }

    if ((data[0] & 0x80) == 0x80)
    {
        Console.WriteLine("  looks like an SSLv2-compatible ClientHello");
        Console.WriteLine("  Kestrel/SChannel will usually reject this before ASP.NET sees HTTP.");
        return;
    }

    Console.WriteLine("  unknown protocol; this is not normal HTTP or a standard TLS ClientHello");
}

static bool LooksLikeHttp(ReadOnlySpan<byte> data)
{
    var text = Encoding.ASCII.GetString(data);
    return text.StartsWith("GET ", StringComparison.Ordinal) ||
           text.StartsWith("POST ", StringComparison.Ordinal) ||
           text.StartsWith("PUT ", StringComparison.Ordinal) ||
           text.StartsWith("DELETE ", StringComparison.Ordinal) ||
           text.StartsWith("HEAD ", StringComparison.Ordinal) ||
           text.StartsWith("OPTIONS ", StringComparison.Ordinal);
}

static void DescribeHttp(ReadOnlySpan<byte> data)
{
    var text = Encoding.ASCII.GetString(data);
    var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
    var requestLine = lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "<missing request line>";
    var hostLine = lines.FirstOrDefault(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));

    Console.WriteLine("  protocol: HTTP");
    Console.WriteLine($"  request: {requestLine}");
    Console.WriteLine($"  host: {hostLine?.Split(':', 2)[1].Trim() ?? "<missing>"}");
}

static bool LooksLikeTlsClientHello(ReadOnlySpan<byte> data)
{
    return data.Length >= 9 &&
           data[0] == 0x16 &&
           data[5] == 0x01;
}

static void DescribeTlsClientHello(ReadOnlySpan<byte> data)
{
    var offset = 1;
    var recordVersion = ReadVersion(ReadUInt16(data, ref offset));
    var recordLength = ReadUInt16(data, ref offset);

    offset++;
    var handshakeLength = ReadUInt24(data, ref offset);
    var clientVersion = ReadVersion(ReadUInt16(data, ref offset));

    Console.WriteLine("  protocol: TLS ClientHello");
    Console.WriteLine($"  record version: {recordVersion}");
    Console.WriteLine($"  record length: {recordLength}");
    Console.WriteLine($"  handshake length: {handshakeLength}");
    Console.WriteLine($"  client legacy version: {clientVersion}");

    if (recordLength + 5 > data.Length)
    {
        Console.WriteLine($"  note: first TLS record is incomplete in this capture ({data.Length}/{recordLength + 5} bytes)");
    }

    if (!Skip(data, ref offset, 32, "random"))
    {
        return;
    }

    if (!TryReadByte(data, ref offset, out var sessionIdLength))
    {
        Console.WriteLine("  malformed ClientHello: missing session id length");
        return;
    }

    if (!Skip(data, ref offset, sessionIdLength, "session id"))
    {
        return;
    }

    if (!TryReadUInt16(data, ref offset, out var cipherSuiteBytes))
    {
        Console.WriteLine("  malformed ClientHello: missing cipher suite length");
        return;
    }

    var cipherSuites = new List<ushort>();
    var cipherSuiteEnd = Math.Min(offset + cipherSuiteBytes, data.Length);
    while (offset + 2 <= cipherSuiteEnd)
    {
        cipherSuites.Add(ReadUInt16(data, ref offset));
    }

    if (!TryReadByte(data, ref offset, out var compressionMethodsLength))
    {
        Console.WriteLine("  malformed ClientHello: missing compression methods");
        PrintCipherSuites(cipherSuites);
        return;
    }

    if (!Skip(data, ref offset, compressionMethodsLength, "compression methods"))
    {
        PrintCipherSuites(cipherSuites);
        return;
    }

    var serverNames = new List<string>();
    var supportedVersions = new List<string>();
    var alpns = new List<string>();

    if (TryReadUInt16(data, ref offset, out var extensionsLength))
    {
        var extensionsEnd = Math.Min(offset + extensionsLength, data.Length);

        while (offset + 4 <= extensionsEnd)
        {
            var extensionType = ReadUInt16(data, ref offset);
            var extensionLength = ReadUInt16(data, ref offset);

            if (offset + extensionLength > extensionsEnd)
            {
                Console.WriteLine($"  malformed extension 0x{extensionType:X4}: declared {extensionLength} bytes");
                break;
            }

            var extensionData = data.Slice(offset, extensionLength);
            offset += extensionLength;

            switch (extensionType)
            {
                case 0x0000:
                    serverNames.AddRange(ReadServerNameExtension(extensionData));
                    break;
                case 0x0010:
                    alpns.AddRange(ReadAlpnExtension(extensionData));
                    break;
                case 0x002B:
                    supportedVersions.AddRange(ReadSupportedVersionsExtension(extensionData));
                    break;
            }
        }
    }

    Console.WriteLine($"  SNI: {(serverNames.Count == 0 ? "<none>" : string.Join(", ", serverNames))}");
    Console.WriteLine($"  supported versions: {(supportedVersions.Count == 0 ? "<not advertised>" : string.Join(", ", supportedVersions))}");
    Console.WriteLine($"  ALPN: {(alpns.Count == 0 ? "<none>" : string.Join(", ", alpns))}");
    PrintCipherSuites(cipherSuites);
}

static IReadOnlyList<string> ReadServerNameExtension(ReadOnlySpan<byte> extensionData)
{
    var names = new List<string>();
    var offset = 0;

    if (!TryReadUInt16(extensionData, ref offset, out var listLength))
    {
        return names;
    }

    var listEnd = Math.Min(offset + listLength, extensionData.Length);
    while (offset + 3 <= listEnd)
    {
        var nameType = extensionData[offset++];
        var nameLength = ReadUInt16(extensionData, ref offset);

        if (offset + nameLength > listEnd)
        {
            break;
        }

        if (nameType == 0)
        {
            names.Add(Encoding.ASCII.GetString(extensionData.Slice(offset, nameLength)));
        }

        offset += nameLength;
    }

    return names;
}

static IReadOnlyList<string> ReadSupportedVersionsExtension(ReadOnlySpan<byte> extensionData)
{
    var versions = new List<string>();
    if (extensionData.Length == 0)
    {
        return versions;
    }

    var length = extensionData[0];
    var offset = 1;
    var end = Math.Min(offset + length, extensionData.Length);

    while (offset + 2 <= end)
    {
        versions.Add(ReadVersion(ReadUInt16(extensionData, ref offset)));
    }

    return versions;
}

static IReadOnlyList<string> ReadAlpnExtension(ReadOnlySpan<byte> extensionData)
{
    var protocols = new List<string>();
    var offset = 0;

    if (!TryReadUInt16(extensionData, ref offset, out var listLength))
    {
        return protocols;
    }

    var listEnd = Math.Min(offset + listLength, extensionData.Length);
    while (offset < listEnd)
    {
        var length = extensionData[offset++];
        if (offset + length > listEnd)
        {
            break;
        }

        protocols.Add(Encoding.ASCII.GetString(extensionData.Slice(offset, length)));
        offset += length;
    }

    return protocols;
}

static void PrintCipherSuites(IReadOnlyCollection<ushort> cipherSuites)
{
    Console.WriteLine($"  cipher suites: {cipherSuites.Count}");

    foreach (var cipherSuite in cipherSuites.Take(24))
    {
        Console.WriteLine($"    {CipherSuiteName(cipherSuite)}");
    }

    if (cipherSuites.Count > 24)
    {
        Console.WriteLine($"    ... {cipherSuites.Count - 24} more");
    }
}

static string CipherSuiteName(ushort value)
{
    var name = value switch
    {
        0x0004 => "TLS_RSA_WITH_RC4_128_MD5",
        0x0005 => "TLS_RSA_WITH_RC4_128_SHA",
        0x000A => "TLS_RSA_WITH_3DES_EDE_CBC_SHA",
        0x002F => "TLS_RSA_WITH_AES_128_CBC_SHA",
        0x0035 => "TLS_RSA_WITH_AES_256_CBC_SHA",
        0x003C => "TLS_RSA_WITH_AES_128_CBC_SHA256",
        0x003D => "TLS_RSA_WITH_AES_256_CBC_SHA256",
        0x009C => "TLS_RSA_WITH_AES_128_GCM_SHA256",
        0x009D => "TLS_RSA_WITH_AES_256_GCM_SHA384",
        0xC009 => "TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA",
        0xC00A => "TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA",
        0xC013 => "TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA",
        0xC014 => "TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA",
        0xC023 => "TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256",
        0xC024 => "TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA384",
        0xC027 => "TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256",
        0xC028 => "TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA384",
        0xC02B => "TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256",
        0xC02C => "TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384",
        0xC02F => "TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256",
        0xC030 => "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384",
        0x1301 => "TLS_AES_128_GCM_SHA256",
        0x1302 => "TLS_AES_256_GCM_SHA384",
        0x1303 => "TLS_CHACHA20_POLY1305_SHA256",
        _ => null
    };

    return name is null ? $"0x{value:X4}" : $"{name} (0x{value:X4})";
}

static string FormatHex(ReadOnlySpan<byte> data, int maxBytes)
{
    var count = Math.Min(data.Length, maxBytes);
    var builder = new StringBuilder(count * 3);

    for (var i = 0; i < count; i++)
    {
        if (i > 0)
        {
            builder.Append(' ');
        }

        builder.Append(data[i].ToString("X2"));
    }

    if (data.Length > count)
    {
        builder.Append(" ...");
    }

    return builder.ToString();
}

static bool TryReadByte(ReadOnlySpan<byte> data, ref int offset, out byte value)
{
    if (offset >= data.Length)
    {
        value = 0;
        return false;
    }

    value = data[offset++];
    return true;
}

static bool TryReadUInt16(ReadOnlySpan<byte> data, ref int offset, out ushort value)
{
    if (offset + 2 > data.Length)
    {
        value = 0;
        return false;
    }

    value = ReadUInt16(data, ref offset);
    return true;
}

static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int offset)
{
    var value = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
    offset += 2;
    return value;
}

static int ReadUInt24(ReadOnlySpan<byte> data, ref int offset)
{
    var value = (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
    offset += 3;
    return value;
}

static bool Skip(ReadOnlySpan<byte> data, ref int offset, int count, string field)
{
    if (offset + count > data.Length)
    {
        Console.WriteLine($"  malformed ClientHello: incomplete {field}");
        return false;
    }

    offset += count;
    return true;
}

static string ReadVersion(ushort version)
{
    return version switch
    {
        0x0300 => "SSL 3.0 (0x0300)",
        0x0301 => "TLS 1.0 (0x0301)",
        0x0302 => "TLS 1.1 (0x0302)",
        0x0303 => "TLS 1.2 (0x0303)",
        0x0304 => "TLS 1.3 (0x0304)",
        _ => $"0x{version:X4}"
    };
}

static string Timestamp()
{
    return DateTime.Now.ToString("HH:mm:ss");
}
