using ICMarkets.Application.Dtos;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace ICMarkets.Application.Mapping;

[Mapper]
public partial class SnapshotMapper
{
    // Mapperly auto-discovers this for the Network (enum) -> Network (string) property mapping.
    private static string MapNetwork(Network network) => NetworkKey.ToKey(network);

    [MapperIgnoreSource(nameof(BlockchainSnapshot.RawJson))]
    [MapperIgnoreSource(nameof(BlockchainSnapshot.DomainEvents))]
    public partial BlockchainSnapshotDto ToDto(BlockchainSnapshot entity);
}
