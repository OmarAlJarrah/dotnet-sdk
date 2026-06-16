// Copyright (c) 2026 dexpace and Omar Aljarrah.
// Licensed under the MIT License. See LICENSE in the repository root for details.

using Dexpace.Sdk.Core.Client;

namespace Dexpace.Sdk.Core.Pipeline;

/// <summary>
/// Builds an <see cref="HttpPipeline"/> from an ordered set of <see cref="HttpPipelinePolicy"/>
/// instances and a terminal transport.
/// </summary>
/// <remarks>
/// <para>
/// Policies are kept in an internal list. <see cref="Add"/> appends to that list;
/// <c>InsertBefore&lt;T&gt;</c>, <c>InsertAfter&lt;T&gt;</c>, <c>Replace&lt;T&gt;</c>, and
/// <c>Remove&lt;T&gt;</c> operate relative to the first policy of runtime type <c>T</c>.
/// </para>
/// <para>
/// <see cref="Build"/> performs a <b>stable sort by <see cref="HttpPipelinePolicy.Stage"/></b>
/// (preserving list order within a stage) and then validates pillar-stage cardinality:
/// stages marked as pillar admit exactly one policy. A violation throws
/// <see cref="InvalidOperationException"/> with an actionable message naming the offending stage.
/// </para>
/// </remarks>
public sealed class PipelineBuilder
{
    private readonly List<HttpPipelinePolicy> _list = [];

    /// <summary>
    /// Appends <paramref name="policy"/> to the internal list. The stage-based sort happens at
    /// <see cref="Build"/> time, not here.
    /// </summary>
    /// <param name="policy">The policy to add.</param>
    /// <returns>This builder (fluent interface).</returns>
    public PipelineBuilder Add(HttpPipelinePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _list.Add(policy);
        return this;
    }

    /// <summary>
    /// Inserts <paramref name="policy"/> immediately before the first policy of runtime type
    /// <typeparamref name="T"/> in the current list.
    /// </summary>
    /// <typeparam name="T">The type to search for.</typeparam>
    /// <param name="policy">The policy to insert.</param>
    /// <returns>This builder (fluent interface).</returns>
    /// <exception cref="InvalidOperationException">
    /// No policy of type <typeparamref name="T"/> is present in the list.
    /// </exception>
    public PipelineBuilder InsertBefore<T>(HttpPipelinePolicy policy)
        where T : HttpPipelinePolicy
    {
        ArgumentNullException.ThrowIfNull(policy);
        var index = FindFirst<T>();
        _list.Insert(index, policy);
        return this;
    }

    /// <summary>
    /// Inserts <paramref name="policy"/> immediately after the first policy of runtime type
    /// <typeparamref name="T"/> in the current list.
    /// </summary>
    /// <typeparam name="T">The type to search for.</typeparam>
    /// <param name="policy">The policy to insert.</param>
    /// <returns>This builder (fluent interface).</returns>
    /// <exception cref="InvalidOperationException">
    /// No policy of type <typeparamref name="T"/> is present in the list.
    /// </exception>
    public PipelineBuilder InsertAfter<T>(HttpPipelinePolicy policy)
        where T : HttpPipelinePolicy
    {
        ArgumentNullException.ThrowIfNull(policy);
        var index = FindFirst<T>();
        _list.Insert(index + 1, policy);
        return this;
    }

    /// <summary>
    /// Replaces the first policy of runtime type <typeparamref name="T"/> with
    /// <paramref name="policy"/>.
    /// </summary>
    /// <typeparam name="T">The type to replace.</typeparam>
    /// <param name="policy">The replacement policy.</param>
    /// <returns>This builder (fluent interface).</returns>
    /// <exception cref="InvalidOperationException">
    /// No policy of type <typeparamref name="T"/> is present in the list.
    /// </exception>
    public PipelineBuilder Replace<T>(HttpPipelinePolicy policy)
        where T : HttpPipelinePolicy
    {
        ArgumentNullException.ThrowIfNull(policy);
        var index = FindFirst<T>();
        _list[index] = policy;
        return this;
    }

    /// <summary>
    /// Removes every policy of runtime type <typeparamref name="T"/> from the list.
    /// If none are present, this is a no-op.
    /// </summary>
    /// <typeparam name="T">The type to remove.</typeparam>
    /// <returns>This builder (fluent interface).</returns>
    public PipelineBuilder Remove<T>()
        where T : HttpPipelinePolicy
    {
        _list.RemoveAll(p => p is T);
        return this;
    }

    /// <summary>
    /// Stable-sorts the registered policies by <see cref="HttpPipelinePolicy.Stage"/>, validates
    /// pillar-stage cardinality, and constructs the <see cref="HttpPipeline"/> with the given
    /// <paramref name="transport"/> as the terminal.
    /// </summary>
    /// <param name="transport">
    /// The terminal transport; invoked after all policies have run.
    /// </param>
    /// <returns>A fully configured <see cref="HttpPipeline"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// A pillar stage contains more than one policy.
    /// </exception>
    public HttpPipeline Build(IAsyncHttpClient transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        // Stable sort by Stage value
        HttpPipelinePolicy[] sorted = [.. _list.OrderBy(p => (int)p.Stage)];

        // Validate pillar cardinality
        foreach (var stage in PipelineStageHelper.PillarStages)
        {
            var count = sorted.Count(p => p.Stage == stage);
            if (count > 1)
            {
                throw new InvalidOperationException(
                    $"Pipeline stage '{stage}' is a pillar stage and may contain at most one policy, " +
                    $"but {count} policies were registered for it. " +
                    $"Remove the duplicate or use a non-pillar stage.");
            }
        }

        return new HttpPipeline(sorted, transport);
    }

    // Returns the index of the first policy of type T, or throws.
    private int FindFirst<T>() where T : HttpPipelinePolicy
    {
        for (var i = 0; i < _list.Count; i++)
        {
            if (_list[i] is T)
            {
                return i;
            }
        }

        throw new InvalidOperationException(
            $"No policy of type '{typeof(T).Name}' is registered in this builder.");
    }
}
