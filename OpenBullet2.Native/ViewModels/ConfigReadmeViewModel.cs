using OpenBullet2.Core.Services;
using RuriLib.Models.Configs;
using System;
using OpenBullet2.Native.ViewModels.Base;


namespace OpenBullet2.Native.ViewModels
{
    public class ConfigReadmeViewModel : ViewModelBase
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

        public ConfigReadmeViewModel(ConfigService configService)
        {
            this.configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }
    }
}
