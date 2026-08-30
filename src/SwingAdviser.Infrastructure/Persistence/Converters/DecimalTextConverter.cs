using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SwingAdviser.Infrastructure.Persistence.Converters;

internal sealed class DecimalTextConverter : ValueConverter<decimal, string>
{
    public static readonly DecimalTextConverter Instance = new();

    private DecimalTextConverter()
        : base(
            value => value.ToString(CultureInfo.InvariantCulture),
            value => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture))
    {
    }
}
