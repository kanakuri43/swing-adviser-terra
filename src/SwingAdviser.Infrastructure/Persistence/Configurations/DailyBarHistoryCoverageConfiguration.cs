using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class DailyBarHistoryCoverageConfiguration : IEntityTypeConfiguration<DailyBarHistoryCoverage>
{
    public void Configure(EntityTypeBuilder<DailyBarHistoryCoverage> builder)
    {
        builder.ToTable("daily_bar_history_coverages");
        builder.HasKey(entity => entity.DailyBarHistoryCoverageId);
        builder.HasIndex(entity => new { entity.InstrumentId, entity.Source, entity.Revision }).IsUnique();
        builder.HasIndex(entity => entity.SupersedesId).IsUnique().HasFilter("supersedes_id IS NOT NULL");
        builder.Property(entity => entity.EarliestReturnedDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.LatestReturnedDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.ObservedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.Instrument).WithMany(entity => entity.DailyBarHistoryCoverages).HasForeignKey(entity => entity.InstrumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Supersedes).WithMany().HasForeignKey(entity => entity.SupersedesId).OnDelete(DeleteBehavior.Restrict);
    }
}
