using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Mapping;
using ICMarkets.Domain;
using MediatR;

namespace ICMarkets.Application.Features.History;

/// <param name="Network">Network key, e.g. <c>btc-main</c>.</param>
/// <param name="Take">Page size (1-200). Defaults to 50.</param>
/// <param name="Cursor">Opaque keyset cursor from a previous page's <c>NextCursor</c>; null for the first page.</param>
public sealed record GetChainHistoryQuery(string Network, int Take = 50, string? Cursor = null)
    : IRequest<HistoryPage>;

public sealed class GetChainHistoryQueryHandler(IBlockchainRepository repo, SnapshotMapper mapper)
    : IRequestHandler<GetChainHistoryQuery, HistoryPage>
{
    public async Task<HistoryPage> Handle(GetChainHistoryQuery request, CancellationToken ct)
    {
        var network = NetworkKey.Parse(request.Network); // validator already guaranteed this parses

        // Cursor was validated to parse (or be absent) by the validator; TryParse again rather than
        // trust a pre-parsed value, keeping the cursor opaque at the boundary.
        HistoryCursor? after = HistoryCursor.TryParse(request.Cursor, out var cursor) ? cursor : null;

        // Repository fetches Take + 1: the extra probe row signals there's a next page.
        var rows = await repo.GetHistoryAsync(network, request.Take, after, ct);

        var hasMore = rows.Count > request.Take;
        var items = (hasMore ? rows.Take(request.Take) : rows)
            .Select(mapper.ToDto)
            .ToList();

        var nextCursor = hasMore
            ? new HistoryCursor(items[^1].CreatedAt, items[^1].Id).Encode()
            : null;

        return new HistoryPage(items, nextCursor);
    }
}
