namespace ICMarkets.Application.Dtos;

// carries the raw response text alongside the parsed DTO, so the entity can
// store "data as provided in the JSON responses" without re-serializing (which
// would silently drop fields the DTO doesn't map, e.g. future ETH gas fields).
public sealed record ChainInfoResult(BlockCypherChainInfoDto Dto, string RawJson);
