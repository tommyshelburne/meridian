namespace Meridian.Application.Pricing;

[Flags]
public enum IntegrationComplexity
{
    None = 0,
    CustomAdapters = 1,
    Sso = 2,
    IsolatedInfra = 4
}
