namespace Ebm.Services.Data;

/// <summary>
/// A save was rejected by a database constraint (duplicate key, missing or still-referenced record, failed check).
/// Nothing from the failed save was written.
/// </summary>
/// <remarks>
/// Not a <see cref="Ebm.Domain.Common.DomainException"/>: callers that can explain the problem to the
/// player (e.g. the database editor) catch it; anywhere else it is an unexpected failure.
/// </remarks>
public sealed class DataConstraintException : Exception
{
    public DataConstraintException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
