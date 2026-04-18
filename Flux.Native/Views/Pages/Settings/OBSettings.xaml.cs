using Microsoft.Win32;
using Flux.Core.Models.Settings;
using Flux.Native.Helpers;
using Flux.Native.ViewModels.Settings;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;


namespace Flux.Native.Views.Pages.Settings;

/// <summary>
/// Interaction logic for OBSettings.xaml
/// </summary>
public partial class OBSettings : Page
{
    private readonly OBSettingsViewModel vm;

    public OBSettings(OBSettingsViewModel vm)
    {
        this.vm = vm;
        DataContext = this.vm;

        InitializeComponent();

        configSectionOnLoadCombobox.ItemsSource = Enum.GetValues(typeof(ConfigSection)).Cast<ConfigSection>();
    }

    private async void Save(object sender, RoutedEventArgs e)
    {
        try
        {
            await vm.Save();
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
        }
    }
    private void Reset(object sender, RoutedEventArgs e) => vm.Reset();
    private void ResetCustomization(object sender, RoutedEventArgs e) => vm.ResetCustomization();

    private void AddProxyCheckTarget(object sender, RoutedEventArgs e) => vm.AddProxyCheckTarget();
    private void RemoveProxyCheckTarget(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ProxyCheckTarget pct)
            vm.RemoveProxyCheckTarget(pct);
    }

    private void AddCustomSnippet(object sender, RoutedEventArgs e) => vm.AddCustomSnippet();
    private void RemoveCustomSnippet(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is CustomSnippet cs)
            vm.RemoveCustomSnippet(cs);
    }

    private void AddRemoteConfigsEndpoint(object sender, RoutedEventArgs e) => vm.AddRemoteConfigsEndpoint();
    private void RemoveRemoteConfigsEndpoint(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is RemoteConfigsEndpoint rce)
            vm.RemoveRemoteConfigsEndpoint(rce);
    }

    private void ChooseBackgroundImage(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Filter = "Images | *.jpg;*.jpeg;*.png;*.bmp",
            FilterIndex = 1
        };

        _ = ofd.ShowDialog();

        if (!string.IsNullOrEmpty(ofd.FileName))
        {
            try
            {
                vm.SetBackgroundImage(ofd.FileName);
            }
            catch (Exception ex)
            {
                Alert.Exception(ex);
            }
        }
    }
}


