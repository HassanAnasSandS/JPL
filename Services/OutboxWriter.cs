using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JPL.Domain;

namespace JPL.Services;

public interface IOutboxWriter
{
    void Enqueue(JPL.Data.JplDbContext db, string entityType, Guid entityId, string operation, object payload);
    void EnqueueEntity(JPL.Data.JplDbContext db, object entity, string operation);
}

public sealed class OutboxWriter : IOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void Enqueue(JPL.Data.JplDbContext db, string entityType, Guid entityId, string operation, object payload)
    {
        // Fire-and-forget seed: EnsureLeagueWriteReady is async; use sync check via ChangeTracker/local rows.
        EnsureCursorSeed(db);

        db.SyncOutbox.Add(new SyncOutboxItem
        {
            IdempotencyKey = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            EntityKeyJson = JsonSerializer.Serialize(new { id = entityId }, JsonOptions),
            Operation = operation,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            Status = SyncOutboxStatus.Pending
        });
    }

    public void EnqueueEntity(JPL.Data.JplDbContext db, object entity, string operation)
    {
        // Snapshot based EF tracking only reports direct CLR property edits after
        // DetectChanges. Capture the original scalar values before SaveChanges so
        // offline edits can be compared safely with the server copy.
        db.ChangeTracker.DetectChanges();
        var metadata = db.Model.FindEntityType(entity.GetType())
                       ?? throw new InvalidOperationException($"Entity is not mapped for sync: {entity.GetType().Name}.");
        var entry = db.Entry(entity);
        var keyProperties = metadata.FindPrimaryKey()?.Properties
                           ?? throw new InvalidOperationException($"Entity has no primary key: {entity.GetType().Name}.");
        var key = keyProperties.ToDictionary(
            property => JsonNamingPolicy.CamelCase.ConvertName(property.Name),
            property => entry.Property(property.Name).CurrentValue);
        var entityId = keyProperties.Select(property => entry.Property(property.Name).CurrentValue)
            .OfType<Guid>().FirstOrDefault();
        if (entityId == Guid.Empty)
            throw new InvalidOperationException($"Sync entity key has no Guid value: {entity.GetType().Name}.");

        var payload = entry.Properties
            .Where(property => !property.Metadata.IsShadowProperty()
                               && !string.Equals(property.Metadata.Name, "ImagePath", StringComparison.Ordinal))
            .ToDictionary(
                property => JsonNamingPolicy.CamelCase.ConvertName(property.Metadata.Name),
                property => property.CurrentValue);
        var basePayload = entry.State is Microsoft.EntityFrameworkCore.EntityState.Modified
                or Microsoft.EntityFrameworkCore.EntityState.Deleted
            ? JsonSerializer.Serialize(metadata.GetProperties()
                .Where(property => !property.IsShadowProperty()
                    && !string.Equals(property.Name, "ImagePath", StringComparison.Ordinal))
                .ToDictionary(
                    property => JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                    property => entry.Property(property.Name).OriginalValue), JsonOptions)
            : null;

        EnsureCursorSeed(db);

        db.SyncOutbox.Add(new SyncOutboxItem
        {
            IdempotencyKey = Guid.NewGuid(),
            EntityType = metadata.ClrType.Name,
            EntityId = entityId,
            EntityKeyJson = JsonSerializer.Serialize(key, JsonOptions),
            BasePayloadJson = basePayload,
            Operation = operation,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            Status = SyncOutboxStatus.Pending
        });
    }

    private static void EnsureCursorSeed(JPL.Data.JplDbContext db)
    {
        if (db.CurrentLeagueId == Guid.Empty)
            return;

        // Prefer Local entries already tracked in this context (same TX as the write).
        if (db.SyncCursors.Local.Any(cursor => cursor.LeagueId == db.CurrentLeagueId))
            return;

        // Avoid a sync DB round-trip on every enqueue: probe once via Find if not tracked.
        var existing = db.SyncCursors.Find(db.CurrentLeagueId);
        if (existing is not null)
            return;

        db.SyncCursors.Add(new SyncCursor
        {
            LeagueId = db.CurrentLeagueId,
            LastSequence = 0,
            IsBootstrapped = false,
            LastSuccessfulSyncUtc = null
        });
    }
}
