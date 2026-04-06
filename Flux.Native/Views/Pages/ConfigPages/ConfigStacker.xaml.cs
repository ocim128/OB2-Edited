using Flux.Core.Services;
using Flux.Native.Enums;
using Flux.Native.Helpers;
using Flux.Native.Services;
using Flux.Native.ViewModels.Configs;
using Flux.Native.Views.Dialogs.Config;
using Flux.Native.Services.Navigation;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RuriLib.Models.Configs;

namespace Flux.Native.Views.Pages.Configs;

public partial class ConfigStacker : Page
{
    private readonly ConfigService configService;
    private readonly ConfigStackerViewModel vm;
    private readonly INavigationHandler navigationHandler;
    private GridLength? storedBlockListWidth;
    private GridLength? storedSplitterWidth;

    public bool IsStackerPaneVisible { get; private set; } = true;

    public ConfigStacker(
        ConfigService configService,
        ConfigStackerViewModel vm,
        INavigationHandler navigationHandler)
    {
        this.configService = configService;
        this.vm = vm;
        this.navigationHandler = navigationHandler;

        InitializeComponent();

        DataContext = this.vm;
        BlockListControl.DataContext = this.vm;
        BlockListControl.AddBlockRequested += ShowAddBlockDialog;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void UpdateViewModel()
    {
        try
        {
            configService.SelectedConfig.ChangeMode(ConfigMode.Stack);
        }
        catch (Exception ex)
        {
            Alert.Exception(ex);
            _ = navigationHandler.NavigateTo(MainWindowPage.Configs);
            return;
        }

        vm.UpdateViewModel();
        if (vm.Stack?.Any() == true)
        {
            vm.HandleBlockClick(vm.Stack.First(), ctrl: false, shift: false);
        }
    }

    public void SetStackerPaneVisibility(bool showStacker)
    {
        RunOnUiThread(() =>
        {
            if (showStacker)
            {
                BlockListColumn.Width = storedBlockListWidth ?? new GridLength(220, GridUnitType.Pixel);
                StackerSplitterColumn.Width = storedSplitterWidth ?? new GridLength(10, GridUnitType.Pixel);
                BlockListControl.Visibility = Visibility.Visible;
                BlockListGridSplitter.Visibility = Visibility.Visible;
                Grid.SetColumn(InspectorControl, 2);
                Grid.SetColumnSpan(InspectorControl, 1);
            }
            else
            {
                storedBlockListWidth ??= BlockListColumn.Width;
                storedSplitterWidth ??= StackerSplitterColumn.Width;
                BlockListColumn.Width = new GridLength(0);
                StackerSplitterColumn.Width = new GridLength(0);
                BlockListControl.Visibility = Visibility.Collapsed;
                BlockListGridSplitter.Visibility = Visibility.Collapsed;
                Grid.SetColumn(InspectorControl, 0);
                Grid.SetColumnSpan(InspectorControl, 3);
            }

            IsStackerPaneVisible = showStacker;
        });
    }

    public void CreateBlock(RuriLib.Models.Blocks.BlockDescriptor descriptor)
    {
        vm.CreateBlockAndResetUndo(descriptor);
    }

    private void ShowAddBlockDialog()
    {
        new MainDialog(new AddBlockDialog(this), "Add block", 760, 620).ShowDialog();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        vm.NotificationRequested -= ShowNotification;
        vm.NotificationRequested += ShowNotification;
        Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        vm.NotificationRequested -= ShowNotification;
    }

    private void PageKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        var focusedElement = Keyboard.FocusedElement;
        var isTextInputFocused = focusedElement is TextBox ||
                                 focusedElement is RichTextBox ||
                                 focusedElement?.GetType().Name.Contains("TextBox") == true;

        if (e.Key == Key.Up && alt)
        {
            vm.MoveSelectedUpCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Down && alt)
        {
            vm.MoveSelectedDownCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.D && ctrl && !isTextInputFocused)
        {
            vm.CloneSelectedCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && !isTextInputFocused && vm.Stack?.Any(block => block is not null && block.Selected) == true)
        {
            vm.RemoveSelectedCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.C && ctrl && !isTextInputFocused)
        {
            vm.CopyCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.V && ctrl && !isTextInputFocused)
        {
            vm.PasteCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && ctrl && !isTextInputFocused)
        {
            vm.UndoCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static void ShowNotification(string title, string message)
    {
        try
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var notification = new SharedNotificationWindow(title, message)
                {
                    ShowActivated = false,
                    Focusable = false
                };

                notification.Show();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
        catch
        {
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.Invoke(action);
        }
    }
}
