// League Account Manager
// Copyright (c) 2026 Tuc2300. All rights reserved.
// Licensed under the BSD 3-Clause License: https://github.com/Tuc2300/LeagueAccountManager/blob/main/LICENSE

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Microsoft.Web.WebView2.Core;
using static FlaUI.Core.FrameworkAutomationElementBase;
using Label = System.Windows.Forms.Label;
using ProgressBar = System.Windows.Forms.ProgressBar;

namespace Accountmanager
{
    public partial class Form1 : Form
    {
        private const string CURRENT_VERSION = "1.2.2";
        private const string GITHUB_REPO_OWNER = "Tuc2300";
        private const string GITHUB_REPO_NAME = "LeagueAccountManager";
        private List<Account> accounts = new List<Account>();

        public Form1()
        {
            InitializeComponent();
            LoadAccounts();
            InitializeAsync();
            _ = CheckForUpdatesAsync();
        }

        async void InitializeAsync()
        {
            await webView21.EnsureCoreWebView2Async(null);

            webView21.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            string htmlPath = Path.Combine(System.Windows.Forms.Application.StartupPath, "index.html");
            webView21.Source = new Uri(htmlPath);
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "LeagueAccountManager");
                    client.Timeout = TimeSpan.FromSeconds(10);

                    string apiUrl = $"https://api.github.com/repos/{GITHUB_REPO_OWNER}/{GITHUB_REPO_NAME}/releases/latest";

                    var response = await client.GetStringAsync(apiUrl);
                    var release = JsonSerializer.Deserialize<GitHubRelease>(response);

                    if (release == null || release.TagName == null)
                        return;

                    string latestVersion = release.TagName.TrimStart('v');

                    if (IsNewerVersion(latestVersion, CURRENT_VERSION))
                    {
                        var asset = release.Assets?.FirstOrDefault(a =>
                            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

                        if (asset != null)
                        {
                            ShowUpdateDialog(latestVersion, release.Body ?? "Keine Details verfügbar", asset.BrowserDownloadUrl);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update-Check fehlgeschlagen: {ex.Message}");
            }
        }

        private async void ShowUpdateDialog(string newVersion, string changelog, string downloadUrl)
        {
            var result = MessageBox.Show(
                $"🎉 Neue Version verfügbar!\n\n" +
                $"Installierte Version: {CURRENT_VERSION}\n" +
                $"Neue Version: {newVersion}\n\n" +
                $"Änderungen:\n{changelog}\n\n" +
                $"Möchtest du die neue Version jetzt installieren?",
                "Update verfügbar",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information
            );

            if (result == DialogResult.Yes)
            {
                await DownloadAndInstallUpdate(newVersion, downloadUrl);
            }
        }

        private async Task DownloadAndInstallUpdate(string newVersion, string downloadUrl)
        {
            Form progressForm = null;
            Label progressLabel = null;
            ProgressBar progressBar = null;

            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), "LeagueAccountManager_Update");
                Directory.CreateDirectory(tempPath);

                string zipFile = Path.Combine(tempPath, $"LeagueAccountManager_{newVersion}.zip");
                string extractPath = Path.Combine(tempPath, "extracted");

                progressForm = new Form
                {
                    Text = "Update wird installiert...",
                    Size = new Size(450, 150),
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterScreen,
                    MaximizeBox = false,
                    MinimizeBox = false,
                    ControlBox = false,
                    TopMost = true
                };

                progressLabel = new Label
                {
                    Text = "Download wird vorbereitet...",
                    AutoSize = false,
                    Size = new Size(410, 40),
                    Location = new Point(20, 20),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font(progressForm.Font.FontFamily, 10)
                };

                progressBar = new ProgressBar
                {
                    Location = new Point(20, 70),
                    Size = new Size(410, 30),
                    Style = ProgressBarStyle.Continuous
                };

                progressForm.Controls.Add(progressLabel);
                progressForm.Controls.Add(progressBar);
                progressForm.Show();
                System.Windows.Forms.Application.DoEvents();

                progressLabel.Text = "Lade Update herunter...";
                System.Windows.Forms.Application.DoEvents();

                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10);
                    client.DefaultRequestHeaders.Add("User-Agent", "LeagueAccountManager");

                    var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    var buffer = new byte[8192];
                    var totalRead = 0L;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(zipFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        int bytesRead;
                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                var percentage = (int)((totalRead * 100) / totalBytes);
                                progressBar.Value = Math.Min(percentage, 100);

                                double downloadedMB = totalRead / 1024.0 / 1024.0;
                                double totalMB = totalBytes / 1024.0 / 1024.0;

                                progressLabel.Text = $"Download: {percentage}% ({downloadedMB:F1} MB / {totalMB:F1} MB)";
                                System.Windows.Forms.Application.DoEvents();
                            }
                        }
                    }
                }

                progressLabel.Text = "Extrahiere Update...";
                progressBar.Style = ProgressBarStyle.Marquee;
                System.Windows.Forms.Application.DoEvents();

                await Task.Run(() =>
                {
                    Directory.CreateDirectory(extractPath);
                    ZipFile.ExtractToDirectory(zipFile, extractPath);
                });

                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 100;

                progressLabel.Text = "Bereite Installation vor...";
                System.Windows.Forms.Application.DoEvents();

                string updaterScript = CreateUpdateScript(extractPath, tempPath);

                progressForm.Close();

                var installResult = MessageBox.Show(
                    "✅ Update wurde heruntergeladen!\n\n" +
                    "Die Anwendung wird jetzt geschlossen und das Update installiert.\n\n" +
                    "Möchtest du fortfahren?",
                    "Update bereit",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (installResult == DialogResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = updaterScript,
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WorkingDirectory = Path.GetDirectoryName(updaterScript)
                    });

                    System.Windows.Forms.Application.Exit();
                }
                else
                {
                    try
                    {
                        Directory.Delete(tempPath, true);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                progressForm?.Close();

                MessageBox.Show(
                    $"❌ Fehler beim Update-Download:\n\n{ex.Message}\n\n" +
                    $"Bitte lade das Update manuell von GitHub herunter.",
                    "Fehler",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"https://github.com/{GITHUB_REPO_OWNER}/{GITHUB_REPO_NAME}/releases/latest",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private string CreateUpdateScript(string extractPath, string tempPath)
        {
            string currentExePath = System.Windows.Forms.Application.ExecutablePath;
            string currentDirectory = Path.GetDirectoryName(currentExePath);
            string scriptPath = Path.Combine(tempPath, "update.bat");
            string backupPath = Path.Combine(currentDirectory, "backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));

            string script = $@"@echo off
                chcp 65001 >nul
                title League Account Manager - Update Installation
                color 0A

                echo.
                echo ╔════════════════════════════════════════════════════════╗
                echo ║   League Account Manager - Update Installation        ║
                echo ╚════════════════════════════════════════════════════════╝
                echo.

                echo [1/5] Warte auf Anwendungsende...
                timeout /t 3 /nobreak >nul

                echo [2/5] Erstelle Backup...
                if not exist ""{backupPath}"" mkdir ""{backupPath}""
                xcopy ""{currentDirectory}\*.*"" ""{backupPath}\"" /E /Y /I /Q >nul 2>&1
                echo ✓ Backup erstellt in: backup_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}

                echo [3/5] Installiere neue Version...
                xcopy ""{extractPath}\*.*"" ""{currentDirectory}\"" /E /Y /I /Q
                if errorlevel 1 (
                    echo ✗ Fehler bei der Installation!
                    echo Backup wird wiederhergestellt...
                    xcopy ""{backupPath}\*.*"" ""{currentDirectory}\"" /E /Y /I /Q >nul 2>&1
                    pause
                    exit /b 1
                )
                echo ✓ Dateien aktualisiert

                echo [4/5] Räume temporäre Dateien auf...
                rd /s /q ""{tempPath}"" >nul 2>&1
                echo ✓ Bereinigung abgeschlossen

                echo [5/5] Starte Anwendung neu...
                timeout /t 2 /nobreak >nul
                start """" ""{currentExePath}""

                echo.
                echo ╔════════════════════════════════════════════════════════╗
                echo ║           Update erfolgreich installiert! ✓           ║
                echo ╚════════════════════════════════════════════════════════╝
                echo.
                echo Drücke eine beliebige Taste zum Beenden...
                pause >nul

                exit
            ";

            File.WriteAllText(scriptPath, script, Encoding.UTF8);
            return scriptPath;
        }

        private bool IsNewerVersion(string latestVersion, string currentVersion)
        {
            try
            {
                latestVersion = latestVersion.Replace("v", "").Replace("V", "");
                currentVersion = currentVersion.Replace("v", "").Replace("V", "");

                var latest = new Version(latestVersion);
                var current = new Version(currentVersion);

                return latest > current;
            }
            catch
            {
                return false;
            }
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.WebMessageAsJson;
                var messageDoc = JsonDocument.Parse(json);
                var root = messageDoc.RootElement;

                string action = root.GetProperty("action").GetString() ?? "";
                long messageId = root.GetProperty("messageId").GetInt64();

                object responseData = null;
                bool success = true;
                string error = null;

                try
                {
                    switch (action)
                    {
                        case "GET_ACCOUNTS":
                            var decryptedAccounts = accounts.Select(a => new Account
                            {
                                Id = a.Id,
                                Name = a.Name,
                                Tag = a.Tag,
                                Username = a.Username,
                                Password = SecureStorage.Decrypt(a.Password),
                                Region = a.Region,
                                Created = a.Created
                            }).ToList();
                            responseData = decryptedAccounts;
                            break;

                        case "CREATE_ACCOUNT":
                            if (root.TryGetProperty("data", out var createData))
                            {
                                string plainPassword = createData.GetProperty("password").GetString() ?? "";

                                var newAccount = new Account
                                {
                                    Name = createData.GetProperty("name").GetString() ?? "",
                                    Tag = createData.GetProperty("tag").GetString() ?? "",
                                    Username = createData.GetProperty("username").GetString() ?? "",
                                    Password = SecureStorage.Encrypt(plainPassword),
                                    Region = createData.GetProperty("region").GetString() ?? "",
                                    Id = accounts.Count > 0 ? accounts.Max(a => a.Id) + 1 : 1,
                                    Created = DateTime.Now.ToString("yyyy-MM-dd")
                                };
                                accounts.Add(newAccount);
                                SaveAccounts();

                                responseData = new Account
                                {
                                    Id = newAccount.Id,
                                    Name = newAccount.Name,
                                    Tag = newAccount.Tag,
                                    Username = newAccount.Username,
                                    Password = plainPassword,
                                    Region = newAccount.Region,
                                    Created = newAccount.Created
                                };
                            }
                            break;

                        case "UPDATE_ACCOUNT":
                            if (root.TryGetProperty("data", out var updateData))
                            {
                                int id = updateData.GetProperty("id").GetInt32();
                                var account = accounts.FirstOrDefault(a => a.Id == id);
                                if (account != null)
                                {
                                    string plainPassword = updateData.GetProperty("password").GetString() ?? "";

                                    account.Name = updateData.GetProperty("name").GetString() ?? account.Name;
                                    account.Tag = updateData.GetProperty("tag").GetString() ?? account.Tag;
                                    account.Username = updateData.GetProperty("username").GetString() ?? account.Username;
                                    account.Password = SecureStorage.Encrypt(plainPassword);
                                    account.Region = updateData.GetProperty("region").GetString() ?? account.Region;
                                    SaveAccounts();

                                    responseData = new Account
                                    {
                                        Id = account.Id,
                                        Name = account.Name,
                                        Tag = account.Tag,
                                        Username = account.Username,
                                        Password = plainPassword,
                                        Region = account.Region,
                                        Created = account.Created
                                    };
                                }
                                else
                                {
                                    success = false;
                                    error = "Account nicht gefunden";
                                    break;
                                }
                            }
                            break;

                        case "DELETE_ACCOUNT":
                            if (root.TryGetProperty("data", out var deleteData))
                            {
                                int id = deleteData.GetProperty("id").GetInt32();
                                accounts.RemoveAll(a => a.Id == id);
                                SaveAccounts();
                                responseData = new { success = true };
                            }
                            break;

                        case "AUTO_LOGIN":
                            if (root.TryGetProperty("data", out var loginData))
                            {
                                string username = loginData.GetProperty("username").GetString() ?? "";
                                string password = loginData.GetProperty("password").GetString() ?? "";
                                string region = loginData.GetProperty("region").GetString() ?? "";

                                StartLeagueAndLogin(username, password, region);

                                responseData = new { success = true, message = "Login gestartet" };
                            }
                            break;

                        default:
                            success = false;
                            error = "Unbekannte Aktion: " + action;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    success = false;
                    error = ex.Message;
                    MessageBox.Show($"Fehler bei Aktion {action}: {ex.Message}\n{ex.StackTrace}");
                }

                var response = new
                {
                    messageId = messageId,
                    success = success,
                    data = responseData,
                    error = error
                };

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                string responseJson = JsonSerializer.Serialize(response, options);
                webView21.CoreWebView2.PostWebMessageAsJson(responseJson);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler bei WebMessage: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void StartLeagueAndLogin(string username, string password, string region)
        {
            try
            {
                string riotClientPath = FindRiotClient();

                if (string.IsNullOrEmpty(riotClientPath))
                {
                    MessageBox.Show("Riot Client wurde nicht gefunden!\n\nBitte stelle sicher, dass League of Legends installiert ist.", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string workingDirectory = Path.GetDirectoryName(riotClientPath);

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = riotClientPath,
                    Arguments = "--launch-product=league_of_legends --launch-patchline=live",
                    UseShellExecute = true,
                    WorkingDirectory = workingDirectory
                };

                Process.Start(startInfo);

                Task.Run(() => PerformAutoLogin(username, password, region));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Starten des Riot Clients:\n\n{ex.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void PerformAutoLogin(string username, string password, string region)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Auto-Login gestartet...");

                int maxWaitTime = 90000;
                int waitedTime = 0;
                Process riotProcess = null;

                System.Diagnostics.Debug.WriteLine("Warte auf Riot Client Prozess...");

                while (waitedTime < maxWaitTime)
                {
                    try
                    {
                        var processes = Process.GetProcessesByName("RiotClientUx");
                        if (processes.Length == 0)
                        {
                            processes = Process.GetProcessesByName("RiotClientServices");
                        }

                        if (processes.Length > 0)
                        {
                            riotProcess = processes[0];
                            System.Diagnostics.Debug.WriteLine($"Riot Client Prozess gefunden nach {waitedTime}ms");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Fehler bei Prozesssuche: {ex.Message}");
                    }

                    System.Threading.Thread.Sleep(1000);
                    waitedTime += 1000;
                }

                if (riotProcess == null)
                {
                    System.Diagnostics.Debug.WriteLine("Riot Client Prozess nicht gefunden!");
                    ShowNotification("Riot Client konnte nicht gefunden werden. Bitte melde dich manuell an.");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("Warte 1 Sekunden auf Login-Fenster...");
                System.Threading.Thread.Sleep(1000);

                System.Diagnostics.Debug.WriteLine("Starte UI Automation...");

                try
                {
                    using (var automation = new UIA3Automation())
                    {
                        AutomationElement window = null;
                        int attempts = 0;
                        int maxAttempts = 20;

                        System.Diagnostics.Debug.WriteLine("Suche Riot Client Fenster...");

                        while (window == null && attempts < maxAttempts)
                        {
                            try
                            {
                                var allWindows = automation.GetDesktop().FindAllChildren();

                                window = allWindows.FirstOrDefault(w =>
                                    w.Name.Contains("Riot Client") ||
                                    w.ClassName.Contains("Riot") ||
                                    w.Name.Contains("League of Legends") ||
                                    w.Name.Contains("Chrome"));

                                if (window != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Fenster gefunden: {window.Name}");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"Versuch {attempts + 1}/{maxAttempts} - Fenster nicht gefunden");
                                    System.Threading.Thread.Sleep(2000);
                                    attempts++;
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Fehler bei Versuch {attempts}: {ex.Message}");
                                System.Threading.Thread.Sleep(2000);
                                attempts++;
                            }
                        }

                        if (window == null)
                        {
                            System.Diagnostics.Debug.WriteLine("Fenster nicht gefunden, verwende Fallback...");
                            UseSendKeysFallback(riotProcess, username, password);
                            return;
                        }

                        System.Threading.Thread.Sleep(3000);

                        try
                        {
                            System.Diagnostics.Debug.WriteLine("Suche UI Elemente...");
                            var allElements = window.FindAllDescendants();
                            System.Diagnostics.Debug.WriteLine($"{allElements.Length} Elemente gefunden");

                            var usernameField = allElements.FirstOrDefault(e =>
                                e.AutomationId.ToLower().Contains("username") ||
                                e.Name.ToLower().Contains("username") ||
                                (e.ControlType == FlaUI.Core.Definitions.ControlType.Edit));

                            if (usernameField != null)
                            {
                                System.Diagnostics.Debug.WriteLine("Username-Feld gefunden, fülle aus...");
                                try
                                {
                                    usernameField.Focus();
                                    System.Threading.Thread.Sleep(500);
                                    usernameField.AsTextBox().Text = username;
                                    System.Threading.Thread.Sleep(1000);
                                }
                                catch
                                {
                                    usernameField.Focus();
                                    System.Threading.Thread.Sleep(500);
                                    SendKeys.SendWait(username);
                                    System.Threading.Thread.Sleep(1000);
                                }

                                SendKeys.SendWait("{TAB}");
                                System.Threading.Thread.Sleep(500);
                                SendKeys.SendWait(password);
                                System.Threading.Thread.Sleep(1000);

                                var loginButton = allElements.FirstOrDefault(b =>
                                    b.Name.ToLower().Contains("sign in") ||
                                    b.Name.ToLower().Contains("anmelden") ||
                                    b.Name.ToLower().Contains("login"));

                                if (loginButton != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Login Button gefunden: {loginButton.Name}");
                                    try
                                    {
                                        loginButton.Click();
                                        System.Diagnostics.Debug.WriteLine("Login Button geklickt");
                                    }
                                    catch
                                    {
                                        SendKeys.SendWait("{ENTER}");
                                        System.Diagnostics.Debug.WriteLine("Enter gedrückt (Fallback)");
                                    }
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("Login Button nicht gefunden, drücke Enter");
                                    SendKeys.SendWait("{ENTER}");
                                }

                                ShowNotification("Login Daten wurden eingegeben!");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("Username-Feld nicht gefunden, verwende SendKeys...");
                                UseSendKeysFallback(riotProcess, username, password);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Fehler bei Element-Suche: {ex.Message}");
                            UseSendKeysFallback(riotProcess, username, password);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UI Automation fehlgeschlagen: {ex.Message}");
                    UseSendKeysFallback(riotProcess, username, password);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Kritischer Fehler: {ex.Message}");
                ShowNotification($"Fehler beim Auto-Login. Bitte manuell anmelden.");
            }
        }

        private void UseSendKeysFallback(Process riotProcess, string username, string password)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Verwende SendKeys Fallback...");

                System.Threading.Thread.Sleep(2000);

                IntPtr handle = riotProcess.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    SetForegroundWindow(handle);
                    System.Threading.Thread.Sleep(1000);
                }

                SendKeys.SendWait(username);
                System.Threading.Thread.Sleep(500);
                SendKeys.SendWait("{TAB}");
                System.Threading.Thread.Sleep(500);
                SendKeys.SendWait(password);
                System.Threading.Thread.Sleep(500);
                SendKeys.SendWait("{ENTER}");

                System.Diagnostics.Debug.WriteLine("SendKeys Fallback abgeschlossen");
                ShowNotification("Login Daten wurden eingegeben (SendKeys)!");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendKeys Fallback fehlgeschlagen: {ex.Message}");
                ShowNotification("Auto-Login fehlgeschlagen. Bitte manuell anmelden.");
            }
        }

        private void ShowNotification(string message)
        {
            try
            {
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)delegate
                    {
                        webView21.CoreWebView2.PostWebMessageAsString($"{{\"type\":\"toast\",\"message\":\"{message}\",\"level\":\"info\"}}");
                    });
                }
                else
                {
                    webView21.CoreWebView2.PostWebMessageAsString($"{{\"type\":\"toast\",\"message\":\"{message}\",\"level\":\"info\"}}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification fehlgeschlagen: {ex.Message}");
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);


        private string cachedRiotClientPath = null;

        private string FindRiotClient()
        {
            if (!string.IsNullOrEmpty(cachedRiotClientPath) && File.Exists(cachedRiotClientPath))
            {
                return cachedRiotClientPath;
            }

            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();

                foreach (DriveInfo drive in drives)
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        string possiblePath = Path.Combine(drive.Name, @"Riot Games\Riot Client\RiotClientServices.exe");

                        if (File.Exists(possiblePath))
                        {
                            cachedRiotClientPath = possiblePath;
                            return possiblePath;
                        }
                    }
                }

                foreach (DriveInfo drive in drives)
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        string[] additionalPaths = new string[]
                        {
                            Path.Combine(drive.Name, @"Program Files\Riot Games\Riot Client\RiotClientServices.exe"),
                            Path.Combine(drive.Name, @"Program Files (x86)\Riot Games\Riot Client\RiotClientServices.exe")
                        };

                        foreach (string path in additionalPaths)
                        {
                            if (File.Exists(path))
                            {
                                cachedRiotClientPath = path;
                                return path;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler bei der Suche nach Riot Client:\n\n{ex.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return null;
        }

        private void LoadAccounts()
        {
            string filePath = Path.Combine(System.Windows.Forms.Application.StartupPath, "accounts.json");
            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    accounts = JsonSerializer.Deserialize<List<Account>>(json) ?? new List<Account>();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Fehler beim Laden der Accounts: {ex.Message}");
                    accounts = new List<Account>();
                }
            }
            else
            {
                accounts = new List<Account>
                {
                    new Account
                    {
                        Id = 1,
                        Name = "TestAccount",
                        Tag = "EUW1",
                        Username = "test@example.com",
                        Password = SecureStorage.Encrypt("Test123!"),
                        Region = "euw",
                        Created = DateTime.Now.ToString("yyyy-MM-dd")
                    }
                };
                SaveAccounts();
            }
        }

        private void SaveAccounts()
        {
            try
            {
                string filePath = Path.Combine(System.Windows.Forms.Application.StartupPath, "accounts.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(accounts, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Speichern der Accounts: {ex.Message}");
            }
        }

        private bool _aboutShown = false;

        private void ShowAbout()
        {
            MessageBox.Show(
                "🎮 **League Account Manager v" + CURRENT_VERSION + "**\n\n" +
                "© 2026 **Tuc2300**. Alle Rechte vorbehalten.\n\n" +
                "**BSD 3-Clause License**\n" +
                "• Attribution erforderlich\n" +
                "• Repository: https://github.com/Tuc2300/LeagueAccountManager\n\n" +
                "⚠️ **Copyright darf nicht entfernt werden!**\n" +
                "   Siehe LICENSE Datei für Details.",
                "Über League Account Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            string flagFile = Path.Combine(System.Windows.Forms.Application.StartupPath, "LeagueAccountManager.flag");

            System.Diagnostics.Debug.WriteLine($"Flag file exists: {File.Exists(flagFile)}");
            System.Diagnostics.Debug.WriteLine($"Flag file path: {flagFile}");

            if (!File.Exists(flagFile))
            {
                System.Diagnostics.Debug.WriteLine("Showing About - flag missing");
                ShowAbout();

                try
                {
                    File.WriteAllText(flagFile, $"About shown: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    File.SetAttributes(flagFile, FileAttributes.Hidden | FileAttributes.System);
                    System.Diagnostics.Debug.WriteLine("Flag created successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Flag creation failed: {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("About skipped - flag exists");
            }

            LoadAccounts();
            InitializeAsync();
            _ = CheckForUpdatesAsync();
        }

    }

    public class Account
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Region { get; set; } = "";
        public string Created { get; set; } = "";
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("body")]
        public string Body { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; }
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}
