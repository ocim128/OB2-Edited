using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Flux.Native.Controls;
using Flux.Native.ViewModels.Configs;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Custom;

namespace Flux.Native.Views.Controls.Config;

public partial class ConfigStackerInspectorControl : UserControl
{
    public ConfigStackerInspectorControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ConfigStackerInspectorViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        if (e.NewValue is ConfigStackerInspectorViewModel newViewModel)
        {
            newViewModel.PropertyChanged += ViewModel_PropertyChanged;
            RenderSelection(newViewModel.SelectedBlock);
        }
        else
        {
            RenderSelection(null);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfigStackerInspectorViewModel.SelectedBlock) &&
            sender is ConfigStackerInspectorViewModel viewModel)
        {
            RenderSelection(viewModel.SelectedBlock);
        }
    }

    private void RenderSelection(BlockViewModel? blockViewModel)
    {
        if (blockViewModel is null)
        {
            BlockInfoContent.Content = null;
            BlockInfoPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        UserControl? content = blockViewModel.Block switch
        {
            AutoBlockInstance => new AutoBlockSettingsViewer(blockViewModel),
            ParseBlockInstance => new ParseBlockSettingsViewer(blockViewModel),
            ScriptBlockInstance => new ScriptBlockSettingsViewer(blockViewModel),
            HttpRequestBlockInstance => new HttpRequestBlockSettingsViewer(blockViewModel),
            KeycheckBlockInstance => new KeycheckBlockSettingsViewer(blockViewModel),
            LoliCodeBlockInstance => new LoliCodeBlockSettingsViewer(blockViewModel),
            _ => null
        };

        BlockInfoContent.Content = content;
        BlockInfoPlaceholder.Visibility = content is null ? Visibility.Visible : Visibility.Collapsed;

        if (content is not null)
        {
            BlockInfoScrollViewer.ScrollToHome();
        }
    }
}
