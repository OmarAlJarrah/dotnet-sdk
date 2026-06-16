// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

namespace Dexpace.Sdk.Core.Pipeline.Policies;

/// <summary>
/// The outermost pipeline policy. Applies the overall-operation timeout configured in
/// <see cref="Configuration.DexpaceClientOptions.OverallTimeout"/> by linking a
/// <see cref="CancellationTokenSource"/> to the caller's token before forwarding the call.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="Configuration.DexpaceClientOptions.OverallTimeout"/> is a positive
/// <see cref="TimeSpan"/>, a new <see cref="CancellationTokenSource"/> is created, the timeout
/// is armed, and <see cref="PipelineContext.CancellationToken"/> is replaced with the linked
/// token so that all policies further down the chain — including the transport — observe the
/// deadline. The CTS is disposed after the call completes (or faults/cancels).
/// </para>
/// <para>
/// <b>Cancellation is not caught.</b> If the deadline fires, <see cref="OperationCanceledException"/>
/// propagates to the caller unchanged. The policy does not distinguish between caller-initiated
/// cancellation and deadline expiry — both surface as <see cref="OperationCanceledException"/>.
/// </para>
/// <para>
/// When no timeout is configured (or the value is non-positive) the policy is transparent:
/// it simply awaits the continuation without allocating a CTS.
/// </para>
/// </remarks>
public sealed class OperationPolicy : HttpPipelinePolicy
{
    /// <inheritdoc/>
    public override PipelineStage Stage => PipelineStage.Operation;

    /// <inheritdoc/>
    public override async ValueTask ProcessAsync(PipelineContext context, PipelineRunner continuation)
    {
        ArgumentNullException.ThrowIfNull(context);

        var timeout = context.Options.OverallTimeout;

        if (timeout is { } ts && ts > TimeSpan.Zero)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            cts.CancelAfter(ts);
            context.CancellationToken = cts.Token;
            await continuation.RunAsync(context).ConfigureAwait(false);
        }
        else
        {
            await continuation.RunAsync(context).ConfigureAwait(false);
        }
    }
}
