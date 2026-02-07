using Flux.Native.Enums;
using Flux.Native.ViewModels.Jobs;
using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using Flux.Native.Services;

namespace Flux.Native.Services.Navigation;

public interface INavigationHandler
{
    System.Windows.Controls.Page CurrentPage { get; }
    Task NavigateTo(MainWindowPage page);
    void DisplayJob(JobViewModel jobVM);
    void EditJob(JobViewModel jobVM);
    event EventHandler<NavigationEventArgs> Navigated;
}
