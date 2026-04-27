using Meridian.Application.Crm;

namespace Meridian.Application.Ports;

public interface ICrmOAuthStateProtector
{
    string Protect(CrmOAuthState state);
    bool TryUnprotect(string token, out CrmOAuthState? state);
}
