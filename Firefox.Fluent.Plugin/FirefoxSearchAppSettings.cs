using Blast.API.Settings;
using Blast.Core.Objects;

namespace Firefox.Fluent.Plugin;

public class FirefoxSearchAppSettings : SearchApplicationSettingsPage
{
    private readonly List<FirefoxProfile> _firefoxProfiles =
        FirefoxProfilesCollectionSettingManager.GetDefaultProfilePaths();

    public FirefoxSearchAppSettings(SearchApplicationInfo searchApplicationInfo) : base(searchApplicationInfo)
    {
    }

    [Setting(Name = nameof(FirefoxProfiles), DisplayedName = "Firefox profiles",
        Description = "Configure profiles to search in Firefox", IconGlyph = "\uEC6C",
        SettingManagerType = typeof(FirefoxProfilesCollectionSettingManager))]
    public List<FirefoxProfile> FirefoxProfiles
    {
        get => _firefoxProfiles;
        set
        {
            foreach (FirefoxProfile firefoxProfile in value)
            {
                FirefoxProfile? firstOrDefault = _firefoxProfiles
                    .FirstOrDefault(s => s.Path.Equals(firefoxProfile.Path, StringComparison.OrdinalIgnoreCase));
                if (firstOrDefault == null)
                    _firefoxProfiles.Add(firefoxProfile);
                else
                    firstOrDefault.IsEnabled = firefoxProfile.IsEnabled;
            }
        }
    }

    public class FirefoxProfilesCollectionSettingManager : CollectionSettingManager<FirefoxProfile>
    {
        public override IList<FirefoxProfile> GetDefaultValue()
        {
            return GetDefaultProfilePaths();
        }

        internal static List<FirefoxProfile> GetDefaultProfilePaths()
        {
            string profilesPath = Environment.ExpandEnvironmentVariables(@"%appdata%\Mozilla\Firefox\Profiles\");
            if (!Directory.Exists(profilesPath))
                return new List<FirefoxProfile>();

            return Directory.EnumerateFiles(profilesPath, "places.sqlite", SearchOption.AllDirectories).Select(p =>
            {
                DirectoryInfo directoryInfo = Directory.GetParent(p)!;
                string profileName = directoryInfo.Name;
                return new FirefoxProfile
                {
                    Path = directoryInfo.FullName,
                    IsEnabled = profileName.Contains("default")
                };
            }).ToList();
        }
    }
}