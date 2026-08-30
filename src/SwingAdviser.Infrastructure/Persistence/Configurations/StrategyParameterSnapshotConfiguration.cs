using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class StrategyParameterSnapshotConfiguration : IEntityTypeConfiguration<StrategyParameterSnapshot>
{
    public void Configure(EntityTypeBuilder<StrategyParameterSnapshot> builder)
    {
        builder.ToTable("strategy_parameter_snapshots");
        builder.HasKey(entity => entity.StrategyParameterSnapshotId);
        builder.HasIndex(entity => entity.ContentSha256).IsUnique();
        builder.Property(entity => entity.CreatedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
    }
}
