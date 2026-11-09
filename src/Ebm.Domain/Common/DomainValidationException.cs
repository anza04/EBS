namespace Ebm.Domain.Common;

/// <summary>An entity was given a value that breaks one of its rules (empty name, value out of range, …).</summary>
public sealed class DomainValidationException : DomainException
{
    public DomainValidationException(string message)
        : base(message)
    {
    }
}
