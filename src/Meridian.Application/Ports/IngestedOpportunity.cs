using Meridian.Domain.Common;

namespace Meridian.Application.Ports;

public record IngestedOpportunity(
    string ExternalId,
    string Title,
    string Description,
    string AgencyName,
    AgencyType AgencyType,
    string? AgencyState,
    DateTimeOffset PostedDate,
    DateTimeOffset? ResponseDeadline,
    string? NaicsCode,
    decimal? EstimatedValue,
    ProcurementVehicle? ProcurementVehicle,
    IReadOnlyDictionary<string, string>? Metadata = null);
