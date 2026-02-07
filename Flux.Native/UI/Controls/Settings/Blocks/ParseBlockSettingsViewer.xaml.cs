using Flux.Native.ViewModels;
using Flux.Native.ViewModels.Configs;
using RuriLib.Models.Blocks.Custom;
using RuriLib.Models.Blocks.Custom.Parse;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Flux.Native.Controls
{
    /// <summary>
    /// Interaction logic for ParseBlockSettingsViewer.xaml
    /// </summary>
    public partial class ParseBlockSettingsViewer : UserControl
    {
        private readonly ParseBlockSettingsViewerViewModel vm;

        public ParseBlockSettingsViewer(BlockViewModel blockVM)
        {
            if (blockVM.Block is not ParseBlockInstance)
            {
                throw new Exception("Wrong block type for this UC");
            }

            vm = new ParseBlockSettingsViewerViewModel(blockVM);
            vm.ModeChanged += mode => tabControl.SelectedIndex = (int)mode;
            DataContext = vm;

            InitializeComponent();

            tabControl.SelectedIndex = (int)vm.Mode;
            BindSettings();
            ReloadConditionalCases();
        }

        // TODO: Find a way to automatically scout the visual tree and get the settings viewers by Tag
        // to set their Setting property automatically basing on the Tag instead of doing it manually
        private void BindSettings()
        {
            // General
            inputSetting.Setting = vm.ParseBlock.Settings["input"];
            prefixSetting.Setting = vm.ParseBlock.Settings["prefix"];
            suffixSetting.Setting = vm.ParseBlock.Settings["suffix"];
            urlEncodeOutputSetting.Setting = vm.ParseBlock.Settings["urlEncodeOutput"];

            // LR
            leftDelimSetting.Setting = vm.ParseBlock.Settings["leftDelim"];
            rightDelimSetting.Setting = vm.ParseBlock.Settings["rightDelim"];
            caseSensitiveSetting.Setting = vm.ParseBlock.Settings["caseSensitive"];

            // CSS
            cssSelectorSetting.Setting = vm.ParseBlock.Settings["cssSelector"];
            attributeNameSetting.Setting = vm.ParseBlock.Settings["attributeName"];

            // XPath
            xPathSetting.Setting = vm.ParseBlock.Settings["xPath"];

            // Json
            jTokenSetting.Setting = vm.ParseBlock.Settings["jToken"];

            // Regex
            patternSetting.Setting = vm.ParseBlock.Settings["pattern"];
            outputFormatSetting.Setting = vm.ParseBlock.Settings["outputFormat"];
            multiLineSetting.Setting = vm.ParseBlock.Settings["multiLine"];
        }

        private void ReloadConditionalCases()
        {
            conditionalCasesPanel.Children.Clear();
            foreach (var conditionalCase in vm.ParseBlock.ConditionalCases)
            {
                AddCaseViewer(conditionalCase);
            }
        }

        private void AddCaseViewer(ParseBlockInstance.ParseConditionalCase conditionalCase)
        {
            var container = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(6)
            };

            var stack = new StackPanel();
            container.Child = stack;

            var conditionViewer = new ConditionalCaseViewer(conditionalCase)
            {
                Margin = new Thickness(0, 0, 0, 6)
            };

            conditionViewer.OnDeleted += (s, e) => DeleteCase(conditionalCase, container);
            conditionViewer.OnMoveUp += (s, e) => MoveCase(conditionalCase, -1);
            conditionViewer.OnMoveDown += (s, e) => MoveCase(conditionalCase, 1);
            stack.Children.Add(conditionViewer);

            var overridePanel = new StackPanel();

            overridePanel.Children.Add(new TextBlock
            {
                Text = "Override Parse Mode",
                Foreground = new SolidColorBrush(Color.FromRgb(254, 195, 77)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            });

            var modeTabs = CreateModeTabs(conditionalCase);

            var modeCombo = new ComboBox
            {
                ItemsSource = Enum.GetValues(typeof(ParseMode)),
                SelectedItem = conditionalCase.OverrideMode,
                Margin = new Thickness(0, 4, 0, 8),
                Width = 160
            };
            modeCombo.SelectionChanged += (s, e) =>
            {
                if (modeCombo.SelectedItem is ParseMode mode)
                {
                    conditionalCase.OverrideMode = mode;
                    modeTabs.SelectedIndex = (int)mode;
                }
            };
            overridePanel.Children.Add(modeCombo);

            overridePanel.Children.Add(new StringSettingViewer { Setting = conditionalCase.Settings["prefix"] });
            overridePanel.Children.Add(new StringSettingViewer { Setting = conditionalCase.Settings["suffix"] });
            overridePanel.Children.Add(modeTabs);

            stack.Children.Add(overridePanel);

            conditionalCasesPanel.Children.Add(container);
        }

        private TabControl CreateModeTabs(ParseBlockInstance.ParseConditionalCase conditionalCase)
        {
            var tabs = new TabControl
            {
                SelectedIndex = (int)conditionalCase.OverrideMode,
                Margin = new Thickness(0, 6, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };

            var tabStyle = new Style(typeof(TabItem));
            tabStyle.Setters.Add(new Setter(TabItem.VisibilityProperty, Visibility.Collapsed));
            tabs.ItemContainerStyle = tabStyle;

            var lrPanel = new StackPanel();
            lrPanel.Children.Add(new StringSettingViewer { Setting = conditionalCase.Settings["leftDelim"] });
            lrPanel.Children.Add(new StringSettingViewer { Setting = conditionalCase.Settings["rightDelim"] });
            lrPanel.Children.Add(new BoolSettingViewer { Setting = conditionalCase.Settings["caseSensitive"] });
            tabs.Items.Add(new TabItem { Content = lrPanel });

            var cssPanel = new StackPanel();
            cssPanel.Children.Add(new StringSettingViewer { Setting = conditionalCase.Settings["cssSelector"] });
            cssPanel.Children.Add(new StringSettingViewer { Setting = conditionalCase.Settings["attributeName"] });
            tabs.Items.Add(new TabItem { Content = cssPanel });

            var xPathPanel = new StackPanel();
            xPathPanel.Children.Add(new StringSettingViewer { Setting = conditionalCase.Settings["xPath"] });
            xPathPanel.Children.Add(new StringSettingViewer { Setting = conditionalCase.Settings["attributeName"] });
            tabs.Items.Add(new TabItem { Content = xPathPanel });

            var jsonPanel = new StackPanel();
            jsonPanel.Children.Add(new StringSettingViewer { Setting = conditionalCase.Settings["jToken"] });
            tabs.Items.Add(new TabItem { Content = jsonPanel });

            var regexPanel = new StackPanel();
            regexPanel.Children.Add(new StringSettingViewer { Setting = conditionalCase.Settings["pattern"] });
            regexPanel.Children.Add(new StringSettingViewer { Setting = conditionalCase.Settings["outputFormat"] });
            regexPanel.Children.Add(new BoolSettingViewer { Setting = conditionalCase.Settings["multiLine"] });
            tabs.Items.Add(new TabItem { Content = regexPanel });

            return tabs;
        }

        private void DeleteCase(ParseBlockInstance.ParseConditionalCase conditionalCase, FrameworkElement container)
        {
            if (vm.ParseBlock.ConditionalCases.Remove(conditionalCase))
            {
                conditionalCasesPanel.Children.Remove(container);
            }
        }

        private void MoveCase(ParseBlockInstance.ParseConditionalCase conditionalCase, int offset)
        {
            var index = vm.ParseBlock.ConditionalCases.IndexOf(conditionalCase);
            var newIndex = index + offset;

            if (index < 0 || newIndex < 0 || newIndex >= vm.ParseBlock.ConditionalCases.Count)
            {
                return;
            }

            vm.ParseBlock.ConditionalCases.RemoveAt(index);
            vm.ParseBlock.ConditionalCases.Insert(newIndex, conditionalCase);
            ReloadConditionalCases();
        }

        private void AddCondition(object sender, RoutedEventArgs e)
        {
            var conditionalCase = vm.ParseBlock.CreateConditionalCase();
            conditionalCase.Name = $"Condition {vm.ParseBlock.ConditionalCases.Count + 1}";

            vm.ParseBlock.ConditionalCases.Add(conditionalCase);
            AddCaseViewer(conditionalCase);
        }
    }

    public class ParseBlockSettingsViewerViewModel : BlockSettingsViewerViewModel
    {
        public ParseBlockInstance ParseBlock => Block as ParseBlockInstance;

        public bool SafeMode
        {
            get => ParseBlock.Safe;
            set
            {
                ParseBlock.Safe = value;
                OnPropertyChanged();
            }
        }


        public event Action<ParseMode> ModeChanged;

        public string OutputVariable
        {
            get => ParseBlock.OutputVariable;
            set
            {
                ParseBlock.OutputVariable = value;
                OnPropertyChanged();
            }
        }

        public bool Recursive
        {
            get => ParseBlock.Recursive;
            set
            {
                ParseBlock.Recursive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ReturnValueType));
            }
        }

        public bool IsCapture
        {
            get => ParseBlock.IsCapture;
            set
            {
                ParseBlock.IsCapture = value;
                OnPropertyChanged();
            }
        }

        public ParseMode Mode
        {
            get => ParseBlock.Mode;
            set
            {
                ParseBlock.Mode = value;
                ModeChanged?.Invoke(value);
                OnPropertyChanged();
            }
        }

        public bool LRMode
        {
            get => Mode == ParseMode.LR;
            set
            {
                if (value)
                {
                    Mode = ParseMode.LR;
                }

                OnPropertyChanged();
            }
        }

        public bool CSSMode
        {
            get => Mode == ParseMode.CSS;
            set
            {
                if (value)
                {
                    Mode = ParseMode.CSS;
                }

                OnPropertyChanged();
            }
        }

        public bool XPathMode
        {
            get => Mode == ParseMode.XPath;
            set
            {
                if (value)
                {
                    Mode = ParseMode.XPath;
                }

                OnPropertyChanged();
            }
        }

        public bool JsonMode
        {
            get => Mode == ParseMode.Json;
            set
            {
                if (value)
                {
                    Mode = ParseMode.Json;
                }

                OnPropertyChanged();
            }
        }

        public bool RegexMode
        {
            get => Mode == ParseMode.Regex;
            set
            {
                if (value)
                {
                    Mode = ParseMode.Regex;
                }

                OnPropertyChanged();
            }
        }

        public string ReturnValueType => $"Output variable ({(ParseBlock.Recursive ? "ListOfStrings" : "String")})";

        public ParseBlockSettingsViewerViewModel(BlockViewModel block) : base(block)
        {
             
        }
    }
}
