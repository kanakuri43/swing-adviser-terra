using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SwingAdviser.Infrastructure.Persistence.Converters;

internal sealed class JstDateTimeTextConverter : ValueConverter<DateTimeOffset, string>
{
    private static readonly TimeSpan JapanStandardTimeOffset = TimeSpan.FromHours(9);

    public static readonly JstDateTimeTextConverter Instance = new();

    private JstDateTimeTextConverter()
        : base(
            value => value.ToOffset(JapanStandardTimeOffset).ToString("O", CultureInfo.InvariantCulture),
            value => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToOffset(JapanStandardTimeOffset))
    {
    }
}
