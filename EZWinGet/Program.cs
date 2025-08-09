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
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Program());
        }
        public Program()
        {
            InitializeTrayIcon();
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            Console.WriteLine("EZWinGet started at " + DateTime.Now.ToString("hh:mm tt"));
        }
        private void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = GetEmbeddedIcon("EZWinGet.EZ.ico"), // Adjust if EZ.ico is in a subfolder
                Visible = true,
                Text = "EZWinGet"
            };
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Check for Upgrades Now", null, async (s, e) => await CheckUpgradesAsync());
            contextMenu.Items.Add("Install Upgrades", null, async (s, e) => await InstallUpgradesAsync());
            contextMenu.Items.Add("Open WinGet Console", null, async (s, e) => await OpenWinGetConsoleAsync());
            contextMenu.Items.Add("Exit", null, (s, e) => Application.Exit());
            _trayIcon.ContextMenuStrip = contextMenu;
            Console.WriteLine("Tray icon initialized with EZ.ico");
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
                return SystemIcons.Application; // Fallback to default icon
            }
        }
        private async Task CheckUpgradesAsync()
        {
            Console.WriteLine("Checking for upgrades at " + DateTime.Now.ToString("hh:mm tt"));
            var upgrades = await GetAvailableUpgradesAsync();
            if (upgrades.Count > 0)
            {
                Console.WriteLine($"{upgrades.Count} app{(upgrades.Count > 1 ? "s" : "")} can be updated:");
                foreach (var upgrade in upgrades)
                {
                    Console.WriteLine($"{upgrade.Name} ({upgrade.Id}): {upgrade.Current} -> {upgrade.Available}");
                }
            }
            else
            {
                Console.WriteLine("No upgrades found");
            }
        }
        private async Task InstallUpgradesAsync()
        {
            Console.WriteLine("Install Upgrades menu option clicked at " + DateTime.Now.ToString("hh:mm tt"));
            await RunWingetCommand("upgrade --all", elevate: true);
        }
        private async Task OpenWinGetConsoleAsync()
        {
            Console.WriteLine("Open WinGet Console menu option clicked at " + DateTime.Now.ToString("hh:mm tt"));
            await RunWingetCommand(""); // Run winget without arguments for help text
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
        private async Task<string> RunWingetCommand(string args, bool elevate = false)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"$host.ui.RawUI.WindowTitle = 'EZWinGet'; winget {args}\"", // Set title and run winget
                UseShellExecute = true,
                CreateNoWindow = false
            };
            if (elevate)
            {
                startInfo.Verb = "runas";
            }
            var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
                await Task.Run(() => process.WaitForExit());
                Console.WriteLine($"Ran winget {args} in PowerShell");
                return "PowerShell console displayed. Check window for output.";
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
            // Auto-start
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
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            _trayIcon.Dispose();
            Console.WriteLine("EZWinGet exiting at " + DateTime.Now.ToString("hh:mm tt"));
        }
    }
}