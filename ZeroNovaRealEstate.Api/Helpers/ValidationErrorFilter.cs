using HotChocolate.Execution;
using ZeroNovaRealEstate.Application.Exceptions;

namespace ZeroNovaRealEstate.Api.Helpers;

/// <summary>
/// Surfaces <see cref="ValidatorException"/> messages with a <c>VALIDATION_ERROR</c> code and the
/// per-field list (walking the InnerException chain, since the mediator may wrap it). Any other
/// exception stays masked for the client and is logged server-side with its real cause.
/// </summary>
public sealed class ValidationErrorFilter(ILogger<ValidationErrorFilter> logger) : IErrorFilter
{
    public IError OnError(IError error)
    {
        if (error.Exception is null)
            return error;

        for (var current = error.Exception; current is not null; current = current.InnerException)
        {
            if (current is ValidatorException validator)
                return ErrorBuilder.FromError(error)
                    .SetMessage(validator.Message)
                    .SetCode(ValidatorException.Code)
                    .SetExtension("validationErrors", validator.GraphqlErrors)
                    .SetException(null)
                    .Build();
        }

        logger.LogError(error.Exception, "Masked GraphQL error on path '{Path}': {ExceptionType}",
            error.Path?.ToString(), error.Exception.GetType().FullName);
        return error;
    }
}
