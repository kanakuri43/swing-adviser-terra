using Microsoft.EntityFrameworkCore;
using SwingAdviser.Application.Positions;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Positions;

public sealed class EfInstrumentLookup(SwingAdviserDbContext context) : IInstrumentLookup
{
    public Task<int?> FindCurrentInstrumentIdByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return Task.FromResult<int?>(null);
        return context.InstrumentMasterRevisions
            .Where(revision => revision.Code == code.Trim() && revision.Status == "Active")
            .OrderByDescending(revision => revision.AvailableAtUtc)
            .ThenByDescending(revision => revision.Revision)
            .Select(revision => (int?)revision.InstrumentId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
