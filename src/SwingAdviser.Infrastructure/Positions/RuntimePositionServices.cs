using SwingAdviser.Application.Positions;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Positions;

/// <summary>Presentation composition root that keeps EF Core implementation details out of WPF.</summary>
public sealed class RuntimePositionServices : IDisposable
{
    private readonly SwingAdviserDbContext _context;

    private RuntimePositionServices(SwingAdviserDbContext context)
    {
        _context = context;
        Registration = new ManualTradeRegistrationService(new EfManualTradeRegistrationStore(context));
        InstrumentLookup = new EfInstrumentLookup(context);
        OverviewReader = new EfManualPositionOverviewReader(context);
    }

    public ManualTradeRegistrationService Registration { get; }
    public IInstrumentLookup InstrumentLookup { get; }
    public IManualPositionOverviewReader OverviewReader { get; }

    public static RuntimePositionServices Create() => new(RuntimeSwingAdviserDbContextFactory.CreateMigratedContext());

    public void Dispose() => _context.Dispose();
}
