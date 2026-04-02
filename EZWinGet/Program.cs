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
using System.Threading;

namespace EZWinGet
{
    public class Program : Form
    {
        private NotifyIcon _trayIcon;
        private System.Windows.Forms.Timer _upgradeCheckTimer;
        private Form _currentUpgradePopup;

        // Settings fields
        private int _updateIntervalHours = 8;
        private bool _showExitOption = true;
        private bool _showWinGetConsoleOption = true;
        private bool _checkUpdatesOnUnlock = true;
        private bool _showBlockUnblockOption = true;   // NEW SETTING

        private readonly string _iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini");

        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "EZWinGet_SingleInstanceMutex", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("EZWinGet is already running.", "EZWinGet", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Program());
            }
        }

        public Program()
        {
            LoadOrCreateSettings();
            InitializeTrayIcon();
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            Console.WriteLine("EZWinGet started at " + DateTime.Now.ToString("hh:mm tt"));
            InitializeUpgradeCheckTimer();
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                _upgradeCheckTimer?.Dispose();
                _trayIcon?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (_checkUpdatesOnUnlock && e.Reason == SessionSwitchReason.SessionUnlock)
            {
                Task.Run(async () => await CheckUpgradesAsync());
            }
        }

        private void LoadOrCreateSettings()
        {
            if (!File.Exists(_iniPath))
            {
                // Create default INI with new setting
                File.WriteAllLines(_iniPath, new[]
                {
                    "[Settings]",
                    "UpdateIntervalHours=0",
                    "ShowExitOption=true",
                    "ShowWinGetConsoleOption=true",
                    "CheckUpdatesOnUnlock=true",
                    "ShowBlockUnblockOption=true"   // NEW
                });
                _updateIntervalHours = 8;
                _showExitOption = true;
                _showWinGetConsoleOption = true;
                _checkUpdatesOnUnlock = true;
                _showBlockUnblockOption = true;
            }
            else
            {
                foreach (var line in File.ReadAllLines(_iniPath))
                {
                    if (line.StartsWith("UpdateIntervalHours=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(line.Substring("UpdateIntervalHours=".Length), out int hours))
                            _updateIntervalHours = hours;
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
                    else if (line.StartsWith("CheckUpdatesOnUnlock=", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line.Substring("CheckUpdatesOnUnlock=".Length).Trim().ToLowerInvariant();
                        _checkUpdatesOnUnlock = value == "true" || value == "1" || value == "yes";
                    }
                    else if (line.StartsWith("ShowBlockUnblockOption=", StringComparison.OrdinalIgnoreCase))   // NEW
                    {
                        var value = line.Substring("ShowBlockUnblockOption=".Length).Trim().ToLowerInvariant();
                        _showBlockUnblockOption = value == "true" || value == "1" || value == "yes";
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

            // Show/hide controlled by INI
            if (_showBlockUnblockOption)
                contextMenu.Items.Add("Block/UnBlock Upgrade", null, (s, e) => BlockUnblockUpgrade());

            if (_showWinGetConsoleOption)
                contextMenu.Items.Add("Open WinGet Console", null, async (s, e) => await OpenWinGetConsoleAsync());

            if (_showExitOption)
                contextMenu.Items.Add("Exit", null, (s, e) => Application.Exit());

            _trayIcon.ContextMenuStrip = contextMenu;
            Console.WriteLine("Tray icon initialized with EZ.ico");
        }

        private void InitializeUpgradeCheckTimer()
        {
            if (_updateIntervalHours > 0)
            {
                _upgradeCheckTimer = new System.Windows.Forms.Timer
                {
                    Interval = _updateIntervalHours * 60 * 60 * 1000
                };
                _upgradeCheckTimer.Tick += async (s, e) => await CheckUpgradesAsync();
                _upgradeCheckTimer.Start();
                Console.WriteLine($"Periodic upgrade check timer initialized (every {_updateIntervalHours} hours)");
            }
            else
            {
                Console.WriteLine("Periodic upgrade check timer disabled (UpdateIntervalHours=0)");
            }
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

            if (_currentUpgradePopup != null && !_currentUpgradePopup.IsDisposed)
            {
                try
                {
                    _currentUpgradePopup.Invoke(new Action(() =>
                    {
                        _currentUpgradePopup.Close();
                        _currentUpgradePopup.Dispose();
                    }));
                }
                catch
                {
                    _currentUpgradePopup.Dispose();
                }
                _currentUpgradePopup = null;
            }

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

            var form = new Form
            {
                Text = "EZWinGet - Available Upgrades?",
                Size = new Size(finalWidth, 400),
                FormBorderStyle = FormBorderStyle.Sizable,
                StartPosition = FormStartPosition.CenterScreen,
                TopMost = true
            };

            var textBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Text = filteredOutput,
                Font = new Font("Consolas", 10),
                TabStop = false
            };

            form.Shown += (s, e) =>
            {
                textBox.SelectionLength = 0;
                textBox.SelectionStart = 0;
                form.ActiveControl = null;
                form.TopMost = true;
                form.Activate();
                form.BringToFront();
                form.Focus();
            };

            var installButton = new Button
            {
                Text = "Install Upgrades",
                Dock = DockStyle.Bottom,
                Height = 30
            };
            installButton.Click += async (s, e) =>
            {
                form.Close();
                await InstallUpgradesAsync();
            };

            var okButton = new Button
            {
                Text = "OK",
                Dock = DockStyle.Bottom,
                Height = 60
            };
            okButton.Click += (s, e) => form.Close();

            form.Controls.Add(textBox);
            form.Controls.Add(installButton);
            form.Controls.Add(okButton);

            _currentUpgradePopup = form;
            form.ShowDialog();
            _currentUpgradePopup = null;
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

        // ====================== SIMPLE BLOCK/UNBLOCK ======================
        private void BlockUnblockUpgrade()
        {
            Console.WriteLine("Block/UnBlock Upgrade clicked at " + DateTime.Now.ToString("hh:mm tt"));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Verb = "runas",
                UseShellExecute = true,
                Arguments = "-NoProfile -NoExit -Command \"" +
                            "$host.UI.RawUI.WindowTitle = 'EZWinGet - Block / UnBlock Upgrade';" +
                            "Clear-Host;" +
                            "Write-Host '=== Installed Apps ===' -ForegroundColor Cyan;" +
                            "winget list;" +
                            "Write-Host '';" +
                            "Write-Host '=== Blocked Apps ===' -ForegroundColor Cyan;" +
                            "winget pin list;" +
                            "Write-Host '';" +
                            "Write-Host 'Use the popup window to Block or Unblock apps.' -ForegroundColor Yellow;"
            };

            Process consoleProcess = null;
            try
            {
                consoleProcess = Process.Start(psi);

                while (true)
                {
                    using (var form = new Form
                    {
                        Text = "Block / UnBlock App",
                        Size = new Size(520, 230),
                        StartPosition = FormStartPosition.CenterScreen,
                        FormBorderStyle = FormBorderStyle.FixedDialog,
                        MaximizeBox = false,
                        MinimizeBox = false
                    })
                    {
                        var label = new Label
                        {
                            Text = "Enter exact App ID:",
                            Location = new Point(20, 20),
                            AutoSize = true
                        };

                        var txtId = new TextBox
                        {
                            Location = new Point(20, 50),
                            Width = 460,
                            Font = new Font("Consolas", 10)
                        };

                        var btnBlock = new Button
                        {
                            Text = "Block",
                            Location = new Point(20, 90),
                            Width = 120,
                            Height = 35
                        };

                        var btnUnblock = new Button
                        {
                            Text = "Unblock",
                            Location = new Point(160, 90),
                            Width = 120,
                            Height = 35
                        };

                        var btnClose = new Button
                        {
                            Text = "Close",
                            Location = new Point(300, 90),
                            Width = 100,
                            Height = 35
                        };

                        btnBlock.Click += (s, e) =>
                        {
                            string id = txtId.Text.Trim();
                            if (!string.IsNullOrEmpty(id))
                            {
                                RunWingetCommand($"pin add --id \"{id}\" --blocking", elevate: true, captureOutput: false);
                                MessageBox.Show($"Blocked: {id}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        };

                        btnUnblock.Click += (s, e) =>
                        {
                            string id = txtId.Text.Trim();
                            if (!string.IsNullOrEmpty(id))
                            {
                                RunWingetCommand($"pin remove --id \"{id}\"", elevate: true, captureOutput: false);
                                MessageBox.Show($"Unblocked: {id}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                        };

                        btnClose.Click += (s, e) => form.Close();

                        form.Controls.Add(label);
                        form.Controls.Add(txtId);
                        form.Controls.Add(btnBlock);
                        form.Controls.Add(btnUnblock);
                        form.Controls.Add(btnClose);

                        form.CancelButton = btnClose;

                        if (form.ShowDialog() != DialogResult.OK)
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                MessageBox.Show("Failed to start the Block/UnBlock tool.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                try { consoleProcess?.Kill(); } catch { }
            }
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
                    return !string.IsNullOrEmpty(error) ? error : output;
                }
                else
                {
                    process.Start();
                    await Task.Run(() => process.WaitForExit());
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