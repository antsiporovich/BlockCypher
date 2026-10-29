using ICMarkets.Domain;

namespace ICMarkets.Application.Dtos;

/// <summary>
/// BlockCypher answered 200 but the body is not a usable chain summary for the
/// requested network (empty <c>{}</c>, an error object, wrong network, missing
/// fee/gas family...). Treated as a fetch failure, never stored as a snapshot.
/// </summary>
public sealed class InvalidChainInfoException(Network network, IReadOnlyList<string> problems)
    : Exception($"Invalid BlockCypher response for '{NetworkKey.ToKey(network)}': {string.Join("; ", problems)}")
{
    public Network Network { get; } = network;
    public IReadOnlyList<string> Problems { get; } = problems;
}

// "HTTP 200" only proves the transport worked, not that the body is a chain
// summary: a `{}` or `{"error":"..."}` deserializes cleanly into a DTO full of
// defaults (Name = "", Height = 0, Time = 0001-01-01) that would otherwise be
// persisted as a successful snapshot. This runs before any domain snapshot is
// built, so an invalid response is reported in `Failed`, not `Succeeded`.
//
// Explicit guards rather than a FluentValidation validator: the check needs the
// requested network and the clock as inputs, and the result is an exception on
// the fetch path, not a MediatR request-validation failure.
public static class ChainInfoValidation
{
    // Bitcoin's genesis block (2009) - nothing BlockCypher serves is older.
    private static readonly DateTimeOffset EarliestPlausibleTime = new(2009, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // tolerance for clock skew between BlockCypher and this host.
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromDays(1);

    public static void EnsureValid(Network network, BlockCypherChainInfoDto dto, DateTimeOffset now)
    {
        var problems = Validate(network, dto, now);
        if (problems.Count > 0)
            throw new InvalidChainInfoException(network, problems);
    }

    public static IReadOnlyList<string> Validate(Network network, BlockCypherChainInfoDto dto, DateTimeOffset now)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(dto.Name))
            problems.Add("'name' is missing or empty");
        else if (!string.Equals(dto.Name, ExpectedName(network), StringComparison.OrdinalIgnoreCase))
            problems.Add($"'name' is '{dto.Name}' but '{ExpectedName(network)}' was expected");

        if (string.IsNullOrWhiteSpace(dto.Hash))
            problems.Add("'hash' is missing or empty");

        if (dto.Height <= 0)
            problems.Add("'height' is missing or not positive");

        if (dto.Time < EarliestPlausibleTime)
            problems.Add("'time' is missing or implausibly old");
        else if (dto.Time > now + FutureTolerance)
            problems.Add("'time' is in the future");

        // exactly one fee family per network: gas prices for ETH, fee-per-kb
        // for the Bitcoin-like chains (fee XOR gas).
        var hasFee = dto.HighFeePerKb is not null && dto.MediumFeePerKb is not null && dto.LowFeePerKb is not null;
        var hasGas = dto.HighGasPrice is not null && dto.MediumGasPrice is not null && dto.LowGasPrice is not null;
        var anyFee = dto.HighFeePerKb is not null || dto.MediumFeePerKb is not null || dto.LowFeePerKb is not null;
        var anyGas = dto.HighGasPrice is not null || dto.MediumGasPrice is not null || dto.LowGasPrice is not null;

        if (network == Network.EthMain)
        {
            if (!hasGas)
                problems.Add("gas prices (high/medium/low_gas_price) are required for an Ethereum network");
            if (anyFee)
                problems.Add("fee-per-kb fields are not expected for an Ethereum network");
        }
        else
        {
            if (!hasFee)
                problems.Add("fee rates (high/medium/low_fee_per_kb) are required for this network");
            if (anyGas)
                problems.Add("gas price fields are not expected for this network");
        }

        return problems;
    }

    // BlockCypher names chains "BTC.main", "ETH.main", "BTC.test3" ... - the
    // network key ("btc-main", "btc-test3") as COIN.chain. Comparison is
    // case-insensitive; this casing is only used in the error message.
    private static string ExpectedName(Network network)
    {
        var parts = NetworkKey.ToKey(network).Split('-', 2);
        return $"{parts[0].ToUpperInvariant()}.{parts[1]}";
    }
}
