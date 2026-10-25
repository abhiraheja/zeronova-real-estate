namespace ZeroNovaRealEstate.Models.Configuration;

/// <summary>
/// Config section <c>"GoogleSecretManager"</c> — where secrets come from. One GCP project per environment (dev =
/// <c>wealthgrow-qa</c>); a secret named <c>{Prefix}--Mongo--ConnectionString</c> becomes config key
/// <c>Mongo:ConnectionString</c>. The prefix keeps this service's secrets apart from Core's in the same project.
/// </summary>
public sealed class GoogleSecretManagerOptions
{
    public const string SectionName = "GoogleSecretManager";

    /// <summary>GCP project id. Empty = Secret Manager is not used (no secrets are loaded).</summary>
    public string ProjectId { get; set; } = string.Empty;

    public string Prefix { get; set; } = "zeronovarealestate";

    /// <summary>
    /// Other services' secrets this one uses as they are — full ids, e.g. <c>zeronovacore--Mongo--ConnectionString</c> →
    /// config <c>Mongo:ConnectionString</c>. The Mongo connection is Core's: one value, one place.
    /// </summary>
    public List<string> SharedSecrets { get; set; } = [];
}

/// <summary>
/// Config section <c>"Mongo"</c>. Core's cluster, this service's own database (<see cref="DatabaseName"/>, database per
/// service).
/// </summary>
public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    /// <summary>Secret — Core's <c>zeronovacore--Mongo--ConnectionString</c>, read as a shared secret.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    public string DatabaseName { get; set; } = string.Empty;
}

/// <summary>Config section <c>"Supabase"</c> — validates the same access tokens Core does (same project, same claims).</summary>
public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    public string Url { get; set; } = string.Empty;

    public string Audience { get; set; } = "authenticated";

    public string Issuer => $"{Url.TrimEnd('/')}/auth/v1";
}

/// <summary>Config section <c>"Graphql"</c>.</summary>
public sealed class GraphqlOptions
{
    public const string SectionName = "Graphql";

    /// <summary>Schema introspection outside Development (needed by the gateway). Off by default.</summary>
    public bool AllowIntrospection { get; set; }

    public bool IncludeExceptionDetails { get; set; }

    /// <summary>Per-operation execution timeout. 0 keeps HotChocolate's default (30s).</summary>
    public int ExecutionTimeoutSeconds { get; set; }
}

/// <summary>
/// Config section <c>"Documents"</c> — zeronova-document-helper through its service door: this service's key
/// (<see cref="ServiceKey"/>, secret <c>zeronovarealestate--Documents--ServiceKey</c>, the same value as
/// <c>zeronovadocs--ServiceKeys--realestate</c>) and the company it works for. Property photos and papers live there.
/// </summary>
public sealed class DocumentsOptions
{
    public const string SectionName = "Documents";

    public string Url { get; set; } = string.Empty;

    public string ServiceKey { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(ServiceKey);
}

/// <summary>
/// Config section <c>"Encryption"</c> — Core's master key (base64, 32 bytes, from Secret Manager): owners' and buyers'
/// Aadhaar and PAN are sealed with it (D11). A missing or placeholder key leaves them unsaved, never the service down.
/// </summary>
public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";

    public string Key { get; set; } = string.Empty;
}

/// <summary>Config section <c>"Share"</c> — the app's public address; a share link is <c>{BaseUrl}/p/{token}</c>.</summary>
public sealed class ShareOptions
{
    public const string SectionName = "Share";

    public string BaseUrl { get; set; } = "https://app.zeronova.in";
}
