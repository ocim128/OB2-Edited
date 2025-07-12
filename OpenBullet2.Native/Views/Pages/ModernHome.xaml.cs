using System.Windows.Controls;

namespace OpenBullet2.Native.Views.Pages
{
    public partial class ModernHome : Page
    {
        private readonly HomeViewModel vm;

        public ModernHome()
        {
            InitializeComponent();
            
            vm = new HomeViewModel();
            DataContext = vm;
        }
    }
} 