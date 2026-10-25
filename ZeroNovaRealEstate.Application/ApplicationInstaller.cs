using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ZeroNova_Packages.MediatR;
using ZeroNova_Packages.MediatR.Interfaces;
using ZeroNovaRealEstate.Application.Behavior;

namespace ZeroNovaRealEstate.Application;

/// <summary>Marker type so the Application assembly can be referenced cheaply for scanning.</summary>
public sealed class ApplicationAssembly;

public static class ApplicationInstaller
{
    /// <summary>
    /// MediatR (+ pipeline behaviors) and FluentValidation; handlers and validators are picked up by assembly scan.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(ApplicationAssembly).Assembly;

        services.AddValidatorsFromAssembly(assembly);
        services.AddMediator(assembly);

        // Registration order = execution order: UnhandledException wraps Validation wraps the handler.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        // What the caller may see (D14, D15), and properties as members see them.
        services.AddScoped<Features.Common.EstateScope>();
        services.AddScoped<Features.Properties.PropertyMapper>();
        // Matching (M1): one place scores a property against a requirement and keeps the matches current.
        services.AddScoped<Features.Matching.MatchEngine>();
        services.AddScoped<Features.Properties.HoldRelease>();
        // Site visits and sharing (M2).
        services.AddScoped<Features.Visits.VisitReminders>();
        services.AddScoped<Features.Visits.VisitMapper>();
        // Deals and agreements (M3).
        services.AddScoped<Features.Deals.DealMapper>();
        services.AddScoped<Features.Agreements.AgreementSource>();

        return services;
    }
}
