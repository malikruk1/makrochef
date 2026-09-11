using System.Security.Cryptography;
using System.Text;

namespace MakroChef.Mcp.OAuth;

/// <summary>AES-GCM encryption for MCP tokens at rest, keyed by TOKEN_ENCRYPTION_KEY
/// (TASKS.md 3.2). The key is accepted as any UTF-8 string and hashed to a fixed 256-bit key
/// so operators aren't required to generate base64-exact key material by hand.</summary>
public class TokenEncryptor(string encryptionKey)
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes(encryptionKey));

    public EncryptedToken Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Store tag appended to ciphertext so a single byte[] column round-trips cleanly.
        var combined = new byte[ciphertext.Length + tag.Length];
        Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);

        return new EncryptedToken(combined, nonce);
    }

    public string Decrypt(EncryptedToken token)
    {
        var ciphertextLength = token.Ciphertext.Length - TagSizeBytes;
        var ciphertext = token.Ciphertext[..ciphertextLength];
        var tag = token.Ciphertext[ciphertextLength..];
        var plaintextBytes = new byte[ciphertextLength];

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Decrypt(token.Nonce, ciphertext, tag, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}

public record EncryptedToken(byte[] Ciphertext, byte[] Nonce);
