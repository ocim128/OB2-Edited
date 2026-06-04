using Flux.Core.Models.Settings;
using Flux.Core.Services;
using Flux.Native.Helpers;

using Flux.Native.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Flux.Native.ViewModels.Base;

namespace Flux.Native.ViewModels.Settings
{
    public class OBSettingsViewModel : ViewModelBase
    {
        private readonly FluxSettingsService service;
        private readonly IThemeService themeService;
        private GeneralSettings General => service.Settings.GeneralSettings;
        private RemoteSettings Remote => service.Settings.RemoteSettings;
        private CustomizationSettings Customization => service.Settings.CustomizationSettings;

        public OBSettingsViewModel(FluxSettingsService fluxSettingsService, IThemeService themeService)
        {
            service = fluxSettingsService ?? throw new ArgumentNullException(nameof(fluxSettingsService));
            this.themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
            CreateCollections();
        }

        public ConfigSection ConfigSectionOnLoad
        {
            get => General.ConfigSectionOnLoad;
            set
            {
                General.ConfigSectionOnLoad = value;
                OnPropertyChanged();
            }
        }

        public bool AutoSetRecommendedBots
        {
            get => General.AutoSetRecommendedBots;
            set
            {
                General.AutoSetRecommendedBots = value;
                OnPropertyChanged();
            }
        }

        public bool WarnConfigNotSaved
        {
            get => General.WarnConfigNotSaved;
            set
            {
                General.WarnConfigNotSaved = value;
                OnPropertyChanged();
            }
        }

        public string DefaultAuthor
        {
            get => General.DefaultAuthor;
            set
            {
                General.DefaultAuthor = value;
                OnPropertyChanged();
            }
        }

        public bool EnableJobLogging
        {
            get => General.EnableJobLogging;
            set
            {
                General.EnableJobLogging = value;
                OnPropertyChanged();
            }
        }

        public int LogBufferSize
        {
            get => General.LogBufferSize;
            set
            {
                General.LogBufferSize = value;
                OnPropertyChanged();
            }
        }

        public int AutoSaveInterval
        {
            get => General.AutoSaveInterval;
            set
            {
                General.AutoSaveInterval = value;
                OnPropertyChanged();
            }
        }

        public JobDisplayMode DefaultJobDisplayMode
        {
            get => General.DefaultJobDisplayMode;
            set
            {
                General.DefaultJobDisplayMode = value;
                OnPropertyChanged();
            }
        }

        public bool GroupCapturesInDebugger
        {
            get => General.GroupCapturesInDebugger;
            set
            {
                General.GroupCapturesInDebugger = value;
                OnPropertyChanged();
            }
        }

        public bool IgnoreWordlistNameOnHitsDedupe
        {
            get => General.IgnoreWordlistNameOnHitsDedupe;
            set
            {
                General.IgnoreWordlistNameOnHitsDedupe = value;
                OnPropertyChanged();
            }
        }

        public bool PerformConfirmationOnDestructiveActions
        {
            get => General.PerformConfirmationOnDestructiveActions;
            set
            {
                General.PerformConfirmationOnDestructiveActions = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<ProxyCheckTarget> proxyCheckTargetsCollection;
        public ObservableCollection<ProxyCheckTarget> ProxyCheckTargetsCollection
        {
            get => proxyCheckTargetsCollection;
            set
            {
                proxyCheckTargetsCollection = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<CustomSnippet> customSnippetsCollection;
        public ObservableCollection<CustomSnippet> CustomSnippetsCollection
        {
            get => customSnippetsCollection;
            set
            {
                customSnippetsCollection = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<RemoteConfigsEndpoint> remoteConfigsEndointsCollection;
        public ObservableCollection<RemoteConfigsEndpoint> RemoteConfigsEndpointsCollection
        {
            get => remoteConfigsEndointsCollection;
            set
            {
                remoteConfigsEndointsCollection = value;
                OnPropertyChanged();
            }
        }

        public bool PlaySoundOnHit
        {
            get => Customization.PlaySoundOnHit;
            set
            {
                Customization.PlaySoundOnHit = value;
                OnPropertyChanged();
            }
        }

        public bool WordWrap
        {
            get => Customization.WordWrap;
            set
            {
                Customization.WordWrap = value;
                OnPropertyChanged();
            }
        }

        public bool UseDarkMode
        {
            get => string.Equals(Customization.NativeThemeMode, "Dark", StringComparison.OrdinalIgnoreCase);
            set
            {
                var selectedTheme = value ? "Dark" : "Light";
                if (string.Equals(Customization.NativeThemeMode, selectedTheme, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Customization.NativeThemeMode = selectedTheme;
                ApplyThemePreset(selectedTheme);
                UpdateViewModel();
                RefreshTheme();
            }
        }

        public string BackgroundMain
        {
            get => Customization.BackgroundMain;
            set
            {
                Customization.BackgroundMain = value;

                // Call this instead of SetAppColor because otherwise it will not
                // update the background if we previously set an image
                RefreshTheme();

                OnPropertyChanged();
            }
        }

        public string BackgroundSecondary
        {
            get => Customization.BackgroundSecondary;
            set
            {
                Customization.BackgroundSecondary = value;
                Brush.SetAppColor("BackgroundSecondary", value);
                OnPropertyChanged();
            }
        }

        public string BackgroundInput
        {
            get => Customization.BackgroundInput;
            set
            {
                Customization.BackgroundInput = value;
                Brush.SetAppColor("BackgroundInput", value);
                OnPropertyChanged();
            }
        }

        public string ForegroundMain
        {
            get => Customization.ForegroundMain;
            set
            {
                Customization.ForegroundMain = value;
                Brush.SetAppColor("ForegroundMain", value);
                OnPropertyChanged();
            }
        }

        public string ForegroundInput
        {
            get => Customization.ForegroundInput;
            set
            {
                Customization.ForegroundInput = value;
                Brush.SetAppColor("ForegroundInput", value);
                OnPropertyChanged();
            }
        }

        public string ForegroundGood
        {
            get => Customization.ForegroundGood;
            set
            {
                Customization.ForegroundGood = value;
                Brush.SetAppColor("ForegroundGood", value);
                OnPropertyChanged();
            }
        }

        public string ForegroundBad
        {
            get => Customization.ForegroundBad;
            set
            {
                Customization.ForegroundBad = value;
                Brush.SetAppColor("ForegroundBad", value);
                OnPropertyChanged();
            }
        }

        public string ForegroundCustom
        {
            get => Customization.ForegroundCustom;
            set
            {
                Customization.ForegroundCustom = value;
                Brush.SetAppColor("ForegroundCustom", value);
                OnPropertyChanged();
            }
        }

        public string ForegroundRetry
        {
            get => Customization.ForegroundRetry;
            set
            {
                Customization.ForegroundRetry = value;
                Brush.SetAppColor("ForegroundRetry", value);
                OnPropertyChanged();
            }
        }

        public string ForegroundBanned
        {
            get => Customization.ForegroundBanned;
            set
            {
                Customization.ForegroundBanned = value;
                Brush.SetAppColor("ForegroundBanned", value);
                OnPropertyChanged();
            }
        }

        public string ForegroundToCheck
        {
            get => Customization.ForegroundToCheck;
            set
            {
                Customization.ForegroundToCheck = value;
                Brush.SetAppColor("ForegroundToCheck", value);
                OnPropertyChanged();
            }
        }

        public string ForegroundMenuSelected
        {
            get => Customization.ForegroundMenuSelected;
            set
            {
                Customization.ForegroundMenuSelected = value;
                Brush.SetAppColor("ForegroundMenuSelected", value);
                OnPropertyChanged();
            }
        }

        public string SuccessButton
        {
            get => Customization.SuccessButton;
            set
            {
                Customization.SuccessButton = value;
                Brush.SetAppColor("SuccessButton", value);
                OnPropertyChanged();
            }
        }

        public string PrimaryButton
        {
            get => Customization.PrimaryButton;
            set
            {
                Customization.PrimaryButton = value;
                Brush.SetAppColor("PrimaryButton", value);
                OnPropertyChanged();
            }
        }

        public string WarningButton
        {
            get => Customization.WarningButton;
            set
            {
                Customization.WarningButton = value;
                Brush.SetAppColor("WarningButton", value);
                OnPropertyChanged();
            }
        }

        public string DangerButton
        {
            get => Customization.DangerButton;
            set
            {
                Customization.DangerButton = value;
                Brush.SetAppColor("DangerButton", value);
                OnPropertyChanged();
            }
        }

        public string ForegroundButton
        {
            get => Customization.ForegroundButton;
            set
            {
                Customization.ForegroundButton = value;
                Brush.SetAppColor("ForegroundButton", value);
                OnPropertyChanged();
            }
        }

        public string BackgroundButton
        {
            get => Customization.BackgroundButton;
            set
            {
                Customization.BackgroundButton = value;
                Brush.SetAppColor("BackgroundButton", value);
                OnPropertyChanged();
            }
        }

        public string BackgroundImagePath
        {
            get => Customization.BackgroundImagePath;
            private set
            {
                Customization.BackgroundImagePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowBackgroundImage));

                BackgroundImage = new(new Uri(value));
            }
        }

        public double BackgroundOpacity
        {
            get => Customization.BackgroundOpacity;
            set
            {
                Customization.BackgroundOpacity = value;
                OnPropertyChanged();
                RefreshTheme();
            }
        }

        private BitmapImage backgroundImage;
        public BitmapImage BackgroundImage
        {
            get => backgroundImage;
            set
            {
                backgroundImage = value;
                OnPropertyChanged();
                RefreshTheme();
            }
        }

        public bool ShowBackgroundImage => !string.IsNullOrEmpty(BackgroundImagePath);

        public void SetBackgroundImage(string path) => BackgroundImagePath = path;

        public Task Save()
        {
            General.ProxyCheckTargets = ProxyCheckTargetsCollection.ToList();
            General.CustomSnippets = CustomSnippetsCollection.ToList();
            Remote.ConfigsEndpoints = RemoteConfigsEndpointsCollection.ToList();
            return service.SaveAsync();
        }

        public void Reset()
        {
            service.Recreate();
            CreateCollections();
            UpdateViewModel();
            RefreshTheme();
        }

        public void ResetCustomization()
        {
            service.Settings.CustomizationSettings = new CustomizationSettings();
            UpdateViewModel();
            RefreshTheme();
        }

        private void RefreshTheme() => themeService.SetTheme(Customization);

        public void AddProxyCheckTarget() => ProxyCheckTargetsCollection.Add(new ProxyCheckTarget());
        public void RemoveProxyCheckTarget(ProxyCheckTarget target) => ProxyCheckTargetsCollection.Remove(target);

        public void AddCustomSnippet() => CustomSnippetsCollection.Add(new CustomSnippet());
        public void RemoveCustomSnippet(CustomSnippet snippet) => CustomSnippetsCollection.Remove(snippet);

        public void AddRemoteConfigsEndpoint() => RemoteConfigsEndpointsCollection.Add(new RemoteConfigsEndpoint());
        public void RemoveRemoteConfigsEndpoint(RemoteConfigsEndpoint endpoint) => RemoteConfigsEndpointsCollection.Remove(endpoint);

        private void ApplyThemePreset(string themeMode)
        {
            if (string.Equals(themeMode, "Dark", StringComparison.OrdinalIgnoreCase))
            {
                Customization.BackgroundMain = "#0F172A";
                Customization.BackgroundInput = "#1E293B";
                Customization.BackgroundSecondary = "#1E293B";
                Customization.ForegroundMain = "#F8FAFC";
                Customization.ForegroundInput = "#F8FAFC";
                Customization.ForegroundGood = "#10B981";
                Customization.ForegroundBad = "#EF4444";
                Customization.ForegroundCustom = "#F97316";
                Customization.ForegroundRetry = "#EAB308";
                Customization.ForegroundBanned = "#8B5CF6";
                Customization.ForegroundToCheck = "#14B8A6";
                Customization.ForegroundMenuSelected = "#3B82F6";
                Customization.SuccessButton = "#10B981";
                Customization.PrimaryButton = "#3B82F6";
                Customization.WarningButton = "#F59E0B";
                Customization.DangerButton = "#EF4444";
                Customization.ForegroundButton = "#F8FAFC";
                Customization.BackgroundButton = "#374151";
                return;
            }

            Customization.BackgroundMain = "#F8FAFC";
            Customization.BackgroundInput = "#FFFFFF";
            Customization.BackgroundSecondary = "#EEF2F7";
            Customization.ForegroundMain = "#0F172A";
            Customization.ForegroundInput = "#0F172A";
            Customization.ForegroundGood = "#10B981";
            Customization.ForegroundBad = "#EF4444";
            Customization.ForegroundCustom = "#F97316";
            Customization.ForegroundRetry = "#EAB308";
            Customization.ForegroundBanned = "#8B5CF6";
            Customization.ForegroundToCheck = "#14B8A6";
            Customization.ForegroundMenuSelected = "#2563EB";
            Customization.SuccessButton = "#10B981";
            Customization.PrimaryButton = "#2563EB";
            Customization.WarningButton = "#F59E0B";
            Customization.DangerButton = "#EF4444";
            Customization.ForegroundButton = "#0F172A";
            Customization.BackgroundButton = "#E2E8F0";
        }

        public override void UpdateViewModel()
        {
            // Bulk state changes (ApplyThemePreset, Reset, ResetCustomization) mutate
            // the underlying Customization instance directly, so the per-property
            // setters never fire. Re-raise notifications for the properties the
            // settings page binds to so the ColorPickers and the
            // ForegroundMenuSelected toggle re-fetch their values.
            OnPropertyChanged(nameof(BackgroundMain));
            OnPropertyChanged(nameof(BackgroundSecondary));
            OnPropertyChanged(nameof(BackgroundInput));
            OnPropertyChanged(nameof(ForegroundMain));
            OnPropertyChanged(nameof(ForegroundInput));
            OnPropertyChanged(nameof(ForegroundGood));
            OnPropertyChanged(nameof(ForegroundBad));
            OnPropertyChanged(nameof(ForegroundCustom));
            OnPropertyChanged(nameof(ForegroundRetry));
            OnPropertyChanged(nameof(ForegroundBanned));
            OnPropertyChanged(nameof(ForegroundToCheck));
            OnPropertyChanged(nameof(ForegroundMenuSelected));
            OnPropertyChanged(nameof(SuccessButton));
            OnPropertyChanged(nameof(PrimaryButton));
            OnPropertyChanged(nameof(WarningButton));
            OnPropertyChanged(nameof(DangerButton));
            OnPropertyChanged(nameof(ForegroundButton));
            OnPropertyChanged(nameof(BackgroundButton));
            OnPropertyChanged(nameof(UseDarkMode));
        }

        private void CreateCollections()
        {
            ProxyCheckTargetsCollection = new ObservableCollection<ProxyCheckTarget>(General.ProxyCheckTargets);
            CustomSnippetsCollection = new ObservableCollection<CustomSnippet>(General.CustomSnippets);
            RemoteConfigsEndpointsCollection = new ObservableCollection<RemoteConfigsEndpoint>(Remote.ConfigsEndpoints);
        }
    }
}


