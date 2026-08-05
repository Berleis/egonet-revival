using System.Globalization;
using System.Text;

namespace RaceNetShowdown.Server.RaceNet;

internal static class EgoNetBinaryFormatter
{
    public static string Format(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return "<empty>";
        }

        var formatter = new Formatter(bytes);
        return formatter.Format();
    }

    private sealed class Formatter(byte[] bytes)
    {
        private const int MaxDepth = 64;
        private const int StringPreviewLength = 200;
        private const int HexPreviewLength = 64;

        private readonly StringBuilder _builder = new();
        private int _position;

        public string Format()
        {
            _builder.AppendLine($"length={bytes.Length}");

            try
            {
                ReadValue(null, 0);

                if (_position < bytes.Length)
                {
                    Line(0, $"trailing-bytes offset={Offset(_position)} length={bytes.Length - _position} hex={HexPreview(bytes.AsSpan(_position))}");
                }
            }
            catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or ArgumentOutOfRangeException)
            {
                Line(0, $"parse-stopped offset={Offset(_position)} error=\"{ex.Message}\"");
                if (_position < bytes.Length)
                {
                    Line(0, $"remaining hex={HexPreview(bytes.AsSpan(_position))}");
                }
            }

            return _builder.ToString();
        }

        private void ReadValue(string? name, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidDataException("Maximum EgoNet nesting depth exceeded.");
            }

            var offset = _position;
            var tag = ReadTag();
            var label = name is null ? tag : $"{name}: {tag}";

            switch (tag)
            {
                case "vdic":
                    ReadDictionary(label, offset, depth);
                    break;

                case "vvtr":
                    ReadVector(label, offset, depth);
                    break;

                case "dstr":
                    ReadString(label, offset, depth);
                    break;

                case "blob":
                    ReadBlob(label, offset, depth);
                    break;

                case "si08":
                    Line(depth, $"{Offset(offset)} {label} value={ReadSByte()}");
                    break;

                case "ui08":
                    Line(depth, $"{Offset(offset)} {label} value={ReadByte()}");
                    break;

                case "si16":
                    Line(depth, $"{Offset(offset)} {label} value={ReadInt16()}");
                    break;

                case "ui16":
                    Line(depth, $"{Offset(offset)} {label} value={ReadUInt16()}");
                    break;

                case "si32":
                    Line(depth, $"{Offset(offset)} {label} value={ReadInt32()}");
                    break;

                case "ui32":
                    Line(depth, $"{Offset(offset)} {label} value={ReadUInt32()}");
                    break;

                case "si64":
                    Line(depth, $"{Offset(offset)} {label} value={ReadInt64()}");
                    break;

                case "ui64":
                    Line(depth, $"{Offset(offset)} {label} value={ReadUInt64()}");
                    break;

                case "fl32":
                    Line(depth, $"{Offset(offset)} {label} value={ReadSingle().ToString(CultureInfo.InvariantCulture)}");
                    break;

                case "fl64":
                    Line(depth, $"{Offset(offset)} {label} value={ReadDouble().ToString(CultureInfo.InvariantCulture)}");
                    break;

                case "tutc":
                    ReadUtcTime(label, offset, depth);
                    break;

                case "bool":
                    Line(depth, $"{Offset(offset)} {label} value={(ReadByte() != 0).ToString().ToLowerInvariant()}");
                    break;

                default:
                    Line(depth, $"{Offset(offset)} unknown-tag value=\"{Escape(tag)}\" remaining={bytes.Length - _position} hex={HexPreview(bytes.AsSpan(_position))}");
                    _position = bytes.Length;
                    break;
            }
        }

        private void ReadDictionary(string label, int offset, int depth)
        {
            var count = ReadInt32();
            Line(depth, $"{Offset(offset)} {label} fields={count}");

            for (var i = 0; i < count; i++)
            {
                var fieldOffset = _position;
                var fieldName = ReadName();
                if (string.IsNullOrEmpty(fieldName))
                {
                    Line(depth + 1, $"{Offset(fieldOffset)} <empty-name>");
                }

                ReadValue(fieldName, depth + 1);
            }
        }

        private void ReadVector(string label, int offset, int depth)
        {
            var count = ReadInt32();
            Line(depth, $"{Offset(offset)} {label} count={count}");

            for (var i = 0; i < count; i++)
            {
                ReadValue($"[{i}]", depth + 1);
            }
        }

        private void ReadString(string label, int offset, int depth)
        {
            var length = ReadInt32();
            Ensure(length);
            var value = Encoding.UTF8.GetString(bytes, _position, length);
            _position += length;

            var suffix = value.Length > StringPreviewLength ? "..." : string.Empty;
            var preview = value.Length > StringPreviewLength
                ? value[..StringPreviewLength]
                : value;
            Line(depth, $"{Offset(offset)} {label} length={length} value=\"{Escape(preview)}{suffix}\"");
        }

        private void ReadBlob(string label, int offset, int depth)
        {
            var length = ReadInt32();
            Ensure(length);
            var preview = HexPreview(bytes.AsSpan(_position, length));
            _position += length;
            Line(depth, $"{Offset(offset)} {label} length={length} hex={preview}");
        }

        private void ReadUtcTime(string label, int offset, int depth)
        {
            var seconds = ReadInt32();
            string instant;
            try
            {
                instant = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                instant = "<out-of-range>";
            }

            Line(depth, $"{Offset(offset)} {label} seconds={seconds} utc={instant}");
        }

        private string ReadName()
        {
            var length = ReadByte();
            Ensure(length);
            var value = Encoding.ASCII.GetString(bytes, _position, length);
            _position += length;
            return value;
        }

        private string ReadTag()
        {
            Ensure(4);
            var value = Encoding.ASCII.GetString(bytes, _position, 4);
            _position += 4;
            return value;
        }

        private byte ReadByte()
        {
            Ensure(1);
            return bytes[_position++];
        }

        private sbyte ReadSByte()
        {
            return unchecked((sbyte)ReadByte());
        }

        private short ReadInt16()
        {
            Ensure(2);
            var value = BitConverter.ToInt16(bytes, _position);
            _position += 2;
            return value;
        }

        private ushort ReadUInt16()
        {
            Ensure(2);
            var value = BitConverter.ToUInt16(bytes, _position);
            _position += 2;
            return value;
        }

        private int ReadInt32()
        {
            Ensure(4);
            var value = BitConverter.ToInt32(bytes, _position);
            _position += 4;
            return value;
        }

        private uint ReadUInt32()
        {
            Ensure(4);
            var value = BitConverter.ToUInt32(bytes, _position);
            _position += 4;
            return value;
        }

        private long ReadInt64()
        {
            Ensure(8);
            var value = BitConverter.ToInt64(bytes, _position);
            _position += 8;
            return value;
        }

        private ulong ReadUInt64()
        {
            Ensure(8);
            var value = BitConverter.ToUInt64(bytes, _position);
            _position += 8;
            return value;
        }

        private float ReadSingle()
        {
            Ensure(4);
            var value = BitConverter.ToSingle(bytes, _position);
            _position += 4;
            return value;
        }

        private double ReadDouble()
        {
            Ensure(8);
            var value = BitConverter.ToDouble(bytes, _position);
            _position += 8;
            return value;
        }

        private void Ensure(int count)
        {
            if (count < 0)
            {
                throw new InvalidDataException($"Negative length: {count}");
            }

            if (_position + count > bytes.Length)
            {
                throw new EndOfStreamException($"Need {count} bytes, only {bytes.Length - _position} available.");
            }
        }

        private void Line(int depth, string text)
        {
            _builder.Append(' ', depth * 2);
            _builder.AppendLine(text);
        }

        private static string Offset(int value)
        {
            return $"0x{value:X8}";
        }

        private static string HexPreview(ReadOnlySpan<byte> value)
        {
            var length = Math.Min(value.Length, HexPreviewLength);
            if (length == 0)
            {
                return "<empty>";
            }

            var builder = new StringBuilder(length * 3 + 3);
            for (var i = 0; i < length; i++)
            {
                if (i > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(value[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            if (value.Length > length)
            {
                builder.Append(" ...");
            }

            return builder.ToString();
        }

        private static string Escape(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                builder.Append(character switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\r' => "\\r",
                    '\n' => "\\n",
                    '\t' => "\\t",
                    >= ' ' and <= '~' => character,
                    _ => $"\\u{(int)character:X4}"
                });
            }

            return builder.ToString();
        }
    }
}
