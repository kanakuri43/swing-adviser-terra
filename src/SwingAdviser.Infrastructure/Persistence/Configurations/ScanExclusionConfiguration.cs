using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class ScanExclusionConfiguration : IEntityTypeConfiguration<ScanExclusion>
{
    public void Configure(EntityTypeBuilder<ScanExclusion> builder)
    {
        builder.ToTable("scan_exclusions");
        builder.HasKey(entity => entity.ScanExclusionId);
        builder.HasIndex(entity => new { entity.ScanRunId, entity.InstrumentId }).IsUnique();
        builder.HasOne(entity => entity.ScanRun).WithMany(entity => entity.ScanExclusions).HasForeignKey(entity => entity.ScanRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Instrument).WithMany().HasForeignKey(entity => entity.InstrumentId).OnDelete(DeleteBehavior.Restrict);
    }
}
