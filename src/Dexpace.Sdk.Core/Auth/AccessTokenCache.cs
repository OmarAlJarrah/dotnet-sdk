// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

namespace Dexpace.Sdk.Core.Auth;

/// <summary>
/// An in-memory token cache that wraps a <see cref="TokenCredential"/> and provides
/// proactive refresh, expiry-based invalidation, and single-flight protection against
/// concurrent refresh stampedes.
/// </summary>
/// <remarks>
/// <para>
/// Tokens are cached keyed by the <see cref="TokenRequestContext.CacheKey"/>. A cached token
/// is served without calling the underlying credential as long as both:
/// <list type="bullet">
///   <item><c>now &lt; ExpiresOn</c></item>
///   <item><c>RefreshOn</c> is <see langword="null"/> OR <c>now &lt; RefreshOn</c></item>
/// </list>
/// </para>
/// <para>
/// Once either condition fails, a single caller acquires the per-key semaphore and calls
/// the credential. All concurrent callers wait on the semaphore and perform a double-checked
/// read after acquiring it. Only one network round-trip fires per key per refresh cycle.
/// </para>
/// <para>
/// <strong>Failure while valid:</strong> if the credential throws but a token remains valid
/// (<c>now &lt; ExpiresOn</c>), the cached token is returned silently. If no valid token
/// exists the exception propagates.
/// </para>
/// <para>
/// This class is thread-safe. Inject a custom <see cref="TimeProvider"/> for deterministic
/// unit testing.
/// </para>
/// </remarks>
public sealed class AccessTokenCache
{
    private readonly TokenCredential _credential;
    private readonly TimeProvider _time;

    // Per-key state: the current cached token (null = never fetched) + a semaphore for
    // single-flight. One CacheEntry per unique TokenRequestContext.CacheKey.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CacheEntry> _entries = new();

    /// <summary>
    /// Initializes an <see cref="AccessTokenCache"/> backed by the given credential.
    /// </summary>
    /// <param name="credential">The underlying credential to call when a token is needed.</param>
    /// <param name="timeProvider">
    /// A <see cref="TimeProvider"/> used to determine "now". Defaults to
    /// <see cref="TimeProvider.System"/> when <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="credential"/> is <see langword="null"/>.</exception>
    public AccessTokenCache(TokenCredential credential, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _credential = credential;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns a valid <see cref="AccessToken"/> for the given <paramref name="context"/>,
    /// fetching and caching one if necessary.
    /// </summary>
    /// <param name="context">The token request context identifying the scopes and claims.</param>
    /// <param name="ct">A token to cancel an in-progress credential call.</param>
    /// <returns>
    /// A <see cref="ValueTask{AccessToken}"/> resolving to a valid access token.
    /// </returns>
    public async ValueTask<AccessToken> GetAsync(TokenRequestContext context, CancellationToken ct = default)
    {
        var entry = _entries.GetOrAdd(context.CacheKey, static _ => new CacheEntry());
        var now = _time.GetUtcNow();

        // Fast path: read the holder once (volatile acquire) and return immediately when the
        // token is still valid and does not need a proactive refresh. The volatile read ensures
        // that any holder published by a slow-path writer is fully visible to this thread.
        var fastHolder = entry.Holder;
        if (fastHolder is not null && IsValid(fastHolder.Token, now) && !NeedsRefresh(fastHolder.Token, now))
        {
            return fastHolder.Token;
        }

        // Slow path: acquire the semaphore for this key.
        await entry.Semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring.
            now = _time.GetUtcNow();
            var recheckedHolder = entry.Holder;
            if (recheckedHolder is not null && IsValid(recheckedHolder.Token, now) && !NeedsRefresh(recheckedHolder.Token, now))
            {
                return recheckedHolder.Token;
            }

            // Try to refresh from the credential.
            try
            {
                var fresh = await _credential.GetTokenAsync(context, ct).ConfigureAwait(false);
                // Volatile write releases the fully-constructed holder to all threads.
                entry.Holder = new TokenHolder(fresh);
                return fresh;
            }
            catch
            {
                // Re-sample time: GetTokenAsync may have taken a long time. Validate the
                // cached token against the clock *after* the (slow) failing call, so a token
                // that expired during the attempt is not returned as valid.
                var nowAfterFailure = _time.GetUtcNow();
                var fallbackHolder = entry.Holder;
                if (fallbackHolder is not null && IsValid(fallbackHolder.Token, nowAfterFailure))
                {
                    return fallbackHolder.Token;
                }

                throw;
            }
        }
        finally
        {
            entry.Semaphore.Release();
        }
    }

    // A token is "valid" while the clock hasn't yet reached ExpiresOn.
    private static bool IsValid(AccessToken token, DateTimeOffset now) =>
        now < token.ExpiresOn;

    // A token "needs refresh" once RefreshOn has been reached (proactive hint).
    private static bool NeedsRefresh(AccessToken token, DateTimeOffset now) =>
        token.RefreshOn is { } refreshOn && now >= refreshOn;

    // Immutable wrapper so the token can be published through a single volatile reference,
    // giving acquire/release ordering on all platforms. AccessToken is a multi-field struct;
    // publishing it directly would allow fast-path readers to observe a torn or partially
    // written value. Boxing it inside a reference type (TokenHolder) reduces the published
    // value to a single pointer-width write, which the .NET memory model guarantees to be
    // atomic and not torn.
    private sealed class TokenHolder
    {
        /// <summary>Initializes a <see cref="TokenHolder"/> wrapping the given token.</summary>
        /// <param name="token">The token to publish atomically.</param>
        public TokenHolder(AccessToken token) => Token = token;

        /// <summary>The wrapped access token.</summary>
        public AccessToken Token { get; }
    }

    private sealed class CacheEntry
    {
        // volatile ensures that a fast-path reader always observes the most recently published
        // holder (acquire semantics on read, release semantics on write). Combined with the
        // pointer-width atomicity of reference reads/writes, this is safe without a lock on
        // the fast path.
        private volatile TokenHolder? _holder;

        /// <summary>
        /// The most recently published token holder, or <see langword="null"/> if no token
        /// has been fetched yet. Reads and writes are reference-atomic and carry
        /// acquire/release ordering via the <see langword="volatile"/> modifier.
        /// </summary>
        public TokenHolder? Holder
        {
            get => _holder;
            set => _holder = value;
        }

        /// <summary>
        /// A semaphore (initial count = 1) that serializes refresh calls for this key.
        /// </summary>
        public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1, 1);
    }
}
