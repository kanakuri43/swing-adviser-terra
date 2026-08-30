using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Positions;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class MarginLotContractTermRevisionConfiguration : IEntityTypeConfiguration<MarginLotContractTermRevision>
{
    public void Configure(EntityTypeBuilder<MarginLotContractTermRevision> builder)
    {
        builder.ToTable("margin_lot_contract_term_revisions");
        builder.HasKey(entity => entity.ContractTermRevisionId);
        builder.HasIndex(entity => new { entity.MarginLotId, entity.Revision }).IsUnique();
        builder.HasIndex(entity => entity.FinalRepaymentDate)
            .HasFilter("status = 'Active'");
        builder.Property(entity => entity.FinalRepaymentDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.ConfirmedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.Property(entity => entity.RecordedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT");
        builder.HasOne(entity => entity.MarginLot).WithMany(entity => entity.ContractTermRevisions).HasForeignKey(entity => entity.MarginLotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(entity => entity.SupersedesRevision).WithMany().HasForeignKey(entity => entity.SupersedesRevisionId).OnDelete(DeleteBehavior.Restrict);
    }
}
