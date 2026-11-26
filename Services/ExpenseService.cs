using JPL.Data;
using JPL.Domain;
using Microsoft.EntityFrameworkCore;

namespace JPL.Services;

public sealed record ExpenseContributionDraft(Guid PlayerId, decimal Amount, string Status);
public sealed record ExpenseDraft(string Name, Guid PurchasedByPlayerId, decimal Amount,
    IReadOnlyList<ExpenseContributionDraft> Contributions);

public sealed class ExpenseListItem
{
    public Guid Id { get; init; }
    public Guid CreatedByUserId { get; init; }
    public string CreatedByUsername { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Guid PurchasedByPlayerId { get; init; }
    public string PurchasedByPlayerName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public decimal ContributedAmount { get; init; }
    public decimal OutstandingAmount { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public IReadOnlyList<ExpenseContributionItem> Contributions { get; init; } = Array.Empty<ExpenseContributionItem>();
    public bool CanEdit { get; init; }
    public decimal PaidAmount => Contributions.Where(x => string.Equals(x.Status, ExpenseContributionStatus.Paid, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount);
    public decimal PendingAmount => Contributions.Where(x => !string.Equals(x.Status, ExpenseContributionStatus.Paid, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount);
    public string DateText => CreatedAtUtc.ToString("dd MMM yyyy");
    public string AmountText => Amount.ToString("N0");
    public string ContributedText => ContributedAmount.ToString("N0");
    public string OutstandingText => OutstandingAmount.ToString("N0");
    public string ContributorsLine => Contributions.Count == 0 ? "No player contributions" : $"Paid {PaidAmount:N0} · Pending {PendingAmount:N0}";
    public string OwnerLine => $"Added by {CreatedByUsername}";
}

public sealed class ExpenseDetail
{
    public Guid Id { get; init; }
    public Guid CreatedByUserId { get; init; }
    public string CreatedByUsername { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Guid PurchasedByPlayerId { get; init; }
    public string PurchasedByPlayerName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public bool CanEdit { get; init; }
    public IReadOnlyList<ExpenseContributionItem> Contributions { get; init; } = Array.Empty<ExpenseContributionItem>();
}

public sealed class ExpenseContributionItem
{
    public Guid PlayerId { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Status { get; init; } = ExpenseContributionStatus.Pending;
}

public interface IExpenseService
{
    Task<ApiPagedResult<ExpenseListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default);
    Task<ExpenseDetail?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ExpenseDetail> CreateAsync(ExpenseDraft draft, CancellationToken ct = default);
    Task<ExpenseDetail> UpdateAsync(Guid id, ExpenseDraft draft, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>SQLite keeps a complete expense ledger offline; parent and contributions sync in one local transaction.</summary>
public sealed class ExpenseService : IExpenseService
{
    private readonly IApiClient _api;
    private readonly IAuthService _auth;
    private readonly ILeagueContext _league;
    private readonly IDbContextFactory<JplDbContext> _dbFactory;
    private readonly IOutboxWriter _outbox;
    private readonly ISyncTrigger _sync;

    public ExpenseService(IApiClient api, IAuthService auth, ILeagueContext league, IDbContextFactory<JplDbContext> dbFactory,
        IOutboxWriter outbox, ISyncTrigger sync)
    {
        _api = api; _auth = auth; _league = league; _dbFactory = dbFactory; _outbox = outbox; _sync = sync;
    }

    public async Task<ApiPagedResult<ExpenseListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetExpensesPageAsync(skip, q, ct).ConfigureAwait(false);
            return live.WithItems((live.Items ?? [])
                .Select(dto => ToListItem(dto, CanEdit(dto.CreatedByUserId))).ToList());
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await LocalPageAsync(db, skip, q, ct).ConfigureAwait(false);
    }

    public async Task<ExpenseDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (!LeagueSyncPolicy.UsesLocalStorage(_auth.Current))
        {
            var live = await _api.GetExpenseAsync(id, ct).ConfigureAwait(false);
            return live is null ? null : ToDetail(live, CanEdit(live.CreatedByUserId));
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await LocalDetailAsync(db, id, ct).ConfigureAwait(false);
    }

    public async Task<ExpenseDetail> CreateAsync(ExpenseDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite(); Validate(draft);
        var userId = _auth.Current?.UserId ?? Guid.Empty;
        if (userId == Guid.Empty) throw new InvalidOperationException("Sign in again before adding an expense.");
        var now = DateTime.UtcNow;
        var entity = new Expense { Id = Guid.NewGuid(), CreatedByUserId = userId, Name = draft.Name.Trim(),
            PurchasedByPlayerId = draft.PurchasedByPlayerId, Amount = draft.Amount, Version = 1,
            CreatedAtUtc = now, UpdatedAtUtc = now };
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.Expenses.Add(entity);
        foreach (var row in draft.Contributions)
            db.ExpenseContributions.Add(new ExpenseContribution { ExpenseId = entity.Id, PlayerId = row.PlayerId,
                Amount = row.Amount, Status = ExpenseContributionStatus.Normalize(row.Status) });
        _outbox.EnqueueEntity(db, entity, "Upsert");
        foreach (var row in db.ChangeTracker.Entries<ExpenseContribution>().Where(entry => entry.State == EntityState.Added))
            _outbox.EnqueueEntity(db, row.Entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
        return await LocalDetailAsync(db, entity.Id, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("The expense was saved locally but could not be loaded.");
    }

    public async Task<ExpenseDetail> UpdateAsync(Guid id, ExpenseDraft draft, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite(); Validate(draft);
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.Expenses.Include(row => row.Contributions).FirstOrDefaultAsync(row => row.Id == id, ct)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Expense not found.");
        if (!CanEdit(entity.CreatedByUserId)) throw new InvalidOperationException("Only the admin who created this expense can edit it.");

        entity.Name = draft.Name.Trim(); entity.PurchasedByPlayerId = draft.PurchasedByPlayerId;
        entity.Amount = draft.Amount; entity.Version++; entity.UpdatedAtUtc = DateTime.UtcNow;
        var desired = draft.Contributions.ToDictionary(row => row.PlayerId);
        foreach (var contribution in entity.Contributions.ToList())
        {
            if (!desired.Remove(contribution.PlayerId, out var next))
            {
                db.ExpenseContributions.Remove(contribution);
                _outbox.EnqueueEntity(db, contribution, "Delete");
                continue;
            }
            var normalizedStatus = ExpenseContributionStatus.Normalize(next.Status);
            if (contribution.Amount != next.Amount || contribution.Status != normalizedStatus)
            {
                contribution.Amount = next.Amount; contribution.Status = normalizedStatus;
                _outbox.EnqueueEntity(db, contribution, "Upsert");
            }
        }
        foreach (var next in desired.Values)
        {
            var contribution = new ExpenseContribution { ExpenseId = entity.Id, PlayerId = next.PlayerId,
                Amount = next.Amount, Status = ExpenseContributionStatus.Normalize(next.Status) };
            db.ExpenseContributions.Add(contribution);
            _outbox.EnqueueEntity(db, contribution, "Upsert");
        }
        _outbox.EnqueueEntity(db, entity, "Upsert");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
        return await LocalDetailAsync(db, id, ct).ConfigureAwait(false)
               ?? throw new InvalidOperationException("The expense was saved locally but could not be loaded.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _auth.EnsureCanWrite();
        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await db.Expenses.Include(row => row.Contributions).FirstOrDefaultAsync(row => row.Id == id, ct)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Expense not found.");
        if (!CanEdit(entity.CreatedByUserId)) throw new InvalidOperationException("Only the admin who created this expense can delete it.");
        foreach (var contribution in entity.Contributions.ToList())
        {
            db.ExpenseContributions.Remove(contribution);
            _outbox.EnqueueEntity(db, contribution, "Delete");
        }
        entity.IsDeleted = true; entity.Version++; entity.UpdatedAtUtc = DateTime.UtcNow;
        _outbox.EnqueueEntity(db, entity, "Delete");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _sync.RequestSyncNow();
    }

    private async Task CacheAsync(JplDbContext db, IEnumerable<ApiExpenseDto> rows, CancellationToken ct)
    {
        var list = rows.ToList();
        var ids = list.Select(row => row.Id).ToHashSet();
        var pending = await db.SyncOutbox.AsNoTracking().Where(row => row.EntityType == nameof(Expense)
            && row.Status != SyncOutboxStatus.Completed && ids.Contains(row.EntityId))
            .Select(row => row.EntityId).ToListAsync(ct).ConfigureAwait(false);
        var protectedIds = pending.ToHashSet();
        foreach (var dto in list.Where(row => !protectedIds.Contains(row.Id)))
        {
            var entity = await db.Expenses.IgnoreQueryFilters().Include(row => row.Contributions)
                .FirstOrDefaultAsync(row => row.Id == dto.Id, ct).ConfigureAwait(false);
            if (entity is null) { entity = new Expense { Id = dto.Id }; db.Expenses.Add(entity); }
            entity.CreatedByUserId = dto.CreatedByUserId; entity.Name = dto.Name; entity.PurchasedByPlayerId = dto.PurchasedByPlayerId;
            entity.Amount = dto.Amount; entity.Version = dto.Version; entity.CreatedAtUtc = dto.CreatedAtUtc;
            entity.UpdatedAtUtc = dto.UpdatedAtUtc; entity.IsDeleted = dto.IsDeleted;
            var desired = dto.Contributions.ToDictionary(row => row.PlayerId);
            foreach (var contribution in entity.Contributions.Where(row => !desired.ContainsKey(row.PlayerId)).ToList())
                db.ExpenseContributions.Remove(contribution);
            var have = entity.Contributions.ToDictionary(row => row.PlayerId);
            foreach (var row in desired.Values)
            {
                if (have.TryGetValue(row.PlayerId, out var contribution))
                { contribution.Amount = row.Amount; contribution.Status = ExpenseContributionStatus.Normalize(row.Status); }
                else entity.Contributions.Add(new ExpenseContribution { ExpenseId = entity.Id, PlayerId = row.PlayerId,
                    Amount = row.Amount, Status = ExpenseContributionStatus.Normalize(row.Status) });
            }
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<ApiPagedResult<ExpenseListItem>> LocalPageAsync(JplDbContext db, int skip, string? q, CancellationToken ct)
    {
        var expenses = await db.Expenses.AsNoTracking().Include(row => row.Contributions).ToListAsync(ct).ConfigureAwait(false);
        var players = await db.Players.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.FullName, ct).ConfigureAwait(false);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Username, ct).ConfigureAwait(false);
        var list = expenses.Select(row => ToListItem(row, row.Contributions.ToList(), players, users))
            .Where(row => string.IsNullOrWhiteSpace(q) || row.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)
                || row.PurchasedByPlayerName.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(row => row.CreatedAtUtc).ToList();
        return new ApiPagedResult<ExpenseListItem>(list.Skip(Math.Max(0, skip)).Take(ListPaging.PageSize).ToList(),
            list.Count, Math.Max(0, skip), ListPaging.PageSize);
    }

    private async Task<ExpenseDetail?> LocalDetailAsync(JplDbContext db, Guid id, CancellationToken ct)
    {
        var entity = await db.Expenses.AsNoTracking().Include(row => row.Contributions)
            .FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
        if (entity is null) return null;
        var players = await db.Players.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.FullName, ct).ConfigureAwait(false);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Username, ct).ConfigureAwait(false);
        return ToDetail(entity, entity.Contributions.ToList(), players, users);
    }

    private ExpenseListItem ToListItem(ApiExpenseDto dto, bool canEdit) => new()
    {
        Id = dto.Id, CreatedByUserId = dto.CreatedByUserId, CreatedByUsername = dto.CreatedByUsername,
        Name = dto.Name, PurchasedByPlayerId = dto.PurchasedByPlayerId, PurchasedByPlayerName = dto.PurchasedByPlayerName,
        Amount = dto.Amount, ContributedAmount = dto.ContributedAmount, OutstandingAmount = dto.OutstandingAmount,
        CreatedAtUtc = dto.CreatedAtUtc, CanEdit = canEdit,
        Contributions = dto.Contributions.Select(ToContribution).ToList()
    };

    private ExpenseDetail ToDetail(ApiExpenseDto dto, bool canEdit) => new()
    {
        Id = dto.Id, CreatedByUserId = dto.CreatedByUserId, CreatedByUsername = dto.CreatedByUsername,
        Name = dto.Name, PurchasedByPlayerId = dto.PurchasedByPlayerId, PurchasedByPlayerName = dto.PurchasedByPlayerName,
        Amount = dto.Amount, CanEdit = canEdit, Contributions = dto.Contributions.Select(ToContribution).ToList()
    };

    private ExpenseListItem ToListItem(Expense entity, IReadOnlyList<ExpenseContribution> contributions,
        IReadOnlyDictionary<Guid, string> players, IReadOnlyDictionary<Guid, string> users)
    {
        var contributionItems = contributions.Select(row => ToContribution(row, players)).ToList();
        var contributed = contributionItems.Sum(row => row.Amount);
        return new ExpenseListItem { Id = entity.Id, CreatedByUserId = entity.CreatedByUserId,
            CreatedByUsername = users.GetValueOrDefault(entity.CreatedByUserId, CurrentUsername(entity.CreatedByUserId)),
            Name = entity.Name, PurchasedByPlayerId = entity.PurchasedByPlayerId,
            PurchasedByPlayerName = players.GetValueOrDefault(entity.PurchasedByPlayerId, string.Empty),
            Amount = entity.Amount, ContributedAmount = contributed, OutstandingAmount = entity.Amount - contributed,
            CreatedAtUtc = entity.CreatedAtUtc, Contributions = contributionItems, CanEdit = CanEdit(entity.CreatedByUserId) };
    }

    private ExpenseDetail ToDetail(Expense entity, IReadOnlyList<ExpenseContribution> contributions,
        IReadOnlyDictionary<Guid, string> players, IReadOnlyDictionary<Guid, string> users)
        => new() { Id = entity.Id, CreatedByUserId = entity.CreatedByUserId,
            CreatedByUsername = users.GetValueOrDefault(entity.CreatedByUserId, CurrentUsername(entity.CreatedByUserId)),
            Name = entity.Name, PurchasedByPlayerId = entity.PurchasedByPlayerId,
            PurchasedByPlayerName = players.GetValueOrDefault(entity.PurchasedByPlayerId, string.Empty),
            Amount = entity.Amount, CanEdit = CanEdit(entity.CreatedByUserId),
            Contributions = contributions.Select(row => ToContribution(row, players)).ToList() };

    private static ExpenseContributionItem ToContribution(ApiExpenseContributionDto row) => new()
    { PlayerId = row.PlayerId, PlayerName = row.PlayerName, Amount = row.Amount, Status = ExpenseContributionStatus.Normalize(row.Status) };

    private static ExpenseContributionItem ToContribution(ExpenseContribution row, IReadOnlyDictionary<Guid, string> players) => new()
    { PlayerId = row.PlayerId, PlayerName = players.GetValueOrDefault(row.PlayerId, string.Empty), Amount = row.Amount,
        Status = ExpenseContributionStatus.Normalize(row.Status) };

    private string CurrentUsername(Guid userId) => _auth.Current?.UserId == userId ? _auth.Current.Username : string.Empty;
    private bool CanEdit(Guid ownerId) => _auth.CanWrite && _auth.Current?.UserId == ownerId;
    private Task<bool> IsBootstrappedAsync(JplDbContext db, CancellationToken ct)
        => LocalCacheGate.PreferLocalAsync(db, _auth.Current, ct);

    private static void Validate(ExpenseDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Trim().Length > 160) throw new ArgumentException("Expense name is required (maximum 160 characters).");
        if (draft.PurchasedByPlayerId == Guid.Empty) throw new ArgumentException("Select the player who purchased this expense.");
        if (draft.Amount < 0) throw new ArgumentException("Amount cannot be negative.");
        var ids = new HashSet<Guid>();
        foreach (var row in draft.Contributions)
        {
            if (row.PlayerId == Guid.Empty || !ids.Add(row.PlayerId)) throw new ArgumentException("Each contributing player should be added only once.");
            if (row.Amount < 0) throw new ArgumentException("Contribution amount cannot be negative.");
            if (!ExpenseContributionStatus.IsValid(row.Status)) throw new ArgumentException("Contribution status must be Pending or Paid.");
        }
    }
}
