using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;
using ICMarkets.Application.Mapping;
using ICMarkets.Application.Telemetry;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Domain.ValueObjects;
using MediatR;

namespace ICMarkets.Application.Features.Ingestion;

// Split out of IngestChainCommand so the batch pipeline (IngestAllChainsCommand)
// can fetch and persist as separate producer/consumer stages without
// re-fetching or duplicating the entity-construction/mapping logic.
// FetchedAt is stamped by the producer the moment the BlockCypher response
// arrives and becomes the snapshot's CreatedAt. This handler deliberately does
// NOT read the clock: under backpressure a fetched response can wait in the
// Channel before it is persisted, and CreatedAt must reflect when the data was
// fetched, not when the row happened to be written.
public sealed record PersistSnapshotCommand(Network Network, ChainInfoResult Result, DateTimeOffset FetchedAt)
    : IRequest<BlockchainSnapshotDto>;

public sealed class PersistSnapshotCommandHandler(
    IBlockchainRepository repo,
    IUnitOfWork unitOfWork,
    SnapshotMapper mapper) : IRequestHandler<PersistSnapshotCommand, BlockchainSnapshotDto>
{
    public async Task<BlockchainSnapshotDto> Handle(PersistSnapshotCommand request, CancellationToken ct)
    {
        using var activity = IngestionTelemetry.ActivitySource.StartActivity("persist-snapshot");
        var networkKey = NetworkKey.ToKey(request.Network);
        activity?.SetTag("icmarkets.network", networkKey);

        var info = request.Result.Dto;

        FeeRates? feeRates = info.HighFeePerKb is not null || info.MediumFeePerKb is not null || info.LowFeePerKb is not null
            ? new FeeRates(info.HighFeePerKb, info.MediumFeePerKb, info.LowFeePerKb)
            : null;

        GasPrices? gasPrices = info.HighGasPrice is not null || info.MediumGasPrice is not null || info.LowGasPrice is not null
            ? new GasPrices(
                info.HighGasPrice, info.MediumGasPrice, info.LowGasPrice,
                info.BaseFee, info.HighPriorityFee, info.MediumPriorityFee, info.LowPriorityFee)
            : null;

        var snapshot = BlockchainSnapshot.Create(
            network: request.Network,
            name: info.Name,
            height: info.Height,
            hash: info.Hash,
            time: info.Time,
            latestUrl: info.LatestUrl,
            previousHash: info.PreviousHash,
            previousUrl: info.PreviousUrl,
            peerCount: info.PeerCount,
            unconfirmedCount: info.UnconfirmedCount,
            feeRates: feeRates,
            gasPrices: gasPrices,
            lastForkHeight: info.LastForkHeight,
            lastForkHash: info.LastForkHash,
            rawJson: request.Result.RawJson,
            createdAt: request.FetchedAt);

        await repo.AddAsync(snapshot, ct);
        await unitOfWork.SaveChangesAsync(ct);

        IngestionTelemetry.SnapshotsIngested.Add(1, IngestionTelemetry.NetworkTag(networkKey));

        return mapper.ToDto(snapshot);
    }
}
