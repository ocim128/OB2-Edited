using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Flux.Core.Models.Jobs;
using Flux.Core.Services;
using Flux.Native.ViewModels.Base;
using Flux.Shared.Models;
using RuriLib.Models.Jobs;
using RuriLib.Models.Jobs.StartConditions;
using RuriLib.Models.Proxies;
using RuriLib.Parallelization.Models;

namespace Flux.Native.ViewModels.Jobs;

public class ProxyCheckJobViewerViewModel : ViewModelBase, IDisposable
{
        private readonly Timer secondsTicker;
        private readonly ProxyCheckJob proxyCheckJob;

        public event Action<object, string, Color> NewMessage;

        public ProxyCheckJobViewModel Job { get; set; }
        private ProxyCheckJob ProxyCheckJob => proxyCheckJob;

        #region Properties that need to be updated every second
        public string RemainingWaitString => ProxyCheckJob.StartCondition switch
        {
            RelativeTimeStartCondition r => (ProxyCheckJob.StartTime + r.StartAfter - DateTime.Now).ToString(@"hh\:mm\:ss"),
            AbsoluteTimeStartCondition a => (a.StartAt - DateTime.Now).ToString(@"hh\:mm\:ss"),
            _ => throw new NotImplementedException()
        };
        #endregion

        #region Properties that need to be updated when the status changes
        public bool CanStart => ProxyCheckJob.Status is JobStatus.Idle;
        public bool CanSkipWait => ProxyCheckJob.Status is JobStatus.Waiting;
        public bool CanPause => ProxyCheckJob.Status is JobStatus.Running;
        public bool CanResume => ProxyCheckJob.Status is JobStatus.Paused;
        public bool CanStop => ProxyCheckJob.Status is JobStatus.Running or JobStatus.Paused;
        public bool CanAbort => ProxyCheckJob.Status is JobStatus.Running or JobStatus.Paused or JobStatus.Pausing or JobStatus.Stopping;

        public bool IsStopping => ProxyCheckJob.Status is JobStatus.Stopping;
        public bool IsWaiting => ProxyCheckJob.Status is JobStatus.Waiting;
        public bool IsPausing => ProxyCheckJob.Status is JobStatus.Pausing;
        #endregion

        #region Properties that need to be updated when a new result comes in
        public double Progress => Math.Clamp(ProxyCheckJob.Progress * 100, 0, 100);
        #endregion

        public ProxyCheckJobViewerViewModel(ProxyCheckJobViewModel jobVM, JobManagerService jobManager)
        {
            Job = jobVM;
            proxyCheckJob = jobManager.Jobs.OfType<ProxyCheckJob>().First(job => job.Id == jobVM.Id);
            RefreshJobSnapshot();

            #region Bind events and timers
            ProxyCheckJob.OnCompleted += UpdateOnCompleted;
            ProxyCheckJob.OnResult += UpdateViewModel;
            ProxyCheckJob.OnStatusChanged += UpdateStatus;
            ProxyCheckJob.OnProgress += UpdateViewModel;

            ProxyCheckJob.OnResult += OnResult;
            ProxyCheckJob.OnTaskError += OnTaskError;
            ProxyCheckJob.OnError += OnError;

            secondsTicker = new Timer(new TimerCallback(_ => PeriodicUpdate()), null, 1000, 1000);
            #endregion
        }

        #region Update methods
        // Periodic update for stuff that needs to be updated every second
        private void PeriodicUpdate()
        {
            if (ProxyCheckJob.Status == JobStatus.Waiting)
            {
                OnPropertyChanged(nameof(RemainingWaitString));
            }

            RefreshJobSnapshot();
        }

        // Updates everything (only when a job completes, just to be safe, not expensive)
        private void UpdateOnCompleted(object sender, EventArgs e) => UpdateViewModel();

        // Updates the stats after every successful check
        private void UpdateViewModel(object sender, ResultDetails<ProxyCheckInput, Proxy> details)
        {
            RefreshJobSnapshot();
            OnPropertyChanged(nameof(Progress));
        }

        // Update the stuff related to a job's status change
        private void UpdateStatus(object sender, JobStatus status)
        {
            RefreshJobSnapshot();

            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanSkipWait));
            OnPropertyChanged(nameof(CanResume));
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanAbort));

            OnPropertyChanged(nameof(IsStopping));
            OnPropertyChanged(nameof(IsWaiting));
            OnPropertyChanged(nameof(IsPausing));
        }


        private void UpdateViewModel(object sender, float progress) => UpdateViewModel();
        #endregion

        #region Logging
        private void OnResult(object sender, ResultDetails<ProxyCheckInput, Proxy> details)
        {
            var proxy = details.Result;

            var message = $"Proxy checked ({proxy}) with ping {proxy.Ping} ms and country {proxy.Country}";
            var color = proxy.WorkingStatus == ProxyWorkingStatus.Working ? Colors.YellowGreen : Colors.Tomato;

            NewMessage?.Invoke(this, message, color);
        }

        private void OnTaskError(object sender, ErrorDetails<ProxyCheckInput> details)
        {
            var message = $"Task error ({details.Item.Proxy})! {details.Exception.Message}";
            NewMessage?.Invoke(this, message, Colors.Tomato);
        }

        private void OnError(object sender, Exception ex)
            => NewMessage?.Invoke(this, $"Job error: {ex.Message}", Colors.Tomato);
        #endregion

        #region Controls
        public Task Start() => ProxyCheckJob.Start();

        public Task Stop() => ProxyCheckJob.Stop();
        public Task Abort() => ProxyCheckJob.Abort();
        public Task Pause() => ProxyCheckJob.Pause();
        public Task Resume() => ProxyCheckJob.Resume();
        public void SkipWait() => ProxyCheckJob.SkipWait();

        public async Task ChangeBotsAsync(int newValue)
        {
            // TODO: Also edit the job options! So the number of bots is persisted

            await ProxyCheckJob.ChangeBots(newValue);
            ProxyCheckJob.Bots = newValue;
            RefreshJobSnapshot();
        }
        #endregion

        private void RefreshJobSnapshot()
            => Job.ApplySnapshot(new DesktopJobListItemDto(
                ProxyCheckJob.Id,
                JobType.ProxyCheck,
                ProxyCheckJob.Status,
                ProxyCheckJob.Name,
                "Proxy Check",
                "Proxy Check Job",
                $"URL: {ProxyCheckJob.Url}",
                ProxyCheckJob.Url,
                ProxyCheckJob.Bots,
                0,
                JobProxyMode.Off,
                ProxyCheckJob.CPM,
                Math.Clamp(ProxyCheckJob.Progress, 0, 1),
                ProxyCheckJob.Tested,
                ProxyCheckJob.Total,
                ProxyCheckJob.Working,
                0,
                ProxyCheckJob.StartTime,
                ProxyCheckJob.Elapsed,
                ProxyCheckJob.Remaining,
                false));

        public void Dispose()
        {
            try
            {
                secondsTicker?.Dispose();

                ProxyCheckJob.OnCompleted -= UpdateOnCompleted;
                ProxyCheckJob.OnResult -= UpdateViewModel;
                ProxyCheckJob.OnStatusChanged -= UpdateStatus;
                ProxyCheckJob.OnProgress -= UpdateViewModel;

                ProxyCheckJob.OnResult -= OnResult;
                ProxyCheckJob.OnTaskError -= OnTaskError;
                ProxyCheckJob.OnError -= OnError;
            }
            catch
            {

            }
        }
    }



