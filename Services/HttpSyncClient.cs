using System.Net.Http.Json;
using System.Text.Json;
using JPL.Domain;

namespace JPL.Services;

public sealed record ApiSyncChange(
    long Sequence,
    string EntityType,
    string EntityKey,
    string Operation,
    long? EntityVersion,
    DateTime ChangedAtUtc,
    string PayloadJson);

public sealed record ApiSyncChangesPage(
    long AfterSequence,
    long ThroughSequence,
    long NextSequence,
    bool HasMore,
    IReadOnlyList<ApiSyncChange> Changes);

public sealed record ApiSyncSnapshotRow(
    string EntityType,
    string EntityKey,
    string Operation,
    string PayloadJson);

public sealed record ApiSyncSnapshotPage(
    string EntityType,
    string? Cursor,
    string? NextCursor,
    bool HasMore,
    IReadOnlyList<ApiSyncSnapshotRow> Rows);

public sealed class SyncConflictException(string message, string? serverPayloadJson = null) : Exception(message)
{
    public string? ServerPayloadJson { get; } = serverPayloadJson;
}

public sealed class HttpSyncClient : ISqlServerSyncClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
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
                payloadJson = item.PayloadJson,
                basePayloadJson = item.BasePayloadJson
            };

            using var response = await http.PostAsJsonAsync("api/sync", body, JsonOptions, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                if (code == 409)
                    throw await ReadConflictAsync(response, ct).ConfigureAwait(false);
                if (code is 502 or 503 or 504)
                    throw new OfflineException(NetworkUi.OfflineMessage);
                var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Sync API returned HTTP {code} ({response.StatusCode}): {TrimDetail(detail)}",
                    null,
                    response.StatusCode);
            }
        }
        catch (OfflineException)
        {
            throw;
        }
        catch (SyncConflictException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (NetworkUi.IsHttpTimeout(ex))
        {
            throw new TimeoutException(NetworkUi.SlowServerMessage, ex);
        }
        catch (Exception ex) when (NetworkUi.IsNetworkFailure(ex))
        {
            throw new OfflineException(NetworkUi.OfflineMessage, ex);
        }
    }

    private static async Task<SyncConflictException> ReadConflictAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            var parsed = JsonSerializer.Deserialize<SyncPushConflictBody>(raw, JsonOptions);
            if (parsed is not null)
            {
                var message = string.IsNullOrWhiteSpace(parsed.Message) ? raw : parsed.Message;
                return new SyncConflictException($"SYNC_CONFLICT: {message}", parsed.ServerPayloadJson);
            }
        }
        catch (JsonException)
        {
            /* fall through */
        }

        return new SyncConflictException($"SYNC_CONFLICT: {raw}");
    }

    private sealed record SyncPushConflictBody(bool Accepted, string Message, string? ServerPayloadJson);

    public Task<IReadOnlyList<string>> GetEntityTypesAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<string>>("api/sync/entity-types", ct);

    public Task<ApiSyncChangesPage> GetChangesAsync(
        long afterSequence,
        long? throughSequence,
        int take,
        CancellationToken ct = default)
    {
        var query = $"api/sync/changes?afterSequence={afterSequence}&take={take}";
        if (throughSequence is long through)
            query += $"&throughSequence={through}";
        return GetAsync<ApiSyncChangesPage>(query, ct);
    }

    public Task<ApiSyncSnapshotPage> GetSnapshotAsync(
        string entityType,
        string? cursor,
        int take,
        CancellationToken ct = default)
        => GetAsync<ApiSyncSnapshotPage>(
            $"api/sync/snapshot?entityType={Uri.EscapeDataString(entityType)}&cursor={Uri.EscapeDataString(cursor ?? string.Empty)}&take={take}", ct);

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        try
        {
            var http = _httpClientFactory.CreateClient(ApiOptions.HttpClientName);
            using var response = await http.GetAsync(path, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                if (code == 409)
                {
                    var conflictDetail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    throw new SyncConflictException($"SYNC_CONFLICT: {conflictDetail}");
                }
                if (code is 502 or 503 or 504)
                    throw new OfflineException(NetworkUi.OfflineMessage);
                var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new HttpRequestException(
                    $"Sync API returned HTTP {code} ({response.StatusCode}): {TrimDetail(detail)}",
                    null,
                    response.StatusCode);
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("The sync API returned an empty response.");
        }
        catch (OfflineException)
        {
            throw;
        }
        catch (SyncConflictException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (NetworkUi.IsHttpTimeout(ex))
        {
            throw new TimeoutException(NetworkUi.SlowServerMessage, ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "The Sync API returned a response this app could not read. Check the deployed API version and response.",
                ex);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException(
                "The Sync API returned data in an unsupported format. Check the deployed API version and response.",
                ex);
        }
        catch (Exception ex) when (NetworkUi.IsNetworkFailure(ex))
        {
            throw new OfflineException(NetworkUi.OfflineMessage, ex);
        }
    }

    private static string TrimDetail(string detail)
    {
        var trimmed = detail.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "…";
    }
}
