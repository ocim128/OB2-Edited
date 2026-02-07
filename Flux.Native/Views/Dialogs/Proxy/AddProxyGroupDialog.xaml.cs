using Flux.Core.Entities;
using Flux.Native.Helpers;
using Flux.Native.Views.Pages;
using Flux.Native.Views.Pages.Data;
using System.Windows;
using System.Windows.Controls;

namespace Flux.Native.Views.Dialogs.Proxy
{
    /// <summary>
    /// Interaction logic for AddProxyGroupDialog.xaml
    /// </summary>
    public partial class AddProxyGroupDialog : Page
    {
        private readonly object caller;
        private ProxyGroupEntity entity;

        public AddProxyGroupDialog(object caller)
        {
            this.caller = caller;
            InitializeComponent();
        }

        /// <summary>
        /// Use this constructor for edit mode.
        /// </summary>
        public AddProxyGroupDialog(object caller, ProxyGroupEntity entity)
        {
            this.caller = caller;
            InitializeComponent();

            this.entity = entity;
            nameTextbox.Text = entity.Name;
        }

        private void Accept(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(nameTextbox.Text))
            {
                Alert.Error("Invalid name", "The name cannot be blank");
                return;
            }

            if (caller is Proxies page)
            {
                if (entity is null)
                {
                    page.AddGroup(new ProxyGroupEntity { Name = nameTextbox.Text });
                }
                else
                {
                    entity.Name = nameTextbox.Text;
                    page.EditGroup(entity);
                }
            }

            ((MainDialog)Parent).Close();
        }
    }
}
