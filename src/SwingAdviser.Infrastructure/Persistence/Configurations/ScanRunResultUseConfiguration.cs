using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class ScanRunResultUseConfiguration : IEntityTypeConfiguration<ScanRunResultUse>
{
    public void Configure(EntityTypeBuilder<ScanRunResultUse> builder)
    {
        builder.ToTable("scan_run_result_uses");
        builder.HasKey(entity => entity.ScanRunResultUseId);
        builder.HasIndex(entity => new { entity.ScanRunId, entity.IndicatorResultId }).IsUnique();
        builder.Property(entity => entity.UsedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.ScanRun).WithMany(entity => entity.ResultUses).HasForeignKey(entity => entity.ScanRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.IndicatorResult).WithMany(entity => entity.ScanRunUses).HasForeignKey(entity => entity.IndicatorResultId).OnDelete(DeleteBehavior.Restrict);
    }
}
