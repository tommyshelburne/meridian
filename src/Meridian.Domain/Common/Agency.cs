namespace Meridian.Domain.Common;

public class Agency
{
    public string Name { get; private set; } = null!;
    public AgencyType Type { get; private set; }
    public int Tier { get; private set; }
    public string? State { get; private set; }

    private Agency() { }

    public static Agency Create(string name, AgencyType type, int tier, string? state = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Agency name is required.", nameof(name));

        if (tier < 0 || tier > 3)
            throw new ArgumentOutOfRangeException(nameof(tier), "Agency tier must be between 0 and 3.");

        return new Agency
        {
            Name = name,
            Type = type,
            Tier = tier,
            State = state
        };
    }
}
