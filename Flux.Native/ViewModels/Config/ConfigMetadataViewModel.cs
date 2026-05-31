using Flux.Core.Helpers;
using Flux.Core.Services;
using Flux.Native.Utils;
using RuriLib.Models.Configs;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Flux.Native.ViewModels.Base;


namespace Flux.Native.ViewModels.Configs
{
    public class ConfigMetadataViewModel(ConfigService configService) : ViewModelBase
    {
        private string iconSourceBase64;
        private BitmapImage icon;

        private static readonly Lazy<HttpClient> SharedClient = new(() =>
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Flux-Native/1.0");
            return client;
        });

        private Config Config => configService.SelectedConfig;

        public string Name
        {
            get => Config?.Metadata.Name;
            set
            {
                Config.Metadata.Name = value;
                OnPropertyChanged();
            }
        }

        public string Author
        {
            get => Config?.Metadata.Author;
            set
            {
                Config.Metadata.Author = value;
                OnPropertyChanged();
            }
        }

        public string Category
        {
            get => Config?.Metadata.Category;
            set
            {
                Config.Metadata.Category = value;
                OnPropertyChanged();
            }
        }

        public BitmapImage Icon
        {
            get
            {
                if (Config is null)
                {
                    return null;
                }

                var base64 = Config.Metadata.Base64Image ?? string.Empty;
                if (iconSourceBase64 != base64)
                {
                    icon = Images.Base64ToBitmapImage(base64);
                    iconSourceBase64 = base64;
                }

                return icon;
            }
        }

        public void SetIconFromFile(string fileName)
        {
            var bytes = ImageEditor.ToCompatibleFormat(File.ReadAllBytes(fileName));

            var base64 = Convert.ToBase64String(bytes);
            Config.Metadata.Base64Image = base64;
            OnPropertyChanged(nameof(Icon));
        }

        public async Task SetIconFromUrlAsync(string url)
        {
            using var response = await SharedClient.Value.GetAsync(url).ConfigureAwait(false);
            var bytes = ImageEditor.ToCompatibleFormat(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));

            var base64 = Convert.ToBase64String(bytes);
            Config.Metadata.Base64Image = base64;
            OnPropertyChanged(nameof(Icon));
        }
    }
}

