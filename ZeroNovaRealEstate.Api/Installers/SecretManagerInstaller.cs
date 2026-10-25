using ZeroNovaRealEstate.Api.Configuration;
using ZeroNovaRealEstate.Models.Configuration;

namespace ZeroNovaRealEstate.Api.Installers;

public static class SecretManagerInstaller
{
    /// <summary>
    /// Merges the environment's Google Secret Manager secrets into configuration. Call it FIRST, before
    /// any installer reads config. Added after appsettings / environment variables, so a secret wins over
    /// a same-named plain setting. No-op while <c>GoogleSecretManager:ProjectId</c> is empty.
    /// </summary>
    public static WebApplicationBuilder AddGoogleSecretManager(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(GoogleSecretManagerOptions.SectionName).Get<GoogleSecretManagerOptions>()
            ?? new GoogleSecretManagerOptions();

        if (string.IsNullOrWhiteSpace(options.ProjectId))
            return builder;

        ((IConfigurationBuilder)builder.Configuration).Add(new GoogleSecretManagerConfigurationSource(options));
        return builder;
    }
}
