using Microsoft.Extensions.DependencyInjection;
using OpenBullet2.Core.Services;
using OpenBullet2.Native.Views.Pages;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;


namespace OpenBullet2.Native.Views.Dialogs.Job
{
    /// <summary>
    /// Interaction logic for ChangeBotsDialog.xaml
    /// </summary>
    public partial class ChangeBotsDialog : Page
    {
        private readonly object caller;

        public ChangeBotsDialog(object caller, int oldValue)
        {
            this.caller = caller;

            InitializeComponent();
            bots.Maximum = App.ServiceProvider.GetRequiredService<JobFactoryService>().BotLimit;
            bots.Value = oldValue;
        }

        private void Accept(object sender, RoutedEventArgs e)
        {
            if (caller is MultiRunJobViewer mr)
            {
                mr.ChangeBots((int)bots.Value);
            }
            else if (caller is ProxyCheckJobViewer pc)
            {
                pc.ChangeBots((int)bots.Value);
            }

            ((MainDialog)Parent).Close();
        }
    }
}
