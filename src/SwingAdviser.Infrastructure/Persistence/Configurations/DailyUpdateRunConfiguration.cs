using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class DailyUpdateRunConfiguration : IEntityTypeConfiguration<DailyUpdateRun>
{
    public void Configure(EntityTypeBuilder<DailyUpdateRun> builder)
    {
        builder.ToTable("daily_update_runs");
        builder.HasKey(entity => entity.DailyUpdateRunId);
        builder.Property(entity => entity.StartedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.CompletedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
    }
}
