using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace EZWinGet
{
    public class Program : Form
    {
        private NotifyIcon _trayIcon;
        private System.Windows.Forms.Timer _upgradeCheckTimer;

        // Settings fields
        private int _updateIntervalHours = 8;
        private bool _showExitOption = true;
        private bool _showWinGetConsoleOption = true;
        private readonly string _iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Program());
        }

        public Program()
        {
            LoadOrCreateSettings();
            InitializeTrayIcon();
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            Console.WriteLine("EZWinGet started at " + DateTime.Now.ToString("hh:mm tt"));
            InitializeUpgradeCheckTimer();
        }

        private void LoadOrCreateSettings()
        {
            if (!File.Exists(_iniPath))
            {
                // Create INI with default values
                File.WriteAllLines(_iniPath, new[]
                {
                    "[Settings]",
                    "UpdateIntervalHours=8",
                    "ShowExitOption=true",
                    "ShowWinGetConsoleOption=true"
                });
                _updateIntervalHours = 8;
                _showExitOption = true;
                _showWinGetConsoleOption = true;
            }
            else
            {
                // Read settings
                foreach (var line in File.ReadAllLines(_iniPath))
                {
                    if (line.StartsWith("UpdateIntervalHours=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(line.Substring("UpdateIntervalHours=".Length), out int hours))
                            _updateIntervalHours = Math.Max(1, hours);
                    }
                    else if (line.StartsWith("ShowExitOption=", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line.Substring("ShowExitOption=".Length).Trim().ToLowerInvariant();
                        _showExitOption = value == "true" || value == "1" || value == "yes";
                    }
                    else if (line.StartsWith("ShowWinGetConsoleOption=", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line.Substring("ShowWinGetConsoleOption=".Length).Trim().ToLowerInvariant();
                        _showWinGetConsoleOption = value == "true" || value == "1" || value == "yes";
                    }
                }
            }
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = GetEmbeddedIcon("EZWinGet.EZ.ico"),
                Visible = true,
                Text = "EZWinGet"
            };
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Check for Upgrades Now", null, async (s, e) => await CheckUpgradesAsync());
            contextMenu.Items.Add("Install Upgrades", null, async (s, e) => await InstallUpgradesAsync());
            if (_showWinGetConsoleOption)
                contextMenu.Items.Add("Open WinGet Console", null, async (s, e) => await OpenWinGetConsoleAsync());
            if (_showExitOption)
                contextMenu.Items.Add("Exit", null, (s, e) => Application.Exit());
            _trayIcon.ContextMenuStrip = contextMenu;
            Console.WriteLine("Tray icon initialized with EZ.ico");
        }

        private void InitializeUpgradeCheckTimer()
        {
            _upgradeCheckTimer = new System.Windows.Forms.Timer
            {
                Interval = _updateIntervalHours * 60 * 60 * 1000
            };
            _upgradeCheckTimer.Tick += async (s, e) => await CheckUpgradesAsync();
            _upgradeCheckTimer.Start();
            Console.WriteLine($"Periodic upgrade check timer initialized (every {_updateIntervalHours} hours)");
        }

        private Icon GetEmbeddedIcon(string resourceName)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new FileNotFoundException($"Resource {resourceName} not found.");
                    return new Icon(stream);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load EZ.ico: {ex.Message}");
                return SystemIcons.Application;
            }
        }

        private async Task CheckUpgradesAsync()
        {
            Console.WriteLine("Checking for upgrades at " + DateTime.Now.ToString("hh:mm tt"));
            var output = await RunWingetCommand("upgrade --include-unknown", elevate: false, captureOutput: true);

            int noIndex = output.IndexOf("No", StringComparison.OrdinalIgnoreCase);
            int nameIndex = output.IndexOf("Name", StringComparison.OrdinalIgnoreCase);
            int filterIndex = -1;
            if (noIndex >= 0 && nameIndex >= 0)
                filterIndex = Math.Min(noIndex, nameIndex);
            else if (noIndex >= 0)
                filterIndex = noIndex;
            else if (nameIndex >= 0)
                filterIndex = nameIndex;
            string filteredOutput = filterIndex >= 0 ? output.Substring(filterIndex) : output;
            if (string.IsNullOrWhiteSpace(filteredOutput))
                filteredOutput = "No output available.";

            var lines = filteredOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            int maxLineWidth = 0;
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                var font = new Font("Consolas", 10);
                foreach (var line in lines)
                {
                    int lineWidth = (int)g.MeasureString(line, font).Width;
                    if (lineWidth > maxLineWidth)
                        maxLineWidth = lineWidth;
                }
            }
            int padding = 40;
            int minWidth = 400;
            int desiredWidth = Math.Max(minWidth, maxLineWidth + padding);
            int screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
            int finalWidth = Math.Min(desiredWidth, screenWidth - 40);

            using (var form = new Form())
            {
                form.Text = "EZWinGet - Available Upgrades?";
                form.Size = new Size(finalWidth, 400);
                form.FormBorderStyle = FormBorderStyle.Sizable;
                form.StartPosition = FormStartPosition.CenterScreen;

                var textBox = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Fill,
                    Text = filteredOutput,
                    Font = new Font("Consolas", 10),
                    TabStop = false // Prevents focus via Tab
                };

                // Prevent selection and blinking cursor
                form.Shown += (s, e) =>
                {
                    textBox.SelectionLength = 0;
                    textBox.SelectionStart = 0;
                    form.ActiveControl = null; // Remove focus from the TextBox
                };

                var okButton = new Button
                {
                    Text = "OK",
                    Dock = DockStyle.Bottom,
                    Height = 30
                };
                okButton.Click += (s, e) => form.Close();

                form.Controls.Add(textBox);
                form.Controls.Add(okButton);
                form.ShowDialog();
            }
        }

        private async Task InstallUpgradesAsync()
        {
            Console.WriteLine("Install Upgrades menu option clicked at " + DateTime.Now.ToString("hh:mm tt"));
            await RunWingetCommand("upgrade --all -h --include-unknown --accept-package-agreements --accept-source-agreements", elevate: true, captureOutput: false, keepConsoleOpen: false, showPause: true);
        }

        private async Task OpenWinGetConsoleAsync()
        {
            Console.WriteLine("Open WinGet Console menu option clicked at " + DateTime.Now.ToString("hh:mm tt"));
            await RunWingetCommand("", elevate: true, captureOutput: false, keepConsoleOpen: true);
        }

        private async Task<List<(string Name, string Id, string Current, string Available)>> GetAvailableUpgradesAsync()
        {
            var output = await RunWingetCommand("upgrade --include-unknown");
            var upgrades = new List<(string Name, string Id, string Current, string Available)>();
            var lines = output.Split('\n');
            for (int i = 2; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.Contains("No applicable update")) continue;
                var parts = Regex.Split(line, @"\s{2,}");
                if (parts.Length >= 4)
                    upgrades.Add((parts[0], parts[1], parts[2], parts[3]));
            }
            Console.WriteLine($"Parsed {upgrades.Count} upgrades from winget output");
            return upgrades;
        }

        private async Task<string> RunWingetCommand(string args, bool elevate = false, bool captureOutput = false, bool keepConsoleOpen = false, bool showPause = false)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = captureOutput
                    ? $"-NoProfile -Command \"winget {args}\""
                    : $"-NoProfile {(keepConsoleOpen ? "-NoExit " : "")}-Command \"$host.ui.RawUI.WindowTitle = 'EZWinGet'; winget {args}{(showPause ? "; pause" : "")}\"",
                UseShellExecute = !captureOutput,
                CreateNoWindow = captureOutput,
                RedirectStandardOutput = captureOutput,
                RedirectStandardError = captureOutput
            };
            if (elevate)
            {
                startInfo.Verb = "runas";
            }
            var process = new Process { StartInfo = startInfo };
            try
            {
                if (captureOutput)
                {
                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit());
                    Console.WriteLine($"Ran winget {args} in background");
                    return !string.IsNullOrEmpty(error) ? error : output;
                }
                else
                {
                    process.Start();
                    await Task.Run(() => process.WaitForExit());
                    Console.WriteLine($"Ran winget {args} in PowerShell");
                    return "PowerShell console displayed. Check window for output.";
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Console.WriteLine($"Winget {args} failed: {ex.Message}");
                return "Elevation cancelled.";
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Hide();
            try
            {
                Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)?
                    .SetValue("EZWinGet", Application.ExecutablePath);
                Console.WriteLine("Auto-start registered at " + DateTime.Now.ToString("hh:mm tt"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to register auto-start: {ex.Message}");
            }
            Task.Run(async () => await CheckUpgradesAsync());
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            _upgradeCheckTimer?.Stop();
            _upgradeCheckTimer?.Dispose();
            _trayIcon.Dispose();
            Console.WriteLine("EZWinGet exiting at " + DateTime.Now.ToString("hh:mm tt"));
        }
    }
}