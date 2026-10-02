using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RhythiansInstaller;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var uninstall = args.Contains("--uninstall") || Path.GetFileName(Environment.ProcessPath!).Contains("Uninstall", StringComparison.OrdinalIgnoreCase);
        var index = Array.IndexOf(args, "--game-dir");
        var root = index >= 0 && index + 1 < args.Length ? args[index + 1] : uninstall ? AppContext.BaseDirectory : FindGame();
        if (uninstall && Path.GetFullPath(Environment.ProcessPath!).StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
        {
            var copy = Path.Combine(Path.GetTempPath(), "Rhythians-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Environment.ProcessPath!, copy);
            var start = new ProcessStartInfo(copy) { UseShellExecute = true };
            start.ArgumentList.Add("--uninstall"); start.ArgumentList.Add("--game-dir"); start.ArgumentList.Add(root);
            Process.Start(start);
            return;
        }
        Application.Run(new SetupWindow(root, uninstall, args.Contains("--update"), args.Contains("--restart")));
    }

    private static string FindGame()
    {
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null)?.ToString() ?? @"C:\Program Files (x86)\Steam";
        var libraries = new List<string> { steam };
        var file = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(file)) foreach (Match match in Regex.Matches(File.ReadAllText(file), "\"path\"\\s+\"([^\"]+)\"")) libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
        return libraries.Select(path => Path.Combine(path, "steamapps", "common", "rhythia")).FirstOrDefault(path => File.Exists(Path.Combine(path, "rhythia.exe"))) ?? Path.Combine(steam, "steamapps", "common", "rhythia");
    }

    private sealed class SetupWindow : Form
    {
        private readonly TextBox folder;
        private readonly Label message;
        private readonly Button action;
        private readonly bool uninstall, update, restart;
        private bool working;

        public SetupWindow(string root, bool uninstall, bool update, bool restart)
        {
            this.uninstall = uninstall; this.update = update; this.restart = restart;
            Text = uninstall ? "Uninstall Rhythians" : "Rhythians Beta";
            ClientSize = new Size(620, 320); MinimumSize = Size; MaximumSize = Size;
            StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10);
            BackColor = Color.FromArgb(18, 18, 22); ForeColor = Color.White;
            var title = new Label { Text = Text, Font = new Font(Font.FontFamily, 23, FontStyle.Bold), AutoSize = true, Location = new Point(26, 22) };
            message = new Label { Text = uninstall ? "Restore the original game and remove the mod files." : "Your profile, map ratings, and challenge passes inside Rhythia.", Location = new Point(28, 80), Size = new Size(560, 65) };
            folder = new TextBox { Text = root, Location = new Point(28, 158), Width = 455, ReadOnly = uninstall };
            var browse = new Button { Text = "Browse", Location = new Point(493, 155), Size = new Size(98, 33), Enabled = !uninstall };
            browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog { SelectedPath = folder.Text }; if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; };
            action = new Button { Text = uninstall ? "Uninstall" : update ? "Update" : "Install", Location = new Point(410, 245), Size = new Size(180, 42), FlatStyle = FlatStyle.Flat };
            action.Click += async (_, _) => await Run();
            var cancel = new Button { Text = "Close", Location = new Point(28, 245), Size = new Size(120, 42), FlatStyle = FlatStyle.Flat };
            cancel.Click += (_, _) => Close();
            Controls.AddRange([title, message, folder, browse, action, cancel]);
            FormClosing += (_, args) => { if (working) args.Cancel = true; };
            if (update) Shown += async (_, _) => await Run();
        }

        private async Task Run()
        {
            action.Enabled = false;
            working = true;
            try
            {
                var root = Path.GetFullPath(folder.Text);
                if (!Directory.Exists(root)) throw new IOException("Choose the folder containing rhythia.exe.");
                var test = Path.Combine(root, ".rhythians-write-test");
                try { File.WriteAllText(test, ""); File.Delete(test); }
                catch (UnauthorizedAccessException)
                {
                    if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw;
                    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
                    if (uninstall) start.ArgumentList.Add("--uninstall");
                    if (update) start.ArgumentList.Add("--update");
                    if (restart) start.ArgumentList.Add("--restart");
                    start.ArgumentList.Add("--game-dir"); start.ArgumentList.Add(root);
                    Process.Start(start); working = false; Close(); return;
                }
                var games = Process.GetProcessesByName("rhythia").Where(process => string.Equals(process.MainModule?.FileName, Path.Combine(root, "rhythia.exe"), StringComparison.OrdinalIgnoreCase)).ToArray();
                if (games.Length > 0 && !update) throw new IOException("Close Rhythia, then try again.");
                message.Text = update ? "Closing Rhythia and installing the update..." : uninstall ? "Restoring Rhythia..." : "Installing Rhythians...";
                foreach (var game in games)
                {
                    game.CloseMainWindow();
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    try { await game.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { throw new IOException("Rhythia is still running. Close it and retry the update."); }
                }
                foreach (var helper in Process.GetProcessesByName("Rhythians.Bridge")) if (string.Equals(helper.MainModule?.FileName, Path.Combine(root, "Rhythians", "Rhythians.Bridge.exe"), StringComparison.OrdinalIgnoreCase)) { helper.Kill(); await helper.WaitForExitAsync(); }
                await Task.Run(() => {
                    if (uninstall) Installation.Uninstall(root);
                    else { using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("Rhythians.payload.zip") ?? throw new IOException("The installation package is missing."); Installation.Install(root, payload, Environment.ProcessPath!); }
                });
                message.Text = uninstall ? "Uninstalled. Your game data and saved login are untouched." : "Installed. Open Rhythia to review the first-run notice and connect your account.";
                if (restart) Process.Start(new ProcessStartInfo("steam://run/2250500") { UseShellExecute = true });
                action.Text = "Done";
                working = false;
                if (update) Close();
            }
            catch (Exception error) { message.Text = error.Message; action.Enabled = true; working = false; }
        }
    }
}
