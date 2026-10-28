using ICMarkets.Application.Abstractions;
using ICMarkets.Application.Dtos;
using ICMarkets.Application.Mapping;
using ICMarkets.Domain;
using ICMarkets.Domain.Entities;
using ICMarkets.Domain.ValueObjects;
using MediatR;

namespace ICMarkets.Application.Features.Ingestion;

public sealed record IngestChainCommand(Network Network) : IRequest<BlockchainSnapshotDto>;

public sealed class IngestChainCommandHandler(
    IBlockCypherClient client,
    IBlockchainRepository repo,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    SnapshotMapper mapper) : IRequestHandler<IngestChainCommand, BlockchainSnapshotDto>
{
    public async Task<BlockchainSnapshotDto> Handle(IngestChainCommand request, CancellationToken ct)
    {
        var result = await client.GetChainInfoAsync(request.Network, ct);
        var info = result.Dto;

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
            rawJson: result.RawJson,
            createdAt: clock.GetUtcNow());

        await repo.AddAsync(snapshot, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return mapper.ToDto(snapshot);
    }
}
