namespace ICMarkets.Application.Dtos;

/// <summary>Gas price tiers, in wei. ETH only.</summary>
/// <param name="High">High-priority gas price.</param>
/// <param name="Medium">Medium-priority gas price.</param>
/// <param name="Low">Low-priority gas price.</param>
/// <param name="BaseFee">EIP-1559 base fee.</param>
/// <param name="HighPriorityFee">EIP-1559 high-priority tip.</param>
/// <param name="MediumPriorityFee">EIP-1559 medium-priority tip.</param>
/// <param name="LowPriorityFee">EIP-1559 low-priority tip.</param>
public sealed record GasPricesDto(
    long? High,
    long? Medium,
    long? Low,
    long? BaseFee,
    long? HighPriorityFee,
    long? MediumPriorityFee,
    long? LowPriorityFee);
