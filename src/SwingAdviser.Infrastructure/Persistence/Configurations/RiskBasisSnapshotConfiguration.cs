using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Risk;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class RiskBasisSnapshotConfiguration : IEntityTypeConfiguration<RiskBasisSnapshot>
{
    public void Configure(EntityTypeBuilder<RiskBasisSnapshot> builder)
    {
        builder.ToTable("risk_basis_snapshots");
        builder.HasKey(entity => entity.RiskBasisId);
        builder.HasIndex(entity => entity.MarginLotId).IsUnique();
        builder.Property(entity => entity.EntryBasisPrice).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.AtrBasis).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.AtrReferenceBarDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.CreatedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.MarginLot).WithMany().HasForeignKey(entity => entity.MarginLotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.SourceCandidateResult).WithMany().HasForeignKey(entity => entity.SourceCandidateResultId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.SourceIndicatorResult).WithMany().HasForeignKey(entity => entity.SourceIndicatorResultId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.ManualOpenAnalysisInputManifest).WithMany().HasForeignKey(entity => entity.ManualOpenAnalysisInputManifestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.StrategyParameterSnapshot).WithMany().HasForeignKey(entity => entity.StrategyParameterSnapshotId).OnDelete(DeleteBehavior.Restrict);
    }
}
