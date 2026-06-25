using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Pricing.Domain.Promotions;

/// <summary>Fenêtre d'activation d'une promotion (StartAt / EndAt). Value Object.</summary>
public sealed class DateRange : ValueObject
{
    private DateRange(DateTime startUtc, DateTime endUtc)
    {
        StartUtc = startUtc;
        EndUtc = endUtc;
    }

    public DateTime StartUtc { get; }
    public DateTime EndUtc { get; }

    public static Result<DateRange> Create(DateTime startUtc, DateTime endUtc)
    {
        if (endUtc <= startUtc)
        {
            return Error.Validation("pricing.daterange.invalid", "La date de fin doit être postérieure à la date de début.");
        }

        return new DateRange(startUtc, endUtc);
    }

    public bool Contains(DateTime instantUtc) => instantUtc >= StartUtc && instantUtc <= EndUtc;

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return StartUtc;
        yield return EndUtc;
    }
}
