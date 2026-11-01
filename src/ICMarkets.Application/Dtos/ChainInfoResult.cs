namespace ICMarkets.Application.Dtos;

/// <summary>The neutral chain summary plus the provider's raw payload, kept for audit.</summary>
public sealed record ChainInfoResult(ChainInfo Info, string RawJson);
