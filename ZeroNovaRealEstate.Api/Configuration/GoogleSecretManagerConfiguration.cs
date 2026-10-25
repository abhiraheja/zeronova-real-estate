using Google.Api.Gax.ResourceNames;
using Google.Cloud.SecretManager.V1;
using Grpc.Core;
using ZeroNovaRealEstate.Models.Configuration;

namespace ZeroNovaRealEstate.Api.Configuration;

/// <summary>Adds the secrets of one GCP project to <see cref="IConfiguration"/>.</summary>
public sealed class GoogleSecretManagerConfigurationSource(GoogleSecretManagerOptions options) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new GoogleSecretManagerConfigurationProvider(options);
}

/// <summary>
/// Loads every secret whose id starts with <c>{Prefix}--</c> (latest version) once at startup. Credentials
/// come from Application Default Credentials: <c>gcloud auth application-default login</c> on a laptop,
/// the workload's service account when deployed. That identity needs
/// <c>roles/secretmanager.secretAccessor</c> and <c>roles/secretmanager.viewer</c> (to list) on the project.
/// <para>
/// Any failure stops startup. Running on with missing secrets would only move the failure to the first
/// request — and to a less obvious error than "could not read secrets from project X".
/// </para>
/// </summary>
public sealed class GoogleSecretManagerConfigurationProvider(GoogleSecretManagerOptions options) : ConfigurationProvider
{
    private const string Separator = "--";

    public override void Load()
    {
        var prefix = options.Prefix + Separator;
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var client = SecretManagerServiceClient.Create();

            // Core's secrets this service shares (one Mongo cluster, one master key) — read by name, not copied. Loaded
            // first, so a secret under this service's own prefix with the same key would win.
            foreach (var id in options.SharedSecrets)
            {
                var cut = id.IndexOf(Separator, StringComparison.Ordinal);
                if (cut <= 0)
                    throw new InvalidOperationException($"Shared secret '{id}' must look like '<prefix>--<Section>--<Key>'.");
                // A shared secret not created yet, or with no enabled version (Core's Gmail / Microsoft client secrets
                // before they are added): its setting stays empty and the feature says it is not set up — the service
                // must still start (Core crash-looped on exactly this, 2026-10-17).
                if (Access(client, id) is not { } sharedValue) continue;
                data[id[(cut + Separator.Length)..].Replace(Separator, ConfigurationPath.KeyDelimiter)] = sharedValue;
            }

            foreach (var secret in client.ListSecrets(new ProjectName(options.ProjectId)))
            {
                var id = secret.SecretName.SecretId;
                if (!id.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                if (Access(client, id) is not { } value) continue;
                var key = id[prefix.Length..].Replace(Separator, ConfigurationPath.KeyDelimiter);
                data[key] = value;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not load secrets from Google Secret Manager (project '{options.ProjectId}', prefix '{prefix}'). " +
                "Locally, run: gcloud auth application-default login", ex);
        }

        Data = data;
    }

    private string? Access(SecretManagerServiceClient client, string id)
    {
        try
        {
            return client.AccessSecretVersion(new SecretVersionName(options.ProjectId, id, "latest")).Payload.Data.ToStringUtf8();
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound or StatusCode.FailedPrecondition)
        {
            Console.Error.WriteLine($"Secret Manager: '{id}' does not exist or has no enabled version yet; its setting stays empty.");
            return null;
        }
    }
}
