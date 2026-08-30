using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class ScanRunConfiguration : IEntityTypeConfiguration<ScanRun>
{
    public void Configure(EntityTypeBuilder<ScanRun> builder)
    {
        builder.ToTable("scan_runs");
        builder.HasKey(entity => entity.ScanRunId);
        builder.Property(entity => entity.StartedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.CompletedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");

        builder.HasOne(entity => entity.DailyUpdateRun)
            .WithMany(entity => entity.ScanRuns)
            .HasForeignKey(entity => entity.DailyUpdateRunId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
