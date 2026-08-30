using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class TradeExecutionConfiguration : IEntityTypeConfiguration<TradeExecution>
{
    public void Configure(EntityTypeBuilder<TradeExecution> builder)
    {
        builder.ToTable("trade_executions");
        builder.HasKey(entity => entity.TradeExecutionId);
        builder.Property(entity => entity.ExecutedAt).HasConversion(JstDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.Price).HasConversion(DecimalTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.EnteredAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.Position).WithMany(entity => entity.TradeExecutions).HasForeignKey(entity => entity.PositionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.PrefilledFromCandidateResult).WithMany().HasForeignKey(entity => entity.PrefilledFromCandidateResultId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.Supersedes).WithMany().HasForeignKey(entity => entity.SupersedesId).OnDelete(DeleteBehavior.Restrict);
    }
}
