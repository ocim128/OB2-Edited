using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OpenBullet2.Native.ViewModels;
using RuriLib.Models.Blocks.Custom;

namespace OpenBullet2.Native.Controls;

public partial class ConditionalConstantStringViewer : UserControl
{
    private BlockViewModel blockVm;
    private ConditionalConstantStringBlockInstance Block => blockVm?.Block as ConditionalConstantStringBlockInstance;

    public BlockViewModel BlockVM
    {
        get => blockVm;
        set
        {
            blockVm = value;
            InitializeBlock();
        }
    }

    public ConditionalConstantStringViewer()
    {
        InitializeComponent();
    }

    private void InitializeBlock()
    {
        if (Block == null)
        {
            return;
        }

        if (Block.Settings.TryGetValue("value", out var setting))
        {
            defaultValueViewer.Setting = setting;
        }

        ReloadCases();
    }

    private void ReloadCases()
    {
        casesPanel.Children.Clear();
        if (Block == null)
        {
            return;
        }

        foreach (var conditionalCase in Block.ConditionalCases)
        {
            SpawnCaseViewer(conditionalCase);
        }
    }

    private void SpawnCaseViewer(ConditionalConstantStringCase conditionalCase)
    {
        var viewer = new ConditionalCaseViewer(conditionalCase)
        {
            Margin = new Thickness(0, 0, 0, 8)
        };

        viewer.OnDeleted += (s, e) => DeleteCase(conditionalCase, viewer);
        viewer.OnMoveUp += (s, e) => MoveCase(conditionalCase, -1);
        viewer.OnMoveDown += (s, e) => MoveCase(conditionalCase, 1);

        casesPanel.Children.Add(viewer);
    }

    private void DeleteCase(ConditionalConstantStringCase conditionalCase, ConditionalCaseViewer viewer)
    {
        if (Block == null)
        {
            return;
        }

        Block.ConditionalCases.Remove(conditionalCase);
        casesPanel.Children.Remove(viewer);
    }

    private void MoveCase(ConditionalConstantStringCase conditionalCase, int delta)
    {
        if (Block == null)
        {
            return;
        }

        var index = Block.ConditionalCases.IndexOf(conditionalCase);
        var newIndex = index + delta;

        if (newIndex < 0 || newIndex >= Block.ConditionalCases.Count)
        {
            return;
        }

        Block.ConditionalCases.RemoveAt(index);
        Block.ConditionalCases.Insert(newIndex, conditionalCase);
        ReloadCases();
    }

    private void AddCondition(object sender, RoutedEventArgs e)
    {
        if (Block == null)
        {
            return;
        }

        var conditionalCase = new ConditionalConstantStringCase
        {
            Name = $"Condition {Block.ConditionalCases.Count + 1}"
        };

        Block.ConditionalCases.Add(conditionalCase);
        ReloadCases();
    }
}
