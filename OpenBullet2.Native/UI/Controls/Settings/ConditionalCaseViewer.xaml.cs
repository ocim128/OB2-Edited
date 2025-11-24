using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RuriLib.Models.Blocks.Custom;
using RuriLib.Models.Blocks.Custom.Keycheck;

namespace OpenBullet2.Native.Controls;

public partial class ConditionalCaseViewer : UserControl
{
    private readonly ConditionalConstantStringCase conditionalCase;

    public event EventHandler OnDeleted;
    public event EventHandler OnMoveUp;
    public event EventHandler OnMoveDown;

    public ConditionalCaseViewer(ConditionalConstantStringCase conditionalCase)
    {
        InitializeComponent();
        this.conditionalCase = conditionalCase;

        valueViewer.Setting = conditionalCase.Value;
        nameTextBox.Text = conditionalCase.Name;
        modeCombo.ItemsSource = Enum.GetValues(typeof(KeychainMode));
        modeCombo.SelectedItem = conditionalCase.Mode;

        foreach (var key in conditionalCase.Keys.ToList())
        {
            DisplayKey(key, addToCollection: false);
        }
    }

    private void NameChanged(object sender, TextChangedEventArgs e)
    {
        conditionalCase.Name = nameTextBox.Text;
    }

    private void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (modeCombo.SelectedItem is KeychainMode mode)
        {
            conditionalCase.Mode = mode;
        }
    }

    private void AddStringKey(object sender, RoutedEventArgs e) => DisplayKey(new StringKey());
    private void AddIntKey(object sender, RoutedEventArgs e) => DisplayKey(new IntKey());
    private void AddFloatKey(object sender, RoutedEventArgs e) => DisplayKey(new FloatKey());
    private void AddBoolKey(object sender, RoutedEventArgs e) => DisplayKey(new BoolKey());
    private void AddListKey(object sender, RoutedEventArgs e) => DisplayKey(new ListKey());
    private void AddDictionaryKey(object sender, RoutedEventArgs e) => DisplayKey(new DictionaryKey());

    private void DisplayKey(Key key, bool addToCollection = true)
    {
        if (addToCollection)
        {
            conditionalCase.Keys.Add(key);
        }

        var viewer = new KeyViewer(key)
        {
            Margin = new Thickness(0, 4, 0, 0)
        };
        viewer.OnDeleted += (s, e) => RemoveKey(key, viewer);
        keysPanel.Children.Add(viewer);
    }

    private void RemoveKey(Key key, KeyViewer viewer)
    {
        conditionalCase.Keys.Remove(key);
        keysPanel.Children.Remove(viewer);
    }

    private void Delete(object sender, RoutedEventArgs e) => OnDeleted?.Invoke(this, EventArgs.Empty);
    private void MoveUp(object sender, RoutedEventArgs e) => OnMoveUp?.Invoke(this, EventArgs.Empty);
    private void MoveDown(object sender, RoutedEventArgs e) => OnMoveDown?.Invoke(this, EventArgs.Empty);
}
