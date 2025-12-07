using System;
using OpenBullet2.Native.ViewModels.Infrastructure;
using OpenBullet2.Native.ViewModels.Tools;

namespace OpenBullet2.Native.ViewModels.Pages
{
    /// <summary>
    /// Root view model for the Tools dashboard. Owns per-tool view models.
    /// </summary>
    public sealed class ToolsPageViewModel : ViewModelBase, IDisposable
    {
        public ToolsPageViewModel()
            : this(new OtpToolViewModel())
        {
        }

        internal ToolsPageViewModel(OtpToolViewModel otpTool)
        {
            OtpTool = otpTool ?? throw new ArgumentNullException(nameof(otpTool));
        }

        public OtpToolViewModel OtpTool { get; }

        public void Dispose()
        {
            OtpTool.Dispose();
        }
    }
}
