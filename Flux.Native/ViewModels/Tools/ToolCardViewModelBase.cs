using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Flux.Native.ViewModels.Base;

namespace Flux.Native.ViewModels.Tools;

public abstract class ToolCardViewModelBase : ViewModelBase
{
    private readonly string[] aliases;
    private readonly string searchHaystack;
    private bool isVisible = true;

    protected ToolCardViewModelBase(string title, string category, params string[] keywords)
    {
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Category = category ?? throw new ArgumentNullException(nameof(category));

        aliases = new[] { title, category }
            .Concat(keywords ?? Array.Empty<string>())
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => alias.Trim())
            .ToArray();

        searchHaystack = string.Join(' ', aliases);
    }

    public string Title { get; }

    public string Category { get; }

    public bool IsVisible
    {
        get => isVisible;
        private set
        {
            if (SetProperty(ref isVisible, value))
            {
                OnPropertyChanged(nameof(CardVisibility));
            }
        }
    }

    public Visibility CardVisibility => IsVisible ? Visibility.Visible : Visibility.Collapsed;

    public bool HasAlias(string alias)
        => !string.IsNullOrWhiteSpace(alias) &&
           aliases.Any(a => a.Equals(alias, StringComparison.OrdinalIgnoreCase));

    public bool IsInCategory(string category)
        => string.Equals(Category, category, StringComparison.OrdinalIgnoreCase);

    public bool MatchesSearchTerms(IEnumerable<string> searchTerms)
    {
        if (searchTerms is null)
        {
            return true;
        }

        foreach (var term in searchTerms)
        {
            if (!searchHaystack.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public void SetVisible(bool visible) => IsVisible = visible;
}
