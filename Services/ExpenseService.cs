using JPL.Domain;

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

public sealed class ExpenseService(IApiClient api, IAuthService auth) : IExpenseService
{
    public async Task<ApiPagedResult<ExpenseListItem>> GetPageAsync(int skip, string? q, CancellationToken ct = default)
    {
        var page = await api.GetExpensesPageAsync(skip, q, ct).ConfigureAwait(false);
        return page.WithItems((page.Items ?? []).Select(ToList).ToList());
    }

    public async Task<ExpenseDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await api.GetExpenseAsync(id, ct).ConfigureAwait(false);
        return dto is null ? null : ToDetail(dto);
    }

    public async Task<ExpenseDetail> CreateAsync(ExpenseDraft draft, CancellationToken ct = default)
    {
        auth.EnsureCanWrite(); Validate(draft);
        var row = await api.CreateExpenseAsync(ToRequest(draft), ct).ConfigureAwait(false);
        return ToDetail(row);
    }

    public async Task<ExpenseDetail> UpdateAsync(Guid id, ExpenseDraft draft, CancellationToken ct = default)
    {
        auth.EnsureCanWrite(); Validate(draft);
        var existing = await GetAsync(id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Expense not found.");
        if (!existing.CanEdit) throw new InvalidOperationException("Only the admin who created this expense can edit it.");
        return ToDetail(await api.UpdateExpenseAsync(id, ToRequest(draft), ct).ConfigureAwait(false));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        auth.EnsureCanWrite();
        var existing = await GetAsync(id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Expense not found.");
        if (!existing.CanEdit) throw new InvalidOperationException("Only the admin who created this expense can delete it.");
        await api.DeleteExpenseAsync(id, ct).ConfigureAwait(false);
    }

    private static object ToRequest(ExpenseDraft draft) => new
    {
        name = draft.Name.Trim(), purchasedByPlayerId = draft.PurchasedByPlayerId, amount = draft.Amount,
        contributions = draft.Contributions.Select(x => new
        {
            playerId = x.PlayerId, amount = x.Amount, status = ExpenseContributionStatus.Normalize(x.Status)
        }).ToList()
    };

    private static void Validate(ExpenseDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Trim().Length > 160) throw new ArgumentException("Expense name is required (maximum 160 characters).");
        if (draft.PurchasedByPlayerId == Guid.Empty) throw new ArgumentException("Select the player who purchased this expense.");
        if (draft.Amount < 0) throw new ArgumentException("Amount cannot be negative.");
        var ids = new HashSet<Guid>();
        foreach (var c in draft.Contributions)
        {
            if (c.PlayerId == Guid.Empty || !ids.Add(c.PlayerId)) throw new ArgumentException("Each contributing player should be added only once.");
            if (c.Amount < 0) throw new ArgumentException("Contribution amount cannot be negative.");
            if (!ExpenseContributionStatus.IsValid(c.Status)) throw new ArgumentException("Contribution status must be Pending or Paid.");
        }
    }

    private ExpenseListItem ToList(ApiExpenseDto dto) => new()
    {
        Id = dto.Id, CreatedByUserId = dto.CreatedByUserId, CreatedByUsername = dto.CreatedByUsername,
        Name = dto.Name, PurchasedByPlayerId = dto.PurchasedByPlayerId, PurchasedByPlayerName = dto.PurchasedByPlayerName,
        Amount = dto.Amount, ContributedAmount = dto.ContributedAmount, OutstandingAmount = dto.OutstandingAmount,
        CreatedAtUtc = dto.CreatedAtUtc, CanEdit = CanEdit(dto.CreatedByUserId),
        Contributions = dto.Contributions.Select(ToContribution).ToList()
    };

    private ExpenseDetail ToDetail(ApiExpenseDto dto) => new()
    {
        Id = dto.Id, CreatedByUserId = dto.CreatedByUserId, CreatedByUsername = dto.CreatedByUsername,
        Name = dto.Name, PurchasedByPlayerId = dto.PurchasedByPlayerId, PurchasedByPlayerName = dto.PurchasedByPlayerName,
        Amount = dto.Amount, CanEdit = CanEdit(dto.CreatedByUserId),
        Contributions = dto.Contributions.Select(ToContribution).ToList()
    };

    private bool CanEdit(Guid ownerId) => auth.CanWrite && (auth.Current?.Role == AppRole.SuperAdmin || auth.Current?.UserId == ownerId);
    private static ExpenseContributionItem ToContribution(ApiExpenseContributionDto c) => new()
    {
        PlayerId = c.PlayerId, PlayerName = c.PlayerName, Amount = c.Amount,
        Status = ExpenseContributionStatus.Normalize(c.Status)
    };
}
