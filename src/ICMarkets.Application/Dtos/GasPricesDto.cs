namespace ICMarkets.Application.Dtos;

public sealed record GasPricesDto(
    long? High,
    long? Medium,
    long? Low,
    long? BaseFee,
    long? HighPriorityFee,
    long? MediumPriorityFee,
    long? LowPriorityFee);
