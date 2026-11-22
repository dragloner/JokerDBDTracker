using System.Net.Http;
using System.Net.Http.Headers;

namespace JokerDBDTracker.Services
{
    public sealed class NetworkTimeService
    {
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(1.2)
        };

        private static readonly string[] Endpoints =
        [
            "https://www.google.com/generate_204",
            "https://www.cloudflare.com/cdn-cgi/trace",
            "https://www.microsoft.com"
        ];

        static NetworkTimeService()
        {
            HttpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("JokerDBDTracker", typeof(NetworkTimeService).Assembly.GetName().Version?.ToString() ?? "1.0"));
        }

        public async Task<DateTime?> GetUtcNowAsync(CancellationToken cancellationToken = default)
        {
            foreach (var endpoint in Endpoints)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Head, endpoint);
                    using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (!response.IsSuccessStatusCode || !response.Headers.Date.HasValue)
                    {
                        continue;
                    }

                    return response.Headers.Date.Value.UtcDateTime;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return null; }
                catch
                {
                    // Try next endpoint.
                }
            }

            return null;
        }
    }
}
