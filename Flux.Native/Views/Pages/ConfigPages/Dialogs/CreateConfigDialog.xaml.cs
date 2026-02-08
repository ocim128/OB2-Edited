using Flux.Core.Services;
using Flux.Native.DTOs;
using Flux.Native.Helpers;
using Flux.Native.Views.Pages;
using Flux.Native.Views.Pages.Configs;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;


namespace Flux.Native.Views.Dialogs.Config
{
    /// <summary>
    /// Interaction logic for CreateConfigDialog.xaml
    /// </summary>
    public partial class CreateConfigDialog : Page
    {
        private readonly object caller;

        public CreateConfigDialog(object caller)
        {
            InitializeComponent();
            this.caller = caller;

            var settings = App.ServiceProvider.GetRequiredService<FluxSettingsService>().Settings;
            authorTextbox.Text = settings.GeneralSettings.DefaultAuthor;
            nameTextbox.Focus();

            categoryCombobox.Items.Add("Default");

            var categories = App.ServiceProvider.GetRequiredService<ConfigService>().Configs
                .Select(c => c.Metadata.Category)
                .Where(category => category != "Default")
                .Distinct();

            foreach (var category in categories)
            {
                categoryCombobox.Items.Add(category);
            }

            categoryCombobox.SelectedIndex = 0;
        }

        private void CreateAndClose()
        {
            if (caller is Configs page)
            {
                var dto = new ConfigForCreationDto
                {
                    Name = nameTextbox.Text,
                    Category = categoryCombobox.Text,
                    Author = authorTextbox.Text
                };

                // Check if name is ok
                if (string.IsNullOrWhiteSpace(dto.Name))
                {
                    Alert.Error("Invalid name", "The name cannot be blank");
                    return;
                }

                page.CreateConfig(dto);
            }
            ((MainDialog)Parent).Close();
        }

        private void Accept(object sender, RoutedEventArgs e) => CreateAndClose();

        private void Cancel(object sender, RoutedEventArgs e) => ((MainDialog)Parent).Close();

        private void TextboxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CreateAndClose();
            }
        }
    }
}
