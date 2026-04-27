namespace Meridian.Infrastructure.Crm.Pipedrive;

public class PipedriveOptions
{
    public const string SectionName = "Pipedrive";

    public string BaseUrl { get; set; } = "https://api.pipedrive.com/v1/";
}
