using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OpenBullet2.Native.UI.Controls;

/// <summary>
/// An animated counter control that smoothly transitions between values.
/// Provides a premium feel with number animations.
/// </summary>
public class AnimatedCounter : Control
{
    private TextBlock _textBlock;
    private double _currentDisplayValue;
    private readonly Storyboard _storyboard;
    private bool _isAnimating;

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(AnimatedCounter),
            new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty FormatStringProperty =
        DependencyProperty.Register(nameof(FormatString), typeof(string), typeof(AnimatedCounter),
            new PropertyMetadata("N0"));

    public static readonly DependencyProperty AnimationDurationProperty =
        DependencyProperty.Register(nameof(AnimationDuration), typeof(Duration), typeof(AnimatedCounter),
            new PropertyMetadata(new Duration(TimeSpan.FromMilliseconds(300))));

    public static readonly DependencyProperty TextColorProperty =
        DependencyProperty.Register(nameof(TextColor), typeof(Brush), typeof(AnimatedCounter),
            new PropertyMetadata(Brushes.White, OnTextColorChanged));

    public static readonly DependencyProperty TextSizeProperty =
        DependencyProperty.Register(nameof(TextSize), typeof(double), typeof(AnimatedCounter),
            new PropertyMetadata(16.0, OnTextSizeChanged));

    public static readonly DependencyProperty TextWeightProperty =
        DependencyProperty.Register(nameof(TextWeight), typeof(FontWeight), typeof(AnimatedCounter),
            new PropertyMetadata(FontWeights.Bold, OnTextWeightChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string FormatString
    {
        get => (string)GetValue(FormatStringProperty);
        set => SetValue(FormatStringProperty, value);
    }

    public Duration AnimationDuration
    {
        get => (Duration)GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    public Brush TextColor
    {
        get => (Brush)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public double TextSize
    {
        get => (double)GetValue(TextSizeProperty);
        set => SetValue(TextSizeProperty, value);
    }

    public FontWeight TextWeight
    {
        get => (FontWeight)GetValue(TextWeightProperty);
        set => SetValue(TextWeightProperty, value);
    }

    static AnimatedCounter()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AnimatedCounter),
            new FrameworkPropertyMetadata(typeof(AnimatedCounter)));
    }

    public AnimatedCounter()
    {
        _storyboard = new Storyboard();

        // Create a simple template
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        factory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding(nameof(TextColor)) { Source = this });
        factory.SetBinding(TextBlock.FontSizeProperty, new System.Windows.Data.Binding(nameof(TextSize)) { Source = this });
        factory.SetBinding(TextBlock.FontWeightProperty, new System.Windows.Data.Binding(nameof(TextWeight)) { Source = this });
        factory.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnTextBlockLoaded));

        Template = new ControlTemplate(typeof(AnimatedCounter))
        {
            VisualTree = factory
        };

        Loaded += (_, _) => UpdateDisplayValue(Value, false);
    }

    private void OnTextBlockLoaded(object sender, RoutedEventArgs e)
    {
        _textBlock = sender as TextBlock;
        UpdateDisplayValue(_currentDisplayValue, false);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimatedCounter counter)
        {
            counter.UpdateDisplayValue((double)e.NewValue, true);
        }
    }

    private static void OnTextColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimatedCounter counter && counter._textBlock != null)
        {
            counter._textBlock.Foreground = (Brush)e.NewValue;
        }
    }

    private static void OnTextSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimatedCounter counter && counter._textBlock != null)
        {
            counter._textBlock.FontSize = (double)e.NewValue;
        }
    }

    private static void OnTextWeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AnimatedCounter counter && counter._textBlock != null)
        {
            counter._textBlock.FontWeight = (FontWeight)e.NewValue;
        }
    }

    private void UpdateDisplayValue(double targetValue, bool animate)
    {
        if (_textBlock == null) return;

        if (!animate || AnimationDuration.TimeSpan.TotalMilliseconds < 50)
        {
            _currentDisplayValue = targetValue;
            _textBlock.Text = targetValue.ToString(FormatString);
            return;
        }

        if (_isAnimating)
        {
            // If already animating, just update the target
            _storyboard.Stop();
        }

        _isAnimating = true;
        var startValue = _currentDisplayValue;
        var diff = targetValue - startValue;

        // Use CompositionTarget.Rendering for smooth animation
        var startTime = DateTime.Now;
        var duration = AnimationDuration.TimeSpan;

        CompositionTarget.Rendering -= OnRendering;
        _targetValue = targetValue;
        _animStartValue = startValue;
        _animStartTime = startTime;
        _animDuration = duration;
        CompositionTarget.Rendering += OnRendering;
    }

    private double _targetValue;
    private double _animStartValue;
    private DateTime _animStartTime;
    private TimeSpan _animDuration;

    private void OnRendering(object sender, EventArgs e)
    {
        if (_textBlock == null)
        {
            CompositionTarget.Rendering -= OnRendering;
            return;
        }

        var elapsed = DateTime.Now - _animStartTime;
        var progress = Math.Min(1.0, elapsed.TotalMilliseconds / _animDuration.TotalMilliseconds);

        // Ease-out function for smooth deceleration
        var easedProgress = 1 - Math.Pow(1 - progress, 3);

        _currentDisplayValue = _animStartValue + ((_targetValue - _animStartValue) * easedProgress);
        _textBlock.Text = _currentDisplayValue.ToString(FormatString);

        if (progress >= 1.0)
        {
            _currentDisplayValue = _targetValue;
            _textBlock.Text = _targetValue.ToString(FormatString);
            _isAnimating = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }
}
