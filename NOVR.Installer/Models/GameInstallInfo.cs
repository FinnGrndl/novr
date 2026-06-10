using NOVR.Installer.Services;

namespace NOVR.Installer.Models;

public sealed record GameInstallInfo(
    string GameDir,
    bool IsValid,
    bool HasBepInEx,
    InstallState ModState,
    string? Message)
{
    public string ManagedDir => Path.Combine(GameDir, "NuclearOption_Data", "Managed");
    public string BepInExDir => Path.Combine(GameDir, "BepInEx");
    public string BepInExCoreDir => Path.Combine(BepInExDir, "core");
    public string PluginDir => Path.Combine(BepInExDir, "plugins", InstallerConstants.ModFolderName);
    public string PatcherDir => Path.Combine(BepInExDir, "patchers", InstallerConstants.ModFolderName);
    public string VersionFile => Path.Combine(PluginDir, InstallerConstants.VersionFileName);
    public ReleaseVersion Version
    {
        get
        {
            if (!File.Exists(VersionFile))
            {
                return (ReleaseVersion)"0.0.0";
            }

            return ReleaseVersion.TryParse(File.ReadAllText(VersionFile), out var version)
                ? version
                : (ReleaseVersion)"0.0.0";
        }
    }
}
