// League Account Manager
// Copyright (c) 2026 Tuc2300. All rights reserved.
// Licensed under the BSD 3-Clause License: https://github.com/Tuc2300/LeagueAccountManager/blob/main/LICENSE

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Accountmanager
{
    public static class KeyManager
    {
        public static byte[] GenerateKey()
        {
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            return key;
        }

        public static void SaveKeyToFile(byte[] key, string path)
        {
            File.WriteAllText(path, Convert.ToBase64String(key), new UTF8Encoding(false));
        }

        public static byte[] LoadKeyFromFile(string path)
        {
            string base64 = File.ReadAllText(path).Trim();
            byte[] key = Convert.FromBase64String(base64);
            if (key.Length != 32)
                throw new Exception("Ungültige Schlüsseldatei: Schlüssel muss 256-Bit (32 Bytes) sein.");
            return key;
        }
    }
}
