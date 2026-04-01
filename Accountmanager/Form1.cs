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

namespace Accountmanager
{
    public partial class Form1 : Form
    {
        private const string CURRENT_VERSION = "1.2.2";
        private const string GITHUB_REPO_OWNER = "Tuc2300";
        private const string GITHUB_REPO_NAME = "LeagueAccountManager";
        private List<Account> accounts = new List<Account>();
        private string pendingUpdateVersion = null;
        private string pendingUpdateChangelog = null;
        private string pendingUpdateDownloadUrl = null;
        private string pendingUpdateScript = null;

        public Form1()
        {
            InitializeComponent();
            LoadAccounts();
            InitializeAsync();
        }

        async void InitializeAsync()
        {
            await webView21.EnsureCoreWebView2Async(null);

            webView21.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            string htmlPath = Path.Combine(System.Windows.Forms.Application.StartupPath, "index.html");
            webView21.Source = new Uri(htmlPath);

            webView21.CoreWebView2.NavigationCompleted += async (s, e) =>
            {
                if (e.IsSuccess)
                {
                    await Task.Delay(500);
                    await CheckForUpdatesAsync();
                }
            };
        }

        private void SendToFrontend(object data)
        {
            try
            {
                if (this.InvokeRequired)
                {
                    this.Invoke((MethodInvoker)delegate { SendToFrontend(data); });
                    return;
                }
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                string json = JsonSerializer.Serialize(data, options);
                webView21.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendToFrontend failed: {ex.Message}");
            }
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
                            pendingUpdateVersion = latestVersion;
                            pendingUpdateChangelog = release.Body ?? "Keine Details verfügbar";
                            pendingUpdateDownloadUrl = asset.BrowserDownloadUrl;

                            SendToFrontend(new
                            {
                                type = "updateAvailable",
                                newVersion = latestVersion,
                                changelog = release.Body ?? "Keine Details verfügbar",
                                downloadUrl = asset.BrowserDownloadUrl
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update-Check fehlgeschlagen: {ex.Message}");
            }
        }

        private async Task DownloadAndInstallUpdate(string downloadUrl)
        {
            try
            {
                string newVersion = pendingUpdateVersion ?? "update";
                string tempPath = Path.Combine(Path.GetTempPath(), "LeagueAccountManager_Update");
                Directory.CreateDirectory(tempPath);

                string zipFile = Path.Combine(tempPath, $"LeagueAccountManager_{newVersion}.zip");
                string extractPath = Path.Combine(tempPath, "extracted");

                SendToFrontend(new { type = "updateProgress", percentage = 0, message = "Download wird gestartet..." });

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
                                double downloadedMB = totalRead / 1024.0 / 1024.0;
                                double totalMB = totalBytes / 1024.0 / 1024.0;

                                SendToFrontend(new
                                {
                                    type = "updateProgress",
                                    percentage = Math.Min(percentage, 95),
                                    message = $"Download: {percentage}% ({downloadedMB:F1} MB / {totalMB:F1} MB)"
                                });
                            }
                        }
                    }
                }

                SendToFrontend(new { type = "updateProgress", percentage = 96, message = "Extrahiere Update..." });

                await Task.Run(() =>
                {
                    Directory.CreateDirectory(extractPath);
                    ZipFile.ExtractToDirectory(zipFile, extractPath);
                });

                SendToFrontend(new { type = "updateProgress", percentage = 98, message = "Bereite Installation vor..." });

                pendingUpdateScript = CreateUpdateScript(extractPath, tempPath);

                SendToFrontend(new { type = "updateReady" });

                await Task.Delay(2000);

                Process.Start(new ProcessStartInfo
                {
                    FileName = pendingUpdateScript,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(pendingUpdateScript)
                });

                System.Windows.Forms.Application.Exit();
            }
            catch (Exception ex)
            {
                SendToFrontend(new
                {
                    type = "toast",
                    message = $"Update fehlgeschlagen: {ex.Message}",
                    level = "error"
                });

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

                        case "START_UPDATE":
                            if (root.TryGetProperty("data", out var updateRequestData))
                            {
                                string downloadUrl = updateRequestData.GetProperty("downloadUrl").GetString() ?? "";
                                if (!string.IsNullOrEmpty(downloadUrl))
                                {
                                    _ = DownloadAndInstallUpdate(downloadUrl);
                                    responseData = new { success = true, message = "Update gestartet" };
                                }
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
                    SendToFrontend(new { type = "loginProgress", step = 0, totalSteps = 4, message = "Riot Client wurde nicht gefunden! Bitte stelle sicher, dass League of Legends installiert ist.", status = "error" });
                    return;
                }

                SendToFrontend(new { type = "loginProgress", step = 1, totalSteps = 4, message = "Riot Client wird gestartet...", status = "active" });

                string workingDirectory = Path.GetDirectoryName(riotClientPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = riotClientPath,
                    Arguments = "--launch-product=league_of_legends --launch-patchline=live",
                    UseShellExecute = true,
                    WorkingDirectory = workingDirectory
                });

                Task.Run(async () => await PerformAutoLogin(username, password));
            }
            catch (Exception ex)
            {
                SendToFrontend(new { type = "loginProgress", step = 0, totalSteps = 4, message = $"Fehler beim Starten: {ex.Message}", status = "error" });
            }
        }

        private async Task PerformAutoLogin(string username, string password)
        {
            try
            {
                // Step 1 already sent by StartLeagueAndLogin
                SendToFrontend(new { type = "loginProgress", step = 1, totalSteps = 4, message = "Riot Client gestartet!", status = "done" });

                // Step 2: Wait for process
                SendToFrontend(new { type = "loginProgress", step = 2, totalSteps = 4, message = "Warte auf Riot Client Prozess...", status = "active" });

                int maxWaitTime = 90000;
                int waitedTime = 0;
                Process riotProcess = null;

                while (waitedTime < maxWaitTime)
                {
                    var processes = Process.GetProcessesByName("RiotClientUx");
                    if (processes.Length == 0)
                        processes = Process.GetProcessesByName("RiotClientServices");

                    if (processes.Length > 0)
                    {
                        riotProcess = processes[0];
                        break;
                    }

                    await Task.Delay(1000);
                    waitedTime += 1000;
                }

                if (riotProcess == null)
                {
                    SendToFrontend(new { type = "loginProgress", step = 2, totalSteps = 4, message = "Riot Client Prozess konnte nicht gefunden werden.", status = "error" });
                    return;
                }

                SendToFrontend(new { type = "loginProgress", step = 2, totalSteps = 4, message = "Riot Client gefunden!", status = "done" });

                // Step 3: Try API login first, then fallback
                SendToFrontend(new { type = "loginProgress", step = 3, totalSteps = 4, message = "Sende Login-Daten via Riot Client API...", status = "active" });

                bool apiSuccess = await TryLoginViaRiotApi(username, password);

                if (!apiSuccess)
                {
                    SendToFrontend(new { type = "loginProgress", step = 3, totalSteps = 4, message = "API nicht verf\u00fcgbar, verwende UI-Automation...", status = "active" });
                    await Task.Delay(1500);
                    PerformFallbackLogin(riotProcess, username, password);
                }

                SendToFrontend(new { type = "loginProgress", step = 3, totalSteps = 4, message = "Login-Daten gesendet!", status = "done" });

                // Step 4: Done
                SendToFrontend(new { type = "loginProgress", step = 4, totalSteps = 4, message = "Login-Daten wurden erfolgreich eingegeben!", status = "done" });
            }
            catch (Exception ex)
            {
                SendToFrontend(new { type = "loginProgress", step = 0, totalSteps = 4, message = $"Fehler beim Auto-Login: {ex.Message}", status = "error" });
            }
        }

        private async Task<bool> TryLoginViaRiotApi(string username, string password)
        {
            try
            {
                string lockfilePath = FindRiotClientLockfile();
                if (lockfilePath == null) return false;

                // Wait for lockfile to appear
                int attempts = 0;
                while (!File.Exists(lockfilePath) && attempts < 30)
                {
                    await Task.Delay(1000);
                    attempts++;
                }

                if (!File.Exists(lockfilePath)) return false;

                // Wait a moment for the client to be ready
                await Task.Delay(2000);

                // Parse lockfile: name:pid:port:token:protocol
                string lockfileContent = File.ReadAllText(lockfilePath);
                string[] parts = lockfileContent.Split(':');
                if (parts.Length < 5) return false;

                string port = parts[2];
                string token = parts[3];

                // Create HTTP client that ignores SSL errors (Riot uses self-signed cert)
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;

                using var client = new HttpClient(handler);
                string authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{token}"));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authToken);
                client.Timeout = TimeSpan.FromSeconds(10);

                string url = $"https://127.0.0.1:{port}/rso-auth/v1/session/credentials";
                var body = new { username, password, persistLogin = false };
                var jsonContent = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                var response = await client.PutAsync(url, jsonContent);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Riot API Login fehlgeschlagen: {ex.Message}");
                return false;
            }
        }

        private string FindRiotClientLockfile()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string lockfilePath = Path.Combine(localAppData, "Riot Games", "Riot Client", "Config", "lockfile");

            if (File.Exists(lockfilePath)) return lockfilePath;

            // Also check in the installation directory
            if (!string.IsNullOrEmpty(cachedRiotClientPath))
            {
                string installDir = Path.GetDirectoryName(Path.GetDirectoryName(cachedRiotClientPath));
                if (installDir != null)
                {
                    string altPath = Path.Combine(installDir, "Config", "lockfile");
                    if (File.Exists(altPath)) return altPath;
                }
            }

            // Return expected path (we'll wait for it to appear)
            return lockfilePath;
        }

        private void PerformFallbackLogin(Process riotProcess, string username, string password)
        {
            try
            {
                using (var automation = new UIA3Automation())
                {
                    AutomationElement window = null;
                    int attempts = 0;
                    int maxAttempts = 20;

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

                            if (window == null)
                            {
                                System.Threading.Thread.Sleep(2000);
                                attempts++;
                            }
                        }
                        catch
                        {
                            System.Threading.Thread.Sleep(2000);
                            attempts++;
                        }
                    }

                    if (window == null)
                    {
                        UseSendKeysFallback(riotProcess, username, password);
                        return;
                    }

                    System.Threading.Thread.Sleep(3000);

                    var allElements = window.FindAllDescendants();
                    var usernameField = allElements.FirstOrDefault(e =>
                        e.AutomationId.ToLower().Contains("username") ||
                        e.Name.ToLower().Contains("username") ||
                        (e.ControlType == FlaUI.Core.Definitions.ControlType.Edit));

                    if (usernameField != null)
                    {
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
                            try { loginButton.Click(); }
                            catch { SendKeys.SendWait("{ENTER}"); }
                        }
                        else
                        {
                            SendKeys.SendWait("{ENTER}");
                        }
                    }
                    else
                    {
                        UseSendKeysFallback(riotProcess, username, password);
                    }
                }
            }
            catch
            {
                UseSendKeysFallback(riotProcess, username, password);
            }
        }

        private void UseSendKeysFallback(Process riotProcess, string username, string password)
        {
            try
            {
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendKeys Fallback fehlgeschlagen: {ex.Message}");
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

            if (!File.Exists(flagFile))
            {
                ShowAbout();

                try
                {
                    File.WriteAllText(flagFile, $"About shown: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    File.SetAttributes(flagFile, FileAttributes.Hidden | FileAttributes.System);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Flag creation failed: {ex.Message}");
                }
            }
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
