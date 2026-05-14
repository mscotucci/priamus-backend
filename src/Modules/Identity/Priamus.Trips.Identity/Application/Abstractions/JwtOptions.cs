using System.ComponentModel.DataAnnotations;

namespace Priamus.Trips.Identity.Application.Abstractions;

/// <summary>
/// Configuration options for JWT issuance. Bound from the "Jwt" configuration section.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>The <c>iss</c> claim value.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The <c>aud</c> claim value.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC signing key; minimum 32 characters (256 bits for HS256).</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Lifetime of the short-lived access JWT. Defaults to 1 hour.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Lifetime of the opaque refresh token. Defaults to 12 hours.</summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromHours(12);
}
