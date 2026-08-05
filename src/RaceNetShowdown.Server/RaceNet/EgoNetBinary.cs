using System.Text;
using RaceNetShowdown.Server.Data;
using RaceNetShowdown.Server.Infrastructure;

namespace RaceNetShowdown.Server.RaceNet;

internal sealed record EgoNetField(string Name, Action<BinaryWriter> WriteValue);

internal static class EgoNetBinary
{
    public static byte[] Dictionary(params EgoNetField[] fields)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteDictionaryValue(writer, fields);
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] BlobValue(byte[] value)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteTag(writer, "blob");
        writer.Write(value.Length);
        writer.Write(value);
        writer.Flush();
        return stream.ToArray();
    }

    public static EgoNetField Dict(string name, params EgoNetField[] fields)
    {
        return new EgoNetField(name, writer => WriteDictionaryValue(writer, fields));
    }

    public static EgoNetField Vector(string name, params Action<BinaryWriter>[] values)
    {
        return new EgoNetField(name, writer =>
        {
            WriteTag(writer, "vvtr");
            writer.Write(values.Length);
            foreach (var value in values)
            {
                value(writer);
            }
        });
    }

    public static Action<BinaryWriter> DictValue(params EgoNetField[] fields)
    {
        return writer => WriteDictionaryValue(writer, fields);
    }

    public static EgoNetField Si32(string name, int value)
    {
        return new EgoNetField(name, writer =>
        {
            WriteTag(writer, "si32");
            writer.Write(value);
        });
    }

    public static EgoNetField Si64(string name, long value)
    {
        return new EgoNetField(name, writer =>
        {
            WriteTag(writer, "si64");
            writer.Write(value);
        });
    }

    public static EgoNetField Ui64(string name, ulong value)
    {
        return new EgoNetField(name, writer =>
        {
            WriteTag(writer, "ui64");
            writer.Write(value);
        });
    }

    public static EgoNetField Bool(string name, bool value)
    {
        return new EgoNetField(name, writer =>
        {
            WriteTag(writer, "bool");
            writer.Write(value);
        });
    }

    public static EgoNetField Dstr(string name, string value)
    {
        return new EgoNetField(name, writer =>
        {
            WriteTag(writer, "dstr");
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        });
    }

    public static EgoNetField Blob(string name, byte[] value)
    {
        return new EgoNetField(name, writer =>
        {
            WriteTag(writer, "blob");
            writer.Write(value.Length);
            writer.Write(value);
        });
    }

    public static EgoNetField Tutc(string name, DateTimeOffset value)
    {
        return new EgoNetField(name, writer =>
        {
            WriteTag(writer, "tutc");
            writer.Write(unchecked((int)value.ToUnixTimeSeconds()));
        });
    }

    private static void WriteDictionaryValue(BinaryWriter writer, IReadOnlyList<EgoNetField> fields)
    {
        WriteTag(writer, "vdic");
        writer.Write(fields.Count);
        foreach (var field in fields)
        {
            WriteName(writer, field.Name);
            field.WriteValue(writer);
        }
    }

    private static void WriteName(BinaryWriter writer, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name);
        if (bytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException($"EgoNet field name is too long: {name}");
        }

        writer.Write((byte)bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteTag(BinaryWriter writer, string tag)
    {
        writer.Write(Encoding.ASCII.GetBytes(tag));
    }
}

internal sealed record EgoNetChallengeRequestContext(
    IReadOnlyList<RaceNetPrincipal> Principals,
    RaceNetPrincipal? Presence,
    RaceNetChallengeDraft? ChallengeData,
    EgoNetSubmittedChallengeResult? ChallengeResult,
    long? GhostSlotId,
    byte[]? GhostData,
    int? RaceEventId,
    int? VehicleId);

internal sealed record EgoNetSubmittedChallengeResult(
    long ChallengeId,
    long Result,
    int Attempts);

internal static class EgoNetRequestParser
{
    public static IReadOnlyList<RaceNetPrincipal> ReadPrincipals(CapturedBody body)
    {
        return ReadChallengeContext(body).Principals;
    }

    public static EgoNetChallengeRequestContext ReadChallengeContext(CapturedBody body)
    {
        if (body.BodyBytes.Length == 0)
        {
            return new EgoNetChallengeRequestContext([], null, null, null, null, null, null, null);
        }

        try
        {
            using var stream = new MemoryStream(body.BodyBytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (ReadTag(reader) != "vdic")
            {
                return new EgoNetChallengeRequestContext([], null, null, null, null, null, null, null);
            }

            IReadOnlyList<RaceNetPrincipal> principals = [];
            RaceNetPrincipal? presence = null;
            RaceNetChallengeDraft? challengeData = null;
            EgoNetSubmittedChallengeResult? challengeResult = null;
            long? ghostSlotId = null;
            byte[]? ghostData = null;
            int? raceEventId = null;
            int? vehicleId = null;
            var fields = reader.ReadInt32();
            for (var i = 0; i < fields; i++)
            {
                var name = ReadName(reader);
                var tag = ReadTag(reader);

                if (name == "Principals" && tag == "vvtr")
                {
                    principals = ReadPrincipalVector(reader);
                    continue;
                }

                if (name == "Presence" && tag == "vdic")
                {
                    presence = ReadPrincipalFields(reader);
                    continue;
                }

                if (name == "ChallengeData" && tag == "vdic")
                {
                    challengeData = ReadChallengeData(reader);
                    continue;
                }

                if (name == "Results" && tag == "vdic")
                {
                    challengeResult = ReadChallengeResult(reader);
                    continue;
                }

                if ((name == "SlotId" || name == "GhostSlotID") && tag == "si64")
                {
                    ghostSlotId = reader.ReadInt64();
                    continue;
                }

                if ((name == "SlotId" || name == "GhostSlotID") && tag == "ui64")
                {
                    ghostSlotId = checked((long)reader.ReadUInt64());
                    continue;
                }

                if ((name == "SlotId" || name == "GhostSlotID") && tag == "si32")
                {
                    ghostSlotId = reader.ReadInt32();
                    continue;
                }

                if (name == "GhostData" && tag == "blob")
                {
                    ghostData = ReadBlob(reader);
                    continue;
                }

                if (name == "RaceEventID" && tag == "si32")
                {
                    raceEventId = reader.ReadInt32();
                    continue;
                }

                if (name == "VehicleID" && tag == "si32")
                {
                    vehicleId = reader.ReadInt32();
                    continue;
                }

                SkipValue(reader, tag);
            }

            return new EgoNetChallengeRequestContext(principals, presence, challengeData, challengeResult, ghostSlotId, ghostData, raceEventId, vehicleId);
        }
        catch
        {
            return new EgoNetChallengeRequestContext([], null, null, null, null, null, null, null);
        }
    }

    private static IReadOnlyList<RaceNetPrincipal> ReadPrincipalVector(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var principals = new List<RaceNetPrincipal>(Math.Min(count, 128));

        for (var i = 0; i < count; i++)
        {
            var principal = ReadPrincipal(reader);
            if (principal is not null)
            {
                principals.Add(principal);
            }
        }

        return principals;
    }

    private static RaceNetPrincipal? ReadPrincipal(BinaryReader reader)
    {
        if (ReadTag(reader) != "vdic")
        {
            return null;
        }

        return ReadPrincipalFields(reader);
    }

    private static RaceNetPrincipal? ReadPrincipalFields(BinaryReader reader)
    {
        var fields = reader.ReadInt32();
        ulong? steamId = null;
        string? name = null;

        for (var i = 0; i < fields; i++)
        {
            var fieldName = ReadName(reader);
            var tag = ReadTag(reader);

            if (fieldName == "SteamId" && tag == "ui64")
            {
                steamId = reader.ReadUInt64();
                continue;
            }

            if (fieldName == "Name" && tag == "dstr")
            {
                name = ReadString(reader);
                continue;
            }

            SkipValue(reader, tag);
        }

        return steamId is null || string.IsNullOrWhiteSpace(name)
            ? null
            : new RaceNetPrincipal(steamId.Value, name);
    }

    private static RaceNetChallengeDraft ReadChallengeData(BinaryReader reader)
    {
        var fields = reader.ReadInt32();
        var careerEventId = 0;
        var gridPosition = 0;
        var difficulty = 0;
        long resultToBeat = 0;
        var timeBased = false;
        var vehicleId = 0;
        var liveryId = 0;
        var strength = 0;
        var power = 0;
        var handling = 0;

        for (var i = 0; i < fields; i++)
        {
            var fieldName = ReadName(reader);
            var tag = ReadTag(reader);

            switch (fieldName, tag)
            {
                case ("CareerEventID", "si32"):
                    careerEventId = reader.ReadInt32();
                    break;
                case ("GridPosition", "si32"):
                    gridPosition = reader.ReadInt32();
                    break;
                case ("Difficulty", "si32"):
                    difficulty = reader.ReadInt32();
                    break;
                case ("ResultToBeat", "si64"):
                    resultToBeat = reader.ReadInt64();
                    break;
                case ("TimeBased", "bool"):
                    timeBased = reader.ReadBoolean();
                    break;
                case ("VehicleID", "si32"):
                    vehicleId = reader.ReadInt32();
                    break;
                case ("LiveryID", "si32"):
                    liveryId = reader.ReadInt32();
                    break;
                case ("Strength", "si32"):
                    strength = reader.ReadInt32();
                    break;
                case ("Power", "si32"):
                    power = reader.ReadInt32();
                    break;
                case ("Handling", "si32"):
                    handling = reader.ReadInt32();
                    break;
                default:
                    SkipValue(reader, tag);
                    break;
            }
        }

        return new RaceNetChallengeDraft(
            careerEventId,
            gridPosition,
            difficulty,
            resultToBeat,
            timeBased,
            vehicleId,
            liveryId,
            strength,
            power,
            handling);
    }

    private static EgoNetSubmittedChallengeResult ReadChallengeResult(BinaryReader reader)
    {
        var fields = reader.ReadInt32();
        long challengeId = 0;
        long result = 0;
        var attempts = 0;

        for (var i = 0; i < fields; i++)
        {
            var fieldName = ReadName(reader);
            var tag = ReadTag(reader);

            switch (fieldName, tag)
            {
                case ("ChallengeID", "si64"):
                    challengeId = reader.ReadInt64();
                    break;
                case ("ChallengeID", "ui64"):
                    challengeId = checked((long)reader.ReadUInt64());
                    break;
                case ("Result", "si64"):
                    result = reader.ReadInt64();
                    break;
                case ("Attempts", "si32"):
                    attempts = reader.ReadInt32();
                    break;
                default:
                    SkipValue(reader, tag);
                    break;
            }
        }

        return new EgoNetSubmittedChallengeResult(challengeId, result, attempts);
    }

    private static string ReadName(BinaryReader reader)
    {
        var length = reader.ReadByte();
        return Encoding.ASCII.GetString(reader.ReadBytes(length));
    }

    private static string ReadTag(BinaryReader reader)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(4));
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static byte[] ReadBlob(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        return reader.ReadBytes(length);
    }

    private static void SkipValue(BinaryReader reader, string tag)
    {
        switch (tag)
        {
            case "vdic":
                var fieldCount = reader.ReadInt32();
                for (var i = 0; i < fieldCount; i++)
                {
                    _ = ReadName(reader);
                    SkipValue(reader, ReadTag(reader));
                }
                break;

            case "vvtr":
                var itemCount = reader.ReadInt32();
                for (var i = 0; i < itemCount; i++)
                {
                    SkipValue(reader, ReadTag(reader));
                }
                break;

            case "dstr":
            case "blob":
                reader.BaseStream.Position += reader.ReadInt32();
                break;

            case "si32":
            case "tutc":
                reader.BaseStream.Position += 4;
                break;

            case "ui64":
            case "si64":
                reader.BaseStream.Position += 8;
                break;

            case "bool":
                reader.BaseStream.Position += 1;
                break;

            default:
                throw new InvalidDataException($"Unknown EgoNet value type: {tag}");
        }
    }
}
