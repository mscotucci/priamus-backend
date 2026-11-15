# 0005 — JWT authentication with refresh token rotation

## Status

Accepted (2026-05)

## Context

The system serves two user populations with different session profiles:

- **Drivers** use the PWA on personal smartphones (BYOD per PRD assumptions). Sessions are long: per RNF-02, drivers should not have to re-authenticate during a working day. PRD specifies a configurable session duration of around 12 hours.
- **Back-office users** use desktop browsers and have shorter, more controlled sessions.

The PRD also requires (RNF-02) that sessions can be revoked remotely by the secretariat or admin.

Authentication options considered:

1. **Server-side sessions** with cookies and a sessions table.
2. **Stateless JWT** access tokens, no refresh tokens.
3. **JWT access tokens + opaque refresh tokens** with rotation.

## Decision

Use **JWT access tokens combined with opaque refresh tokens with rotation**:

- **Access token**: signed JWT with claims `sub` (UserId), `role`, and `driver_id` (when applicable). Short lifetime (default 1 hour, configurable).
- **Refresh token**: opaque value (cryptographically random), stored hashed in the database with `UserId`, `ExpiresAt`, and `RevokedAt?`. Long lifetime (default 12 hours, configurable per PRD RNF-02).
- **Rotation**: every successful refresh issues a new refresh token AND invalidates the old one. The client must store the new refresh token and use it on the next refresh.
- **Revocation**: the secretariat or admin can revoke an individual refresh token (setting `RevokedAt`), which forces the client to re-authenticate. Revoking all sessions for a user is out of scope for MVP (achievable by revoking each refresh token individually).
- **Modeling**: `RefreshToken` is a separate aggregate from `User`, not a child entity. This allows the back office to manage revocation without loading the full user.
- **Reuse detection**: a refresh token used twice is rejected on the second use (replay protection). Token-family invalidation (revoking sibling tokens on replay detection) is out of scope for MVP.

Token signing uses HS256 (symmetric) for MVP. Asymmetric signing (RS256) is a future consideration if we ever need to share validation with other services.

## Consequences

**Positive:**

- The backend remains stateless for routine API calls: an access token alone is sufficient, no database hit for authentication.
- Refresh rotation provides defense against token theft: a stolen refresh token is invalidated the next time the legitimate client refreshes, after which it stops working.
- Per-token revocation is precise: revoking one device does not affect others.
- Short access tokens limit damage if leaked.

**Negative:**

- Two tokens to manage on the client side (more complex storage logic than a single session cookie).
- Refresh rotation introduces a race window: if a client refreshes twice nearly simultaneously, one of the two operations will fail. The client must serialize refresh calls.
- Stolen refresh token detection is reactive: we know a theft happened only when the legitimate user's next refresh fails. Sophisticated detection (e.g. invalidating the entire token family on suspicious activity) is out of scope for MVP.

**Neutral:**

- Logout is implemented as "revoke this refresh token". The access token continues to be valid until it expires (typically within an hour); this is the standard JWT trade-off.

## Out of scope for MVP

- Self-service registration (users are created by the secretariat/admin only)
- Password reset flow
- Two-factor authentication
- Account lockout after N failed login attempts
- "Revoke all sessions for this user" as a single operation
- Email verification
- Detection of refresh token theft via token family invalidation

These are documented as gaps to be addressed in future ADRs when the corresponding features are scoped.

## Alternatives considered

**Server-side sessions with cookies** was rejected because:

- The PWA is served as a static asset and consumes the API across origins; cookies require CORS coordination and SameSite tuning.
- Mobile clients consuming JSON APIs traditionally prefer Authorization headers over cookies.
- Storing sessions in the database adds a roundtrip per request unless cached, defeating the simplicity argument.

**Stateless JWT without refresh tokens** was rejected because:

- A long-lived JWT (12 hours per PRD) cannot be revoked. If a phone is lost or compromised, there is no way to invalidate access until the JWT expires.
- The PRD explicitly requires revocation capability (RNF-02).
