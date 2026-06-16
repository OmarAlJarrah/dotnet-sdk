// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using System.Diagnostics;
using Dexpace.Sdk.Core.Configuration;
using Dexpace.Sdk.Core.Http.Request;
using Dexpace.Sdk.Core.Http.Response;

namespace Dexpace.Sdk.Core.Pipeline;

/// <summary>
/// Carries all per-call mutable state as it flows through the pipeline delegate chain.
/// </summary>
/// <remarks>
/// One instance is created per client call and passed to every policy in the chain. Policies
/// read and replace <see cref="Request"/> (for redirect/auth rewriting), stash cross-policy
/// coordination data in the property bag (see <see cref="GetProperty{T}"/> /
/// <see cref="SetProperty{T}"/>), and observe <see cref="Response"/> once the transport has
/// responded.
/// </remarks>
public sealed class PipelineContext
{
    private Dictionary<string, object?>? _properties;

    /// <summary>
    /// Initializes a new <see cref="PipelineContext"/> for a single client call.
    /// </summary>
    /// <param name="request">The initial request. Policies may replace it during the call.</param>
    /// <param name="options">A snapshot of the client options for this call.</param>
    /// <param name="cancellationToken">
    /// An optional cancellation token that can abort the call.
    /// </param>
    public PipelineContext(
        Request request,
        DexpaceClientOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        Request = request;
        Options = options;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// The current request. Policies (e.g. redirect, auth) may replace this during the call.
    /// </summary>
    public Request Request { get; set; }

    /// <summary>
    /// The response from the transport, or <see langword="null"/> before the transport responds.
    /// Set by the pipeline after the outermost transport send completes.
    /// </summary>
    public Response? Response { get; internal set; }

    /// <summary>
    /// The active SDK tracing span, or <see langword="null"/> when no <see cref="ActivitySource"/>
    /// listener is registered (near-zero overhead when unobserved).
    /// </summary>
    public Activity? Activity { get; internal set; }

    /// <summary>
    /// A snapshot of the client options that applies to this call.
    /// </summary>
    public DexpaceClientOptions Options { get; }

    /// <summary>
    /// A token that can cancel the in-flight operation. The operation policy may replace this
    /// with a timeout-linked token before forwarding the call so the overall deadline is
    /// enforced throughout the chain.
    /// </summary>
    public CancellationToken CancellationToken { get; internal set; }

    /// <summary>
    /// The zero-based retry attempt counter. <c>0</c> on the initial send;
    /// incremented by the retry policy before each subsequent attempt.
    /// </summary>
    public int AttemptNumber { get; internal set; }

    /// <summary>
    /// Retrieves a typed value from the cross-policy property bag.
    /// </summary>
    /// <typeparam name="T">The expected type of the stored value.</typeparam>
    /// <param name="key">The key used when the value was stored.</param>
    /// <returns>
    /// The stored value cast to <typeparamref name="T"/>, or <see langword="default"/>
    /// if the key is absent or the stored value cannot be cast.
    /// </returns>
    public T? GetProperty<T>(string key)
    {
        if (_properties is null || !_properties.TryGetValue(key, out var value))
        {
            return default;
        }

        return value is T typed ? typed : default;
    }

    /// <summary>
    /// Stores a typed value in the cross-policy property bag.
    /// Overwrites any existing value for <paramref name="key"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value to store.</typeparam>
    /// <param name="key">A key that identifies the value within this call.</param>
    /// <param name="value">The value to store.</param>
    public void SetProperty<T>(string key, T value)
    {
        _properties ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        _properties[key] = value;
    }
}
