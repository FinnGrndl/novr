using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NOVR.Installer.Models;
using NOVR.Installer.Services;

namespace NOVR.Installer.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly GameLocator _gameLocator = new();
    private readonly GitHubReleaseClient _releaseClient = new();
    private readonly FileSystemInstaller _installer = new();
    private readonly ProtonPrefixService _protonPrefixService = new();

    private GameInstallInfo? _gameInfo;
    private NovrRelease? _selectedRelease;
    private string _gamePath = string.Empty;
    private string _status = "Ready.";
    private string _details = string.Empty;
    private string _releaseStatus = "Releases have not loaded yet.";
    private string _primaryActionText = "Install";
    private bool _isBusy;
    private bool _isInstalledMode;
    private bool _removeBepInExOnFinish;
    private bool _showUninstallFinish;
    private Func<Task<string?>>? _browseForFolderAsync;

    public MainWindowViewModel()
    {
        PrimaryActionCommand = new AsyncCommand(PrimaryActionAsync, CanInstallSelectedRelease);
        RepairUpdateCommand = new AsyncCommand(RepairUpdateAsync, CanInstallSelectedRelease);
        UninstallCommand = new AsyncCommand(UninstallAsync, () => !IsBusy && GameInfo?.IsValid == true);
        FinishUninstallCommand = new AsyncCommand(FinishUninstallAsync, () => !IsBusy && GameInfo?.IsValid == true);
        RescanCommand = new AsyncCommand(ScanAsync, () => !IsBusy);
        ReloadReleasesCommand = new AsyncCommand(LoadReleasesAsync, () => !IsBusy);
        BrowseCommand = new AsyncCommand(BrowseAsync, () => !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<NovrRelease> AvailableReleases { get; } = new();

    public string GamePath
    {
        get => _gamePath;
        set
        {
            if (SetField(ref _gamePath, value))
            {
                _gameInfo = string.IsNullOrWhiteSpace(value) ? null : _gameLocator.Inspect(value);
                RefreshMode();
                ReleaseStatus = BuildReleaseStatus(GameInfo);
                Details = BuildDetails(GameInfo);
            }
        }
    }

    public NovrRelease? SelectedRelease
    {
        get => _selectedRelease;
        set
        {
            if (SetField(ref _selectedRelease, value))
            {
                RefreshMode();
                ReleaseStatus = BuildReleaseStatus(GameInfo);
                Details = BuildDetails(GameInfo);
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public string Details
    {
        get => _details;
        private set => SetField(ref _details, value);
    }

    public string ReleaseStatus
    {
        get => _releaseStatus;
        private set => SetField(ref _releaseStatus, value);
    }

    public string PrimaryActionText
    {
        get => _primaryActionText;
        private set => SetField(ref _primaryActionText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsInstalledMode
    {
        get => _isInstalledMode;
        private set => SetField(ref _isInstalledMode, value);
    }

    public bool IsInstallMode => !IsInstalledMode && !ShowUninstallFinish;

    public bool ShowUninstallFinish
    {
        get => _showUninstallFinish;
        private set
        {
            if (SetField(ref _showUninstallFinish, value))
            {
                OnPropertyChanged(nameof(IsInstallMode));
            }
        }
    }

    public bool RemoveBepInExOnFinish
    {
        get => _removeBepInExOnFinish;
        set => SetField(ref _removeBepInExOnFinish, value);
    }

    public GameInstallInfo? GameInfo
    {
        get => _gameInfo;
        private set
        {
            if (SetField(ref _gameInfo, value))
            {
                GamePath = value?.GameDir ?? GamePath;
                RefreshMode();
                ReleaseStatus = BuildReleaseStatus(value);
            }
        }
    }

    public ICommand PrimaryActionCommand { get; }
    public ICommand RepairUpdateCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand FinishUninstallCommand { get; }
    public ICommand RescanCommand { get; }
    public ICommand ReloadReleasesCommand { get; }
    public ICommand BrowseCommand { get; }

    public void SetFolderBrowser(Func<Task<string?>> browseForFolderAsync)
    {
        _browseForFolderAsync = browseForFolderAsync;
    }

    public async Task InitializeAsync()
    {
        await ScanAsync();
        await LoadReleasesAsync();
    }

    private Task LoadReleasesAsync()
    {
        return RunBusyAsync(async () =>
        {
            var progress = new Progress<string>(message => Status = message);
            var releases = await _releaseClient.GetNovrReleasesAsync(progress, CancellationToken.None);

            AvailableReleases.Clear();
            foreach (var release in releases)
            {
                AvailableReleases.Add(release);
            }

            SelectedRelease = releases.FirstOrDefault(release => !release.IsPrerelease) ?? releases[0];
            Status = $"Loaded {releases.Count} release(s) from {InstallerConstants.GitHubOwner}/{InstallerConstants.GitHubRepo}.";
            ReleaseStatus = BuildReleaseStatus(GameInfo);
            Details = BuildDetails(GameInfo);
        });
    }

    private Task ScanAsync()
    {
        return RunBusyAsync(() =>
        {
            ShowUninstallFinish = false;
            RemoveBepInExOnFinish = false;
            Status = "Searching for Nuclear Option...";
            var found = _gameLocator.FindGame();
            if (found is null)
            {
                GameInfo = null;
                GamePath = string.Empty;
                Status = "Nuclear Option was not found automatically.";
                Details = "Choose the game folder manually. It must contain NuclearOption_Data/Managed.";
                ReleaseStatus = BuildReleaseStatus(null);
                PrimaryActionText = "Install";
            }
            else
            {
                GameInfo = found;
                Status = found.ModState == InstallState.FullyInstalled
                    ? $"NOVR installed version: {found.Version}"
                    : "Nuclear Option found.";
                Details = BuildDetails(found);
                ReleaseStatus = BuildReleaseStatus(found);
            }

            return Task.CompletedTask;
        });
    }

    private async Task BrowseAsync()
    {
        if (_browseForFolderAsync is null)
        {
            Status = "Folder picker is unavailable. Paste the Nuclear Option path into the box.";
            return;
        }

        var path = await _browseForFolderAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            GamePath = path;
            Status = "Selected game folder.";
            Details = BuildDetails(GameInfo);
            ReleaseStatus = BuildReleaseStatus(GameInfo);
        }
    }

    private Task PrimaryActionAsync()
    {
        return InstallOrUpdateAsync(GetSelectedInstallType());
    }

    private Task RepairUpdateAsync()
    {
        return InstallOrUpdateAsync(GetSelectedInstallType());
    }

    private async Task InstallOrUpdateAsync(InstallType installType)
    {
        if (SelectedRelease is null)
        {
            Status = "Select a NOVR release first.";
            Details = "The release list is loaded from GitHub. Try refreshing releases if the list is empty.";
            return;
        }

        var selectedRelease = SelectedRelease;
        var info = ValidateSelectedGame();
        if (info is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var progress = new Progress<string>(message => Status = message);
            var tempDir = Path.Combine(Path.GetTempPath(), "novr-installer-" + Guid.NewGuid().ToString("N"));

            try
            {
                if (!info.HasBepInEx)
                {
                    var bepInExZip = await _releaseClient.DownloadLatestBepInEx5Async(tempDir, progress, CancellationToken.None);
                    await _installer.InstallBepInExAsync(info, bepInExZip, progress, CancellationToken.None);
                    info = _gameLocator.Inspect(info.GameDir);
                }

                var novrZip = await _releaseClient.DownloadNovrReleaseAsync(
                    selectedRelease,
                    tempDir,
                    progress,
                    CancellationToken.None);

                await _installer.InstallOrUpdateNovrAsync(
                    info,
                    novrZip,
                    selectedRelease.Version.ToString(),
                    progress,
                    CancellationToken.None);

                var protonMessage = await _protonPrefixService.TryConfigureWinHttpOverrideAsync(info, progress, CancellationToken.None);

                GameInfo = _gameLocator.Inspect(info.GameDir);
                Status = $"{installType} {selectedRelease.TagName} complete.";
                Details = BuildDetails(GameInfo) + Environment.NewLine + protonMessage;
                ReleaseStatus = BuildReleaseStatus(GameInfo);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    private async Task UninstallAsync()
    {
        var info = ValidateSelectedGame();
        if (info is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            var progress = new Progress<string>(message => Status = message);
            await _installer.UninstallNovrAsync(info, progress, CancellationToken.None);
            GameInfo = _gameLocator.Inspect(info.GameDir);
            ShowUninstallFinish = true;
            IsInstalledMode = false;
            Status = "NOVR was uninstalled.";
            Details = "Optionally remove BepInEx, then click Finish to return to the install screen.";
            ReleaseStatus = BuildReleaseStatus(GameInfo);
        });
    }

    private async Task FinishUninstallAsync()
    {
        var info = ValidateSelectedGame();
        if (info is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            if (RemoveBepInExOnFinish)
            {
                var progress = new Progress<string>(message => Status = message);
                await _installer.UninstallBepInExAsync(info, progress, CancellationToken.None);
            }

            ShowUninstallFinish = false;
            GameInfo = _gameLocator.Inspect(info.GameDir);
            Status = "Ready to install.";
            Details = BuildDetails(GameInfo);
            ReleaseStatus = BuildReleaseStatus(GameInfo);
        });
    }

    private GameInstallInfo? ValidateSelectedGame()
    {
        if (string.IsNullOrWhiteSpace(GamePath))
        {
            Status = "Select your Nuclear Option folder first.";
            Details = "The selected path must contain NuclearOption_Data/Managed.";
            return null;
        }

        var info = _gameLocator.Inspect(GamePath);
        if (!info.IsValid)
        {
            Status = "Invalid Nuclear Option folder.";
            Details = info.Message ?? "The selected path must contain NuclearOption_Data/Managed.";
            GameInfo = info;
            return null;
        }

        GameInfo = info;
        return info;
    }

    private void RefreshMode()
    {
        if (ShowUninstallFinish)
        {
            OnPropertyChanged(nameof(IsInstallMode));
            RaiseCommandStates();
            return;
        }

        IsInstalledMode = GameInfo?.ModState == InstallState.FullyInstalled;
        PrimaryActionText = GetPrimaryActionText();
        OnPropertyChanged(nameof(IsInstallMode));
        RaiseCommandStates();
    }

    private string GetPrimaryActionText()
    {
        if (SelectedRelease is null)
        {
            return "Install";
        }

        return GetSelectedInstallType() switch
        {
            InstallType.Update => "Update",
            InstallType.Repair => "Repair",
            InstallType.Downgrade => "Downgrade",
            _ => "Install"
        };
    }

    private InstallType GetSelectedInstallType()
    {
        if (GameInfo?.ModState == InstallState.PartiallyInstalled)
        {
            return InstallType.Repair;
        }

        if (GameInfo?.ModState != InstallState.FullyInstalled || SelectedRelease is null)
        {
            return InstallType.Install;
        }

        if (GameInfo.Version < SelectedRelease.Version)
        {
            return InstallType.Update;
        }

        if (GameInfo.Version > SelectedRelease.Version)
        {
            return InstallType.Downgrade;
        }

        return InstallType.Repair;
    }

    private string BuildDetails(GameInstallInfo? info)
    {
        var lines = new List<string>();

        if (info is null)
        {
            lines.Add("No game folder selected.");
        }
        else
        {
            lines.Add($"Game: {info.GameDir}");
            lines.Add($"BepInEx: {(info.HasBepInEx ? "installed" : "not installed")}");
            lines.Add($"NOVR: {info.ModState}");
        }

        if (SelectedRelease is not null)
        {
            lines.Add($"Release: {SelectedRelease.TagName}");
            lines.Add($"Release asset: {SelectedRelease.AssetName}");
            lines.Add($"Source: github.com/{InstallerConstants.GitHubOwner}/{InstallerConstants.GitHubRepo}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string BuildReleaseStatus(GameInstallInfo? info)
    {
        if (SelectedRelease is null)
        {
            return "No NOVR release selected.";
        }

        if (info?.ModState != InstallState.FullyInstalled)
        {
            return $"Selected release: {SelectedRelease.TagName}.";
        }

        return info.Version == SelectedRelease.Version
            ? $"Selected release: {SelectedRelease.TagName}. Installed version matches."
            : $"Selected release: {SelectedRelease.TagName}. Installed: {info.Version}.";
    }

    private bool CanInstallSelectedRelease()
    {
        return !IsBusy && SelectedRelease is not null && GameInfo?.IsValid == true;
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (Exception ex)
        {
            Status = "Operation failed.";
            Details = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RaiseCommandStates()
    {
        (PrimaryActionCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (RepairUpdateCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (UninstallCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (FinishUninstallCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (RescanCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (ReloadReleasesCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (BrowseCommand as AsyncCommand)?.RaiseCanExecuteChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
