using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Flux.Core.Services;
using Flux.Native.ViewModels;
using Flux.Native.ViewModels.Configs;
using RuriLib.Models.Blocks;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;


namespace Flux.Native.Controls
{
    /// <summary>
    /// Interaction logic for LoliCodeBlockSettingsViewer.xaml
    /// </summary>
    public partial class LoliCodeBlockSettingsViewer : UserControl
    {
        private readonly LoliCodeBlockSettingsViewerViewModel vm;
        private readonly FluxSettingsService fluxSettingsService;

        public LoliCodeBlockSettingsViewer(BlockViewModel blockVM)
        {
            if (blockVM.Block is not LoliCodeBlockInstance)
            {
                throw new Exception("Wrong block type for this UC");
            }

            fluxSettingsService = App.ServiceProvider.GetRequiredService<FluxSettingsService>();
            vm = new LoliCodeBlockSettingsViewerViewModel(blockVM);
            DataContext = vm;

            InitializeComponent();

            editor.WordWrap = fluxSettingsService.Settings.CustomizationSettings.WordWrap;
            editor.Text = vm.Script;
            HighlightSyntax();
            SearchPanel.Install(editor);
        }

        private void EditorLostFocus(object sender, RoutedEventArgs e) => vm.Script = editor.Text;
        private void EditorTextChanged(object sender, EventArgs e) => vm.Script = editor.Text;

        private void HighlightSyntax()
        {
            using var reader = XmlReader.Create("Highlighting/LoliCode.xshd");
            editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            editor.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(Colors.DodgerBlue);
            editor.TextArea.TextView.LinkTextUnderline = false;
        }
    }

    public class LoliCodeBlockSettingsViewerViewModel : BlockSettingsViewerViewModel
    {
        public LoliCodeBlockInstance LoliCodeBlock => Block as LoliCodeBlockInstance;

        public string Script
        {
            get => LoliCodeBlock.Script;
            set
            {
                LoliCodeBlock.Script = value;
                OnPropertyChanged();
            }
        }

        public LoliCodeBlockSettingsViewerViewModel(BlockViewModel block) : base(block)
        {
            
        }
    }
}
