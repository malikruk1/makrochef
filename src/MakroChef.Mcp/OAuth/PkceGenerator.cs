using System.Security.Cryptography;
using MakroChef.Domain.OAuth;

namespace MakroChef.Mcp.OAuth;

/// <summary>RFC 7636 PKCE pair generation for OAuth 2.1 (TASKS.md 3.2).</summary>
public static class PkceGenerator
{
    public static PkcePair Generate()
    {
        // RFC 7636 §4.1: verifier is 43-128 chars from [A-Z a-z 0-9 - . _ ~].
        // 32 random bytes -> 43-char base64url string, comfortably in range.
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var codeVerifier = Base64UrlEncode(verifierBytes);

        var challengeBytes = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(codeVerifier));
        var codeChallenge = Base64UrlEncode(challengeBytes);

        return new PkcePair(codeVerifier, codeChallenge);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
