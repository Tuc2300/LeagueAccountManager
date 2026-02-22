using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Microsoft.Web.WebView2.Core;

namespace Accountmanager
{
    public partial class Form1 : Form
    {
        private List<Account> accounts = new List<Account>();
        private static readonly byte[] EncryptionKey = Encoding.UTF8.GetBytes("YourSecretKey123YourSecretKey123"); // 32 Bytes für AES-256

        public Form1()
        {
            InitializeComponent();
            LoadAccounts();
            InitializeAsync();
            _ = CheckForUpdates();
        }

        async void InitializeAsync()
        {
            await webView21.EnsureCoreWebView2Async(null);

            // WebMessage Handler registrieren
            webView21.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            // Lokale HTML laden
            string htmlPath = Path.Combine(System.Windows.Forms.Application.StartupPath, "index.html");
            webView21.Source = new Uri(htmlPath);
        }

        private async Task CheckForUpdates()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "LeagueAccountManager");

                    // GitHub API - Latest Release
                    string apiUrl = "https://api.github.com/repos/DEIN-USERNAME/DEIN-REPO/releases/latest";

                    var response = await client.GetStringAsync(apiUrl);
                    var release = JsonSerializer.Deserialize<JsonElement>(response);

                    string latestVersion = release.GetProperty("tag_name").GetString().Replace("v", "");
                    string currentVersion = "1.0.0"; // Deine aktuelle Version

                    if (latestVersion != currentVersion)
                    {
                        string downloadUrl = release.GetProperty("assets")[0].GetProperty("browser_download_url").GetString();

                        var result = MessageBox.Show(
                            $"Neue Version {latestVersion} verfügbar!\n\nMöchtest du jetzt updaten?",
                            "Update verfügbar",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information
                        );

                        if (result == DialogResult.Yes)
                        {
                            System.Diagnostics.Process.Start(new ProcessStartInfo
                            {
                                FileName = downloadUrl,
                                UseShellExecute = true
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update-Check fehlgeschlagen: {ex.Message}");
            }
        }

        // Passwort verschlüsseln
        private string EncryptPassword(string plainText)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = EncryptionKey;
                aes.GenerateIV();

                ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    // IV am Anfang speichern
                    msEncrypt.Write(aes.IV, 0, aes.IV.Length);

                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                    {
                        swEncrypt.Write(plainText);
                    }

                    return Convert.ToBase64String(msEncrypt.ToArray());
                }
            }
        }

        // Passwort entschlüsseln
        private string DecryptPassword(string cipherText)
        {
            try
            {
                byte[] fullCipher = Convert.FromBase64String(cipherText);

                using (Aes aes = Aes.Create())
                {
                    aes.Key = EncryptionKey;

                    // IV aus den ersten 16 Bytes extrahieren
                    byte[] iv = new byte[16];
                    Array.Copy(fullCipher, 0, iv, 0, iv.Length);
                    aes.IV = iv;

                    ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                    using (MemoryStream msDecrypt = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length))
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                    {
                        return srDecrypt.ReadToEnd();
                    }
                }
            }
            catch
            {
                return cipherText; // Falls Entschlüsselung fehlschlägt, Originalwert zurückgeben
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
                            // Passwörter für JavaScript entschlüsseln
                            var decryptedAccounts = accounts.Select(a => new Account
                            {
                                Id = a.Id,
                                Name = a.Name,
                                Tag = a.Tag,
                                Username = a.Username,
                                Password = DecryptPassword(a.Password),
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
                                    Password = EncryptPassword(plainPassword), // Verschlüsselt speichern
                                    Region = createData.GetProperty("region").GetString() ?? "",
                                    Id = accounts.Count > 0 ? accounts.Max(a => a.Id) + 1 : 1,
                                    Created = DateTime.Now.ToString("yyyy-MM-dd")
                                };
                                accounts.Add(newAccount);
                                SaveAccounts();

                                // Entschlüsseltes Passwort für Response
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
                                    account.Password = EncryptPassword(plainPassword); // Verschlüsselt speichern
                                    account.Region = updateData.GetProperty("region").GetString() ?? account.Region;
                                    SaveAccounts();

                                    // Entschlüsseltes Passwort für Response
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

                                // League of Legends starten und einloggen
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

                // Response zurück an JavaScript
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

                // Starte Auto-Login in separatem Thread - KEINE MessageBox hier!
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
                // Log für Debug
                System.Diagnostics.Debug.WriteLine("Auto-Login gestartet...");

                // Warte bis Riot Client Prozess gestartet ist (max 90 Sekunden)
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

                        // Versuche Felder zu finden
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
                        // Verwende Toast statt MessageBox!
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
            // Verwende gecachten Pfad
            if (!string.IsNullOrEmpty(cachedRiotClientPath) && File.Exists(cachedRiotClientPath))
            {
                return cachedRiotClientPath;
            }

            try
            {
                // Durchsuche alle Laufwerke
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
                        Password = EncryptPassword("Test123!"),
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

        private void Form1_Load(object sender, EventArgs e)
        {

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
}
