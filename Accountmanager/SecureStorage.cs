// League Account Manager
// Copyright (c) 2026 Tuc2300. All rights reserved.
// Licensed under the BSD 3-Clause License: https://github.com/Tuc2300/LeagueAccountManager/blob/main/LICENSE

using System;
using System.Security.Cryptography;
using System.Text;

namespace Accountmanager
{
    public static class SecureStorage
    {
        private static byte[] _key;
        private const string V2_PREFIX = "v2:";

        public static bool IsInitialized => _key != null;

        public static void Initialize(byte[] key)
        {
            if (key == null || key.Length != 32)
                throw new ArgumentException("Schlüssel muss 32 Bytes (256-Bit) sein.", nameof(key));
            _key = key;
        }

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            if (_key == null) throw new InvalidOperationException("SecureStorage nicht initialisiert.");

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);
            byte[] ciphertext = new byte[plainBytes.Length];
            byte[] tag = new byte[16];

            using var aes = new AesGcm(_key, 16);
            aes.Encrypt(nonce, plainBytes, ciphertext, tag);

            // Layout: nonce(12) + tag(16) + ciphertext
            byte[] result = new byte[12 + 16 + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, 12);
            Buffer.BlockCopy(tag, 0, result, 12, 16);
            Buffer.BlockCopy(ciphertext, 0, result, 28, ciphertext.Length);

            return V2_PREFIX + Convert.ToBase64String(result);
        }

        public static string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText)) return string.Empty;

            if (!encryptedText.StartsWith(V2_PREFIX))
                return DecryptLegacy(encryptedText);

            if (_key == null) throw new InvalidOperationException("SecureStorage nicht initialisiert.");

            byte[] data = Convert.FromBase64String(encryptedText.Substring(V2_PREFIX.Length));
            if (data.Length < 28) throw new Exception("Ungültiges verschlüsseltes Format.");

            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            byte[] ciphertext = new byte[data.Length - 28];

            Buffer.BlockCopy(data, 0, nonce, 0, 12);
            Buffer.BlockCopy(data, 12, tag, 0, 16);
            Buffer.BlockCopy(data, 28, ciphertext, 0, ciphertext.Length);

            byte[] plainBytes = new byte[ciphertext.Length];
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(nonce, ciphertext, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }

        // Returns true for passwords encrypted with the old Windows DPAPI method
        public static bool IsLegacyEncrypted(string encryptedText)
            => !string.IsNullOrEmpty(encryptedText) && !encryptedText.StartsWith(V2_PREFIX);

        private static string DecryptLegacy(string encryptedText)
        {
            try
            {
                byte[] encrypted = Convert.FromBase64String(encryptedText);
                byte[] data = System.Security.Cryptography.ProtectedData.Unprotect(
                    encrypted, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception ex)
            {
                throw new Exception("Fehler beim Entschlüsseln (Legacy DPAPI)", ex);
            }
        }
    }
}
