namespace Parakeet.Domain.Common;

internal static class Guard
{
    public static string Required(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new DomainException("Required", $"{field} is required.");
        if (trimmed.Length > maxLength)
            throw new DomainException("TooLong", $"{field} can be at most {maxLength} characters.");
        return trimmed;
    }

    public static string? Optional(string? value, string field, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? null : Required(value, field, maxLength);

    public static decimal NotNegative(decimal value, string field)
        => value >= 0 ? value : throw new DomainException("Negative", $"{field} can't be negative.");

    public static decimal Positive(decimal value, string field)
        => value > 0 ? value : throw new DomainException("NotPositive", $"{field} must be greater than zero.");
}
