namespace Ebm.Domain.Common;

/// <summary>
/// How far a record's information can be trusted as real-world data (docs/PLAYER_DATABASE.md §2). Stored per
/// player (profile and attributes separately), contract and club, so missing or estimated data is never shown as verified.
/// </summary>
public enum DataStatus
{
    /// <summary>Real-world information that has not been checked against a source (the import default).</summary>
    Unverified,

    /// <summary>Checked against the cited source; requires a source.</summary>
    Verified,

    /// <summary>Filled in by the game from partial information (missing attributes, missing salary).</summary>
    Estimated,

    /// <summary>Fictional: created by the game or a dataset generator, not real-world information.</summary>
    Generated,
}

/// <summary>Rules shared by every entity with a <see cref="DataStatus"/>.</summary>
public static class DataQuality
{
    public const int SourceMaxLength = 200;

    /// <summary>Validates a status and its source: a verified record must name its source.</summary>
    public static (DataStatus Status, string? Source) Check(DataStatus status, string? source, string what)
    {
        Guard.Defined(status, what);
        var trimmed = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        if (trimmed is { Length: > SourceMaxLength })
        {
            throw new DomainValidationException($"Source must be at most {SourceMaxLength} characters.");
        }

        if (status == DataStatus.Verified && trimmed is null)
        {
            throw new DomainValidationException($"{what} can only be 'Verified' with a source.");
        }

        return (status, trimmed);
    }
}
