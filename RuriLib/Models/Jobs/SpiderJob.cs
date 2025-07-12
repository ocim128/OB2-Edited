using RuriLib.Logging;
using RuriLib.Services;

namespace RuriLib.Models.Jobs;

public class SpiderJob(RuriLibSettingsService settings, PluginRepository pluginRepo, IJobLogger logger = null) : Job(settings, pluginRepo, logger)
{
}
