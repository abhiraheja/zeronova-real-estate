using Microsoft.Extensions.Logging;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;

namespace ZeroNovaRealEstate.Application.Behavior;

/// <summary>
/// Outermost behavior: logs any unexpected exception once, with the request name, and rethrows it as a
/// <see cref="ValidatorException"/> so it surfaces as a GraphQL error. Request data is NOT logged —
/// commands carry students' names and phone numbers.
/// </summary>
public sealed class UnhandledExceptionBehavior<TRequest, TResponse>(
    ILogger<UnhandledExceptionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, CancellationToken ct, RequestHandlerDelegate<TResponse> next)
    {
        try
        {
            return await next(ct);
        }
        catch (ValidatorException)
        {
            throw; // already client-shaped — don't re-wrap or double-log
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for request {RequestName}", typeof(TRequest).Name);
            throw new ValidatorException(ex);
        }
    }
}
