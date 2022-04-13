namespace Firefox.Fluent.Plugin;

public class FirefoxProfile
{
    public bool IsEnabled { get; set; } = true;
    public string Path { get; init; }

    private bool Equals(FirefoxProfile other)
    {
        return Path == other.Path && IsEnabled == other.IsEnabled;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((FirefoxProfile) obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Path);
    }
}