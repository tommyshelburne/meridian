namespace Meridian.Domain.Common;

public class Agency
{
    public string Name { get; private set; } = null!;
    public AgencyType Type { get; private set; }
    public string? State { get; private set; }

    private Agency() { }

    public static Agency Create(string name, AgencyType type, string? state = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Agency name is required.", nameof(name));

        return new Agency
        {
            Name = name,
            Type = type,
            State = state
        };
    }
}
