using ICMarkets.Application.Dtos;

namespace ICMarkets.Application.Features.History;

/// <summary>
/// One page of chain history, newest first, with an opaque cursor to fetch the next page.
/// </summary>
/// <param name="Items">The snapshots on this page, ordered by <c>CreatedAt</c> descending.</param>
/// <param name="NextCursor">
/// Pass this back as <c>cursor</c> to fetch the next page; <c>null</c> when there are no more rows.
/// </param>
public sealed record HistoryPage(IReadOnlyList<BlockchainSnapshotDto> Items, string? NextCursor);
