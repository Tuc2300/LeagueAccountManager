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
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(plainText);
                byte[] encrypted = ProtectedData.Protect(
                    data,
                    null,
                    DataProtectionScope.CurrentUser
                );
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                throw new Exception("Fehler beim Verschlüsseln", ex);
            }
        }

        public static string Decrypt(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return string.Empty;

            try
            {
                byte[] encrypted = Convert.FromBase64String(encryptedText);
                byte[] data = ProtectedData.Unprotect(
                    encrypted,
                    null,
                    DataProtectionScope.CurrentUser
                );
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception ex)
            {
                throw new Exception("Fehler beim Entschlüsseln", ex);
            }
        }

        public static byte[] Encrypt(byte[] plainData)
        {
            if (plainData == null || plainData.Length == 0)
                return Array.Empty<byte>();

            try
            {
                return ProtectedData.Protect(
                    plainData,
                    null,
                    DataProtectionScope.CurrentUser
                );
            }
            catch (Exception ex)
            {
                throw new Exception("Fehler beim Verschlüsseln", ex);
            }
        }

        public static byte[] Decrypt(byte[] encryptedData)
        {
            if (encryptedData == null || encryptedData.Length == 0)
                return Array.Empty<byte>();

            try
            {
                return ProtectedData.Unprotect(
                    encryptedData,
                    null,
                    DataProtectionScope.CurrentUser
                );
            }
            catch (Exception ex)
            {
                throw new Exception("Fehler beim Entschlüsseln", ex);
            }
        }
    }
}
