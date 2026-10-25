using FluentValidation.Results;
using HotChocolate;

namespace ZeroNovaRealEstate.Application.Exceptions;

/// <summary>
/// The single client-facing error type for business and validation failures. Extends
/// <see cref="GraphQLException"/>, which HotChocolate turns into a GraphQL error directly.
/// <para>
/// The <c>VALIDATION_ERROR</c> code and the <c>validationErrors</c> list are built into the error HERE, not
/// in an error filter: HotChocolate converts a <see cref="GraphQLException"/> without attaching the
/// exception to the error, so a filter keyed on <c>error.Exception</c> never sees it (caught by the T01
/// end-to-end test — the message arrived, the code did not).
/// </para>
/// </summary>
public class ValidatorException : GraphQLException
{
    public const string Code = "VALIDATION_ERROR";

    public string[] GraphqlErrors { get; }

    public ValidatorException(string message) : this([message]) { }

    public ValidatorException(Exception ex) : this([ex.Message]) { }

    public ValidatorException(IEnumerable<ValidationFailure> failures)
        : this(failures.Select(f => f.ErrorMessage).Distinct().ToArray()) { }

    private ValidatorException(string[] messages) : base(BuildError(messages))
        => GraphqlErrors = messages;

    private static IError BuildError(string[] messages) =>
        ErrorBuilder.New()
            .SetMessage(string.Join(", ", messages))
            .SetCode(Code)
            .SetExtension("validationErrors", messages)
            .Build();
}
