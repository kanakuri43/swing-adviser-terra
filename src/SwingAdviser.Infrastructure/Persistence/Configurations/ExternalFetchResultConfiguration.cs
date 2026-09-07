using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class ExternalFetchResultConfiguration : IEntityTypeConfiguration<ExternalFetchResult>
{
    public void Configure(EntityTypeBuilder<ExternalFetchResult> builder)
    {
        builder.ToTable("external_fetch_results");
        builder.HasKey(entity => entity.FetchResultId);
        builder.Property(entity => entity.AttemptedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");

        builder.HasOne(entity => entity.DailyUpdateRun)
            .WithMany(entity => entity.ExternalFetchResults)
            .HasForeignKey(entity => entity.DailyUpdateRunId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.DailyUpdateFetchCheckpoint)
            .WithMany(entity => entity.ExternalFetchResults)
            .HasForeignKey(entity => entity.DailyUpdateFetchCheckpointId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Instrument)
            .WithMany()
            .HasForeignKey(entity => entity.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
