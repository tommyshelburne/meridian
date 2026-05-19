namespace Meridian.Domain.Markets;

/// <summary>
/// Federal contracting small-business set-aside categories. Determines which
/// socioeconomic program (if any) a procurement is restricted to.
/// </summary>
public enum SetAsideCategory
{
    /// <summary>Full and open competition — no set-aside restriction.</summary>
    None,

    /// <summary>SBA 8(a) Business Development program.</summary>
    EightA,

    /// <summary>Service-Disabled Veteran-Owned Small Business.</summary>
    Sdvosb,

    /// <summary>Women-Owned Small Business.</summary>
    Wosb,

    /// <summary>HUBZone (Historically Underutilized Business Zone) program.</summary>
    HubZone
}
