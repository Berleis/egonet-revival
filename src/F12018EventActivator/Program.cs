using System.Diagnostics;
using System.Globalization;

const string GameName = "F1 2018";
string[] processNames = ["F1_2018", "F1_2018_dx12"];

try
{
    var options = LauncherOptions.Parse(args);
    if (options.ShowHelp)
    {
        PrintUsage();
        return 0;
    }

    if (options.WaitForGame)
    {
        WaitForGame(processNames, options.WaitTimeoutSeconds);
    }

    var activatorArgs = new List<string>
    {
        "--binary",
        "--days",
        options.DurationDays.ToString(CultureInfo.InvariantCulture)
    };

    if (options.DryRun)
    {
        activatorArgs.Add("--dry-run");
    }

    F1EventMemoryActivator.Activate(GameName, processNames, activatorArgs);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static void WaitForGame(string[] processNames, int timeoutSeconds)
{
    var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
    Console.WriteLine($"Waiting for F1 2018 for up to {timeoutSeconds} second(s)...");

    while (DateTimeOffset.UtcNow <= deadline)
    {
        if (Process.GetProcesses().Any(process =>
                processNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase)))
        {
            Console.WriteLine("F1 2018 process found.");
            Console.WriteLine();
            return;
        }

        Thread.Sleep(TimeSpan.FromSeconds(1));
    }

    throw new TimeoutException("F1 2018 was not found before the wait timeout.");
}

static void PrintUsage()
{
    Console.WriteLine("F1 2018 Event Activator");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  EgoNet Revival - F1 2018 Event Activator.exe [--days 30] [--dry-run] [--wait [seconds]]");
    Console.WriteLine();
    Console.WriteLine("Open F1 2018, enter the Events screen, then run this tool.");
}

sealed record LauncherOptions(
    int DurationDays,
    bool DryRun,
    bool WaitForGame,
    int WaitTimeoutSeconds,
    bool ShowHelp)
{
    public static LauncherOptions Parse(IReadOnlyList<string> args)
    {
        var durationDays = 30;
        var dryRun = false;
        var waitForGame = false;
        var waitTimeoutSeconds = 120;
        var showHelp = false;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];

            if (arg is "-h" or "--help" or "/?")
            {
                showHelp = true;
                continue;
            }

            if (arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                continue;
            }

            if (arg.Equals("--wait", StringComparison.OrdinalIgnoreCase))
            {
                waitForGame = true;
                if (index + 1 < args.Count &&
                    int.TryParse(args[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout))
                {
                    waitTimeoutSeconds = timeout;
                    index++;
                }

                continue;
            }

            if (arg.StartsWith("--wait=", StringComparison.OrdinalIgnoreCase))
            {
                waitForGame = true;
                waitTimeoutSeconds = ParsePositiveInt(arg["--wait=".Length..], "--wait");
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

            throw new ArgumentException($"Unknown option: {arg}");
        }

        return new LauncherOptions(durationDays, dryRun, waitForGame, waitTimeoutSeconds, showHelp);
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
}
