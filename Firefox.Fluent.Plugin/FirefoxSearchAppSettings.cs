using Blast.API.Settings;
using Blast.Core.Objects;

namespace Firefox.Fluent.Plugin;

public class FirefoxSearchAppSettings : SearchApplicationSettingsPage
{
    public FirefoxSearchAppSettings(SearchApplicationInfo searchApplicationInfo) : base(searchApplicationInfo)
    {
    }

    [Setting(Name = "Firefox profiles", Description = "Configure profiles to search in Firefox",
        IconGlyph = "\uEC6C", SettingManagerType = typeof(FirefoxProfilesCollectionSettingManager))]
    public List<FirefoxProfile> FirefoxProfiles { get; set; } =
        FirefoxProfilesCollectionSettingManager.GetDefaultProfilePaths();

    public class FirefoxProfilesCollectionSettingManager : CollectionSettingManager<FirefoxProfile>
    {
        public override IList<FirefoxProfile> GetDefaultValue()
        {
            return GetDefaultProfilePaths();
        }

        internal static List<FirefoxProfile> GetDefaultProfilePaths()
        {
            string profilesPath = Environment.ExpandEnvironmentVariables(@"%appdata%\Mozilla\Firefox\Profiles\");
            return Directory.EnumerateFiles(profilesPath, "places.sqlite", SearchOption.AllDirectories).Select(p =>
            {
                DirectoryInfo directoryInfo = Directory.GetParent(p)!;
                string profileName = directoryInfo.Name;
                return new FirefoxProfile
                {
                    Name = profileName,
                    Path = directoryInfo.FullName,
                    IsEnabled = profileName.Contains("default")
                };
            }).ToList();
        }
    }
}