namespace Ebm.Domain.Common;

/// <summary>Argument checks shared by entities. Violations throw <see cref="DomainValidationException"/>.</summary>
internal static class Guard
{
    public static string Text(string? value, string name, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new DomainValidationException($"{name} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new DomainValidationException($"{name} must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    public static int Range(int value, int min, int max, string name)
    {
        if (value < min || value > max)
        {
            throw new DomainValidationException($"{name} must be between {min} and {max}.");
        }

        return value;
    }

    public static TId Required<TId>(TId id, string name)
        where TId : struct, IEntityId<TId>
    {
        if (id.Value == Guid.Empty)
        {
            throw new DomainValidationException($"{name} must be set.");
        }

        return id;
    }

    public static TId? Optional<TId>(TId? id, string name)
        where TId : struct, IEntityId<TId> =>
        id is { } value ? Required(value, name) : null;

    public static TEnum Defined<TEnum>(TEnum value, string name)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new DomainValidationException($"{name} has an unknown value '{value}'.");
        }

        return value;
    }

    public static DateOnly After(DateOnly value, DateOnly start, string name)
    {
        if (value <= start)
        {
            throw new DomainValidationException($"{name} must be after {start:yyyy-MM-dd}.");
        }

        return value;
    }
}
