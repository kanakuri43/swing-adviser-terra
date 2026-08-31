using SwingAdviser.Domain.Positions;

namespace SwingAdviser.Domain.Risk;

public interface IBusinessDayCalendar
{
    int CountBusinessDaysInclusive(DateOnly from, DateOnly through);
}

/// <summary>Weekday-only default calendar. Production callers can replace it with a JPX holiday-aware calendar.</summary>
public sealed class WeekdayBusinessDayCalendar : IBusinessDayCalendar
{
    public int CountBusinessDaysInclusive(DateOnly from, DateOnly through)
    {
        if (through < from) return -CountBusinessDaysInclusive(through, from);
        var count = 0;
        for (var day = from; day <= through; day = day.AddDays(1)) if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) count++;
        return count;
    }
}

public sealed record MarginLotMaturityInput(int MarginLotId, string LotStatus, IReadOnlyCollection<MarginLotContractTermRevision> ContractTerms);
public sealed record MarginLotMaturityResult(int MarginLotId, string Status, DateOnly? FinalRepaymentDate, int? RemainingBusinessDays);
public sealed record PositionMaturityResult(string Status, DateOnly? EarliestFinalRepaymentDate, int? RemainingBusinessDays, IReadOnlyList<MarginLotMaturityResult> Lots);

/// <summary>Aggregates the earliest broker-confirmed repayment deadline without inferring unknown terms.</summary>
public sealed class RepaymentMaturityAggregator
{
    public PositionMaturityResult Evaluate(IEnumerable<MarginLotMaturityInput> inputs, DateOnly asOf, RiskManagementParameters parameters, IBusinessDayCalendar? calendar = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        calendar ??= new WeekdayBusinessDayCalendar();
        var lots = inputs.Where(input => input.LotStatus == RiskVocabulary.Open).OrderBy(input => input.MarginLotId).Select(input => EvaluateLot(input, asOf, parameters, calendar)).ToArray();
        if (lots.Length == 0) return new PositionMaturityResult("NotApplicable", null, null, lots);
        if (lots.Any(lot => lot.Status == "Unknown")) return new PositionMaturityResult("Unknown", null, null, lots);
        var earliest = lots.OrderBy(lot => lot.FinalRepaymentDate).First();
        return new PositionMaturityResult(earliest.Status, earliest.FinalRepaymentDate, earliest.RemainingBusinessDays, lots);
    }

    private static MarginLotMaturityResult EvaluateLot(MarginLotMaturityInput input, DateOnly asOf, RiskManagementParameters parameters, IBusinessDayCalendar calendar)
    {
        var terms = input.ContractTerms.Where(term => term.MarginLotId == input.MarginLotId).ToArray();
        var ids = terms.Select(term => term.ContractTermRevisionId).ToHashSet();
        var superseded = terms.Where(term => term.SupersedesRevisionId.HasValue).Select(term => term.SupersedesRevisionId!.Value).ToHashSet();
        var active = terms.Where(term => term.Status == "Active" && !superseded.Contains(term.ContractTermRevisionId)).ToArray();
        if (active.Length != 1 || active[0].TermType != "FixedDate" || !active[0].FinalRepaymentDate.HasValue) return new MarginLotMaturityResult(input.MarginLotId, "Unknown", null, null);
        var date = active[0].FinalRepaymentDate!.Value;
        var remaining = calendar.CountBusinessDaysInclusive(asOf, date);
        var status = remaining < 0 ? "PastDue" : parameters.EffectiveMaturityWarningBusinessDays.Contains(remaining) ? $"Warning{remaining}" : "Normal";
        return new MarginLotMaturityResult(input.MarginLotId, status, date, remaining);
    }
}
