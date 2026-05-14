namespace Priamus.Trips.Identity.Features.Refresh;

/// <summary>
/// Response payload for a successful token refresh.
/// </summary>
/// <param name="AccessToken">New short-lived JWT access token.</param>
/// <param name="AccessTokenExpiresAt">UTC expiry of the new access token.</param>
/// <param name="RefreshToken">New opaque refresh token (base64url, 43 chars). The old token is now invalid.</param>
/// <param name="RefreshTokenExpiresAt">UTC expiry of the new refresh token.</param>
public sealed record RefreshResponse(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAt, DateTime RefreshTokenExpiresAt);
