using Microsoft.EntityFrameworkCore;
using Flux.Core.Entities;
using Flux.Core.Repositories;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using Flux.Native.ViewModels.Base;


namespace Flux.Native.ViewModels.Data;

    public class WordlistsViewModel : ViewModelBase
{
    private readonly IWordlistRepository wordlistRepo;
    private bool initialized;

    private ObservableCollection<WordlistEntity> wordlistsCollection;
    public ObservableCollection<WordlistEntity> WordlistsCollection
    {
        get => wordlistsCollection;
        private set
        {
            wordlistsCollection = value;
            OnPropertyChanged();
        }
    }

    public int Total => WordlistsCollection.Count;

    private string searchString = string.Empty;
    public string SearchString
    {
        get => searchString;
        set
        {
            if (value == searchString) return;
            searchString = value;
            OnPropertyChanged();
            CollectionViewSource.GetDefaultView(WordlistsCollection).Refresh();
            OnPropertyChanged(nameof(Total));
        }
    }

    public WordlistsViewModel(IWordlistRepository wordlistRepository)
    {
        wordlistRepo = wordlistRepository ?? throw new ArgumentNullException(nameof(wordlistRepository));
        WordlistsCollection = [];
    }

    public async Task InitializeAsync()
    {
        if (!initialized)
        {
            await RefreshListAsync().ConfigureAwait(false);
            initialized = true;
        }
    }

    private void HookFilters()
    {
        var view = (CollectionView)CollectionViewSource.GetDefaultView(WordlistsCollection);
        view.Filter = WordlistsFilter;
    }

    private bool WordlistsFilter(object item) => (item as WordlistEntity)?.Name?.Contains(searchString, StringComparison.OrdinalIgnoreCase) == true;

    public WordlistEntity GetWordlistByName(string name) => WordlistsCollection.First(w => w.Name == name);

    public Task AddAsync(WordlistEntity wordlist)
    {
        if (WordlistsCollection.Any(w => w.FileName == wordlist.FileName))
        {
            throw new InvalidOperationException($"Wordlist already present: {wordlist.FileName}");
        }

        WordlistsCollection.Add(wordlist);
        return wordlistRepo.AddAsync(wordlist);
    }

    public async Task RefreshListAsync()
    {
        var items = await wordlistRepo.GetAll().ToListAsync().ConfigureAwait(false);
        WordlistsCollection = new ObservableCollection<WordlistEntity>(items);
        HookFilters();
    }

    public async Task UpdateAsync(WordlistEntity wordlist) => await wordlistRepo.UpdateAsync(wordlist).ConfigureAwait(false);

    public async Task DeleteAsync(WordlistEntity wordlist)
    {
        _ = WordlistsCollection.Remove(wordlist);
        await wordlistRepo.DeleteAsync(wordlist, false).ConfigureAwait(false);
        OnPropertyChanged(nameof(Total));
    }

    public void DeleteAll()
    {
        WordlistsCollection.Clear();
        wordlistRepo.Purge();
        OnPropertyChanged(nameof(Total));
    }

    public async Task<int> DeleteNotFoundAsync()
    {
        var deleted = 0;

        for (var i = WordlistsCollection.Count - 1; i >= 0; i--)
        {
            var wordlist = WordlistsCollection[i];

            if (!File.Exists(wordlist.FileName))
            {
                await DeleteAsync(wordlist).ConfigureAwait(false);
                deleted++;
            }
        }

        return deleted;
    }
}


