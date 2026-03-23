using System;
using System.Windows;
using System.Windows.Controls;

namespace Flux.Native.Views.Dialogs.Job.Components
{
    public partial class StartConditionSection : UserControl
    {
        public StartConditionSection()
        {
            InitializeComponent();
        }

        // Logic for binding startConditionTabControl index to radio buttons is handled by binding
        // or could be moved here if it was predominantly code-behind.
        // In the original file, it seems the TabControl might have been switched via code-behind or triggers.
        // Let's check the original code-behind in next steps and migrate logic if needed.
        // For now, setting up the basic class structure.
    }
}
