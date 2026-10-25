using FluentValidation;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Exceptions;

namespace ZeroNovaRealEstate.Application.Behavior;

/// <summary>Runs every FluentValidation validator for the request; any failure becomes one <see cref="ValidatorException"/>.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, CancellationToken ct, RequestHandlerDelegate<TResponse> next)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct)));
            var failures = results.SelectMany(r => r.Errors).Where(e => e is not null).ToList();
            if (failures.Count > 0)
                throw new ValidatorException(failures);
        }

        return await next(ct);
    }
}
