namespace Ebm.Domain.Common;

/// <summary>
/// Base type for violations of game rules (e.g. an invalid roster or an illegal transfer).
/// </summary>
/// <remarks>
/// The message of a <see cref="DomainException"/> must be safe and understandable for the player:
/// the global error-handling policy shows it to the user and lets the application continue.
/// Any other exception type is treated as an unexpected technical failure.
/// </remarks>
public class DomainException : Exception
{
    public DomainException()
    {
    }

    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
