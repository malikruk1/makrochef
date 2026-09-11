using MakroChef.Mcp.OAuth;
using Xunit;

namespace MakroChef.Tests.OAuth;

public class TokenEncryptorTests
{
    [Fact]
    public void Encrypt_ThenDecrypt_RoundTripsOriginalPlaintext()
    {
        var encryptor = new TokenEncryptor("test-encryption-key");
        const string plaintext = "mcp_at_abc123.refresh_token_xyz";

        var encrypted = encryptor.Encrypt(plaintext);
        var decrypted = encryptor.Decrypt(encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_SamePlaintextTwice_ProducesDifferentCiphertextAndNonce()
    {
        var encryptor = new TokenEncryptor("test-encryption-key");
        const string plaintext = "same-token";

        var first = encryptor.Encrypt(plaintext);
        var second = encryptor.Encrypt(plaintext);

        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(Convert.ToHexString(first.Ciphertext), Convert.ToHexString(second.Ciphertext));
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsInsteadOfReturningGarbage()
    {
        var encrypted = new TokenEncryptor("correct-key").Encrypt("secret-token");
        var wrongKeyEncryptor = new TokenEncryptor("wrong-key");

        Assert.ThrowsAny<Exception>(() => wrongKeyEncryptor.Decrypt(encrypted));
    }

    [Fact]
    public void Ciphertext_NeverContainsPlaintextSubstring()
    {
        var encryptor = new TokenEncryptor("test-encryption-key");
        const string plaintext = "mcp_token_super_secret_value";

        var encrypted = encryptor.Encrypt(plaintext);

        Assert.DoesNotContain(plaintext, Convert.ToHexString(encrypted.Ciphertext));
    }
}
