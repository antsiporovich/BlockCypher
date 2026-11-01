using System.Buffers.Text;
using System.Text;

namespace ICMarkets.Application.Features.History;

/// <summary>
/// An opaque keyset-pagination cursor: the <c>(CreatedAt, Id)</c> of the last row of the previous
/// page. The next page selects rows strictly after this in <c>(CreatedAt DESC, Id DESC)</c> order,
/// so an insert between page requests can neither shift nor duplicate a page (unlike OFFSET).
/// Clients treat it as opaque - they pass back exactly what they received and never construct it.
/// </summary>
public readonly record struct HistoryCursor(DateTimeOffset CreatedAt, int Id)
{
    // Encoded as base64url of "{CreatedAt UtcTicks}:{Id}": compact, URL-safe, and
    // UtcTicks matches the SQLite BIGINT storage so the keyset comparison translates on both providers.
    public string Encode()
    {
        var bytes = Encoding.UTF8.GetBytes($"{CreatedAt.UtcTicks}:{Id}");
        return Base64Url.EncodeToString(bytes);
    }

    public static bool TryParse(string? token, out HistoryCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            return false;
        }

        var text = Encoding.UTF8.GetString(bytes);
        var separator = text.IndexOf(':');
        if (separator <= 0 || separator == text.Length - 1)
            return false;

        if (!long.TryParse(text.AsSpan(0, separator), out var ticks)
            || !int.TryParse(text.AsSpan(separator + 1), out var id))
            return false;

        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
            return false;

        cursor = new HistoryCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}
