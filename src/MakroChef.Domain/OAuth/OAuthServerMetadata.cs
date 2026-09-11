using System.Text.Json.Serialization;

namespace MakroChef.Domain.OAuth;

/// <summary>Parsed response of GET /.well-known/oauth-authorization-server (RFC 8414).</summary>
public record OAuthServerMetadata(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: JsonPropertyName("registration_endpoint")] string? RegistrationEndpoint,
    [property: JsonPropertyName("code_challenge_methods_supported")] IReadOnlyList<string>? CodeChallengeMethodsSupported);
