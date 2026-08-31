using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.DailyUpdates;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.DailyUpdates;

public sealed class EfCandidateResultCounter(SwingAdviserDbContext context) : ICandidateResultCounter
{
    public Task<int> CountMatchedAsync(int scanRunId, string direction, CancellationToken cancellationToken) =>
        context.CandidateResults.CountAsync(candidate => candidate.IndicatorResult.ScanRunId == scanRunId && candidate.Direction == direction && candidate.Matched, cancellationToken);
}
