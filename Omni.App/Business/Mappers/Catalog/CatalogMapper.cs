using Omni.App.Entities.Models;
using Omni.App.Entities.Responses.Catalog;

namespace Omni.App.Business.Mappers.Catalog;

public static class CatalogMapper
{
    public static ServiceResponse ToResponse(Service s) =>
        new(s.Id, s.ClientId, s.Name, s.Code, s.Description, s.BookingDuration, s.IsActive);
}
