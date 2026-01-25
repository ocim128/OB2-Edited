using System;
using System.Windows;
using System.Windows.Threading;

namespace OpenBullet2.Native.Services.Sidebar;

public class SidebarAnimator
{
    private readonly TimeSpan _animationDuration = TimeSpan.FromMilliseconds(200);

    public void AnimateWidth(double from, double to, Action<double> updateAction)
    {
        var startTime = DateTime.Now;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };

        timer.Tick += (s, e) =>
        {
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            var progress = Math.Min(elapsed / _animationDuration.TotalMilliseconds, 1.0);

            // Quadratic ease-in-out
            var easedProgress = progress < 0.5
                ? 2 * progress * progress
                : 1 - Math.Pow(-2 * progress + 2, 2) / 2;

            var currentWidth = from + (to - from) * easedProgress;
            updateAction(currentWidth);

            if (progress >= 1.0)
            {
                timer.Stop();
                updateAction(to);
            }
        };

        timer.Start();
    }
}
