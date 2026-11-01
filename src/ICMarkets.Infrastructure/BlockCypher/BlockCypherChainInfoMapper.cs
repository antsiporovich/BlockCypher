using ICMarkets.Application.Dtos;
using ICMarkets.Domain.ValueObjects;

namespace ICMarkets.Infrastructure.BlockCypher;

// BlockCypher wire contract -> provider-neutral ChainInfo. Hand-written rather than
// generated: the fee/gas value objects are only built when the provider actually sent
// that family (an all-null FeeRates/GasPrices must never be fabricated).
public static class BlockCypherChainInfoMapper
{
    public static ChainInfo ToChainInfo(BlockCypherChainInfoDto dto)
    {
        FeeRates? feeRates = dto.HighFeePerKb is not null || dto.MediumFeePerKb is not null || dto.LowFeePerKb is not null
            ? new FeeRates(dto.HighFeePerKb, dto.MediumFeePerKb, dto.LowFeePerKb)
            : null;

        GasPrices? gasPrices = dto.HighGasPrice is not null || dto.MediumGasPrice is not null || dto.LowGasPrice is not null
            ? new GasPrices(
                dto.HighGasPrice, dto.MediumGasPrice, dto.LowGasPrice,
                dto.BaseFee, dto.HighPriorityFee, dto.MediumPriorityFee, dto.LowPriorityFee)
            : null;

        return new ChainInfo(
            Name: dto.Name,
            Height: dto.Height,
            Hash: dto.Hash,
            Time: dto.Time,
            LatestUrl: dto.LatestUrl,
            PreviousHash: dto.PreviousHash,
            PreviousUrl: dto.PreviousUrl,
            PeerCount: dto.PeerCount,
            UnconfirmedCount: dto.UnconfirmedCount,
            FeeRates: feeRates,
            GasPrices: gasPrices,
            LastForkHeight: dto.LastForkHeight,
            LastForkHash: dto.LastForkHash);
    }
}
