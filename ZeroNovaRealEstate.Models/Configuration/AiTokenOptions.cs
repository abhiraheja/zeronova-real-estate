namespace ZeroNovaRealEstate.Models.Configuration;

/// <summary>
/// Config section <c>"AiTokens"</c> (Core T23): AI Executives' tokens, which zeronova-core issues and signs (ES256). This
/// service trusts them beside Supabase's. <see cref="JwksUrl"/> empty = not trusted here: only people's tokens work, as
/// before T23.
/// </summary>
public sealed class AiTokenOptions
{
    public const string SectionName = "AiTokens";

    /// <summary>Core's AI issuer — must equal Core's <c>AiTokens:Issuer</c>.</summary>
    public string Issuer { get; set; } = "https://core.zrnova.com/ai";

    public string Audience { get; set; } = "zeronova-services";

    /// <summary>Core's public keys, e.g. <c>http://zeronova-core:8080/.well-known/ai-jwks.json</c> (cluster-internal).</summary>
    public string JwksUrl { get; set; } = string.Empty;
}
