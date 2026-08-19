using System.Net.Http.Json;
using System.Text.Json;
using JPL.Domain;

namespace JPL.Services;

public sealed class HttpSyncClient : ISqlServerSyncClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public HttpSyncClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public bool IsEnabled => true;

    public async Task PushAsync(SyncOutboxItem item, CancellationToken ct = default)
    {
        try
        {
            var http = _httpClientFactory.CreateClient(ApiOptions.HttpClientName);
            var body = new
            {
                idempotencyKey = item.IdempotencyKey,
                entityType = item.EntityType,
                entityId = item.EntityId,
                operation = item.Operation,
                payloadJson = item.PayloadJson
            };

            using var response = await http.PostAsJsonAsync("api/sync", body, JsonOptions, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                if (code is 502 or 503 or 504)
                    throw new OfflineException(NetworkUi.OfflineMessage);

                var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException($"Sync failed ({code}): {detail}");
            }
        }
        catch (OfflineException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (NetworkUi.IsNetworkFailure(ex))
        {
            throw new OfflineException(NetworkUi.OfflineMessage, ex);
        }
    }
}
