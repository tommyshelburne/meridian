using Meridian.Application.Common;
using Meridian.Domain.Common;

namespace Meridian.Application.Ports;

public record ToneProfile(string Formality, string Emphasis, string CallToActionStyle);

public interface IToneAdaptor
{
    Task<ServiceResult<ToneProfile>> GetToneProfileAsync(AgencyType agencyType, Guid? contactId,
        CancellationToken ct);
}
