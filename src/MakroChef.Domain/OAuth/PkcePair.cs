namespace MakroChef.Domain.OAuth;

public record PkcePair(string CodeVerifier, string CodeChallenge, string CodeChallengeMethod = "S256");
