using Meridian.Domain.Common;

namespace Meridian.Application.Crm;

// Round-trip carrier for the OAuth `state` query parameter. Created on the
// /connect leg, decoded on the /callback leg. Carries everything the callback
// needs to validate the round-trip and to resume the user's portal flow.
public record CrmOAuthState(
    Guid TenantId,
    CrmProvider Provider,
    string ReturnUrl,
    DateTimeOffset ExpiresAt,
    string Nonce);
