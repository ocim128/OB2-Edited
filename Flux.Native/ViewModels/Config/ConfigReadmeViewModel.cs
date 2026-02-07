using Flux.Core.Services;
using RuriLib.Models.Configs;
using System;
using Flux.Native.ViewModels.Base;


namespace Flux.Native.ViewModels.Configs;

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



