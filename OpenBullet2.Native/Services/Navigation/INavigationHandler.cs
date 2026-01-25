using OpenBullet2.Native.Enums;
using OpenBullet2.Native.ViewModels.Jobs;
using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using OpenBullet2.Native.Services;

namespace OpenBullet2.Native.Services.Navigation;

public interface INavigationHandler
{
    System.Windows.Controls.Page CurrentPage { get; }
    Task NavigateTo(MainWindowPage page);
    void DisplayJob(JobViewModel jobVM);
    void EditJob(JobViewModel jobVM);
    event EventHandler<NavigationEventArgs> Navigated;
}
