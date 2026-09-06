using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

static partial class F1EventMemoryActivator
{
    private const int MaxChunkBytes = 4 * 1024 * 1024;
    private const int BytesBeforeAnchor = 8 * 1024;
    private const int BytesAfterAnchor = 96 * 1024;
    private const int MaxBinaryTimestampReports = 200;
    private const uint MemCommit = 0x1000;
    private const uint PageNoAccess = 0x01;
    private const uint PageGuard = 0x100;
    private const uint PageExecuteReadWrite = 0x40;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;

    private static readonly byte[] Anchor = Encoding.ASCII.GetBytes("gameSetupMode");
    private static readonly KnownTimestamp[] KnownTimestamps =
    [
        new("LBV2018FE20 activeFrom", 1_555_930_800, TimestampRole.ActiveFrom),
        new("LBV2018FE20 activeUntil", 1_557_097_199, TimestampRole.ActiveUntil),
        new("LBV2018FE21 activeFrom", 1_557_140_400, TimestampRole.ActiveFrom),
        new("LBV2018FE21 activeUntil", 1_558_306_799, TimestampRole.ActiveUntil),
    ];

    public static void Activate(string gameName, string[] processNames, IReadOnlyList<string> args)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Memory patching is only available on Windows.");
        }

        var options = ParseOptions(args);
        var now = DateTimeOffset.UtcNow;
        var activeFrom = now.AddDays(-1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var activeUntil = now.AddDays(options.DurationDays).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        if (activeFrom.Length != 10 || activeUntil.Length != 10)
        {
            throw new InvalidOperationException("Replacement timestamps must be 10 digits to preserve JSON length.");
        }

        var processes = Process.GetProcesses()
            .Where(process => processNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (processes.Length == 0)
        {
            Console.WriteLine($"{gameName} is not running.");
            Console.WriteLine("Open the game, leave it on the Events screen, then run this command again.");
            return;
        }

        Console.WriteLine(options.DryRun ? $"F1 event activation dry run: {gameName}" : $"F1 event activation: {gameName}");
        Console.WriteLine($"New activeFrom: {activeFrom} ({DateTimeOffset.FromUnixTimeSeconds(long.Parse(activeFrom, CultureInfo.InvariantCulture)):u})");
        Console.WriteLine($"New activeUntil: {activeUntil} ({DateTimeOffset.FromUnixTimeSeconds(long.Parse(activeUntil, CultureInfo.InvariantCulture)):u})");
        Console.WriteLine($"Binary timestamp scan: {(options.PatchBinaryTimestamps ? "enabled" : "disabled")}");
        Console.WriteLine();

        var totalCandidates = 0;
        var totalJsonWrites = 0;
        var totalBinaryWrites = 0;

        foreach (var process in processes)
        {
            Console.WriteLine($"{process.ProcessName} ({process.Id})");
            using var handle = ProcessHandle.Open(process.Id, canWrite: !options.DryRun);
            var result = ScanProcess(handle.DangerousGetHandle(), process.Id, activeFrom, activeUntil, options);
            totalCandidates += result.Candidates;
            totalJsonWrites += result.JsonWrites;
            totalBinaryWrites += result.BinaryWrites;
            Console.WriteLine();
        }

        Console.WriteLine($"{(options.DryRun ? "Would patch" : "Patched")} {totalJsonWrites} JSON timestamp field(s) in {totalCandidates} event payload candidate(s).");
        if (options.PatchBinaryTimestamps)
        {
            Console.WriteLine($"{(options.DryRun ? "Would patch" : "Patched")} {totalBinaryWrites} binary timestamp value(s).");
        }
    }

    private static ActivationOptions ParseOptions(IReadOnlyList<string> args)
    {
        var durationDays = 30;
        var dryRun = false;
        var patchBinaryTimestamps = false;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];

            if (arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }

            if (arg.Equals("--binary", StringComparison.OrdinalIgnoreCase))
            {
                patchBinaryTimestamps = true;
                continue;
            }

            if (arg.Equals("--days", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Count)
                {
                    throw new ArgumentException("--days requires a number.");
                }

                durationDays = ParsePositiveInt(args[index], "--days");
                continue;
            }

            if (arg.StartsWith("--days=", StringComparison.OrdinalIgnoreCase))
            {
                durationDays = ParsePositiveInt(arg["--days=".Length..], "--days");
                continue;
            }

            throw new ArgumentException($"Unknown f1-activate-events option: {arg}");
        }

        return new ActivationOptions(durationDays, dryRun, patchBinaryTimestamps);
    }

    private static int ParsePositiveInt(string value, string optionName)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ||
            number <= 0)
        {
            throw new ArgumentException($"{optionName} must be a positive number.");
        }

        return number;
    }

    private static ActivationResult ScanProcess(
        IntPtr processHandle,
        int processId,
        string activeFrom,
        string activeUntil,
        ActivationOptions options)
    {
        var candidates = 0;
        var jsonWrites = 0;
        var binaryWrites = 0;
        var patchedAddresses = new HashSet<ulong>();
        var binaryReports = 0;

        ulong address = 0;
        var maxAddress = Environment.Is64BitProcess ? 0x00007fffffffffffUL : uint.MaxValue;

        while (address < maxAddress)
        {
            var result = VirtualQueryEx(
                processHandle,
                new UIntPtr(address),
                out var memoryInfo,
                (UIntPtr)Marshal.SizeOf<MemoryBasicInformation>());
            if (result == UIntPtr.Zero)
            {
                break;
            }

            var regionBase = memoryInfo.BaseAddress.ToUInt64();
            var regionSize = memoryInfo.RegionSize.ToUInt64();
            var nextAddress = regionBase + Math.Max(regionSize, 0x1000UL);

            if (IsReadable(memoryInfo))
            {
                var regionResult = ScanRegion(
                    processHandle,
                    processId,
                    regionBase,
                    regionSize,
                    activeFrom,
                    activeUntil,
                    options,
                    patchedAddresses,
                    ref binaryReports);
                candidates += regionResult.Candidates;
                jsonWrites += regionResult.JsonWrites;
                binaryWrites += regionResult.BinaryWrites;
            }

            if (nextAddress <= address)
            {
                break;
            }

            address = nextAddress;
        }

        if (candidates == 0 && binaryWrites == 0)
        {
            Console.WriteLine("  no loaded F1 event payloads found");
        }

        if (binaryReports == MaxBinaryTimestampReports)
        {
            Console.WriteLine("  binary timestamp report limit reached; remaining matches are still counted");
        }

        return new ActivationResult(candidates, jsonWrites, binaryWrites);
    }

    private static bool IsReadable(MemoryBasicInformation memoryInfo)
    {
        return memoryInfo.State == MemCommit &&
            (memoryInfo.Protect & PageNoAccess) == 0 &&
            (memoryInfo.Protect & PageGuard) == 0;
    }

    private static ActivationResult ScanRegion(
        IntPtr processHandle,
        int processId,
        ulong regionBase,
        ulong regionSize,
        string activeFrom,
        string activeUntil,
        ActivationOptions options,
        ISet<ulong> patchedAddresses,
        ref int binaryReports)
    {
        var candidates = 0;
        var jsonWrites = 0;
        var binaryWrites = 0;
        var regionEnd = regionBase + regionSize;
        if (regionEnd < regionBase)
        {
            regionEnd = ulong.MaxValue;
        }

        var offset = 0UL;
        while (offset < regionSize)
        {
            var remaining = regionSize - offset;
            var chunkSize = checked((int)Math.Min(remaining, MaxChunkBytes));
            var buffer = new byte[chunkSize];
            var baseAddress = regionBase + offset;

            if (!ReadProcessMemory(
                    processHandle,
                    new IntPtr(unchecked((long)baseAddress)),
                    buffer,
                    buffer.Length,
                    out var bytesRead) ||
                bytesRead == 0)
            {
                offset += (ulong)chunkSize;
                continue;
            }

            if (bytesRead != buffer.Length)
            {
                Array.Resize(ref buffer, bytesRead);
            }

            if (options.PatchBinaryTimestamps)
            {
                binaryWrites += PatchKnownBinaryTimestamps(
                    processHandle,
                    buffer,
                    baseAddress,
                    activeFrom,
                    activeUntil,
                    options,
                    patchedAddresses,
                    ref binaryReports);
            }

            var searchFrom = 0;
            while (searchFrom <= buffer.Length - Anchor.Length)
            {
                var relative = buffer.AsSpan(searchFrom).IndexOf(Anchor);
                if (relative < 0)
                {
                    break;
                }

                var anchorOffset = searchFrom + relative;
                searchFrom = anchorOffset + Anchor.Length;

                var absoluteAnchor = baseAddress + (ulong)anchorOffset;
                var candidate = ReadCandidate(processHandle, regionBase, regionEnd, absoluteAnchor);
                if (candidate is null)
                {
                    continue;
                }

                var patchResult = PatchCandidate(
                    processHandle,
                    processId,
                    candidate,
                    activeFrom,
                    activeUntil,
                    options,
                    patchedAddresses);
                if (!patchResult.IsEventPayload)
                {
                    continue;
                }

                candidates++;
                jsonWrites += patchResult.Writes;
            }

            offset += (ulong)chunkSize;
        }

        return new ActivationResult(candidates, jsonWrites, binaryWrites);
    }

    private static int PatchKnownBinaryTimestamps(
        IntPtr processHandle,
        byte[] buffer,
        ulong baseAddress,
        string activeFrom,
        string activeUntil,
        ActivationOptions options,
        ISet<ulong> patchedAddresses,
        ref int binaryReports)
    {
        var writes = 0;

        foreach (var knownTimestamp in KnownTimestamps)
        {
            var replacement = knownTimestamp.Role == TimestampRole.ActiveFrom
                ? uint.Parse(activeFrom, CultureInfo.InvariantCulture)
                : uint.Parse(activeUntil, CultureInfo.InvariantCulture);

            writes += PatchBinaryTimestamp(
                processHandle,
                buffer,
                baseAddress,
                knownTimestamp,
                BitConverter.GetBytes((long)knownTimestamp.OldValue),
                BitConverter.GetBytes((long)replacement),
                replacement,
                "i64",
                options,
                patchedAddresses,
                ref binaryReports);

            writes += PatchBinaryTimestamp(
                processHandle,
                buffer,
                baseAddress,
                knownTimestamp,
                BitConverter.GetBytes(knownTimestamp.OldValue),
                BitConverter.GetBytes(replacement),
                replacement,
                "u32",
                options,
                patchedAddresses,
                ref binaryReports);
        }

        return writes;
    }

    private static int PatchBinaryTimestamp(
        IntPtr processHandle,
        byte[] buffer,
        ulong baseAddress,
        KnownTimestamp knownTimestamp,
        byte[] oldBytes,
        byte[] newBytes,
        uint replacement,
        string storage,
        ActivationOptions options,
        ISet<ulong> patchedAddresses,
        ref int binaryReports)
    {
        var writes = 0;
        var searchFrom = 0;

        while (searchFrom <= buffer.Length - oldBytes.Length)
        {
            var relative = buffer.AsSpan(searchFrom).IndexOf(oldBytes);
            if (relative < 0)
            {
                break;
            }

            var offset = searchFrom + relative;
            searchFrom = offset + 1;

            var address = baseAddress + (ulong)offset;
            if (!patchedAddresses.Add(address))
            {
                continue;
            }

            if (binaryReports < MaxBinaryTimestampReports)
            {
                Console.WriteLine(
                    $"  {knownTimestamp.Label}: binary {storage} {knownTimestamp.OldValue}->{replacement} at 0x{address:x}");
                binaryReports++;
            }

            if (!options.DryRun)
            {
                WriteBytes(processHandle, address, newBytes);
            }

            writes++;
        }

        return writes;
    }

    private static MemoryCandidate? ReadCandidate(
        IntPtr processHandle,
        ulong regionBase,
        ulong regionEnd,
        ulong anchorAddress)
    {
        var start = anchorAddress > regionBase + BytesBeforeAnchor
            ? anchorAddress - BytesBeforeAnchor
            : regionBase;
        var end = Math.Min(regionEnd, anchorAddress + BytesAfterAnchor);
        var length = checked((int)(end - start));
        if (length <= 0)
        {
            return null;
        }

        var bytes = new byte[length];
        if (!ReadProcessMemory(
                processHandle,
                new IntPtr(unchecked((long)start)),
                bytes,
                bytes.Length,
                out var bytesRead) ||
            bytesRead == 0)
        {
            return null;
        }

        if (bytesRead != bytes.Length)
        {
            Array.Resize(ref bytes, bytesRead);
        }

        var text = Encoding.Latin1.GetString(bytes);
        var anchorOffset = checked((int)(anchorAddress - start));
        var rootStart = FindRootStart(text, anchorOffset);
        if (rootStart < 0)
        {
            return null;
        }

        var rootEnd = FindJsonEnd(text, rootStart);
        if (rootEnd < 0)
        {
            rootEnd = Math.Min(text.Length - 1, rootStart + BytesAfterAnchor - 1);
        }

        return new MemoryCandidate(start, rootStart, rootEnd, text);
    }

    private static PatchCandidateResult PatchCandidate(
        IntPtr processHandle,
        int processId,
        MemoryCandidate candidate,
        string activeFrom,
        string activeUntil,
        ActivationOptions options,
        ISet<ulong> patchedAddresses)
    {
        var jsonish = candidate.Text.Substring(candidate.RootStart, candidate.RootEnd - candidate.RootStart + 1);
        if (!jsonish.Contains("\"gameSetupMode\"", StringComparison.Ordinal) ||
            !jsonish.Contains("\"f1\"", StringComparison.Ordinal) ||
            !jsonish.Contains("\"leaderboard\"", StringComparison.Ordinal) ||
            !jsonish.Contains("\"descriptions\"", StringComparison.Ordinal))
        {
            return new PatchCandidateResult(false, 0);
        }

        var leaderboard = FindStringProperty(jsonish, "leaderboard") ?? "<unknown>";
        var driver = FindStringProperty(jsonish, "driver") ?? "<unknown>";
        var track = FindStringProperty(jsonish, "track") ?? "<unknown>";
        var fieldWrites = 0;

        foreach (Match match in TimestampFieldRegex().Matches(jsonish))
        {
            var fieldName = match.Groups["field"].Value;
            var oldValue = match.Groups["value"].Value;
            var newValue = fieldName.Equals("activeFrom", StringComparison.Ordinal)
                ? activeFrom
                : activeUntil;

            if (oldValue.Length != newValue.Length)
            {
                Console.WriteLine($"  {leaderboard} {driver}/{track}: skip {fieldName}; length {oldValue.Length}->{newValue.Length}");
                continue;
            }

            var valueOffset = candidate.RootStart + match.Groups["value"].Index;
            var valueAddress = candidate.StartAddress + (ulong)valueOffset;
            if (!patchedAddresses.Add(valueAddress))
            {
                continue;
            }

            Console.WriteLine(
                $"  {leaderboard} {driver}/{track}: {fieldName} {oldValue}->{newValue} at 0x{valueAddress:x}");

            if (!options.DryRun)
            {
                WriteAscii(processHandle, valueAddress, newValue);
            }

            fieldWrites++;
        }

        return new PatchCandidateResult(true, fieldWrites);
    }

    private static int FindRootStart(string text, int anchorOffset)
    {
        var minStart = Math.Max(0, anchorOffset - BytesBeforeAnchor);
        for (var index = anchorOffset; index >= minStart; index--)
        {
            if (text[index] == '{')
            {
                return index;
            }
        }

        return -1;
    }

    private static string? FindStringProperty(string text, string propertyName)
    {
        var regex = new Regex(
            $"\"{Regex.Escape(propertyName)}\"\\s*:\\s*\"(?<value>[^\"]*)\"",
            RegexOptions.CultureInvariant);
        var match = regex.Match(text);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static void WriteAscii(IntPtr processHandle, ulong address, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);

        WriteBytes(processHandle, address, bytes);
    }

    private static void WriteBytes(IntPtr processHandle, ulong address, byte[] bytes)
    {

        if (!VirtualProtectEx(
                processHandle,
                new IntPtr(unchecked((long)address)),
                (UIntPtr)bytes.Length,
                PageExecuteReadWrite,
                out var oldProtect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not change page protection at 0x{address:x}.");
        }

        try
        {
            if (!WriteProcessMemory(
                    processHandle,
                    new IntPtr(unchecked((long)address)),
                    bytes,
                    bytes.Length,
                    out var bytesWritten) ||
                bytesWritten != bytes.Length)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not write memory at 0x{address:x}.");
            }
        }
        finally
        {
            VirtualProtectEx(
                processHandle,
                new IntPtr(unchecked((long)address)),
                (UIntPtr)bytes.Length,
                oldProtect,
                out _);
        }
    }

    private static int FindJsonEnd(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escaping = false;

        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];

            if (inString)
            {
                if (escaping)
                {
                    escaping = false;
                }
                else if (character == '\\')
                {
                    escaping = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                continue;
            }

            if (character is '{' or '[')
            {
                depth++;
                continue;
            }

            if (character is '}' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }

                if (depth < 0)
                {
                    return -1;
                }
            }
        }

        return -1;
    }

    [GeneratedRegex("\"(?<field>activeFrom|activeUntil|canWriteToLeaderboardsUntil)\"\\s*:\\s*(?<value>\\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampFieldRegex();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr processHandle,
        IntPtr baseAddress,
        [Out] byte[] buffer,
        int size,
        out int bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr processHandle,
        IntPtr baseAddress,
        byte[] buffer,
        int size,
        out int bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtectEx(
        IntPtr processHandle,
        IntPtr address,
        UIntPtr size,
        uint newProtect,
        out uint oldProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr VirtualQueryEx(
        IntPtr processHandle,
        UIntPtr address,
        out MemoryBasicInformation buffer,
        UIntPtr length);

    private sealed record ActivationOptions(int DurationDays, bool DryRun, bool PatchBinaryTimestamps);

    private sealed record ActivationResult(int Candidates, int JsonWrites, int BinaryWrites);

    private sealed record PatchCandidateResult(bool IsEventPayload, int Writes);

    private sealed record KnownTimestamp(string Label, uint OldValue, TimestampRole Role);

    private enum TimestampRole
    {
        ActiveFrom,
        ActiveUntil,
    }

    private sealed record MemoryCandidate(
        ulong StartAddress,
        int RootStart,
        int RootEnd,
        string Text);

    private sealed class ProcessHandle : SafeHandle
    {
        private ProcessHandle()
            : base(IntPtr.Zero, ownsHandle: true)
        {
        }

        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        public static ProcessHandle Open(int processId, bool canWrite)
        {
            var desiredAccess = ProcessQueryInformation | ProcessVmRead;
            if (canWrite)
            {
                desiredAccess |= ProcessVmOperation | ProcessVmWrite;
            }

            var handle = OpenProcess(
                desiredAccess,
                inheritHandle: false,
                processId);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open process {processId}.");
            }

            return new ProcessHandle { handle = handle };
        }

        protected override bool ReleaseHandle()
        {
            return CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public UIntPtr BaseAddress;
        public UIntPtr AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public UIntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }
}
