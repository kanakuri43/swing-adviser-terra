using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.MarketData;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class DailyBarConfiguration : IEntityTypeConfiguration<DailyBar>
{
    public void Configure(EntityTypeBuilder<DailyBar> builder)
    {
        builder.ToTable("daily_bars");
        builder.HasKey(entity => entity.DailyBarId);
        builder.HasIndex(entity => new { entity.InstrumentId, entity.TradingDate, entity.Revision }).IsUnique();
        builder.HasIndex(entity => entity.SupersedesId).IsUnique().HasFilter("supersedes_id IS NOT NULL");

        builder.Property(entity => entity.TradingDate)
            .HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.Open)
            .HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.High)
            .HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.Low)
            .HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.Close)
            .HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.AdjClose)
            .HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.FetchedAtUtc)
            .HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");

        builder.HasOne(entity => entity.Instrument)
            .WithMany(entity => entity.DailyBars)
            .HasForeignKey(entity => entity.InstrumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Supersedes)
            .WithMany()
            .HasForeignKey(entity => entity.SupersedesId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
