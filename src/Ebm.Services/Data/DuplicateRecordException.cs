using Ebm.Domain.Common;

namespace Ebm.Services.Data;

/// <summary>A record would duplicate an existing one (see <see cref="DuplicateKeys"/>). Nothing was saved.</summary>
public sealed class DuplicateRecordException : DomainException
{
    public DuplicateRecordException(string message)
        : base(message)
    {
    }
}
