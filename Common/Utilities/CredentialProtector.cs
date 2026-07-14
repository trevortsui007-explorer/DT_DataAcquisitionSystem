using System;
using System.Security.Cryptography;
using System.Text;

namespace DT_DataAcquisitionSystem.Common.Utilities
{
    public static class CredentialProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DT_DataAcquisitionSystem.FileAccess");

        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;

            byte[] data = Encoding.UTF8.GetBytes(plainText);
            byte[] protectedData = ProtectedData.Protect(data, Entropy, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(protectedData);
        }

        public static string Unprotect(string protectedText)
        {
            if (string.IsNullOrWhiteSpace(protectedText)) return string.Empty;

            byte[] data = Convert.FromBase64String(protectedText);
            byte[] plainData = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plainData);
        }
    }
}
