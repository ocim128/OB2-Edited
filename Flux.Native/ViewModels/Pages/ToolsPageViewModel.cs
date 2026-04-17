using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Flux.Native.ViewModels.Base;
using Flux.Native.ViewModels.Tools;

namespace Flux.Native.ViewModels.Pages
{
    /// <summary>
    /// Root view model for the Tools dashboard. Owns per-tool view models.
    /// </summary>
    public sealed class ToolsPageViewModel : ViewModelBase, IDisposable
    {
        private const string AllCategoriesLabel = "All categories";
        private readonly IReadOnlyList<ToolCardViewModelBase> toolCards;
        private readonly RelayCommand resetFiltersCommand;
        private string searchText = string.Empty;
        private string selectedCategory = AllCategoriesLabel;
        private string filterStatus = string.Empty;
        private bool hasNoMatches;
        private bool disposed;

        public ToolsPageViewModel()
            : this(
                new OtpToolViewModel(),
                new BookmarkletToolViewModel(),
                new TextCleanerToolViewModel(),
                new FirefoxToolViewModel(),
                new LineReducerToolViewModel())
        {
        }

        internal ToolsPageViewModel(
            OtpToolViewModel otpTool,
            BookmarkletToolViewModel bookmarkletTool,
            TextCleanerToolViewModel textCleanerTool,
            FirefoxToolViewModel firefoxTool,
            LineReducerToolViewModel lineReducerTool)
        {
            OtpTool = otpTool ?? throw new ArgumentNullException(nameof(otpTool));
            BookmarkletTool = bookmarkletTool ?? throw new ArgumentNullException(nameof(bookmarkletTool));
            TextCleanerTool = textCleanerTool ?? throw new ArgumentNullException(nameof(textCleanerTool));
            FirefoxTool = firefoxTool ?? throw new ArgumentNullException(nameof(firefoxTool));
            LineReducerTool = lineReducerTool ?? throw new ArgumentNullException(nameof(lineReducerTool));

            toolCards = new ToolCardViewModelBase[]
            {
                OtpTool,
                BookmarkletTool,
                TextCleanerTool,
                FirefoxTool,
                LineReducerTool
            };

            Categories = new[] { AllCategoriesLabel }
                .Concat(toolCards.Select(card => card.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(category => category))
                .ToArray();

            resetFiltersCommand = new RelayCommand(ResetFilters, () => AreFiltersActive);
            ApplyToolFilters();
        }

        public OtpToolViewModel OtpTool { get; }

        public BookmarkletToolViewModel BookmarkletTool { get; }

        public TextCleanerToolViewModel TextCleanerTool { get; }

        public FirefoxToolViewModel FirefoxTool { get; }

        public LineReducerToolViewModel LineReducerTool { get; }

        public IReadOnlyList<string> Categories { get; }

        public RelayCommand ResetFiltersCommand => resetFiltersCommand;

        public string SearchText
        {
            get => searchText;
            set
            {
                if (SetProperty(ref searchText, value ?? string.Empty))
                {
                    ApplyToolFilters();
                }
            }
        }

        public string SelectedCategory
        {
            get => selectedCategory;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? AllCategoriesLabel : value;
                if (SetProperty(ref selectedCategory, normalized))
                {
                    ApplyToolFilters();
                }
            }
        }

        public string FilterStatus
        {
            get => filterStatus;
            private set
            {
                if (SetProperty(ref filterStatus, value))
                {
                    OnPropertyChanged(nameof(FilterStatusVisibility));
                }
            }
        }

        public Visibility FilterStatusVisibility => string.IsNullOrWhiteSpace(FilterStatus) ? Visibility.Collapsed : Visibility.Visible;

        public bool HasNoMatches
        {
            get => hasNoMatches;
            private set
            {
                if (SetProperty(ref hasNoMatches, value))
                {
                    OnPropertyChanged(nameof(NoMatchesVisibility));
                }
            }
        }

        public Visibility NoMatchesVisibility => HasNoMatches ? Visibility.Visible : Visibility.Collapsed;

        public bool AreFiltersActive => !string.IsNullOrWhiteSpace(SearchText) ||
            !string.Equals(SelectedCategory, AllCategoriesLabel, StringComparison.OrdinalIgnoreCase);

        public ToolCardViewModelBase? GetToolByAlias(string alias)
            => toolCards.FirstOrDefault(card => card.HasAlias(alias));

        public void ResetFilters()
        {
            SearchText = string.Empty;
            SelectedCategory = AllCategoriesLabel;
        }

        public async Task CleanupAsync()
        {
            if (disposed)
            {
                return;
            }

            await FirefoxTool.CleanupAsync().ConfigureAwait(false);
            Dispose();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            OtpTool.Dispose();
            FirefoxTool.Dispose();
            LineReducerTool.Dispose();
        }

        private void ApplyToolFilters()
        {
            var searchTerms = string.IsNullOrWhiteSpace(SearchText)
                ? Array.Empty<string>()
                : SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var filterByCategory = !string.IsNullOrWhiteSpace(SelectedCategory) &&
                                   !string.Equals(SelectedCategory, AllCategoriesLabel, StringComparison.OrdinalIgnoreCase);

            var visibleCount = 0;
            foreach (var toolCard in toolCards)
            {
                var matchesCategory = !filterByCategory || toolCard.IsInCategory(SelectedCategory);
                var matchesSearch = searchTerms.Length == 0 || toolCard.MatchesSearchTerms(searchTerms);
                var visible = matchesCategory && matchesSearch;
                toolCard.SetVisible(visible);

                if (visible)
                {
                    visibleCount++;
                }
            }

            FilterStatus = !AreFiltersActive
                ? string.Empty
                : visibleCount switch
                {
                    0 => "No tools matched your filters.",
                    _ when visibleCount == toolCards.Count => $"All {toolCards.Count} tools are visible.",
                    _ => $"Showing {visibleCount} of {toolCards.Count} tools."
                };

            HasNoMatches = visibleCount == 0;
            resetFiltersCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(AreFiltersActive));
        }
    }
}
