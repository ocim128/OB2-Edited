using OpenBullet2.Core.Services;
using RuriLib.Models.Configs;
using OpenBullet2.Native.Infrastructure.DependencyInjection;

namespace OpenBullet2.Native.ViewModels
{
    public class ConfigReadmeViewModel : OpenBullet2.Native.ViewModels.Infrastructure.ViewModelBase
    {
        private readonly ConfigService configService;
        private Config Config => configService.SelectedConfig;

        public string Readme
        {
            get => Config?.Readme;
            set
            {
                Config.Readme = value;
                OnPropertyChanged();
            }
        }

        public ConfigReadmeViewModel()
        {
            configService = ServiceLocator.GetService<ConfigService>();
        }
    }
}
