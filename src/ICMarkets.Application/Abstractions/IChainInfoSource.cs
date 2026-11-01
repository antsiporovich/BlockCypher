using ICMarkets.Application.Dtos;
using ICMarkets.Domain;

namespace ICMarkets.Application.Abstractions;

// Port: "where chain summaries come from". Application knows nothing about the provider
// behind it (BlockCypher today); the adapter owns the wire contract, its validation and the
// mapping into the neutral ChainInfo.
public interface IChainInfoSource
{
    Task<ChainInfoResult> GetChainInfoAsync(Network network, CancellationToken ct);
}
