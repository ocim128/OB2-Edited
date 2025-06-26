using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using OpenBullet2.Core.Entities;
using OpenBullet2.Core.Models.Jobs;
using OpenBullet2.Core.Repositories;
using OpenBullet2.Core.Services;
using RuriLib.Models.Data.DataPools;
using RuriLib.Models.Jobs;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace OpenBullet2.Native.ViewModels
{
    public class JobsViewModel : ViewModelBase
    {
        private readonly IJobRepository jobRepo;
        private readonly JobManagerService jobManager;
        private readonly JobFactoryService jobFactory;
        private readonly Timer timer;

        private ObservableCollection<JobViewModel> jobsCollection;
        public ObservableCollection<JobViewModel> JobsCollection
        {
            get => jobsCollection;
            set
            {
                jobsCollection = value;
                OnPropertyChanged();
            }
        }

        private string searchText = "";
        public string SearchText
        {
            get => searchText;
            set
            {
                searchText = value;
                OnPropertyChanged();
                FilterJobs();
            }
        }

        public JobsViewModel()
        {
            jobRepo = SP.GetService<IJobRepository>();
            jobManager = SP.GetService<JobManagerService>();
            jobFactory = SP.GetService<JobFactoryService>();

            CreateCollection();
            timer = new Timer(new TimerCallback(_ => RefreshJobs()), null, 1000, 1000);
        }

        private void FilterJobs()
        {
            var allJobs = jobManager.Jobs.Select(j => MakeViewModel(j));
            
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                allJobs = allJobs.Where(job => 
                {
                    if (job is MultiRunJobViewModel mrJob)
                    {
                        return mrJob.ConfigDisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                               mrJob.DataPoolDisplayInfo.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                               mrJob.Id.ToString().Contains(SearchText);
                    }
                    else if (job is ProxyCheckJobViewModel pcJob)
                    {
                        return pcJob.ConfigDisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                               pcJob.DataPoolDisplayInfo.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                               pcJob.Id.ToString().Contains(SearchText);
                    }
                    return job.Id.ToString().Contains(SearchText);
                });
            }

            JobsCollection = new ObservableCollection<JobViewModel>(allJobs);
            SortCollection();
        }

        private void RefreshJobs()
        {
            foreach (var job in JobsCollection)
            {
                job.UpdateViewModel();
            }
        }

        private void CreateCollection()
        {
            FilterJobs();
        }

        private void SortCollection()
            => JobsCollection = new ObservableCollection<JobViewModel>(JobsCollection.OrderBy(j => j.Id));

        public async Task<JobViewModel> CreateJobAsync(JobOptions options)
        {
            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var wrapper = new JobOptionsWrapper { Options = options };

            var entity = new JobEntity
            {
                CreationDate = DateTime.Now,
                JobType = GetJobType(options),
                JobOptions = JsonConvert.SerializeObject(wrapper, settings)
            };

            await jobRepo.AddAsync(entity);

            var job = jobFactory.FromOptions(entity.Id, 0, options);
            var jobVM = MakeViewModel(job);

            jobManager.AddJob(job);
            JobsCollection.Add(jobVM);
            SortCollection();

            return jobVM;
        }

        public async Task<JobViewModel> EditJobAsync(JobEntity entity, JobOptions options)
        {
            var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var wrapper = new JobOptionsWrapper { Options = options };
            entity.JobOptions = JsonConvert.SerializeObject(wrapper, jsonSettings);

            await jobRepo.UpdateAsync(entity);

            var oldJob = jobManager.Jobs.First(j => j.Id == entity.Id);
            var newJob = jobFactory.FromOptions(entity.Id, 0, options);

            jobManager.RemoveJob(oldJob);
            jobManager.AddJob(newJob);

            CreateCollection();

            return JobsCollection.First(j => j.Id == newJob.Id);
        }

        public async Task<JobViewModel> CloneJobAsync(JobType type, JobOptions options)
        {
            var jsonSettings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var wrapper = new JobOptionsWrapper { Options = options };
            var entity = new JobEntity
            {
                CreationDate = DateTime.Now,
                JobType = type,
                JobOptions = JsonConvert.SerializeObject(wrapper, jsonSettings)
            };

            await jobRepo.AddAsync(entity);

            var job = jobFactory.FromOptions(entity.Id, 0, options);
            jobManager.AddJob(job);

            JobViewModel jobVM = type switch
            {
                JobType.MultiRun => new MultiRunJobViewModel(job as MultiRunJob),
                JobType.ProxyCheck => new ProxyCheckJobViewModel(job as ProxyCheckJob),
                _ => throw new NotImplementedException()
            };

            JobsCollection.Add(jobVM);
            SortCollection();

            return jobVM;
        }

        public void RemoveAll()
        {
            var notIdleJobs = jobManager.Jobs.Where(j => j.Status != JobStatus.Idle);

            if (notIdleJobs.Any())
            {
                throw new Exception($"The job #{notIdleJobs.First().Id} is not idle, please stop/abort the job first!");
            }

            // If admin, just purge all
            jobRepo.Purge();
            jobManager.Clear();
            JobsCollection.Clear();
        }

        public async Task RemoveJobAsync(JobViewModel jobVM)
        {
            if (jobVM.Job.Status != JobStatus.Idle)
            {
                throw new Exception("The job is not idle, please stop/abort the job first!");
            }

            var entity = await jobRepo.GetAll().FirstAsync(e => e.Id == jobVM.Id);
            await jobRepo.DeleteAsync(entity);
            jobManager.RemoveJob(jobVM.Job);
            JobsCollection.Remove(jobVM);
            SortCollection();
        }

        private static JobViewModel MakeViewModel(Job job) => job switch
        {
            MultiRunJob mr => new MultiRunJobViewModel(mr),
            ProxyCheckJob pc => new ProxyCheckJobViewModel(pc),
            _ => throw new NotImplementedException()
        };

        private static JobType GetJobType(JobOptions options) => options switch
        {
            MultiRunJobOptions => JobType.MultiRun,
            ProxyCheckJobOptions => JobType.ProxyCheck,
            _ => throw new NotImplementedException()
        };

        private static JobType GetJobType(Job job) => job switch
        {
            MultiRunJob => JobType.MultiRun,
            ProxyCheckJob => JobType.ProxyCheck,
            _ => throw new NotImplementedException()
        };
    }

    public class JobViewModel : ViewModelBase
    {
        public Job Job { get; init; }

        public string IdAndStatus => $"#{Id} [{Status}]";
        public int Id => Job.Id;
        public JobStatus Status => Job.Status;

        // UI Display Properties
        public virtual string StatusDisplayText => Status switch
        {
            JobStatus.Idle => "IDLE",
            JobStatus.Starting => "STARTING",
            JobStatus.Running => "RUNNING",
            JobStatus.Pausing => "PAUSING",
            JobStatus.Paused => "PAUSED",
            JobStatus.Stopping => "STOPPING",
            JobStatus.Resuming => "RESUMING",
            _ => "UNKNOWN"
        };

        public virtual SolidColorBrush StatusColor => Status switch
        {
            JobStatus.Idle => new SolidColorBrush(Color.FromRgb(108, 117, 125)), // Gray
            JobStatus.Starting => new SolidColorBrush(Color.FromRgb(255, 193, 7)), // Yellow
            JobStatus.Running => new SolidColorBrush(Color.FromRgb(40, 167, 69)), // Green
            JobStatus.Pausing => new SolidColorBrush(Color.FromRgb(255, 193, 7)), // Yellow
            JobStatus.Paused => new SolidColorBrush(Color.FromRgb(253, 126, 20)), // Orange
            JobStatus.Stopping => new SolidColorBrush(Color.FromRgb(220, 53, 69)), // Red
            JobStatus.Resuming => new SolidColorBrush(Color.FromRgb(23, 162, 184)), // Blue
            _ => new SolidColorBrush(Color.FromRgb(108, 117, 125))
        };

        public JobViewModel(Job job)
        {
            Job = job;
        }

        public virtual void UpdateViewModel()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusDisplayText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(IdAndStatus));
        }
    }

    public class MultiRunJobViewModel : JobViewModel
    {
        private MultiRunJob MultiRunJob => Job as MultiRunJob;

        public string ConfigName => MultiRunJob.Config is null ? "No config" : MultiRunJob.Config.Metadata.Name;
        public string ConfigDisplayName => MultiRunJob.Config is null ? "No Config Selected" : 
            string.IsNullOrEmpty(MultiRunJob.Config.Metadata.Name) ? "Unnamed Config" : MultiRunJob.Config.Metadata.Name;
        
        public string JobTypeDisplay => "Multi-Run Job";
        
        public string DataPoolInfo => MultiRunJob.DataPool switch
        {
            WordlistDataPool w => $"{w.Wordlist.Name} (Wordlist)",
            CombinationsDataPool => "Combinations",
            InfiniteDataPool => "Infinite",
            RangeDataPool => "Range",
            FileDataPool f => $"{Path.GetFileName(f.FileName)} (File)",
            _ => throw new NotImplementedException()
        };

        public string DataPoolDisplayInfo => MultiRunJob.DataPool switch
        {
            WordlistDataPool w => w.Wordlist.Name,
            CombinationsDataPool => "Combinations",
            InfiniteDataPool => "Infinite",
            RangeDataPool => "Range",
            FileDataPool f => Path.GetFileName(f.FileName),
            _ => "Unknown"
        };

        public int Bots => MultiRunJob.Bots;
        public int Skip => MultiRunJob.Skip;
        public JobProxyMode ProxyMode => MultiRunJob.ProxyMode;

        // Stats
        public int DataTested => MultiRunJob.DataTested;
        public int DataHits => MultiRunJob.DataHits;
        public int DataCustom => MultiRunJob.DataCustom;
        public int DataToCheck => MultiRunJob.DataToCheck;
        public int DataFails => MultiRunJob.DataFails;
        public int DataRetried => MultiRunJob.DataRetried;
        public int DataBanned => MultiRunJob.DataBanned;
        public int DataErrors => MultiRunJob.DataErrors;
        public int DataInvalid => MultiRunJob.DataInvalid;

        // Proxy stats
        public int ProxiesTotal => MultiRunJob.ProxiesTotal;
        public int ProxiesAlive => MultiRunJob.ProxiesAlive;
        public int ProxiesBad => MultiRunJob.ProxiesBad;
        public int ProxiesBanned => MultiRunJob.ProxiesBanned;

        public float Progress => MultiRunJob.Progress;
        public string ProgressString
        {
            get
            {
                var tested = MultiRunJob.Status == JobStatus.Idle ? Skip : DataTested + Skip;
                return $"{tested} / {MultiRunJob.DataPool.Size} ({(Progress == -1 ? 0 : Progress * 100):0.00}%)";
            }
        }

        public decimal CaptchaCredit => MultiRunJob.CaptchaCredit;
        public string ElapsedString => $"{(int)MultiRunJob.Elapsed.TotalDays} day(s) {MultiRunJob.Elapsed:hh\\:mm\\:ss}";
        public string RemainingString 
        {
            get
            {
                // If job is completed, show 00:00:00 instead of continuing to calculate
                if (MultiRunJob.Status == JobStatus.Idle || MultiRunJob.Status == JobStatus.Stopping || 
                    MultiRunJob.Progress >= 1.0f || MultiRunJob.DataTested >= MultiRunJob.DataPool.Size)
                {
                    return "0 day(s) 00:00:00";
                }
                return $"{(int)MultiRunJob.Remaining.TotalDays} day(s) {MultiRunJob.Remaining:hh\\:mm\\:ss}";
            }
        }

        public int CPM => MultiRunJob.CPM;

        public MultiRunJobViewModel(MultiRunJob job) : base(job)
        {

        }

        /// <summary>
        /// Update properties that only need to be updated every second.
        /// </summary>
        public void PeriodicUpdate()
        {
            OnPropertyChanged(nameof(ElapsedString));
            OnPropertyChanged(nameof(RemainingString));
            OnPropertyChanged(nameof(CPM));
            OnPropertyChanged(nameof(CaptchaCredit));

            OnPropertyChanged(nameof(DataRetried));
            OnPropertyChanged(nameof(DataBanned));
            OnPropertyChanged(nameof(DataErrors));
            OnPropertyChanged(nameof(DataInvalid));

            OnPropertyChanged(nameof(ProxiesTotal));
            OnPropertyChanged(nameof(ProxiesAlive));
            OnPropertyChanged(nameof(ProxiesBad));
            OnPropertyChanged(nameof(ProxiesBanned));
        }

        /// <summary>
        /// Update properties that need to be updated every time there is a result.
        /// </summary>
        public void UpdateStats()
        {
            OnPropertyChanged(nameof(DataTested));
            OnPropertyChanged(nameof(DataHits));
            OnPropertyChanged(nameof(DataCustom));
            OnPropertyChanged(nameof(DataToCheck));
            OnPropertyChanged(nameof(DataFails));

            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(ProgressString));
        }

        /// <summary>
        /// Update the Bots property.
        /// </summary>
        public void UpdateBots() => OnPropertyChanged(nameof(Bots));

        /// <summary>
        /// Update the Skip property.
        /// </summary>
        public void UpdateSkip() => OnPropertyChanged(nameof(Skip));

        /// <summary>
        /// Updates the status of the job.
        /// </summary>
        public void UpdateStatus()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(IdAndStatus));
        }

        public override void UpdateViewModel()
        {
            base.UpdateViewModel();
            OnPropertyChanged(nameof(ConfigName));
            OnPropertyChanged(nameof(ConfigDisplayName));
            OnPropertyChanged(nameof(DataPoolInfo));
            OnPropertyChanged(nameof(DataPoolDisplayInfo));
        }
    }

    public class ProxyCheckJobViewModel : JobViewModel
    {
        private ProxyCheckJob ProxyCheckJob => Job as ProxyCheckJob;

        public string ConfigDisplayName => "Proxy Check";
        public string JobTypeDisplay => "Proxy Check Job";
        public string DataPoolDisplayInfo => $"URL: {Url}";

        public int Bots => ProxyCheckJob.Bots;
        public string Url => ProxyCheckJob.Url;
        public string SuccessKey => ProxyCheckJob.SuccessKey;
        public bool CheckOnlyUntested => ProxyCheckJob.CheckOnlyUntested;
        public int TimeoutMilliseconds => (int)ProxyCheckJob.Timeout.TotalMilliseconds;

        public int Total => ProxyCheckJob.Total;
        public int Tested => ProxyCheckJob.Tested;
        public int Working => ProxyCheckJob.Working;
        public int NotWorking => ProxyCheckJob.NotWorking;

        // For consistency with MultiRun jobs
        public int DataHits => Working;
        public int DataCustom => 0; // Proxy check doesn't have custom results

        public float Progress => ProxyCheckJob.Progress;
        public string ProgressString => $"{Tested} / {Total} ({(Progress == -1 ? 0 : Progress * 100):0.00}%)";

        public int CPM => ProxyCheckJob.CPM;
        public string ElapsedString => $"{(int)ProxyCheckJob.Elapsed.TotalDays} day(s) {ProxyCheckJob.Elapsed:hh\\:mm\\:ss}";
        public string RemainingString 
        {
            get
            {
                // If job is completed, show 00:00:00 instead of continuing to calculate
                if (ProxyCheckJob.Status == JobStatus.Idle || ProxyCheckJob.Status == JobStatus.Stopping || 
                    ProxyCheckJob.Progress >= 1.0f || ProxyCheckJob.Tested >= ProxyCheckJob.Total)
                {
                    return "0 day(s) 00:00:00";
                }
                return $"{(int)ProxyCheckJob.Remaining.TotalDays} day(s) {ProxyCheckJob.Remaining:hh\\:mm\\:ss}";
            }
        }

        public ProxyCheckJobViewModel(ProxyCheckJob job) : base(job)
        {

        }

        /// <summary>
        /// Update properties that only need to be updated every second.
        /// </summary>
        public void PeriodicUpdate()
        {
            OnPropertyChanged(nameof(ElapsedString));
            OnPropertyChanged(nameof(RemainingString));
            OnPropertyChanged(nameof(CPM));

            OnPropertyChanged(nameof(Total));
            OnPropertyChanged(nameof(Tested));
            OnPropertyChanged(nameof(Working));
            OnPropertyChanged(nameof(NotWorking));
        }

        /// <summary>
        /// Update properties that need to be updated every time there is a result.
        /// </summary>
        public void UpdateStats()
        {
           OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(ProgressString));
            OnPropertyChanged(nameof(DataHits));
        }

        /// <summary>
        /// Update the Bots property.
        /// </summary>
        public void UpdateBots() => OnPropertyChanged(nameof(Bots));

        /// <summary>
        /// Updates the status of the job.
        /// </summary>
        public void UpdateStatus()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(IdAndStatus));
        }

        public override void UpdateViewModel()
        {
            base.UpdateViewModel();
            OnPropertyChanged(nameof(ConfigDisplayName));
            OnPropertyChanged(nameof(DataPoolDisplayInfo));
            OnPropertyChanged(nameof(DataHits));
        }
    }
}
