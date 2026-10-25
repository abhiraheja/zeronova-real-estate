using ZeroNovaRealEstate.Domain.Common;

namespace ZeroNovaRealEstate.Domain.Base;

public interface IDocumentBase
{
    string Id { get; set; }
    ChangedByModel? CreatedBy { get; set; }
    ChangedByModel? UpdatedBy { get; set; }
    bool IsDeleted { get; set; }
    long Version { get; set; }
}
