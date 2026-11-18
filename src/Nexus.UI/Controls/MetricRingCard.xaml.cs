using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Nexus.UI.Controls;

public sealed partial class MetricRingCard : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(MetricRingCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PrimaryValueProperty =
        DependencyProperty.Register(nameof(PrimaryValue), typeof(string), typeof(MetricRingCard), new PropertyMetadata("—"));

    public static readonly DependencyProperty SecondaryValueProperty =
        DependencyProperty.Register(nameof(SecondaryValue), typeof(string), typeof(MetricRingCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BadgeTextProperty =
        DependencyProperty.Register(nameof(BadgeText), typeof(string), typeof(MetricRingCard), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(MetricRingCard), new PropertyMetadata("\uE9D9"));

    public static readonly DependencyProperty PercentageProperty =
        DependencyProperty.Register(nameof(Percentage), typeof(double), typeof(MetricRingCard),
            new PropertyMetadata(0.0, OnPercentageChanged));

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(MetricRingCard),
            new PropertyMetadata(new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 194, 255))));

    public MetricRingCard()
    {
        InitializeComponent();
        UpdateArc(0);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string PrimaryValue
    {
        get => (string)GetValue(PrimaryValueProperty);
        set => SetValue(PrimaryValueProperty, value);
    }

    public string SecondaryValue
    {
        get => (string)GetValue(SecondaryValueProperty);
        set => SetValue(SecondaryValueProperty, value);
    }

    public string BadgeText
    {
        get => (string)GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public double Percentage
    {
        get => (double)GetValue(PercentageProperty);
        set => SetValue(PercentageProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    private static void OnPercentageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MetricRingCard card && e.NewValue is double pct)
        {
            card.UpdateArc(pct);
        }
    }

    private void UpdateArc(double percentage)
    {
        double clamped = Math.Clamp(percentage, 0.0, 99.9);
        if (clamped <= 0.5)
        {
            ArcPath.Data = null;
            return;
        }

        const double center = 24.0;
        const double radius = 20.0;
        double angleDeg = clamped / 100.0 * 360.0;
        double angleRad = (angleDeg - 90.0) * Math.PI / 180.0;

        var startPoint = new Point(center, center - radius);
        var endPoint = new Point(
            center + radius * Math.Cos(angleRad),
            center + radius * Math.Sin(angleRad));

        var figure = new PathFigure { StartPoint = startPoint, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = endPoint,
            Size = new Size(radius, radius),
            IsLargeArc = angleDeg > 180.0,
            SweepDirection = SweepDirection.Clockwise
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        ArcPath.Data = geometry;
    }
}

