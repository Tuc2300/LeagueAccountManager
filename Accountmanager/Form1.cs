// League Account Manager
// Copyright (c) 2026 Tuc2300. All rights reserved.
// Licensed under the BSD 3-Clause License: https://github.com/Tuc2300/LeagueAccountManager/blob/main/LICENSE

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Web.WebView2.Core;

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
        private System.Threading.CancellationTokenSource autoLoginCts = null;

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

        private async Task TestUpdateFlowAsync()
        {
            // DEBUG-only: fetches the latest release regardless of version, surfaces the
            // update banner in the frontend, and runs the full download/install flow.
            // Because Debugger.IsAttached is true, DownloadAndInstallUpdate will skip
            // the actual file replace + Application.Exit and just leave the script + log
            // in %TEMP% for inspection.
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "LeagueAccountManager");
                    client.Timeout = TimeSpan.FromSeconds(15);

                    string apiUrl = $"https://api.github.com/repos/{GITHUB_REPO_OWNER}/{GITHUB_REPO_NAME}/releases/latest";
                    var response = await client.GetStringAsync(apiUrl);
                    var release = JsonSerializer.Deserialize<GitHubRelease>(response);
                    var asset = release?.Assets?.FirstOrDefault(a =>
                        a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

                    if (asset == null)
                    {
                        SendToFrontend(new { type = "toast", message = "Debug-Test: Kein .zip Asset im Latest-Release gefunden.", level = "error" });
                        return;
                    }

                    pendingUpdateVersion = (release.TagName?.TrimStart('v') ?? "debug") + "-DEBUG";
                    pendingUpdateChangelog = release.Body ?? "";
                    pendingUpdateDownloadUrl = asset.BrowserDownloadUrl;

                    SendToFrontend(new
                    {
                        type = "updateAvailable",
                        newVersion = pendingUpdateVersion,
                        changelog = pendingUpdateChangelog,
                        downloadUrl = pendingUpdateDownloadUrl
                    });

                    System.Diagnostics.Debug.WriteLine($"[Update][Test] Banner ausgelöst, Asset: {asset.BrowserDownloadUrl}");
                }
            }
            catch (Exception ex)
            {
                SendToFrontend(new { type = "toast", message = $"Debug-Test fehlgeschlagen: {ex.Message}", level = "error" });
            }
        }

        private async Task DownloadAndInstallUpdate(string downloadUrl)
        {
            try
            {
                string newVersion = pendingUpdateVersion ?? "update";
                string tempPath = Path.Combine(Path.GetTempPath(), "LeagueAccountManager_Update");
                // Clean leftover from previous runs so we never restore stale files
                try { if (Directory.Exists(tempPath)) Directory.Delete(tempPath, true); } catch { }
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

                await Task.Delay(1500);

                if (System.Diagnostics.Debugger.IsAttached)
                {
                    // Don't actually replace files / exit while debugging — let the dev step through
                    SendToFrontend(new
                    {
                        type = "toast",
                        message = $"Debug-Modus: Update-Skript wurde NICHT ausgeführt.\nPfad: {pendingUpdateScript}\nLog: {Path.Combine(tempPath, "update.log")}",
                        level = "info"
                    });
                    System.Diagnostics.Debug.WriteLine($"[Update] Skript bereit unter: {pendingUpdateScript}");
                    System.Diagnostics.Debug.WriteLine($"[Update] Extracted: {extractPath}");
                    System.Diagnostics.Debug.WriteLine($"[Update] Log wird sein: {Path.Combine(tempPath, "update.log")}");
                    return;
                }

                int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{pendingUpdateScript}\" -ParentPid {currentPid}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
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
            string scriptPath = Path.Combine(tempPath, "update.ps1");
            string logPath = Path.Combine(tempPath, "update.log");
            string backupPath = Path.Combine(tempPath, "backup");

            // PowerShell script: waits on parent PID, backs up to TEMP (not the install dir),
            // copies new files via Copy-Item, restores backup on failure, logs everything,
            // then relaunches the app and self-deletes.
            string script = @"param(
    [int]$ParentPid
)

$ErrorActionPreference = 'Stop'
$LogPath       = '" + logPath.Replace("'", "''") + @"'
$InstallDir    = '" + currentDirectory.Replace("'", "''") + @"'
$ExtractDir    = '" + extractPath.Replace("'", "''") + @"'
$BackupDir     = '" + backupPath.Replace("'", "''") + @"'
$AppExe        = '" + currentExePath.Replace("'", "''") + @"'
$TempRoot      = '" + tempPath.Replace("'", "''") + @"'

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = ('{0} [{1}] {2}' -f (Get-Date -Format 'HH:mm:ss.fff'), $Level, $Message)
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
}

try {
    Write-Log ""Updater gestartet (ParentPid=$ParentPid)""
    Write-Log ""InstallDir: $InstallDir""
    Write-Log ""ExtractDir: $ExtractDir""

    # 1. Auf Beendigung der App warten
    if ($ParentPid -gt 0) {
        try {
            $proc = Get-Process -Id $ParentPid -ErrorAction SilentlyContinue
            if ($proc) {
                Write-Log ""Warte auf Prozess $ParentPid ($($proc.ProcessName))...""
                $proc.WaitForExit(15000) | Out-Null
            }
        } catch { Write-Log ""WaitForExit Fehler: $_"" 'WARN' }
    }
    Start-Sleep -Milliseconds 500

    # 2. Backup nach %TEMP% (nicht in den Install-Ordner!)
    Write-Log 'Erstelle Backup...'
    if (Test-Path -LiteralPath $BackupDir) { Remove-Item -LiteralPath $BackupDir -Recurse -Force }
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
    Get-ChildItem -LiteralPath $InstallDir -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $BackupDir -Recurse -Force
    }
    Write-Log ""Backup OK -> $BackupDir""

    # 3. Neue Dateien rüberkopieren
    Write-Log 'Kopiere neue Dateien...'
    Get-ChildItem -LiteralPath $ExtractDir -Force | ForEach-Object {
        $dest = Join-Path $InstallDir $_.Name
        if (Test-Path -LiteralPath $dest) {
            Remove-Item -LiteralPath $dest -Recurse -Force -ErrorAction SilentlyContinue
        }
        Copy-Item -LiteralPath $_.FullName -Destination $InstallDir -Recurse -Force
    }
    Write-Log 'Kopieren OK'

    # 4. Verifizieren dass die App noch existiert
    if (-not (Test-Path -LiteralPath $AppExe)) {
        throw ""Neue Exe nicht gefunden: $AppExe""
    }

    # 5. App neu starten
    Write-Log 'Starte App neu...'
    Start-Process -FilePath $AppExe -WorkingDirectory $InstallDir

    Write-Log 'Update abgeschlossen. Temp-Ordner verbleibt für Diagnose.'
}
catch {
    Write-Log ""FEHLER: $_"" 'ERROR'
    Write-Log 'Versuche Backup wiederherzustellen...' 'WARN'
    try {
        if (Test-Path -LiteralPath $BackupDir) {
            Get-ChildItem -LiteralPath $BackupDir -Force | ForEach-Object {
                Copy-Item -LiteralPath $_.FullName -Destination $InstallDir -Recurse -Force
            }
            Write-Log 'Backup wiederhergestellt.' 'WARN'
        }
    } catch {
        Write-Log ""Restore fehlgeschlagen: $_"" 'ERROR'
    }
    # App trotzdem versuchen zu starten
    if (Test-Path -LiteralPath $AppExe) {
        Start-Process -FilePath $AppExe -WorkingDirectory $InstallDir
    }
    exit 1
}
";

            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
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

                        case "CANCEL_AUTO_LOGIN":
                            try { autoLoginCts?.Cancel(); } catch { }
                            responseData = new { success = true };
                            break;

                        case "IS_DEBUGGER_ATTACHED":
                            responseData = new { attached = System.Diagnostics.Debugger.IsAttached };
                            break;

                        case "TEST_UPDATE":
                            // Only allow when a debugger is attached so we never run this in release
                            if (System.Diagnostics.Debugger.IsAttached)
                            {
                                _ = TestUpdateFlowAsync();
                                responseData = new { success = true };
                            }
                            else
                            {
                                success = false;
                                error = "Nur im Debug-Modus verfügbar.";
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
                    SendToFrontend(new { type = "loginProgress", step = 0, totalSteps = 3, message = "Riot Client wurde nicht gefunden! Bitte stelle sicher, dass League of Legends installiert ist.", status = "error" });
                    return;
                }

                SendToFrontend(new { type = "loginProgress", step = 1, totalSteps = 3, message = "Riot Client wird gestartet...", status = "active" });

                string workingDirectory = Path.GetDirectoryName(riotClientPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = riotClientPath,
                    Arguments = "--launch-product=league_of_legends --launch-patchline=live",
                    UseShellExecute = true,
                    WorkingDirectory = workingDirectory
                });

                try { autoLoginCts?.Cancel(); autoLoginCts?.Dispose(); } catch { }
                autoLoginCts = new System.Threading.CancellationTokenSource();
                var ct = autoLoginCts.Token;
                Task.Run(async () => await PerformAutoLogin(username, password, ct));
            }
            catch (Exception ex)
            {
                SendToFrontend(new { type = "loginProgress", step = 0, totalSteps = 3, message = $"Fehler beim Starten: {ex.Message}", status = "error" });
            }
        }

        private async Task PerformAutoLogin(string username, string password, System.Threading.CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                SendToFrontend(new { type = "loginProgress", step = 1, totalSteps = 3, message = "Riot Client gestartet!", status = "done" });

                // Step 2: Wait for the Riot Client UI process (RiotClientUx)
                SendToFrontend(new { type = "loginProgress", step = 2, totalSteps = 3, message = "Warte auf Login-Fenster...", status = "active" });

                Process riotProcess = null;
                int maxWaitTime = 60000;
                int waitedTime = 0;

                // First wait for RiotClientUx specifically (the UI process with the login window)
                while (waitedTime < maxWaitTime)
                {
                    var processes = GetRiotClientProcesses();
                    if (processes.Length > 0)
                    {
                        riotProcess = processes[0];
                        break;
                    }

                    // Also check RiotClientServices as fallback
                    if (waitedTime > 30000)
                    {
                        processes = Process.GetProcessesByName("RiotClientServices");
                        if (processes.Length > 0)
                        {
                            riotProcess = processes[0];
                            break;
                        }
                    }

                    await Task.Delay(1000, ct);
                    waitedTime += 1000;
                }

                if (riotProcess == null)
                {
                    SendToFrontend(new { type = "loginProgress", step = 2, totalSteps = 3, message = "Riot Client konnte nicht gefunden werden.", status = "error" });
                    return;
                }

                // Wait for the login window handle to appear (Riot Client uses Chromium, so no UIA3 Edit controls)
                SendToFrontend(new { type = "loginProgress", step = 2, totalSteps = 3, message = "Warte auf Login-Fenster...", status = "active" });

                IntPtr loginWindowHandle = IntPtr.Zero;
                for (int i = 0; i < 300; i++)
                {
                    try
                    {
                        loginWindowHandle = FindRiotClientWindow();
                        if (loginWindowHandle != IntPtr.Zero) break;
                    }
                    catch { }

                    await Task.Delay(200, ct);
                }

                ct.ThrowIfCancellationRequested();
                // Brief delay so the Chromium login form is interactive
                await Task.Delay(1200, ct);

                ct.ThrowIfCancellationRequested();

                SendToFrontend(new { type = "loginProgress", step = 2, totalSteps = 3, message = "Login-Fenster bereit!", status = "done" });

                // Step 3: Enter credentials
                SendToFrontend(new { type = "loginProgress", step = 3, totalSteps = 3, message = "Gebe Login-Daten ein...", status = "active" });

                // Riot Client is Chromium-based, so use SendKeys directly on the window handle we found
                UseSendKeysFallback(loginWindowHandle, username, password);

                SendToFrontend(new { type = "loginProgress", step = 3, totalSteps = 3, message = "Login-Daten wurden eingegeben!", status = "done" });
            }
            catch (OperationCanceledException)
            {
                SendToFrontend(new { type = "loginProgress", step = 0, totalSteps = 3, message = "Auto-Login abgebrochen.", status = "error" });
            }
            catch (Exception ex)
            {
                SendToFrontend(new { type = "loginProgress", step = 0, totalSteps = 3, message = $"Fehler beim Auto-Login: {ex.Message}", status = "error" });
            }
        }

        private void UseSendKeysFallback(IntPtr windowHandle, string username, string password)
        {
            try
            {
                if (windowHandle == IntPtr.Zero)
                {
                    windowHandle = FindRiotClientWindow();
                }
                if (windowHandle != IntPtr.Zero)
                {
                    SetForegroundWindow(windowHandle);
                    System.Threading.Thread.Sleep(400);
                }

                // Select all in case there's existing text, then type username
                SendKeys.SendWait("^(a)");
                System.Threading.Thread.Sleep(100);
                SendKeys.SendWait(username);
                System.Threading.Thread.Sleep(300);

                // Tab to password and enter it
                SendKeys.SendWait("{TAB}");
                System.Threading.Thread.Sleep(300);
                SendKeys.SendWait(password);
                System.Threading.Thread.Sleep(300);

                // Submit
                SendKeys.SendWait("{ENTER}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendKeys Fallback fehlgeschlagen: {ex.Message}");
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private static Process[] GetRiotClientProcesses()
        {
            // The actual UI process is named "Riot Client" (with space), not "RiotClientUx"
            var list = new System.Collections.Generic.List<Process>();
            list.AddRange(Process.GetProcessesByName("Riot Client"));
            list.AddRange(Process.GetProcessesByName("RiotClientUx"));
            list.AddRange(Process.GetProcessesByName("RiotClientUxRender"));
            return list.ToArray();
        }

        private static IntPtr FindRiotClientWindow()
        {
            IntPtr found = IntPtr.Zero;
            var uxPids = new System.Collections.Generic.HashSet<uint>(
                GetRiotClientProcesses().Select(p => (uint)p.Id));

            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                int len = GetWindowTextLength(hWnd);
                if (len == 0) return true;
                var sb = new System.Text.StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (title.IndexOf("Riot Client", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = hWnd;
                    return false;
                }
                // Also match by owning RiotClientUx process
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (uxPids.Contains(pid) && !string.IsNullOrWhiteSpace(title))
                {
                    found = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            return found;
        }


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
