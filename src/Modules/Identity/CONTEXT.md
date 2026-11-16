# Identity

The context that knows who may use the system and proves it at every request: user accounts, their role, the sessions they hold, and the trail of what happened to them. It does not know what a driver is (Registry does, ADR 0008) and it holds no business data. Seeded from PRD §3 and RF-01, RF-02, RNF-07; refined at the grilling of 2026-11-15.

## Language

### Accounts

**User** (Utente):
An account that can sign in: a user name, a password, one role and a status. It is not a person's record: a driver's personal data live in Registry.
_Avoid_: account (in code), profile, operator

**Username** (Nome utente):
The name a user signs in with. Unique, 3 to 64 characters, always stored lowercase, so two names that differ only in case are the same name.
_Avoid_: login, e-mail (a user name is not an address)

**Role** (Ruolo):
What a user is allowed to do, one of three fixed values: `Driver` (Autista), `BackOffice` (Segreteria), `Admin`. A user has exactly one. `Admin` can do everything `BackOffice` can, plus manage users.
_Avoid_: permission, profile, group

**Status** (Stato):
Whether a user may sign in: `Active` or `Disabled`. A disabled user keeps its history and can be enabled again.
_Avoid_: deleted, locked, suspended

**Bootstrap administrator** (Primo amministratore):
The one `Admin` the system creates for itself at startup, from configuration, when no user exists yet. It is its own author. Once any user exists the configuration is ignored.
_Avoid_: default admin, root, system user

### Sessions

**Session** (Sessione):
The period during which a signed-in user can keep working without typing the password again. It lasts as long as its refresh token can be renewed.
_Avoid_: login (the act, not the period)

**Access token** (Token di accesso):
The short-lived proof sent with every request. It carries the user id, the role and, for a driver, the linked driver. It cannot be withdrawn: it simply expires.
_Avoid_: JWT (the format), bearer

**Refresh token** (Token di rinnovo):
The long-lived secret a client exchanges for a new access token. Each one is used once.
_Avoid_: session token, remember-me

**Rotation** (Rotazione):
Replacing a refresh token with a new one at every renewal. A token that was already rotated is no longer valid; presenting it again is refused.
_Avoid_: renewal (the user's word), reissue

**Revocation** (Revoca):
Ending a session on purpose, by its owner (sign-out) or by an `Admin` or `BackOffice` user. A rotated token cannot be revoked: rotation already ended it.
_Avoid_: logout (in code), invalidation, kill

### Requests

**Current user** (Utente corrente):
The user a request acts as, read from its access token. `GET /api/identity/me` answers the same user as the database knows it now, so a change of status shows at once there even though the token still says otherwise.
_Avoid_: principal, caller, logged user

**Driver link** (Collegamento autista):
The association between a `Driver` user and a driver record. It lives in Registry; Identity only asks for it when it issues a token or answers who the current user is. A driver user may have none yet.
_Avoid_: driver account, mapping

### Trail

**Audit log** (Registro di audit):
The record of what happened to users and sessions: who did it, to whom, when. Written in the same transaction as the change it describes.
_Avoid_: history, log (the application log is something else)

## Relationships

- Depends on **Registry** only through `IDriverLookup`, to resolve a driver link.
- Every other context depends on Identity for the current user (`ICurrentUser`) and the authorization policies (`AuthorizationPolicies`).
