using System.Security.Cryptography;
using System.Text;

namespace AiAgentTokenObservability.Storage;

public static class PrivacyHash
{
    private const string DefaultSalt = "ai-agent-token-observability-local";

    public static string ContentHash(byte[] content)
    {
        return ToSha256Hex(content);
    }

    public static string OpaqueIdentifier(string value)
    {
        return ToSha256Hex(Encoding.UTF8.GetBytes($"opaque|{value}"));
    }

    public static string UserIdentity(string value, string? salt = null)
    {
        return ToSha256Hex(Encoding.UTF8.GetBytes($"user|{salt ?? DefaultSalt}|{value}"));
    }

    public static string RepoPath(string value, string? salt = null)
    {
        return ToSha256Hex(Encoding.UTF8.GetBytes($"repo-path|{salt ?? DefaultSalt}|{value}"));
    }

    private static string ToSha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
