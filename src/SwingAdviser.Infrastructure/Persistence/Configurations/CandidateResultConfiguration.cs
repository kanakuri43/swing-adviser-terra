using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class CandidateResultConfiguration : IEntityTypeConfiguration<CandidateResult>
{
    public void Configure(EntityTypeBuilder<CandidateResult> builder)
    {
        builder.ToTable("candidate_results");
        builder.HasKey(entity => entity.CandidateResultId);
        builder.HasIndex(entity => new { entity.IndicatorResultId, entity.Direction }).IsUnique();
        builder.HasIndex(entity => new { entity.Direction, entity.Score, entity.InstrumentId })
            .IsDescending(false, true, false);
        builder.Property(entity => entity.Matched).HasColumnType("INTEGER");
        builder.Property(entity => entity.CreatedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.IndicatorResult).WithMany(entity => entity.CandidateResults).HasForeignKey(entity => entity.IndicatorResultId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Instrument).WithMany().HasForeignKey(entity => entity.InstrumentId).OnDelete(DeleteBehavior.Restrict);
    }
}
