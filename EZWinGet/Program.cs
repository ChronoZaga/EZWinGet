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
    // Main application class, inherits from Form to create a system tray application
    public class Program : Form
    {
        // Private field to hold the system tray icon
        private NotifyIcon _trayIcon;
        // Private field for the timer to check upgrades every 8 hours
        private System.Windows.Forms.Timer _upgradeCheckTimer;
        // Entry point for the application
        [STAThread]
        static void Main()
        {
            // Enable visual styles for a modern UI look
            Application.EnableVisualStyles();
            // Set default text rendering compatibility
            Application.SetCompatibleTextRenderingDefault(false);
            // Start the application with an instance of Program
            Application.Run(new Program());
        }
        // Constructor for the Program class
        public Program()
        {
            // Initialize the system tray icon and its context menu
            InitializeTrayIcon();
            // Start the application in a minimized state
            this.WindowState = FormWindowState.Minimized;
            // Hide the form from the taskbar
            this.ShowInTaskbar = false;
            // Log application start time to console
            Console.WriteLine("EZWinGet started at " + DateTime.Now.ToString("hh:mm tt"));
            // Initialize the timer for periodic upgrade checks
            InitializeUpgradeCheckTimer();
        }
        // Initializes the system tray icon and its context menu
        private void InitializeTrayIcon()
        {
            // Create a new NotifyIcon for the system tray
            _trayIcon = new NotifyIcon
            {
                // Load the embedded icon for the tray
                Icon = GetEmbeddedIcon("EZWinGet.EZ.ico"), // Adjust if EZ.ico is in a subfolder
                // Make the icon visible in the system tray
                Visible = true,
                // Set tooltip text for the tray icon
                Text = "EZWinGet"
            };
            // Create a context menu for the tray icon
            var contextMenu = new ContextMenuStrip();
            // Add menu item to check for upgrades
            contextMenu.Items.Add("Check for Upgrades Now", null, async (s, e) => await CheckUpgradesAsync());
            // Add menu item to install upgrades
            contextMenu.Items.Add("Install Upgrades", null, async (s, e) => await InstallUpgradesAsync());
            // Add menu item to open WinGet console
            contextMenu.Items.Add("Open WinGet Console", null, async (s, e) => await OpenWinGetConsoleAsync());
            // Add menu item to exit the application
            contextMenu.Items.Add("Exit", null, (s, e) => Application.Exit());
            // Assign the context menu to the tray icon
            _trayIcon.ContextMenuStrip = contextMenu;
            // Log tray icon initialization
            Console.WriteLine("Tray icon initialized with EZ.ico");
        }
        // Initializes the timer for periodic upgrade checks
        private void InitializeUpgradeCheckTimer()
        {
            _upgradeCheckTimer = new System.Windows.Forms.Timer
            {
                Interval = 8 * 60 * 60 * 1000 // 8 hours in milliseconds
                //Interval = 30000 // 30 seconds for testing purposes, change to 8 * 60 * 60 * 1000 for production
            };
            _upgradeCheckTimer.Tick += async (s, e) => await CheckUpgradesAsync();
            _upgradeCheckTimer.Start();
            Console.WriteLine("Periodic upgrade check timer initialized (every 8 hours)");
        }
        // Retrieves the embedded icon resource
        private Icon GetEmbeddedIcon(string resourceName)
        {
            try
            {
                // Get the resource stream for the specified icon
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    // Check if the resource was found
                    if (stream == null)
                        throw new FileNotFoundException($"Resource {resourceName} not found.");
                    // Create and return an Icon from the stream
                    return new Icon(stream);
                }
            }
            catch (Exception ex)
            {
                // Log error if icon loading fails
                Console.WriteLine($"Failed to load EZ.ico: {ex.Message}");
                // Return default application icon as a fallback
                return SystemIcons.Application;
            }
        }
        // Checks for available upgrades using winget and shows output in a resizable form
        private async Task CheckUpgradesAsync()
        {
            // Log the start of the upgrade check
            Console.WriteLine("Checking for upgrades at " + DateTime.Now.ToString("hh:mm tt"));
            // Get list of available upgrades
            var output = await RunWingetCommand("upgrade --include-unknown", elevate: false, captureOutput: true);
            // Trim leading special characters and spaces until the first alphanumeric character
            int startIndex = 0;
            while (startIndex < output.Length && !char.IsLetterOrDigit(output[startIndex]))
            {
                startIndex++;
            }
            string cleanedOutput = startIndex < output.Length ? output.Substring(startIndex) : "No output available.";
            // Create a new form for displaying the output
            using (var form = new Form())
            {
                form.Text = "EZWinGet - Available Upgrades?";
                form.Size = new Size(600, 400); // Set initial size
                form.FormBorderStyle = FormBorderStyle.Sizable; // Make the form resizable
                form.StartPosition = FormStartPosition.CenterScreen;
                // Create a TextBox to display the output
                var textBox = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Fill,
                    Text = cleanedOutput,
                    Font = new Font("Consolas", 10) // Use a monospaced font for better alignment
                };
                // Create an OK button
                var okButton = new Button
                {
                    Text = "OK",
                    Dock = DockStyle.Bottom,
                    Height = 30
                };
                okButton.Click += (s, e) => form.Close();
                // Add controls to the form
                form.Controls.Add(textBox);
                form.Controls.Add(okButton);
                // Show the form as a dialog
                form.ShowDialog();
            }
        }
        // Installs all available upgrades using winget
        private async Task InstallUpgradesAsync()
        {
            // Log when the Install Upgrades option is clicked
            Console.WriteLine("Install Upgrades menu option clicked at " + DateTime.Now.ToString("hh:mm tt"));
            // Run winget upgrade command with specified arguments, requiring elevation
            await RunWingetCommand("upgrade --all -h --include-unknown --accept-package-agreements --accept-source-agreements", elevate: true, captureOutput: false, keepConsoleOpen: false, showPause: true);
        }
        // Opens the WinGet console in PowerShell
        private async Task OpenWinGetConsoleAsync()
        {
            // Log when the Open WinGet Console option is clicked
            Console.WriteLine("Open WinGet Console menu option clicked at " + DateTime.Now.ToString("hh:mm tt"));
            // Run winget without arguments to show help text
            await RunWingetCommand("", elevate: true, captureOutput: false, keepConsoleOpen: true); // Run winget without arguments for help text
        }
        // Retrieves a list of available upgrades by parsing winget output
        private async Task<List<(string Name, string Id, string Current, string Available)>> GetAvailableUpgradesAsync()
        {
            // Run winget upgrade command and capture output
            var output = await RunWingetCommand("upgrade --include-unknown");
            // Initialize list to store upgrade information
            var upgrades = new List<(string Name, string Id, string Current, string Available)>();
            // Split output into lines
            var lines = output.Split('\n');
            // Start from line 3 to skip header
            for (int i = 2; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                // Skip empty lines or lines indicating no updates
                if (string.IsNullOrWhiteSpace(line) || line.Contains("No applicable update")) continue;
                // Split line by multiple spaces to extract fields
                var parts = Regex.Split(line, @"\s{2,}");
                // Ensure line has at least 4 parts (Name, Id, Current, Available)
                if (parts.Length >= 4)
                    upgrades.Add((parts[0], parts[1], parts[2], parts[3]));
            }
            // Log number of upgrades parsed
            Console.WriteLine($"Parsed {upgrades.Count} upgrades from winget output");
            return upgrades;
        }
        // Runs a winget command in a PowerShell window
        private async Task<string> RunWingetCommand(string args, bool elevate = false, bool captureOutput = false, bool keepConsoleOpen = false, bool showPause = false)
        {
            // Set up process start information for PowerShell
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = captureOutput
                    ? $"-NoProfile -Command \"winget {args}\""
                    : $"-NoProfile {(keepConsoleOpen ? "-NoExit " : "")}-Command \"$host.ui.RawUI.WindowTitle = 'EZWinGet'; winget {args}{(showPause ? "; pause" : "")}\"",
                UseShellExecute = !captureOutput, // UseShellExecute must be false to capture output
                CreateNoWindow = captureOutput, // Hide window if capturing output
                RedirectStandardOutput = captureOutput, // Redirect output if capturing
                RedirectStandardError = captureOutput // Redirect error if capturing
            };
            // Request elevation if specified
            if (elevate)
            {
                startInfo.Verb = "runas";
            }
            // Create and start the process
            var process = new Process { StartInfo = startInfo };
            try
            {
                if (captureOutput)
                {
                    // Start process and capture output
                    process.Start();
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit());
                    // Log successful execution
                    Console.WriteLine($"Ran winget {args} in background");
                    // Combine output and error, prioritizing non-empty error
                    return !string.IsNullOrEmpty(error) ? error : output;
                }
                else
                {
                    // Run as before for non-captured output
                    process.Start();
                    // Wait for the process to exit
                    await Task.Run(() => process.WaitForExit());
                    // Log successful execution
                    Console.WriteLine($"Ran winget {args} in PowerShell");
                    return "PowerShell console displayed. Check window for output.";
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // Log error if the command fails (e.g., user cancels elevation)
                Console.WriteLine($"Winget {args} failed: {ex.Message}");
                return "Elevation cancelled.";
            }
        }
        // Handles form load event
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Hide the form window
            Hide();
            // Attempt to register the application for auto-start
            try
            {
                // Access the registry key for startup applications
                Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)?
                    .SetValue("EZWinGet", Application.ExecutablePath);
                // Log successful auto-start registration
                Console.WriteLine("Auto-start registered at " + DateTime.Now.ToString("hh:mm tt"));
            }
            catch (Exception ex)
            {
                // Log error if auto-start registration fails
                Console.WriteLine($"Failed to register auto-start: {ex.Message}");
            }
            // Run initial upgrade check on application start
            Task.Run(async () => await CheckUpgradesAsync());
        }
        // Handles form closing event
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            // Stop and dispose of the timer
            _upgradeCheckTimer?.Stop();
            _upgradeCheckTimer?.Dispose();
            // Dispose of the tray icon to clean up resources
            _trayIcon.Dispose();
            // Log application exit
            Console.WriteLine("EZWinGet exiting at " + DateTime.Now.ToString("hh:mm tt"));
        }
    }
}