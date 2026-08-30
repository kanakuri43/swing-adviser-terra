using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SwingAdviser.Infrastructure.Persistence.Converters;

internal sealed class JstDateTextConverter : ValueConverter<DateOnly, string>
{
    public static readonly JstDateTextConverter Instance = new();

    private JstDateTextConverter()
        : base(
            value => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            value => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None))
    {
    }
}
