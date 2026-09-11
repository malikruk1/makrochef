using System.Security.Cryptography;
using System.Text;
using MakroChef.Mcp.OAuth;
using Xunit;

namespace MakroChef.Tests.OAuth;

public class PkceGeneratorTests
{
    [Fact]
    public void Generate_CodeVerifier_IsUrlSafeAndCorrectLength()
    {
        var pair = PkceGenerator.Generate();

        // RFC 7636 §4.1: 43-128 chars, unreserved charset only.
        Assert.InRange(pair.CodeVerifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", pair.CodeVerifier);
    }

    [Fact]
    public void Generate_CodeChallenge_IsSha256OfVerifier()
    {
        var pair = PkceGenerator.Generate();

        var expectedHash = SHA256.HashData(Encoding.ASCII.GetBytes(pair.CodeVerifier));
        var expectedChallenge = Convert.ToBase64String(expectedHash).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(expectedChallenge, pair.CodeChallenge);
        Assert.Equal("S256", pair.CodeChallengeMethod);
    }

    [Fact]
    public void Generate_TwoCalls_ProduceDifferentVerifiers()
    {
        var first = PkceGenerator.Generate();
        var second = PkceGenerator.Generate();

        Assert.NotEqual(first.CodeVerifier, second.CodeVerifier);
    }
}
