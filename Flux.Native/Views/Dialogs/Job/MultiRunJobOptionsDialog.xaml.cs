using System;
using System.Windows;
using System.Windows.Controls;
using Flux.Core.Entities;
using Flux.Core.Models.Jobs;
using Flux.Native.Factories;
using Flux.Native.Helpers;
using Flux.Native.ViewModels.Configs;
using Flux.Native.ViewModels.Jobs;
using RuriLib.Models.Jobs;

namespace Flux.Native.Views.Dialogs.Job;

public partial class MultiRunJobOptionsDialog : Page
{
    private readonly Action<JobOptions> onAccept;
    private readonly MultiRunJobOptionsViewModel vm;

    public MultiRunJobOptionsDialog(
        IMultiRunJobOptionsViewModelFactory viewModelFactory,
        MultiRunJobOptions? options = null,
        Action<JobOptions>? onAccept = null)
    {
        this.onAccept = onAccept;
        vm = viewModelFactory.Create(options);
        DataContext = vm;

        InitializeComponent();
    }

    public async void SelectConfig(ConfigViewModel config)
    {
        vm.SelectConfig(config);
        await vm.TrySetRecordAsync();
    }

    public async void SelectWordlist(WordlistEntity entity)
    {
        (vm.DataPoolOptions as WordlistDataPoolOptionsViewModel)?.SelectWordlist(entity);
        await vm.TrySetRecordAsync();
    }

    public async void AddWordlist(WordlistEntity entity) => await vm.AddWordlist(entity);

    private void Accept(object sender, RoutedEventArgs e)
    {
        if (!vm.IsConfigSelected)
        {
            Alert.Error("No config selected", "Please select a config before proceeding");
            return;
        }

        onAccept?.Invoke(vm.Options);
        ((MainDialog)Parent).Close();
    }
}
