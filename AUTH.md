# RubacCore — Authentication Guide

Two authentication paths coexist under the same OpenIddict token issuer.  
The client (Angular app, API calls) always receives a standard JWT — it never needs to know which path was used.

```
External users (local)         Enterprise users (AD/LDAP)
        │                               │
  ASP.NET Identity               LDAP bind (port 389/636)
  (password hash in DB)          (password validated by AD)
        │                               │
        └──────────► OpenIddict ◄───────┘
                     (JWT issuer)
                          │
               RulesBacAdmin / APIs
```

---

## Approach 1 — Local Identity (external users)

### How it works

1. User registers via `POST /api/users` (SuperAdmin only) or the seed worker creates the default admin.
2. Password hash is stored in the `Users` table by ASP.NET Core Identity.
3. On `POST /connect/token` with `grant_type=password`, `AuthService.ValidateCredentialsAsync` calls `SignInManager.CheckPasswordSignInAsync`.
4. On success OpenIddict issues a signed JWT (access token + refresh token).

### User model

| Column | Value |
|---|---|
| `AuthProvider` | `"local"` |
| `LdapDn` | `NULL` |
| `PasswordHash` | set by Identity |

### Configuration

No extra configuration needed — this is the default behaviour when `Ldap.Enabled = false`.

### Login example

```json
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password
&username=admin
&password=Admin1234!
&scope=openid profile email roles offline_access rubac
&client_id=rubac-admin
&client_secret=...
```

---

## Approach 2 — Active Directory / LDAP (enterprise users)

### How it works

1. User types their UPN (`john.doe@corp.local`) + Windows domain password in the Angular login form.
2. `AuthService` detects the `@corp.local` suffix and routes to `LdapService`.
3. `LdapService` opens an LDAP connection to the domain controller and performs a **simple bind** with the user's UPN + password.  
   - If the bind fails → wrong credentials → 401.
   - If the bind succeeds → fetches `cn`, `mail`, `givenName`, `sn` attributes via a search.
4. `EnsureLdapUserAsync` provisions a **shadow `ApplicationUser`** in the local DB (no password hash) on the very first login, or updates the DN if the account was moved in AD.
5. OpenIddict issues the same JWT as for local users. Roles are managed locally in the `Users` table.

### User model

| Column | Value |
|---|---|
| `AuthProvider` | `"ldap"` |
| `LdapDn` | e.g. `CN=John Doe,OU=Staff,DC=corp,DC=local` |
| `PasswordHash` | `NULL` — password is never stored |

### Configuration

Edit `appsettings.json` (or override per-environment in `appsettings.Production.json`):

```json
"Ldap": {
  "Enabled": true,
  "Host": "dc01.corp.local",
  "Port": 389,
  "UseSsl": false,
  "Domain": "corp.local",
  "SearchBase": "DC=corp,DC=local",
  "ServiceAccount": "svc-rubac@corp.local",
  "ServicePassword": "s3cr3t",
  "DefaultRole": "User"
}
```

| Key | Description |
|---|---|
| `Enabled` | `false` — LDAP is skipped entirely (safe default) |
| `Host` | Hostname or IP of your domain controller |
| `Port` | `389` plain · `636` with SSL |
| `UseSsl` | Set to `true` when using port 636 (LDAPS) |
| `Domain` | Suffix used to detect enterprise logins, e.g. `corp.local` |
| `SearchBase` | Root of the LDAP search, e.g. `DC=corp,DC=local` |
| `ServiceAccount` | UPN of a read-only service account for attribute lookups. If empty, the authenticating user's creds are reused for the search. |
| `ServicePassword` | Password of the service account |
| `DefaultRole` | Role assigned to new AD users on their first login (`User`, `Admin`, …) |

> **Production security note:** store `ServicePassword` in an environment variable or a secrets manager, never in source-controlled JSON.  
> ```
> LDAP__ServicePassword=s3cr3t  (environment variable)
> ```

### Login example

Identical request to the local path — just use the AD UPN as the username:

```json
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password
&username=john.doe@corp.local
&password=WindowsPassword1!
&scope=openid profile email roles offline_access rubac
&client_id=rubac-admin
&client_secret=...
```

### Role management

AD groups are **not** synced automatically. Roles are managed in RulesBacAdmin's Users page the same way as local users. The only difference: the `DefaultRole` is assigned at first login.

To give an AD user elevated rights, a SuperAdmin assigns them a role in the admin UI after their first login.

---

## Multi-application role scoping

`ApplicationRole` has an `Application` column. When it is set, that role is **only included in tokens issued for that specific `client_id`**. When it is `null`, the role is **global** — it appears in every app's token.

### Convention

| Role name | `Application` column | Included in tokens for |
|---|---|---|
| `SuperAdmin` | `null` | every app |
| `User` | `null` | every app (safe default) |
| `Admin` | `"rubac-admin"` | rubac-admin only |
| `Editor` | `"dashboard-spa"` | dashboard-spa only |

### How it works

`AuthController` calls `GetRolesForClientAsync(userId, request.ClientId)` for **both** the password grant and the refresh token grant. The query joins `UserRoles → Roles` and filters:

```sql
WHERE Application IS NULL OR Application = @clientId
```

This runs on every token issuance — including refresh — so permission changes made by a SuperAdmin take effect at the next token refresh without requiring a new login.

### First-login default role for AD users

`EnsureLdapUserAsync` resolves the `DefaultRole` from `appsettings.json` against the database:

1. First looks for a role with that name **scoped to the requesting `clientId`**.
2. Falls back to a role with that name with `Application = null` (global).

So if you set `"DefaultRole": "User"` and have both a global `User` role and an `"rubac-admin"`-scoped `User` role, AD users logging in through `rubac-admin` get the scoped version.

### Example — john@corp.local with two apps

```
Assigned roles after SuperAdmin configures them:
  "User"   (Application = null)           → all apps
  "Admin"  (Application = "rubac-admin")  → rubac-admin only

Token issued for rubac-admin  (client_id = "rubac-admin"):
  role = ["User", "Admin"]

Token issued for dashboard-spa (client_id = "dashboard-spa"):
  role = ["User"]             ← "Admin" is excluded
```

---

## Detection logic

```
username contains "@corp.local"  AND  Ldap.Enabled = true
        │                                      │
       YES                                    YES
        └──────────────► LDAP path ◄───────────┘

otherwise ──────────────► Local Identity path
```

`AuthService.IsLdapUser()` performs a case-insensitive `EndsWith` on the configured domain.  
Multiple domains can be supported by extending `LdapSettings` to an array and iterating.

---

## Adding a second LDAP domain

If you have two domains (e.g. `corp.local` and `partner.org`), extend `LdapSettings` and `LdapService` to hold a list:

```json
"LdapDomains": [
  { "Domain": "corp.local",   "Host": "dc01.corp.local",    ... },
  { "Domain": "partner.org",  "Host": "dc01.partner.org",   ... }
]
```

Then loop over the list in `IsLdapUser` / `AuthenticateAsync` to find the matching config.

---

## Relevant files

| File | Role |
|---|---|
| `Models/LdapSettings.cs` | Configuration POCO |
| `Interfaces/ILdapService.cs` | Interface + `LdapUserInfo` record |
| `Services/LdapService.cs` | LDAP bind, attribute fetch, injection prevention |
| `Services/AuthService.cs` | Routes to LDAP or Identity; provisions shadow users |
| `Models/ApplicationUser.cs` | `LdapDn` + `AuthProvider` columns |
| `Program.cs` | `AddScoped<ILdapService>` + `Configure<LdapSettings>` |
| `appsettings.json` | `"Ldap"` section (disabled by default) |
