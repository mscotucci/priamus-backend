namespace Priamus.Trips.Identity.Features.Login;

/// <summary>
/// Response payload for a successful login.
/// </summary>
/// <param name="AccessToken">Short-lived JWT access token.</param>
/// <param name="RefreshToken">Opaque long-lived refresh token (base64url, 43 chars).</param>
/// <param name="AccessTokenExpiresAt">UTC expiry of the access token.</param>
/// <param name="RefreshTokenExpiresAt">UTC expiry of the refresh token.</param>
public sealed record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt);
