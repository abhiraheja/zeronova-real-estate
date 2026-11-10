using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ZeroNovaRealEstate.Domain.Base;
using ZeroNovaRealEstate.Domain.Helper;
using ZeroNovaRealEstate.Models.Videos;

namespace ZeroNovaRealEstate.Domain.Documents;

/// <summary>
/// A video of a property or a project (D36), kept in zeronova-media (client <c>real-estate</c>, account = the company,
/// <see cref="ExternalRef"/> <c>property:&lt;id&gt;</c> / <c>project:&lt;id&gt;</c>). Its own collection, so an upload or a
/// media event never clashes with an edit of the listing. Removed (soft) with the video or the listing.
/// </summary>
[DocumentName("re_videos")]
public sealed class EstateVideoDocument : CompanyDocumentBase
{
    [BsonElement("listingKind")] [BsonRepresentation(BsonType.String)] public EstateListingKind ListingKind { get; set; }
    [BsonElement("listingId")] public required string ListingId { get; set; }

    /// <summary>zeronova-media's id of the video.</summary>
    [BsonElement("videoId")] public required string VideoId { get; set; }

    [BsonElement("externalRef")] public required string ExternalRef { get; set; }
    [BsonElement("title")] public required string Title { get; set; }
    [BsonElement("status")] [BsonRepresentation(BsonType.String)] public EstateVideoStatus Status { get; set; }
    [BsonElement("failureReason")] public string? FailureReason { get; set; }
    [BsonElement("durationSeconds")] public double? DurationSeconds { get; set; }
    [BsonElement("posterUrl")] public string? PosterUrl { get; set; }
    [BsonElement("sortOrder")] public int SortOrder { get; set; }
    [BsonElement("addedAt")] public DateTime AddedAt { get; set; }
    [BsonElement("fileName")] public string? FileName { get; set; }
    [BsonElement("sizeBytes")] public long SizeBytes { get; set; }

    /// <summary>When the last media event applied happened (its <c>At</c>): an older event, delivered late, is not applied.</summary>
    [BsonElement("statusAt")] public DateTime? StatusAt { get; set; }

    public static string RefOf(EstateListingKind kind, string listingId) => $"{(kind == EstateListingKind.Project ? "project" : "property")}:{listingId}";
}
