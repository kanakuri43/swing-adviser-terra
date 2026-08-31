using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SwingAdviser.Domain.Analysis;
using SwingAdviser.Infrastructure.Persistence.Converters;

namespace SwingAdviser.Infrastructure.Persistence.Configurations;

internal sealed class AiCheckAttemptConfiguration : IEntityTypeConfiguration<AiCheckAttempt>
{
 public void Configure(EntityTypeBuilder<AiCheckAttempt> b) { b.ToTable("ai_check_attempts"); b.HasKey(e=>e.AttemptId); b.HasIndex(e=>e.Status); b.HasIndex(e => new { e.CandidateResultId, e.NormalizedInputSnapshotHash, e.Model }); b.Property(e => e.NormalizedInputSnapshotJson).HasColumnType("TEXT"); b.Property(e=>e.RequestedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT"); b.Property(e=>e.StartedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT"); b.Property(e=>e.CompletedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT"); b.Property(e=>e.EvaluationBarDate).HasConversion(JstDateTextConverter.Instance).HasColumnType("TEXT"); b.Property(e=>e.IsStale).HasColumnType("INTEGER"); b.HasOne(e=>e.CandidateResult).WithMany().HasForeignKey(e=>e.CandidateResultId).OnDelete(DeleteBehavior.Restrict); b.HasOne(e=>e.TriggeringDailyUpdateRun).WithMany().HasForeignKey(e=>e.TriggeringDailyUpdateRunId).OnDelete(DeleteBehavior.Restrict); b.HasOne(e=>e.TechnicalInputManifest).WithMany().HasForeignKey(e=>e.TechnicalInputManifestId).OnDelete(DeleteBehavior.Restrict); }
}
internal sealed class AiCheckResultConfiguration : IEntityTypeConfiguration<AiCheckResult>
{
 public void Configure(EntityTypeBuilder<AiCheckResult> b) { b.ToTable("ai_check_results"); b.HasKey(e=>e.ResultId); b.HasIndex(e=>e.AttemptId).IsUnique(); b.Property(e=>e.CheckedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT"); b.HasOne(e=>e.Attempt).WithMany().HasForeignKey(e=>e.AttemptId).OnDelete(DeleteBehavior.Restrict); }
}
internal sealed class AiCheckEvidenceItemConfiguration : IEntityTypeConfiguration<AiCheckEvidenceItem>
{
 public void Configure(EntityTypeBuilder<AiCheckEvidenceItem> b) { b.ToTable("ai_check_evidence_items"); b.HasKey(e=>e.EvidenceItemId); b.HasIndex(e=>new {e.ResultId,e.EvidenceKind,e.Ordinal}).IsUnique(); b.HasOne(e=>e.Result).WithMany().HasForeignKey(e=>e.ResultId).OnDelete(DeleteBehavior.Restrict); }
}
internal sealed class AiCheckSourceConfiguration : IEntityTypeConfiguration<AiCheckSource>
{
 public void Configure(EntityTypeBuilder<AiCheckSource> b) { b.ToTable("ai_check_sources"); b.HasKey(e=>e.SourceId); b.HasIndex(e=>new {e.ResultId,e.Ordinal}).IsUnique(); b.Property(e=>e.PublishedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT"); b.Property(e=>e.RetrievedAtUtc).HasConversion(UtcDateTimeTextConverter.Instance).HasColumnType("TEXT"); b.HasOne(e=>e.Result).WithMany().HasForeignKey(e=>e.ResultId).OnDelete(DeleteBehavior.Restrict); }
}
internal sealed class AiCheckEvidenceCitationConfiguration : IEntityTypeConfiguration<AiCheckEvidenceCitation>
{
 public void Configure(EntityTypeBuilder<AiCheckEvidenceCitation> b) { b.ToTable("ai_check_evidence_citations"); b.HasKey(e=>new {e.EvidenceItemId,e.SourceOrdinal}); b.HasOne(e=>e.EvidenceItem).WithMany().HasForeignKey(e=>e.EvidenceItemId).OnDelete(DeleteBehavior.Restrict); }
}
