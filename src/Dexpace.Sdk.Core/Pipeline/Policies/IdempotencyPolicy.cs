// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Http.Common;

namespace Dexpace.Sdk.Core.Pipeline.Policies;

/// <summary>
/// A per-call pipeline policy that attaches an <c>Idempotency-Key</c> header to outgoing
/// requests for configured HTTP methods, enabling safe retries on transient failures.
/// </summary>
/// <remarks>
/// <para>
/// By default only <c>POST</c> requests receive the header. The key is a GUID v4 generated
/// once per logical call and stashed in the <see cref="PipelineContext"/> property bag under
/// the key <c>"dexpace.idempotency-key"</c>. Redirect hops and retry attempts that re-enter
/// the policy on the same context reuse the same GUID, satisfying the idempotency contract.
/// </para>
/// <para>
/// If the request already carries an <c>Idempotency-Key</c> header (set by the caller or a
/// previous pass), the policy does not overwrite it.
/// </para>
/// </remarks>
public sealed class IdempotencyPolicy : HttpPipelinePolicy
{
    /// <summary>Context property-bag key under which the generated idempotency key is stored.</summary>
    internal const string PropertyKey = "dexpace.idempotency-key";

    private readonly HashSet<Method> _methods;

    /// <summary>
    /// Initializes a new <see cref="IdempotencyPolicy"/>.
    /// </summary>
    /// <param name="methods">
    /// The HTTP methods that should receive an idempotency key. Defaults to
    /// <c>POST</c> when <see langword="null"/>.
    /// </param>
    public IdempotencyPolicy(IEnumerable<Method>? methods = null)
    {
        _methods = methods is not null
            ? new HashSet<Method>(methods)
            : [Method.Post];
    }

    /// <inheritdoc/>
    public override PipelineStage Stage => PipelineStage.PerCall;

    /// <inheritdoc/>
    public override async ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_methods.Contains(context.Request.Method)
            && !context.Request.Headers.Contains(HttpHeaderName.Of("Idempotency-Key").Original))
        {
            // Reuse a key that was already generated for this context (e.g. retry re-entering here),
            // or generate a fresh one and stash it.
            var key = context.GetProperty<string>(PropertyKey);
            if (key is null)
            {
                key = Guid.NewGuid().ToString();
                context.SetProperty(PropertyKey, key);
            }

            context.Request = context.Request with
            {
                Headers = context.Request.Headers.Set("Idempotency-Key", key)
            };
        }

        await continuation.RunAsync(context).ConfigureAwait(false);
    }
}
