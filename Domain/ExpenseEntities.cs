namespace JPL.Domain;

public static class ExpenseContributionStatus
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";

    public static bool IsValid(string? status)
        => string.Equals(status, Pending, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, Paid, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? status)
        => string.Equals(status, Paid, StringComparison.OrdinalIgnoreCase) ? Paid : Pending;
}

public sealed class Expense
{
    public Guid Id { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid PurchasedByPlayerId { get; set; }
    public decimal Amount { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.Now;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.Now;
    public bool IsDeleted { get; set; }
    public ICollection<ExpenseContribution> Contributions { get; set; } = new List<ExpenseContribution>();
}

public sealed class ExpenseContribution
{
    public Guid ExpenseId { get; set; }
    public Expense Expense { get; set; } = null!;
    public Guid PlayerId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = ExpenseContributionStatus.Pending;
}
