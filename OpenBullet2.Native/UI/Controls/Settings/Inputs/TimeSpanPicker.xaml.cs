using System;
using System.Windows;
using System.Windows.Controls;

namespace OpenBullet2.Native.Controls
{
    /// <summary>
    /// Interaction logic for TimeSpanPicker.xaml
    /// </summary>
    public partial class TimeSpanPicker : UserControl
    {
        public TimeSpan TimeSpan
        {
            get => (TimeSpan)GetValue(TimeSpanProperty);
            set => SetValue(TimeSpanProperty, value);
        }

        public static readonly DependencyProperty TimeSpanProperty =
        DependencyProperty.Register(
            nameof(TimeSpan),
            typeof(TimeSpan),
            typeof(TimeSpanPicker),
            new PropertyMetadata(default(TimeSpan), OnTimeSpanPropertyChanged));

        private static void OnTimeSpanPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var newValue = (TimeSpan)e.NewValue;
            var source = d as TimeSpanPicker;

            source.hours.Value = newValue.Hours;
            source.minutes.Value = newValue.Minutes;
            source.seconds.Value = newValue.Seconds;
        }

        public TimeSpanPicker()
        {
            InitializeComponent();
        }

        private void NumberChanged(object sender, RoutedPropertyChangedEventArgs<double?> e)
        {
            try
            {
                if (hours is not null && minutes is not null && seconds is not null)
                {
                    // Ensure values are within valid ranges for TimeSpan constructor
                    // Hours can be large, but minutes and seconds must be 0-59
                    var hoursValue = Math.Max(0, (int)hours.Value);
                    var minutesValue = Math.Clamp((int)minutes.Value, 0, 59);
                    var secondsValue = Math.Clamp((int)seconds.Value, 0, 59);

                    // If the user typed a value >= 60, clamp it back
                    if (minutes.Value >= 60)
                    {
                        minutes.Value = minutesValue;
                    }
                    if (seconds.Value >= 60)
                    {
                        seconds.Value = secondsValue;
                    }

                    TimeSpan = new TimeSpan(hoursValue, minutesValue, secondsValue);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // If somehow an invalid TimeSpan is attempted, reset to a safe default
                TimeSpan = TimeSpan.Zero;
                
                // Reset the UI controls to safe values
                if (hours != null) hours.Value = 0;
                if (minutes != null) minutes.Value = 0;
                if (seconds != null) seconds.Value = 0;
            }
            catch (Exception ex)
            {
                // Log unexpected errors but don't crash
                System.Diagnostics.Debug.WriteLine($"TimeSpanPicker error: {ex.Message}");
                TimeSpan = TimeSpan.Zero;
            }
        }
    }
}
