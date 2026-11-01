namespace ICMarkets.Infrastructure.BlockCypher;

// Reads the whole body INSIDE the handler pipeline so a transport drop during the body
// read surfaces here - inside the resilience retry - instead of after GetStringAsync has
// already returned past it. No retry logic of its own: it only moves WHERE the body is read.
internal sealed class ResponseBufferingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        try
        {
            if (response.Content is not null)
                await response.Content.LoadIntoBufferAsync(ct); // throws in-pipeline on a broken/stalled body
            return response;
        }
        catch
        {
            // The response never leaves this handler, so the resilience strategy cannot dispose it.
            response.Dispose();
            throw;
        }
    }
}
