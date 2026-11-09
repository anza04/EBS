namespace Ebm.Domain.Common;

/// <summary>
/// Strongly typed, persistent entity identifier backed by a <see cref="Guid"/>.
/// </summary>
/// <remarks>
/// IDs are assigned by the caller (new entities use <c>New()</c>, imported entities keep the ID from
/// the data file), so they stay stable across databases, imports and save games.
/// </remarks>
public interface IEntityId<TSelf>
    where TSelf : struct, IEntityId<TSelf>
{
    Guid Value { get; }

    static abstract TSelf From(Guid value);
}
