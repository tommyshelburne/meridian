using System.Text.Json;
using Meridian.Application.Crm;
using Meridian.Application.Ports;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Crm;

// State token implementation. Wraps ASP.NET Core Data Protection with a
// dedicated purpose so a leak / replay of a CrmConnection-stored secret can't
// be used as a state token, and vice-versa.
public class DataProtectionCrmOAuthStateProtector : ICrmOAuthStateProtector
{
    private const string Purpose = "Meridian.Crm.OAuth.State.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<DataProtectionCrmOAuthStateProtector> _logger;

    public DataProtectionCrmOAuthStateProtector(
        IDataProtectionProvider provider,
        ILogger<DataProtectionCrmOAuthStateProtector> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    public string Protect(CrmOAuthState state)
    {
        var json = JsonSerializer.Serialize(state);
        return _protector.Protect(json);
    }

    public bool TryUnprotect(string token, out CrmOAuthState? state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var json = _protector.Unprotect(token);
            state = JsonSerializer.Deserialize<CrmOAuthState>(json);
            return state is not null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to unprotect CRM OAuth state token");
            return false;
        }
    }
}
