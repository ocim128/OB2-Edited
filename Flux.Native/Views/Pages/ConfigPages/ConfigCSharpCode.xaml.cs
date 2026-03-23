using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Flux.Core.Services;
using Flux.Native.Helpers;
using RuriLib.Helpers.Transpilers;
using RuriLib.Models.Configs;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;


using Flux.Native.Enums;
using Flux.Native.Services.Navigation;
using Flux.Native.ViewModels.Base;

namespace Flux.Native.Views.Pages.Configs
{
    /// <summary>
    /// Interaction logic for ConfigCSharpCode.xaml
    /// </summary>
    public partial class ConfigCSharpCode : Page
    {
        private readonly ConfigCSharpCodeViewModel vm;
        private readonly ConfigService configService;
        private readonly INavigationHandler navigationHandler;
        private Config Config => configService.SelectedConfig;

        public ConfigCSharpCode(
            ConfigCSharpCodeViewModel vm,
            ConfigService configService,
            INavigationHandler navigationHandler)
        {
            this.vm = vm;
            this.configService = configService;
            this.navigationHandler = navigationHandler;
            DataContext = this.vm;

            InitializeComponent();

            HighlightSyntax(editor);
            HighlightSyntax(startupEditor);
            SearchPanel.Install(editor);
            SearchPanel.Install(startupEditor);
        }

        public void UpdateViewModel()
        {
            try
            {
                if (Config == null)
                {
                    editor.Text = string.Empty;
                    startupEditor.Text = string.Empty;
                    startupEditorContainer.Visibility = Visibility.Collapsed;
                    return;
                }

                // Transpile if not in CSharp mode
                if (Config.Mode != ConfigMode.CSharp)
                {
                    Config.CSharpScript = Config.Mode == ConfigMode.Stack
                            ? Stack2CSharpTranspiler.Transpile(Config.Stack, Config.Settings)
                            : Loli2CSharpTranspiler.Transpile(Config.LoliCodeScript, Config.Settings);

                    Config.StartupCSharpScript = Loli2CSharpTranspiler.Transpile(
                        Config.StartupLoliCodeScript, Config.Settings);
                }

                // Always refresh the UI text from current config values.
                editor.Text = Config.CSharpScript ?? string.Empty;
                startupEditor.Text = Config.StartupCSharpScript ?? string.Empty;
                startupEditorContainer.Visibility =
                    string.IsNullOrWhiteSpace(Config.StartupCSharpScript)
                        ? Visibility.Collapsed
                        : Visibility.Visible;
            }
            catch (Exception ex)
            {
                // On fail, prompt it to the user and go back to the configs page
                Alert.Exception(ex);
                _ = navigationHandler.NavigateTo(MainWindowPage.Configs);
            }
        }

        private static void HighlightSyntax(TextEditor textEditor)
        {
            using var reader = XmlReader.Create("Highlighting/CSharp.xshd");
            textEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            textEditor.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(Colors.DodgerBlue);
            textEditor.TextArea.TextView.LinkTextUnderline = false;
        }

        private void ToggleUsings(object sender, RoutedEventArgs e) => usingsContainer.Visibility =
            usingsContainer.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;

        private void ToggleStartup(object sender, RoutedEventArgs e) => startupEditorContainer.Visibility =
            startupEditorContainer.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
    }

    public class ConfigCSharpCodeViewModel : ViewModelBase
    {
        private readonly ConfigService configService;
        private readonly FluxSettingsService fluxSettingsService;
        private Config Config => configService.SelectedConfig;

        public ConfigCSharpCodeViewModel(ConfigService configService, FluxSettingsService fluxSettingsService)
        {
            this.configService = configService;
            this.fluxSettingsService = fluxSettingsService;
        }

        public bool WordWrap => fluxSettingsService.Settings.CustomizationSettings.WordWrap;

        public string UsingsString
        {
            get => string.Join(Environment.NewLine, Config.Settings.ScriptSettings.CustomUsings);
            set
            {
                Config.Settings.ScriptSettings.CustomUsings = value.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();
                OnPropertyChanged();
            }
        }
    }
}






