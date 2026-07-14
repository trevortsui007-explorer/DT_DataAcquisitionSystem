using System;
using Newtonsoft.Json.Linq;

namespace DT_DataAcquisitionSystem.Common.Utilities
{
    public class FileAccessOptions
    {
        public string Domain { get; set; }
        public string UserName { get; set; }
        public string PasswordProtected { get; set; }
        public string PasswordPlain { get; set; }
        public bool PasswordSet { get; set; }
        public bool ClearPassword { get; set; }

        public bool HasUserName => !string.IsNullOrWhiteSpace(UserName);
        public bool HasPassword => !string.IsNullOrWhiteSpace(PasswordPlain) || !string.IsNullOrWhiteSpace(PasswordProtected);

        public string EffectiveUserName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(UserName)) return string.Empty;
                if (string.IsNullOrWhiteSpace(Domain)) return UserName.Trim();
                if (UserName.Contains("\\") || UserName.Contains("@")) return UserName.Trim();
                return Domain.Trim() + "\\" + UserName.Trim();
            }
        }

        public string GetPassword()
        {
            if (!string.IsNullOrEmpty(PasswordPlain)) return PasswordPlain;
            return CredentialProtector.Unprotect(PasswordProtected);
        }

        public static FileAccessOptions FromParserOptions(string parserOptions)
        {
            var result = new FileAccessOptions();
            if (string.IsNullOrWhiteSpace(parserOptions)) return result;

            try
            {
                var root = JObject.Parse(parserOptions);
                var fileAccess = GetObjectIgnoreCase(root, "fileAccess");
                if (fileAccess == null) return result;

                result.Domain = GetStringIgnoreCase(fileAccess, "domain");
                result.UserName = GetStringIgnoreCase(fileAccess, "userName");
                result.PasswordProtected = GetStringIgnoreCase(fileAccess, "passwordProtected");
                result.PasswordPlain = GetStringIgnoreCase(fileAccess, "passwordPlain");
                result.PasswordSet = GetBoolIgnoreCase(fileAccess, "passwordSet");
                result.ClearPassword = GetBoolIgnoreCase(fileAccess, "clearPassword");
            }
            catch
            {
                return result;
            }

            return result;
        }

        private static JObject GetObjectIgnoreCase(JObject obj, string name)
        {
            if (obj == null) return null;
            foreach (var property in obj.Properties())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value as JObject;
                }
            }

            return null;
        }

        private static string GetStringIgnoreCase(JObject obj, string name)
        {
            if (obj == null) return string.Empty;
            foreach (var property in obj.Properties())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return Convert.ToString(property.Value);
                }
            }

            return string.Empty;
        }

        private static bool GetBoolIgnoreCase(JObject obj, string name)
        {
            string value = GetStringIgnoreCase(obj, name);
            return bool.TryParse(value, out bool result) && result;
        }
    }
}
