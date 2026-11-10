namespace Parakeet.Domain.Common;

/// <summary>A broken business rule. <see cref="Code"/> is stable and is what API clients switch on.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
