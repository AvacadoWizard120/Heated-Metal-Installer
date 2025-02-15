using System.Text.Json;
using System.Diagnostics;

namespace HeatedMetalUpdater
{
    public partial class MainForm : Form
    {
        private const string RepoOwner = "DataCluster0";
        private const string RepoName = "HeatedMetal";
        private const string ReleaseFile = "HeatedMetal.7z";
        private const string VersionsFile = "versions.txt";
        private const string GameExe = "RainbowSix.exe";

        private readonly HttpClient httpClient = new();
        private string gameDirectory = string.Empty;

        public MainForm()
        {
            InitializeComponent();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "HeatedMetalUpdater");
        }

        private void InitializeComponent()
        {
            // Form setup
            Text = "Heated Metal Updater";
            Size = new Size(600, 200);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            // Directory selection
            var dirLabel = new Label
            {
                Text = "Game Directory:",
                Location = new Point(10, 15),
                AutoSize = true
            };

            var dirTextBox = new TextBox
            {
                Location = new Point(10, 40),
                Width = 450,
                ReadOnly = true
            };

            var browseButton = new Button
            {
                Text = "Browse",
                Location = new Point(470, 38),
                Width = 100
            };

            // Progress display
            var progressBar = new ProgressBar
            {
                Location = new Point(10, 80),
                Width = 560,
                Height = 20
            };

            var statusLabel = new Label
            {
                Location = new Point(10, 110),
                Width = 560,
                AutoSize = true
            };

            // Update button
            var updateButton = new Button
            {
                Text = "Check for Updates",
                Location = new Point(10, 130),
                Width = 560,
                Height = 30,
                Enabled = false
            };

            // Add controls
            Controls.AddRange(new Control[] {
                dirLabel, dirTextBox, browseButton,
                progressBar, statusLabel, updateButton
            });

            // Event handlers
            browseButton.Click += async (s, e) => {
                using var dialog = new FolderBrowserDialog
                {
                    Description = "Select Shadow Legacy Directory"
                };

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var exePath = Path.Combine(dialog.SelectedPath, GameExe);
                    if (File.Exists(exePath))
                    {
                        gameDirectory = dialog.SelectedPath;
                        dirTextBox.Text = gameDirectory;
                        updateButton.Enabled = true;
                    }
                    else
                    {
                        MessageBox.Show($"Selected folder must contain {GameExe}",
                            "Invalid Directory", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            };

            updateButton.Click += async (s, e) => {
                try
                {
                    updateButton.Enabled = false;
                    browseButton.Enabled = false;

                    // Check versions
                    statusLabel.Text = "Checking for updates...";
                    var (currentTag, downloadUrl) = await GetLatestReleaseInfo();
                    var localVersion = GetLocalVersion();

                    if (localVersion == currentTag)
                    {
                        statusLabel.Text = "Already up to date!";
                        return;
                    }

                    // Download update
                    statusLabel.Text = "Downloading update...";
                    var tempFile = Path.Combine(Path.GetTempPath(), ReleaseFile);
                    await DownloadFileWithProgress(downloadUrl, tempFile, progressBar);

                    // Extract update
                    statusLabel.Text = "Extracting update...";
                    await ExtractUpdate(tempFile);
                    File.WriteAllText(Path.Combine(gameDirectory, VersionsFile), currentTag);

                    // Cleanup
                    File.Delete(tempFile);
                    statusLabel.Text = "Update completed successfully!";
                    MessageBox.Show("Update completed successfully!", "Success",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    statusLabel.Text = "Error occurred during update.";
                    MessageBox.Show($"Error: {ex.Message}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    updateButton.Enabled = true;
                    browseButton.Enabled = true;
                    progressBar.Value = 0;
                }
            };
        }

        private async Task<(string TagName, string DownloadUrl)> GetLatestReleaseInfo()
        {
            var response = await httpClient.GetStringAsync(
                $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            return (
                root.GetProperty("tag_name").GetString()!,
                root.GetProperty("assets")[0].GetProperty("browser_download_url").GetString()!
            );
        }

        private string? GetLocalVersion()
        {
            var versionFile = Path.Combine(gameDirectory, VersionsFile);
            return File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : null;
        }

        private async Task DownloadFileWithProgress(string url, string destination, ProgressBar progress)
        {
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(destination);

            var buffer = new byte[8192];
            var totalBytesRead = 0L;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalBytesRead += bytesRead;

                if (totalBytes > 0)
                {
                    var progressPercent = (int)((totalBytesRead * 100) / totalBytes);
                    progress.Value = progressPercent;
                }
            }
        }

        private async Task ExtractUpdate(string archivePath)
        {
            // Try 7-Zip first
            var sevenZipPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe")
            };

            foreach (var path in sevenZipPaths)
            {
                if (File.Exists(path))
                {
                    await RunExtractionTool(path, "x", archivePath);
                    return;
                }
            }

            // Fallback to WinRAR
            var winrarPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinRAR", "WinRAR.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WinRAR", "WinRAR.exe")
            };

            foreach (var path in winrarPaths)
            {
                if (File.Exists(path))
                {
                    await RunExtractionTool(path, "x", archivePath);
                    return;
                }
            }

            throw new Exception("Neither 7-Zip nor WinRAR found. Please install one of them.");
        }

        private async Task RunExtractionTool(string toolPath, string command, string archivePath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = $"{command} \"{archivePath}\" -y",
                WorkingDirectory = gameDirectory,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                throw new Exception("Failed to start extraction process");

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new Exception($"Extraction failed: {error}");
            }
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}