namespace SwingAdviser.Application.Positions;

/// <summary>Resolves a user-visible code to the current internal instrument ID without exposing persistence to the UI.</summary>
public interface IInstrumentLookup
{
    Task<int?> FindCurrentInstrumentIdByCodeAsync(string code, CancellationToken cancellationToken = default);
}
