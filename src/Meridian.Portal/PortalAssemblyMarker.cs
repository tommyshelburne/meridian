namespace Meridian.Portal;

// Unambiguous marker for test factories. WebApplicationFactory uses the type
// only to locate the target assembly; any public type in this assembly works.
// Bare `Program` collides with the Worker's auto-generated Program now that
// both projects use Microsoft.NET.Sdk.Web.
public sealed class PortalAssemblyMarker
{
}
