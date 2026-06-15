# Webhooks — slice design

- **Date:** 2026-06-14
- **Status:** Approved; ready for implementation planning.
- **Part of:** [.NET SDK Platform Architecture & Build Plan](2026-06-14-dotnet-sdk-platform-design.md) — slice 9.

## 1. Purpose & scope

Verify inbound webhook signatures and optionally deserialize the verified payload.

**In scope:** an `IWebhookVerifier` seam, a `StandardWebhooksVerifier` (HMAC-SHA256), and a typed
`Unwrap<T>` convenience.

**Out of scope:** provider-specific schemes (Stripe/GitHub-style) — the seam exists so they can be
added later without breaking changes.

## 2. Decisions

- **Standard Webhooks (HMAC-SHA256) behind an `IWebhookVerifier` interface**, so additional schemes
  slot in later.
- **`TimeProvider` for the clock** (BCL, testable) — no bespoke clock type.
- **Constant-time comparison** via `CryptographicOperations.FixedTimeEquals`.
- **Key rotation supported** — multiple signature tokens in the header are each tried.
- **New error type:** `WebhookVerificationException : SdkException`.

## 3. Surface (sketch)

```csharp
namespace Dexpace.Sdk.Core.Webhooks;

public interface IWebhookVerifier
{
    void Verify(Headers headers, ReadOnlySpan<byte> body);
}

public sealed class StandardWebhooksVerifier : IWebhookVerifier
{
    public StandardWebhooksVerifier(string secret, TimeSpan? tolerance = null, TimeProvider? timeProvider = null);

    public void Verify(Headers headers, ReadOnlySpan<byte> body);                 // throws WebhookVerificationException
    public T Unwrap<T>(Headers headers, ReadOnlySpan<byte> body, ISerde serde);   // verify then deserialize
}
```

## 4. Verification algorithm (Standard Webhooks)

- Secret format `whsec_<base64>` (prefix optional); decode to key bytes.
- Signed content = `"{id}.{timestamp}.{body}"` from the `webhook-id`, `webhook-timestamp`, and
  `webhook-signature` headers.
- Compute HMAC-SHA256, base64-encode, and compare (constant-time) against each space-separated
  `v1,<sig>` token in `webhook-signature`.
- Reject when the timestamp is outside the tolerance window (default ±5 minutes) — guards replay.
- Any failure throws `WebhookVerificationException`; `Unwrap<T>` additionally surfaces
  `DeserializationException` from the serde.

## 5. Project & repo changes

- `Core`: add `Webhooks/` (`IWebhookVerifier`, `StandardWebhooksVerifier`) and
  `Errors/WebhookVerificationException.cs`.
- BCL crypto only (`HMACSHA256`, `CryptographicOperations`); no new dependency.

## 6. Open items (resolve during planning)

- Whether to accept the body as `ReadOnlySpan<byte>` only or also `string` (encoding pitfalls argue
  for bytes as canonical).
- Header-name constants for the `webhook-*` set.
- Tolerance default and whether it's required.
