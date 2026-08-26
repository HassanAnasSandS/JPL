using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JPL.Api.Data;

/// <summary>
/// Existing SQL Server columns are datetimeoffset. App values are local DateTime;
/// convert on the way in/out so we do not ALTER production catalogs.
/// </summary>
internal static class SqlServerLocalDateTimes
{
    private static readonly ValueConverter<DateTime, DateTimeOffset> Converter = new(
        dt => ToOffset(dt),
        dto => dto.LocalDateTime);

    private static readonly ValueConverter<DateTime?, DateTimeOffset?> ConverterNullable = new(
        dt => dt.HasValue ? ToOffset(dt.Value) : null,
        dto => dto.HasValue ? dto.Value.LocalDateTime : null);

    public static void Map(ModelBuilder modelBuilder, bool sqlServer)
    {
        if (!sqlServer)
            return;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                // Do not override entities that already chose datetime2 (Formats table).
                var columnType = property.GetColumnType();
                if (!string.IsNullOrEmpty(columnType)
                    && !columnType.StartsWith("datetimeoffset", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(Converter);
                    property.SetColumnType("datetimeoffset");
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(ConverterNullable);
                    property.SetColumnType("datetimeoffset");
                }
            }
        }
    }

    private static DateTimeOffset ToOffset(DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Utc)
            return new DateTimeOffset(dt).ToLocalTime();
        var local = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}
