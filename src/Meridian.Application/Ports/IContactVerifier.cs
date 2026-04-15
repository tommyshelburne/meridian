using Meridian.Application.Common;

namespace Meridian.Application.Ports;

public interface IContactVerifier
{
    Task<ServiceResult<bool>> VerifyEmailAsync(string emailAddress, CancellationToken ct);
}
