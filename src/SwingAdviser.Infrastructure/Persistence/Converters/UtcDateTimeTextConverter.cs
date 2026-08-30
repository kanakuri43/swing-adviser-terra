using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SwingAdviser.Infrastructure.Persistence.Converters;

internal sealed class UtcDateTimeTextConverter : ValueConverter<DateTime, string>
{
    public static readonly UtcDateTimeTextConverter Instance = new();

    private UtcDateTimeTextConverter()
        : base(
            value => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            value => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime())
    {
    }
}
