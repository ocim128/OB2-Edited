using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OpenBullet2.Native.UI.Controls;

/// <summary>
/// A simple sparkline chart control that displays a mini line graph of data points.
/// Used for visualizing CPM, Hits/minute trends in real-time.
/// </summary>
public class SparkLine : Canvas
{
    private readonly Polyline _line;
    private readonly Polyline _fillArea;
    private readonly List<double> _dataPoints = new();
    private const int MaxDataPoints = 30; // Show last 30 data points

    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Brush), typeof(SparkLine),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(16, 185, 129)), OnLineColorChanged));

    public static readonly DependencyProperty FillColorProperty =
        DependencyProperty.Register(nameof(FillColor), typeof(Brush), typeof(SparkLine),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)), OnFillColorChanged));

    public static readonly DependencyProperty LineThicknessProperty =
        DependencyProperty.Register(nameof(LineThickness), typeof(double), typeof(SparkLine),
            new PropertyMetadata(1.5, OnLineThicknessChanged));

    public Brush LineColor
    {
        get => (Brush)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public Brush FillColor
    {
        get => (Brush)GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    public double LineThickness
    {
        get => (double)GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    public SparkLine()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;

        _fillArea = new Polyline
        {
            Stroke = Brushes.Transparent,
            Fill = FillColor,
            StrokeThickness = 0
        };

        _line = new Polyline
        {
            Stroke = LineColor,
            StrokeThickness = LineThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        Children.Add(_fillArea);
        Children.Add(_line);

        SizeChanged += OnSizeChanged;
    }

    private static void OnLineColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SparkLine sparkLine)
        {
            sparkLine._line.Stroke = (Brush)e.NewValue;
        }
    }

    private static void OnFillColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SparkLine sparkLine)
        {
            sparkLine._fillArea.Fill = (Brush)e.NewValue;
        }
    }

    private static void OnLineThicknessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SparkLine sparkLine)
        {
            sparkLine._line.StrokeThickness = (double)e.NewValue;
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Redraw();
    }

    /// <summary>
    /// Adds a new data point to the sparkline.
    /// </summary>
    public void AddDataPoint(double value)
    {
        _dataPoints.Add(value);

        // Keep only the last MaxDataPoints
        while (_dataPoints.Count > MaxDataPoints)
        {
            _dataPoints.RemoveAt(0);
        }

        Redraw();
    }

    /// <summary>
    /// Clears all data points.
    /// </summary>
    public void Clear()
    {
        _dataPoints.Clear();
        _line.Points.Clear();
        _fillArea.Points.Clear();
    }

    /// <summary>
    /// Sets all data points at once.
    /// </summary>
    public void SetDataPoints(IEnumerable<double> values)
    {
        _dataPoints.Clear();
        _dataPoints.AddRange(values.TakeLast(MaxDataPoints));
        Redraw();
    }

    private void Redraw()
    {
        if (_dataPoints.Count < 2 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            _line.Points.Clear();
            _fillArea.Points.Clear();
            return;
        }

        var points = new PointCollection();
        var fillPoints = new PointCollection();

        var minValue = _dataPoints.Min();
        var maxValue = _dataPoints.Max();
        var range = maxValue - minValue;

        // Prevent division by zero
        if (range < 0.001) range = 1;

        var padding = 2.0;
        var availableWidth = ActualWidth - (padding * 2);
        var availableHeight = ActualHeight - (padding * 2);
        var stepX = availableWidth / (_dataPoints.Count - 1);

        // Start fill area at bottom-left
        fillPoints.Add(new Point(padding, ActualHeight - padding));

        for (var i = 0; i < _dataPoints.Count; i++)
        {
            var x = padding + (i * stepX);
            var normalizedValue = (float)((_dataPoints[i] - minValue) / range);
            var y = padding + (availableHeight * (1 - normalizedValue));

            points.Add(new Point(x, y));
            fillPoints.Add(new Point(x, y));
        }

        // Close fill area at bottom-right
        fillPoints.Add(new Point(padding + availableWidth, ActualHeight - padding));

        _line.Points = points;
        _fillArea.Points = fillPoints;
    }

    /// <summary>
    /// Gets the current data point count.
    /// </summary>
    public int DataPointCount => _dataPoints.Count;

    /// <summary>
    /// Gets the last value or 0 if empty.
    /// </summary>
    public double LastValue => _dataPoints.Count > 0 ? _dataPoints[^1] : 0;
}
