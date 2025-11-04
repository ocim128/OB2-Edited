using OpenBullet2.Native.Helpers;
using OpenBullet2.Native.ViewModels;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Settings;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.IconPacks;

namespace OpenBullet2.Native.Controls
{
    /// <summary>
    /// Interaction logic for AutoBlockSettingsViewer.xaml
    /// </summary>
    public partial class AutoBlockSettingsViewer : UserControl
    {
        private readonly AutoBlockSettingsViewerViewModel vm;

        public AutoBlockSettingsViewer(BlockViewModel blockVM)
        {
            if (blockVM.Block is not AutoBlockInstance)
            {
                throw new Exception("Wrong block type for this UC");
            }

            vm = new AutoBlockSettingsViewerViewModel(blockVM);
            DataContext = vm;

            InitializeComponent();
            CreateControls();
        }

        private void CreateControls()
        {
            CreateSettings();

            foreach (var action in vm.Block.Descriptor.Actions)
            {
                var button = new Button
                {
                    Foreground = Brush.Get("ForegroundMain"),
                    Background = Brush.Get("BackgroundPrimary"),
                    Margin = new Thickness(0, 0, 5, 5),
                    Content = action.Name
                };

                button.Click += (sender, e) =>
                {
                    try
                    {
                        action.Delegate.Invoke(vm.Block);
                    }
                    catch (Exception ex)
                    {
                        Alert.Exception(ex);
                    }

                    CreateSettings();
                    CreateImages();
                    vm.UpdateViewModel();
                };

                actionsPanel.Children.Add(button);
            }

            CreateImages();
        }

        private void CreateSettings()
        {
            settingsPanel.Children.Clear();
            blockHeaderAction.Content = null;
            blockHeaderAction.Visibility = Visibility.Collapsed;

            // Special handling for CreateMultiple block - use custom UI
            if (vm.Block.Descriptor.Id == "CreateMultiple")
            {
                var createMultipleViewer = new CreateMultipleConstantViewer
                {
                    BlockVM = vm.BlockVM,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                settingsPanel.Children.Add(createMultipleViewer);

                blockHeaderAction.Content = CreateAddVariableButton(createMultipleViewer);
                blockHeaderAction.Visibility = Visibility.Visible;
                return;
            }

            foreach (var setting in vm.Block.Settings)
            {
                UserControl viewer = setting.Value.FixedSetting switch
                {
                    StringSetting => new StringSettingViewer { Setting = setting.Value },
                    IntSetting => new IntSettingViewer { Setting = setting.Value },
                    FloatSetting => new FloatSettingViewer { Setting = setting.Value },
                    EnumSetting => new EnumSettingViewer { Setting = setting.Value },
                    ByteArraySetting => new ByteArraySettingViewer { Setting = setting.Value },
                    BoolSetting => new BoolSettingViewer { Setting = setting.Value },
                    ListOfStringsSetting => new ListOfStringsSettingViewer { Setting = setting.Value },
                    DictionaryOfStringsSetting => new DictionaryOfStringsSettingViewer { Setting = setting.Value },
                    _ => null
                };

                if (viewer is not null)
                {
                    settingsPanel.Children.Add(viewer);
                }
            }
        }

        private void CreateImages()
        {
            imagesPanel.Children.Clear();

            foreach (var image in vm.Block.Descriptor.Images)
            {
                var control = new ImagePicker(image.Value.Value);
                control.ImageChanged += (s, bytes) => image.Value.Value = bytes;
                imagesPanel.Children.Add(control);
            }
        }

        private Button CreateAddVariableButton(CreateMultipleConstantViewer viewer)
        {
            var button = new Button
            {
                Style = TryFindResource("ModernButton") as Style,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            content.Children.Add(new PackIconMaterial
            {
                Kind = PackIconMaterialKind.Plus,
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 8, 0)
            });

            content.Children.Add(new TextBlock
            {
                Text = "Add",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            });

            button.Content = content;
            button.Click += (s, e) => viewer.AddVariable();

            return button;
        }
    }

    public class AutoBlockSettingsViewerViewModel : BlockSettingsViewerViewModel
    {
        public AutoBlockInstance AutoBlock => Block as AutoBlockInstance;

        public bool ShowExecutionOptions => !string.Equals(Block.Descriptor.Id, "CreateMultiple", StringComparison.OrdinalIgnoreCase);

        public bool SafeMode
        {
            get => AutoBlock.Safe;
            set
            {
                AutoBlock.Safe = value;
                OnPropertyChanged();
            }
        }

        public bool HasReturnValue => ShowExecutionOptions && Block.Descriptor.ReturnType is not null;
        public string ReturnValueType => $"Output variable ({Block.Descriptor.ReturnType})";
        public bool HasActions => Block.Descriptor.Actions.Any();
        public bool HasImages => Block.Descriptor.Images.Any();

        public string OutputVariable
        {
            get => AutoBlock.OutputVariable;
            set
            {
                AutoBlock.OutputVariable = value;
                OnPropertyChanged();
            }
        }

        public bool IsCapture
        {
            get => AutoBlock.IsCapture;
            set
            {
                AutoBlock.IsCapture = value;
                OnPropertyChanged();
            }
        }

        public AutoBlockSettingsViewerViewModel(BlockViewModel block) : base(block)
        {

        }
    }

    public class BlockSettingsViewerViewModel : OpenBullet2.Native.ViewModels.Infrastructure.ViewModelBase
    {
        public BlockViewModel BlockVM { get; init; }
        public BlockInstance Block => BlockVM.Block;

        public string NameAndId => $"{BlockVM.Block.ReadableName} ({BlockVM.Block.Id})";

        public string Label
        {
            get => BlockVM.Label;
            set
            {
                BlockVM.Label = value;
                OnPropertyChanged();
            }
        }

        public BlockSettingsViewerViewModel(BlockViewModel block)
        {
            BlockVM = block;
        }
    }
}
