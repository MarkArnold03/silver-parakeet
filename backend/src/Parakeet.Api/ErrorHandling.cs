using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Parakeet.Domain.Common;

namespace Parakeet.Api;

/// <summary>
/// The one place that turns exceptions into HTTP errors. Every error body is a ProblemDetails
/// with a stable "code" that clients can switch on (e.g. SlotFull, CutoffPassed).
/// </summary>
public class ErrorHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var sql = exception as SqlException ?? exception.InnerException as SqlException;
        (int Status, string Code, string Message)? error = exception switch
        {
            NotFoundException e => (404, e.Code, e.Message),
            ConflictException e => (409, e.Code, e.Message),
            UnauthorizedException e => (401, e.Code, e.Message),
            DomainException e => (400, e.Code, e.Message),
            DbUpdateConcurrencyException => (409, "ConcurrentChange", "Someone else changed this at the same time. Reload and try again."),
            // Unique index violations: the database is the last line of defence for "only one of these".
            DbUpdateException when sql?.Number is 2601 or 2627 => (409, "Duplicate", "That already exists."),
            DbUpdateException when sql?.Number is 547 => (409, "InUse", "It is used by other data and can't be removed."),
            _ when sql?.Number is 51000 => (503, "Busy", "The system is busy. Try again."),
            _ => null,
        };
        if (error is not { } known)
            return false; // unknown errors become a plain 500 without details

        http.Response.StatusCode = known.Status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = known.Status,
                Title = known.Message,
                Extensions = { ["code"] = known.Code },
            },
        });
    }
}
