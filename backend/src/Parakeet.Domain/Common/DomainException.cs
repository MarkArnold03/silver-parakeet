namespace Parakeet.Domain.Common;

/// <summary>A broken business rule. <see cref="Code"/> is stable and is what API clients switch on.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The request is fine but the current state doesn't allow it (HTTP 409).</summary>
public class ConflictException(string code, string message) : DomainException(code, message);

/// <summary>The thing asked for doesn't exist (HTTP 404).</summary>
public class NotFoundException(string code, string message) : DomainException(code, message);

/// <summary>Wrong or missing credentials (HTTP 401).</summary>
public class UnauthorizedException(string code, string message) : DomainException(code, message);
