using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JPL.Domain;

namespace JPL.Services;

public interface IOutboxWriter
{
    void Enqueue(JPL.Data.JplDbContext db, string entityType, Guid entityId, string operation, object payload);
}

public sealed class OutboxWriter : IOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void Enqueue(JPL.Data.JplDbContext db, string entityType, Guid entityId, string operation, object payload)
    {
        db.SyncOutbox.Add(new SyncOutboxItem
        {
            IdempotencyKey = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            Operation = operation,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            Status = SyncOutboxStatus.Pending
        });
    }
}
