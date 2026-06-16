// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Internal;

namespace Dexpace.Sdk.Core.Configuration;

/// <summary>
/// Top-level configuration options for the Dexpace SDK client.
/// </summary>
/// <remarks>
/// All properties carry sensible defaults; the client is fully usable with <c>new DexpaceClientOptions()</c>.
/// Per-policy sub-options are exposed as nested objects (<see cref="Retry"/>, <see cref="Redirect"/>).
/// </remarks>
public sealed class DexpaceClientOptions
{
    private static readonly string s_defaultUserAgent = BuildDefaultUserAgent();

    /// <summary>
    /// The base address prepended to relative request URLs, or <see langword="null"/> when
    /// requests always use absolute URLs.
    /// </summary>
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// The <c>User-Agent</c> header value sent with every request.
    /// Defaults to <c>dexpace-dotnet/&lt;assembly-version&gt;</c>.
    /// </summary>
    public string UserAgent { get; set; } = s_defaultUserAgent;

    /// <summary>
    /// The wall-clock deadline for an entire operation (all redirect hops and retry attempts
    /// combined), or <see langword="null"/> for no overall deadline.
    /// </summary>
    public TimeSpan? OverallTimeout { get; set; }

    /// <summary>
    /// The deadline for a single send attempt, or <see langword="null"/> for no per-attempt deadline.
    /// </summary>
    public TimeSpan? AttemptTimeout { get; set; }

    /// <summary>
    /// Retry-policy options. Defaults to <see cref="RetryOptions"/> with its built-in defaults.
    /// </summary>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>
    /// Redirect-policy options. Defaults to <see cref="RedirectOptions"/> with its built-in defaults.
    /// </summary>
    public RedirectOptions Redirect { get; set; } = new();

    private static string BuildDefaultUserAgent() =>
        $"dexpace-dotnet/{SdkVersion.Value}";
}

/// <summary>
/// Options for the retry policy.
/// </summary>
public sealed class RetryOptions
{
    /// <summary>
    /// The number of retry attempts after the initial send. Defaults to <c>3</c>.
    /// Matches the Polly v8 / <c>Microsoft.Extensions.Http.Resilience</c> naming convention.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// The base delay for exponential back-off. Defaults to <c>200 ms</c>.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// The maximum back-off delay cap. Defaults to <c>30 s</c>.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When <see langword="true"/>, the retry policy respects a <c>Retry-After</c> response
    /// header. Defaults to <see langword="true"/>.
    /// </summary>
    public bool HonorRetryAfter { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, the retry policy may retry non-idempotent methods (e.g.
    /// <c>POST</c>) if the request body is replayable. Defaults to <see langword="false"/>.
    /// </summary>
    public bool RetryNonIdempotentWhenReplayable { get; set; }
}

/// <summary>
/// Options for the redirect-following policy.
/// </summary>
public sealed class RedirectOptions
{
    /// <summary>
    /// The maximum number of redirect hops to follow. Defaults to <c>20</c>,
    /// matching browser and <c>HttpClient</c> norms.
    /// </summary>
    public int MaxRedirects { get; set; } = 20;

    /// <summary>
    /// When <see langword="true"/>, the policy follows <c>https → http</c> downgrade redirects.
    /// Defaults to <see langword="false"/> for security.
    /// </summary>
    public bool AllowHttpsToHttpDowngrade { get; set; }

    /// <summary>
    /// When <see langword="true"/>, sensitive headers (e.g. <c>Authorization</c>) are stripped
    /// when the redirect crosses an origin boundary. Defaults to <see langword="true"/>.
    /// </summary>
    public bool StripSensitiveHeadersOnCrossOrigin { get; set; } = true;
}
