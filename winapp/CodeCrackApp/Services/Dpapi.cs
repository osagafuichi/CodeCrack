using System;
using System.Security.Cryptography;
using System.Text;

namespace CodeCrackApp.Services;

/// Encrypt/decrypt the Claude API key with the current-user DPAPI scope. Stored as base64 so it
/// travels safely inside the JSON settings file — never plaintext.
public static class Dpapi
{
    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return "";
        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedBase64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return ""; } // unreadable (different user/machine) -> treat as unset
    }
}
