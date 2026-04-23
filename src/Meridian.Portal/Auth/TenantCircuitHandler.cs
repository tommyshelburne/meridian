using Meridian.Domain.Tenants;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Meridian.Portal.Auth;

/// <summary>
/// Per-circuit hook that sets the scoped TenantContext from the authenticated
/// user's tenant claim. The HTTP-pipeline TenantClaimMiddleware only runs for
/// non-interactive requests; once a Blazor Server circuit is open, all
/// OnInitializedAsync runs use the circuit's DI scope where the middleware
/// never fires. Without this handler, every EF query filter
/// (TenantId == _tenantContext.TenantId) sees Guid.Empty and returns no rows.
/// </summary>
public class TenantCircuitHandler : CircuitHandler
{
    private readonly AuthenticationStateProvider _authState;
    private readonly ITenantContext _tenantContext;

    public TenantCircuitHandler(AuthenticationStateProvider authState, ITenantContext tenantContext)
    {
        _authState = authState;
        _tenantContext = tenantContext;
    }

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken ct)
    {
        await SyncTenantAsync();
        _authState.AuthenticationStateChanged += OnAuthChanged;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken ct)
    {
        _authState.AuthenticationStateChanged -= OnAuthChanged;
        return Task.CompletedTask;
    }

    private void OnAuthChanged(Task<AuthenticationState> task)
    {
        _ = SyncTenantAsync();
    }

    private async Task SyncTenantAsync()
    {
        var state = await _authState.GetAuthenticationStateAsync();
        var claim = state.User.FindFirst(ClaimsBuilder.TenantIdClaim)?.Value;
        if (Guid.TryParse(claim, out var tenantId))
            _tenantContext.SetTenant(tenantId);
    }
}
