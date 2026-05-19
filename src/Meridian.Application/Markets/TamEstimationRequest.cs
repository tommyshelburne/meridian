using Meridian.Domain.Markets;

namespace Meridian.Application.Markets;

/// <summary>
/// Input for a TAM estimation. Identifies the slice of the federal procurement
/// market a prospective tenant could compete in.
/// </summary>
/// <param name="NaicsCodes">Six-digit NAICS codes the prospect operates in.</param>
/// <param name="TargetStates">
/// Two-letter US state codes for place of performance. An empty list means
/// nationwide (no state restriction).
/// </param>
/// <param name="SetAside">Set-aside category the prospect qualifies for.</param>
public sealed record TamEstimationRequest(
    IReadOnlyList<string> NaicsCodes,
    IReadOnlyList<string> TargetStates,
    SetAsideCategory SetAside);
