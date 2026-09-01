using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;

namespace DirtShowdownModInstaller;

internal static class Program
{
    private const string DefaultServer = "142.93.206.37";
    private const string DefaultGamePath = @"C:\Program Files (x86)\Steam\steamapps\common\DiRT Showdown";

    [STAThread]
    private static void Main(string[] args)
    {
        if (!IsAdministrator())
        {
            RelaunchAsAdministrator(args);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm(args));
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchAsAdministrator(string[] args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", args.Select(QuoteArgument))
            };

            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                "Administrator permission is required to install the mod.\n\n" + exception.Message,
                "EgoNet Revival",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string QuoteArgument(string argument)
    {
        return "\"" + argument.Replace("\"", "\\\"") + "\"";
    }

    public static string InitialServer(string[] args)
    {
        return args.Length >= 2 ? args[0] : DefaultServer;
    }

    public static string InitialGamePath(string[] args)
    {
        if (args.Length == 1 && Directory.Exists(args[0]))
        {
            return args[0];
        }

        if (args.Length >= 2)
        {
            return args[1];
        }

        return DetectGamePath();
    }

    private static string DetectGamePath()
    {
        var candidates = new List<string> { DefaultGamePath };

        for (var drive = 'C'; drive <= 'Z'; drive++)
        {
            candidates.Add($@"{drive}:\SteamLibrary\steamapps\common\DiRT Showdown");
            candidates.Add($@"{drive}:\Program Files (x86)\Steam\steamapps\common\DiRT Showdown");
            candidates.Add($@"{drive}:\Program Files\Steam\steamapps\common\DiRT Showdown");
        }

        return candidates.FirstOrDefault(Directory.Exists) ?? DefaultGamePath;
    }
}

internal sealed class InstallerForm : Form
{
    private readonly TextBox _serverTextBox;
    private readonly TextBox _gamePathTextBox;
    private readonly Button _browseButton;
    private readonly Button _installButton;
    private readonly RichTextBox _logBox;

    public InstallerForm(string[] args)
    {
        Text = "EgoNet Revival - DiRT Showdown Installer";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);
        Size = new Size(820, 560);

        var titleLabel = new Label
        {
            Text = "EgoNet Revival - DiRT Showdown",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        };

        var descriptionLabel = new Label
        {
            Text = "Select the DiRT Showdown installation folder, then install the RaceNet restoration mod.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 18)
        };

        _serverTextBox = new TextBox
        {
            Text = Program.InitialServer(args),
            Dock = DockStyle.Fill
        };

        _gamePathTextBox = new TextBox
        {
            Text = Program.InitialGamePath(args),
            Dock = DockStyle.Fill
        };

        _browseButton = new Button
        {
            Text = "Browse...",
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        _browseButton.Click += (_, _) => BrowseGameFolder();

        _installButton = new Button
        {
            Text = "Install Mod",
            AutoSize = true,
            Dock = DockStyle.Right
        };
        _installButton.Click += async (_, _) => await InstallAsync();

        _logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.WhiteSmoke,
            Font = new Font("Consolas", 10),
            BorderStyle = BorderStyle.FixedSingle
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 6,
            Padding = new Padding(16)
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        grid.Controls.Add(titleLabel, 0, 0);
        grid.SetColumnSpan(titleLabel, 3);
        grid.Controls.Add(descriptionLabel, 0, 1);
        grid.SetColumnSpan(descriptionLabel, 3);

        grid.Controls.Add(MakeLabel("Server:"), 0, 2);
        grid.Controls.Add(_serverTextBox, 1, 2);
        grid.SetColumnSpan(_serverTextBox, 2);

        grid.Controls.Add(MakeLabel("Game folder:"), 0, 3);
        grid.Controls.Add(_gamePathTextBox, 1, 3);
        grid.Controls.Add(_browseButton, 2, 3);

        grid.Controls.Add(_logBox, 0, 4);
        grid.SetColumnSpan(_logBox, 3);
        grid.Controls.Add(_installButton, 2, 5);

        Controls.Add(grid);
    }

    private static Label MakeLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 6)
        };
    }

    private void BrowseGameFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select your DiRT Showdown installation folder",
            SelectedPath = Directory.Exists(_gamePathTextBox.Text)
                ? _gamePathTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _gamePathTextBox.Text = dialog.SelectedPath;
        }
    }

    private async Task InstallAsync()
    {
        _installButton.Enabled = false;
        _browseButton.Enabled = false;
        _serverTextBox.Enabled = false;
        _gamePathTextBox.Enabled = false;
        _logBox.Clear();

        try
        {
            var server = NormalizeServer(_serverTextBox.Text);
            var gamePath = _gamePathTextBox.Text.Trim();
            var installer = new DirtShowdownInstaller(server, gamePath, Log);

            await Task.Run(installer.Install);

            MessageBox.Show(
                "Done. Open DiRT Showdown and enter RaceNet.",
                "EgoNet Revival",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            Log("FAILED: " + exception.Message);
            MessageBox.Show(
                exception.Message,
                "Installation failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _installButton.Enabled = true;
            _browseButton.Enabled = true;
            _serverTextBox.Enabled = true;
            _gamePathTextBox.Enabled = true;
        }
    }

    private static string NormalizeServer(string server)
    {
        server = server.Trim();
        server = server.Replace("https://", "", StringComparison.OrdinalIgnoreCase);
        server = server.Replace("http://", "", StringComparison.OrdinalIgnoreCase);
        return server.TrimEnd('/');
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(Log), message);
            return;
        }

        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }
}

internal sealed class DirtShowdownInstaller(string server, string gamePath, Action<string> log)
{
    private const int ExpectedRootCertificateLength = 1003;

    private static readonly string[] HostNames =
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

    private static readonly string[] ExecutableNames =
    [
        "showdown.exe",
        "showdown_avx.exe"
    ];

    private static readonly string[] ProcessNames =
    [
        "showdown",
        "showdown_avx"
    ];

    public void Install()
    {
        log("Server: " + server);
        log("Game folder: " + gamePath);

        ValidateGameFolder();
        CloseGameIfRunning();
        UpdateHostsFile();
        FlushDns();

        var rootCertificatePath = DownloadRootCertificate();
        var rootCertificateBytes = File.ReadAllBytes(rootCertificatePath);

        if (rootCertificateBytes.Length != ExpectedRootCertificateLength)
        {
            throw new InvalidOperationException(
                $"Server root certificate must be {ExpectedRootCertificateLength} bytes, but it is {rootCertificateBytes.Length} bytes.");
        }

        InstallRootCertificate(rootCertificateBytes);
        ClearCertificateCache();
        PatchExecutables(rootCertificateBytes);
        TestHealthEndpoint();

        log("Installation completed.");
    }

    private void ValidateGameFolder()
    {
        if (!Directory.Exists(gamePath))
        {
            throw new DirectoryNotFoundException("Game folder not found: " + gamePath);
        }

        if (!ExecutableNames.Any(name => File.Exists(Path.Combine(gamePath, name))))
        {
            throw new FileNotFoundException("DiRT Showdown executables were not found in: " + gamePath);
        }
    }

    private void CloseGameIfRunning()
    {
        var runningProcesses = Process.GetProcesses()
            .Where(process => ProcessNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (runningProcesses.Length == 0)
        {
            return;
        }

        log("Closing DiRT Showdown before patching...");
        foreach (var process in runningProcesses)
        {
            using (process)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
    }

    private void UpdateHostsFile()
    {
        var hostsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"System32\drivers\etc\hosts");

        var hostsItem = new FileInfo(hostsPath);
        if ((hostsItem.Attributes & FileAttributes.ReadOnly) != 0)
        {
            hostsItem.Attributes &= ~FileAttributes.ReadOnly;
        }

        var cleanHosts = new List<string>();
        foreach (var line in File.ReadAllLines(hostsPath))
        {
            var trimmed = line.Trim();
            if (trimmed.Equals("# EgoNet Revival DiRT Showdown", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                cleanHosts.Add(line);
                continue;
            }

            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && HostNames.Contains(parts[1], StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            cleanHosts.Add(line);
        }

        cleanHosts.Add("");
        cleanHosts.Add("# EgoNet Revival DiRT Showdown");
        foreach (var hostName in HostNames)
        {
            cleanHosts.Add(server + " " + hostName);
        }

        File.WriteAllLines(hostsPath, cleanHosts, Encoding.ASCII);
        log("Windows hosts file updated.");
    }

    private void FlushDns()
    {
        RunProcess("ipconfig.exe", "/flushdns", throwOnFailure: false);
        log("DNS cache flushed.");
    }

    private string DownloadRootCertificate()
    {
        var downloadDirectory = Path.Combine(Path.GetTempPath(), "egonet-revival");
        Directory.CreateDirectory(downloadDirectory);

        var certificatePath = Path.Combine(downloadDirectory, "codemasters-local-root-ca.cer");
        var certificateUrl = "http://" + server + "/racenet-root-ca.cer";

        log("Downloading root certificate from " + certificateUrl);
        using var client = new HttpClient();
        var certificateBytes = client.GetByteArrayAsync(certificateUrl).GetAwaiter().GetResult();
        File.WriteAllBytes(certificatePath, certificateBytes);

        return certificatePath;
    }

    private void InstallRootCertificate(byte[] rootCertificateBytes)
    {
        using var certificate = X509CertificateLoader.LoadCertificate(rootCertificateBytes);
        using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);

        store.Open(OpenFlags.ReadWrite);

        var alreadyInstalled = store.Certificates
            .Cast<X509Certificate2>()
            .Any(value => value.Thumbprint.Equals(certificate.Thumbprint, StringComparison.OrdinalIgnoreCase));

        if (alreadyInstalled)
        {
            log("Root certificate is already installed.");
            return;
        }

        store.Add(certificate);
        log("Root certificate installed.");
    }

    private void ClearCertificateCache()
    {
        RunProcess("certutil.exe", "-urlcache * delete", throwOnFailure: false);
    }

    private void PatchExecutables(byte[] rootCertificateBytes)
    {
        log("Patching game executables...");

        foreach (var executableName in ExecutableNames)
        {
            PatchExecutable(Path.Combine(gamePath, executableName), rootCertificateBytes);
        }
    }

    private void PatchExecutable(string executablePath, byte[] rootCertificateBytes)
    {
        if (!File.Exists(executablePath))
        {
            log("Skipping missing file: " + executablePath);
            return;
        }

        var executableBytes = File.ReadAllBytes(executablePath);
        var candidates = FindCodemastersRootCertificates(executableBytes).ToArray();

        if (candidates.Length == 0)
        {
            log(Path.GetFileName(executablePath) + ": Codemasters root CA not found.");
            return;
        }

        var changed = false;
        foreach (var candidate in candidates)
        {
            if (candidate.Bytes.SequenceEqual(rootCertificateBytes))
            {
                log($"{Path.GetFileName(executablePath)}: already patched at 0x{candidate.Offset:x}.");
                continue;
            }

            if (candidate.Bytes.Length != rootCertificateBytes.Length)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(executablePath)}: embedded certificate length is {candidate.Bytes.Length}, but server root certificate is {rootCertificateBytes.Length}.");
            }

            var backupPath = executablePath + ".racenet-original.bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(executablePath, backupPath);
                log(Path.GetFileName(executablePath) + ": backup written.");
            }

            Buffer.BlockCopy(rootCertificateBytes, 0, executableBytes, candidate.Offset, rootCertificateBytes.Length);
            changed = true;
            log($"{Path.GetFileName(executablePath)}: patched at 0x{candidate.Offset:x}.");
        }

        if (changed)
        {
            File.WriteAllBytes(executablePath, executableBytes);
        }
    }

    private void TestHealthEndpoint()
    {
        var curlPath = FindOnPath("curl.exe");
        if (curlPath is null)
        {
            log("curl.exe was not found. Skipping HTTPS health check.");
            return;
        }

        var result = RunProcess(
            curlPath,
            "-fsS --ssl-no-revoke https://prod.egonet.codemasters.com/health",
            throwOnFailure: false);

        if (result.ExitCode == 0)
        {
            log("HTTPS health check passed.");
        }
        else
        {
            log("HTTPS health check could not be completed. The game may still work after Windows refreshes certificate trust.");
        }
    }

    private ProcessResult RunProcess(string fileName, string arguments, bool throwOnFailure)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start process: " + fileName);

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(15000);

        if (throwOnFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException(fileName + " failed: " + error);
        }

        return new ProcessResult(process.ExitCode, output, error);
    }

    private static string? FindOnPath(string executableName)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        return paths
            .Select(path => Path.Combine(path, executableName))
            .FirstOrDefault(File.Exists);
    }

    private static IEnumerable<CertificateCandidate> FindCodemastersRootCertificates(byte[] bytes)
    {
        for (var offset = 0; offset < bytes.Length - 8; offset++)
        {
            var length = TryGetDerSequenceLength(bytes, offset);
            if (length is null or < 256 or > 8192 || offset + length > bytes.Length)
            {
                continue;
            }

            var candidateBytes = bytes.AsSpan(offset, length.Value).ToArray();
            if (!ContainsAscii(candidateBytes, "Codemasters"))
            {
                continue;
            }

            X509Certificate2 certificate;
            try
            {
                certificate = X509CertificateLoader.LoadCertificate(candidateBytes);
            }
            catch (CryptographicException)
            {
                continue;
            }

            if (certificate.Subject.Contains("CN=Codemasters Online Root CA", StringComparison.OrdinalIgnoreCase)
                && certificate.Subject.Contains("OU=Codemasters Online", StringComparison.OrdinalIgnoreCase))
            {
                yield return new CertificateCandidate(offset, candidateBytes);
                offset += length.Value - 1;
            }
        }
    }

    private static int? TryGetDerSequenceLength(byte[] bytes, int offset)
    {
        if (bytes[offset] != 0x30 || offset + 1 >= bytes.Length)
        {
            return null;
        }

        var marker = bytes[offset + 1];
        if ((marker & 0x80) == 0)
        {
            return 2 + marker;
        }

        var lengthBytes = marker & 0x7f;
        if (lengthBytes is <= 0 or > 4 || offset + 2 + lengthBytes > bytes.Length)
        {
            return null;
        }

        var length = 0;
        for (var index = 0; index < lengthBytes; index++)
        {
            length = (length << 8) | bytes[offset + 2 + index];
        }

        return 2 + lengthBytes + length;
    }

    private static bool ContainsAscii(byte[] bytes, string text)
    {
        var pattern = Encoding.ASCII.GetBytes(text);
        if (bytes.Length < pattern.Length)
        {
            return false;
        }

        for (var offset = 0; offset <= bytes.Length - pattern.Length; offset++)
        {
            var matched = true;
            for (var index = 0; index < pattern.Length; index++)
            {
                if (bytes[offset + index] != pattern[index])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record CertificateCandidate(int Offset, byte[] Bytes);

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
