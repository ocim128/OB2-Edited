using OpenBullet2.Native.ViewModels;
using RuriLib.Models.Blocks.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.IconPacks;

namespace OpenBullet2.Native.Controls
{
    /// <summary>
    /// Interaction logic for CreateMultipleConstantViewer.xaml
    /// </summary>
    public partial class CreateMultipleConstantViewer : UserControl
    {
        private CreateMultipleConstantViewerViewModel vm;
        private readonly List<VariableEntryControl> variableEntries = new();

        public BlockViewModel BlockVM
        {
            get => vm?.BlockVM;
            set
            {
                vm = new CreateMultipleConstantViewerViewModel(value);
                DataContext = vm;
                LoadExistingVariables();
            }
        }

        public CreateMultipleConstantViewer()
        {
            InitializeComponent();
        }

        private void LoadExistingVariables()
        {
            VariablesPanel.Children.Clear();
            variableEntries.Clear();

            // Load existing variables from the block settings
            for (int i = 1; i <= 5; i++)
            {
                var varNameKey = $"variableName{i}";
                var valueKey = $"value{i}";

                if (vm.BlockVM.Block.Settings.TryGetValue(varNameKey, out var varNameSetting) &&
                    vm.BlockVM.Block.Settings.TryGetValue(valueKey, out var valueSetting))
                {
                    var varName = (varNameSetting.FixedSetting as StringSetting)?.Value ?? "";
                    var value = (valueSetting.FixedSetting as StringSetting)?.Value ?? "";

                    if (!string.IsNullOrWhiteSpace(varName) || !string.IsNullOrWhiteSpace(value))
                    {
                        AddVariableEntry(varName, value, i);
                    }
                }
            }

            // If no variables exist, add one empty entry
            if (variableEntries.Count == 0)
            {
                AddVariableEntry("", "", 1);
            }
        }

        private void AddVariable_Click(object sender, RoutedEventArgs e)
        {
            int nextIndex = GetNextAvailableIndex();
            if (nextIndex <= 5)
            {
                AddVariableEntry("", "", nextIndex);
            }
            else
            {
                MessageBox.Show("Maximum of 5 variables allowed.", "Limit Reached", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void AddVariableEntry(string variableName, string value, int index)
        {
            var entry = new VariableEntryControl(index, variableName, value);
            entry.VariableChanged += OnVariableChanged;
            entry.RemoveRequested += OnRemoveRequested;

            variableEntries.Add(entry);
            VariablesPanel.Children.Add(entry);
        }

        private void OnVariableChanged(object sender, VariableChangedEventArgs e)
        {
            var entry = sender as VariableEntryControl;
            if (entry != null)
            {
                UpdateBlockSetting($"variableName{entry.Index}", e.VariableName);
                UpdateBlockSetting($"value{entry.Index}", e.Value);
            }
        }

        private void OnRemoveRequested(object sender, EventArgs e)
        {
            var entry = sender as VariableEntryControl;
            if (entry != null && variableEntries.Count > 1)
            {
                RemoveVariableEntry(entry);
            }
            else if (variableEntries.Count == 1)
            {
                // Clear the last entry instead of removing it
                entry.ClearValues();
            }
        }

        private void RemoveVariableEntry(VariableEntryControl entry)
        {
            variableEntries.Remove(entry);
            VariablesPanel.Children.Remove(entry);

            // Clear the block setting
            UpdateBlockSetting($"variableName{entry.Index}", "");
            UpdateBlockSetting($"value{entry.Index}", "");

            // Reindex remaining entries
            ReindexEntries();
        }

        private void ReindexEntries()
        {
            // Clear all settings first
            for (int i = 1; i <= 5; i++)
            {
                UpdateBlockSetting($"variableName{i}", "");
                UpdateBlockSetting($"value{i}", "");
            }

            // Reassign indices and update settings
            for (int i = 0; i < variableEntries.Count; i++)
            {
                var entry = variableEntries[i];
                entry.Index = i + 1;
                UpdateBlockSetting($"variableName{entry.Index}", entry.VariableName);
                UpdateBlockSetting($"value{entry.Index}", entry.Value);
            }
        }



        private void UpdateBlockSetting(string settingName, string value)
        {
            if (vm.BlockVM.Block.Settings.TryGetValue(settingName, out var setting))
            {
                var strSetting = setting.FixedSetting as StringSetting;
                if (strSetting != null)
                {
                    strSetting.Value = value;
                }
            }
        }

        private int GetNextAvailableIndex()
        {
            for (int i = 1; i <= 5; i++)
            {
                if (!variableEntries.Any(e => e.Index == i))
                {
                    return i;
                }
            }
            return 6; // Indicates no available index
        }


    }

    public class CreateMultipleConstantViewerViewModel : ViewModelBase
    {
        public BlockViewModel BlockVM { get; init; }

        public CreateMultipleConstantViewerViewModel(BlockViewModel blockVM)
        {
            BlockVM = blockVM;
        }
    }

    public class VariableEntryControl : UserControl
    {
        public event EventHandler<VariableChangedEventArgs> VariableChanged;
        public event EventHandler RemoveRequested;

        private TextBox variableNameTextBox;
        private TextBox valueTextBox;
        private Button interpolatedToggle;
        private Button lockToggle;
        private Button removeButton;
        private bool isInterpolatedMode;
        private bool isLocked;

        public int Index { get; set; }
        public string VariableName => variableNameTextBox?.Text ?? "";
        public string Value => valueTextBox?.Text ?? "";

        public VariableEntryControl(int index, string variableName, string value)
        {
            Index = index;
            isInterpolatedMode = false;
            isLocked = true; // Lock is active by default
            CreateUI();
            variableNameTextBox.Text = variableName;
            valueTextBox.Text = value;
        }

        private void CreateUI()
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 4, 0, 4),
                Background = System.Windows.Media.Brushes.Transparent
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); // Interpolated toggle
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); // Lock toggle
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); // Remove button

            // Variable Name TextBox
            variableNameTextBox = new TextBox
            {
                Style = FindResource("MatchingTextBox") as Style,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Variable name (e.g., myVar)"
            };
            variableNameTextBox.TextChanged += OnTextChanged;
            Grid.SetColumn(variableNameTextBox, 0);
            grid.Children.Add(variableNameTextBox);

            // Value TextBox
            valueTextBox = new TextBox
            {
                Style = FindResource("MatchingTextBox") as Style,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Variable value (supports <variable> interpolation)"
            };
            valueTextBox.TextChanged += OnTextChanged;
            Grid.SetColumn(valueTextBox, 1);
            grid.Children.Add(valueTextBox);

            // Apply initial styling
            UpdateValueTextBoxStyle();

            // Interpolated Mode Toggle
            interpolatedToggle = new Button
            {
                Width = 32,
                Height = 32,
                BorderThickness = new Thickness(0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)),
                Foreground = System.Windows.Media.Brushes.White,
                Content = new PackIconEntypo { Kind = PackIconEntypoKind.Code, Width = 16, Height = 16 },
                ToolTip = "Toggle interpolated mode for this variable",
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(4, 0, 4, 0)
            };
            interpolatedToggle.Click += OnInterpolatedToggleClick;
            Grid.SetColumn(interpolatedToggle, 2);
            grid.Children.Add(interpolatedToggle);

            // Lock Toggle
            lockToggle = new Button
            {
                Width = 32,
                Height = 32,
                BorderThickness = new Thickness(0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)),
                Foreground = System.Windows.Media.Brushes.White,
                Content = new PackIconMaterial { Kind = PackIconMaterialKind.Lock, Width = 16, Height = 16 },
                ToolTip = "Toggle lock to prevent accidental removal",
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(4, 0, 4, 0)
            };
            lockToggle.Click += OnLockToggleClick;
            Grid.SetColumn(lockToggle, 3);
            grid.Children.Add(lockToggle);

            // Remove Button
            removeButton = new Button
            {
                Width = 32,
                Height = 32,
                BorderThickness = new Thickness(0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)),
                Foreground = System.Windows.Media.Brushes.White,
                Content = new PackIconMaterial { Kind = PackIconMaterialKind.Close, Width = 16, Height = 16 },
                ToolTip = "Remove this variable",
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(4, 0, 0, 0)
            };
            removeButton.Click += OnRemoveClick;
            Grid.SetColumn(removeButton, 4);
            grid.Children.Add(removeButton);

            UpdateToggleStates();

            Content = grid;
        }

        private void OnInterpolatedToggleClick(object sender, RoutedEventArgs e)
        {
            isInterpolatedMode = !isInterpolatedMode;
            UpdateToggleStates();
            UpdateValueTextBoxStyle();
        }

        private void OnLockToggleClick(object sender, RoutedEventArgs e)
        {
            isLocked = !isLocked;
            UpdateToggleStates();
        }

        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            VariableChanged?.Invoke(this, new VariableChangedEventArgs(VariableName, Value));
        }

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            if (!isLocked)
            {
                RemoveRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void UpdateToggleStates()
        {
            if (interpolatedToggle != null)
            {
                interpolatedToggle.Background = isInterpolatedMode
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105)) // Green when active
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)); // Gray when inactive
            }

            if (lockToggle != null)
            {
                lockToggle.Background = isLocked
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38)) // Red when locked
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)); // Gray when unlocked
            }

            if (removeButton != null)
            {
                removeButton.IsEnabled = !isLocked;
                removeButton.Opacity = isLocked ? 0.5 : 1.0;
            }
        }

        public void ClearValues()
        {
            variableNameTextBox.Text = "";
            valueTextBox.Text = "";
        }

        private void UpdateValueTextBoxStyle()
        {
            if (valueTextBox != null)
            {
                if (isInterpolatedMode)
                {
                    // Green styling for interpolated mode
                    valueTextBox.Style = FindResource("GreenTextBox") as Style;
                }
                else
                {
                    // Default modern styling
                    valueTextBox.Style = FindResource("MatchingTextBox") as Style;
                }
            }
        }
    }

    public class VariableChangedEventArgs : EventArgs
    {
        public string VariableName { get; }
        public string Value { get; }

        public VariableChangedEventArgs(string variableName, string value)
        {
            VariableName = variableName;
            Value = value;
        }
    }
}