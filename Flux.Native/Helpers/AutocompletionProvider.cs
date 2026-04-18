using Flux.Core.Services;
using RuriLib;
using RuriLib.Helpers.Blocks;
using RuriLib.Models.Blocks;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;


namespace Flux.Native.Helpers
{
    public static class AutocompletionProvider
    {
        private static List<Snippet> snippets = new();
        private static bool _initialized;

        public static void Init()
        {
            if (_initialized) return;

            // Block snippets
            foreach (var id in Globals.DescriptorsRepository.Descriptors.Keys)
            {
                var block = BlockFactory.GetBlock<BlockInstance>(id);
                snippets.Add(new Snippet($"BLOCK:{id}", $"BLOCK:{id}\r\n{block.ToLC(true)}ENDBLOCK", block.Descriptor.Description));
            }

            // Custom snippets
            foreach (var snippet in App.ServiceProvider.GetRequiredService<FluxSettingsService>().Settings.GeneralSettings.CustomSnippets)
            {
                if (!string.IsNullOrEmpty(snippet.Name))
                {
                    snippets.Add(new Snippet(snippet.Name, snippet.Body, snippet.Description));
                }
            }

            _initialized = true;
        }

        public static IReadOnlyList<Snippet> GetSnippets()
            => snippets.AsReadOnly();
    }

    public readonly struct Snippet
    {
        public string Id { get; init; }
        public string Body { get; init; }
        public string Description { get; init; }

        public Snippet(string id, string body, string description)
        {
            Id = id;
            Body = body;
            Description = description;
        }
    }
}
