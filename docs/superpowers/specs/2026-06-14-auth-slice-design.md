# Auth — slice design

- **Date:** 2026-06-14
- **Status:** Approved; ready for implementation planning.
- **Part of:** [.NET SDK Platform Architecture & Build Plan](2026-06-14-dotnet-sdk-platform-design.md) — slice 6.
- **Related:** OmarAlJarrah/dotnet-sdk#2 (deferred RFC 7616 Digest handler).

## 1. Purpose & scope

Credential types, token caching and refresh, the auth policies, and RFC 7235 challenge parsing.

**In scope:** `ApiKeyCredential`, `BasicCredential`, an abstract `TokenCredential` + `AccessToken`,
an in-memory token cache with proactive refresh and single-flight, the three auth policies, and
challenge parsing + a Basic handler.

**Out of scope:** the Digest (RFC 7616) handler (deferred, #2); the `Azure.Identity` bridge package
(follow-up).

## 2. Decisions

- **Vendor-neutral `TokenCredential` in `Core`** — no Azure dependency. An optional
  `Dexpace.Sdk.Auth.AzureIdentity` adapter package (follow-up) bridges Azure.Identity credentials.
- **Token cache with proactive refresh** keyed by request context; concurrent refreshes serialized
  with `SemaphoreSlim` (no stampede).
- **Auth policies sit at the `Auth` stage** (inside Retry, so a refreshed token is used on retry) and
  **withhold credentials on cross-origin redirects**.
- **Challenge parsing + Basic handler in v1; Digest deferred** (#2).

## 3. Credentials (sketch)

```csharp
namespace Dexpace.Sdk.Core.Auth;

public readonly struct AccessToken
{
    public string Token { get; }
    public DateTimeOffset ExpiresOn { get; }
    public DateTimeOffset? RefreshOn { get; }     // proactive-refresh hint
}

public readonly struct TokenRequestContext        // structurally close to Azure.Core's, to ease the bridge
{
    public IReadOnlyList<string> Scopes { get; }
    public string? Claims { get; }
}

public abstract class TokenCredential
{
    public abstract ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct = default);
    public virtual AccessToken GetToken(TokenRequestContext context, CancellationToken ct = default);   // sync bridge
}

public sealed class ApiKeyCredential               // header default Authorization; optional scheme prefix
{
    public ApiKeyCredential(string key, HttpHeaderName? header = null, string? scheme = null);
}

public sealed class BasicCredential
{
    public BasicCredential(string username, string password);
}
```

## 4. Token cache & refresh

```csharp
public sealed class AccessTokenCache               // wraps a TokenCredential
{
    public ValueTask<AccessToken> GetAsync(TokenRequestContext context, CancellationToken ct = default);
}
```

Serves the cached token until `ExpiresOn`; once past `RefreshOn` it refreshes, serializing concurrent
refreshes through a `SemaphoreSlim` so only one network call fires. A refresh failure while a valid
token remains is swallowed (keep serving); a failure with no valid token surfaces.

## 5. Auth policies (`Auth` stage)

- **`ApiKeyAuthPolicy`** — stamps the configured header (default `Authorization`, optional scheme
  prefix).
- **`BasicAuthPolicy`** — `Authorization: Basic base64(user:password)`.
- **`BearerTokenAuthPolicy`** — resolves a token via `AccessTokenCache`, stamps
  `Authorization: Bearer <token>`; on a `401` carrying a challenge, re-acquires once.
- All three **withhold credentials on cross-origin redirect hops**, coordinated with `RedirectPolicy`'s
  header stripping.

## 6. Challenge handling (RFC 7235)

- `AuthenticationChallenge.Parse` reads `WWW-Authenticate` / `Proxy-Authenticate` (scheme + params +
  token68).
- `ChallengeHandler` (abstract) with `BasicChallengeHandler` (RFC 7617); `CompositeChallengeHandler`
  selects a handler. Digest joins later (#2).

## 7. Project & repo changes

- `Core`: add `Auth/` (`AccessToken`, `TokenRequestContext`, `TokenCredential`, `ApiKeyCredential`,
  `BasicCredential`, `AccessTokenCache`, `AuthenticationChallenge`, `ChallengeHandler`,
  `BasicChallengeHandler`, `CompositeChallengeHandler`) and `Pipeline/Policies/`
  (`ApiKeyAuthPolicy`, `BasicAuthPolicy`, `BearerTokenAuthPolicy`).
- BCL crypto only (`System.Security.Cryptography` for base64/HMAC); no new package dependency.
- Follow-up package: `Dexpace.Sdk.Auth.AzureIdentity`.

## 8. Open items (resolve during planning)

- Cross-origin withholding mechanism: a context flag set by `RedirectPolicy` vs. relying on its
  header stripping.
- Depth of the `401`-challenge re-acquire flow in v1 (single re-acquire vs. fuller negotiation).
- Whether `ApiKeyCredential` supports thread-safe key rotation in v1.
