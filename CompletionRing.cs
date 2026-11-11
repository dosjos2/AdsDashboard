using System.Windows;
using System.Windows.Media;

namespace DevOpsPulse;
public sealed class CompletionRing : FrameworkElement
{
    public static readonly DependencyProperty DoneProperty = DependencyProperty.Register(nameof(Done), typeof(int), typeof(CompletionRing), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TotalProperty = DependencyProperty.Register(nameof(Total), typeof(int), typeof(CompletionRing), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public int Done { get => (int)GetValue(DoneProperty); set => SetValue(DoneProperty, value); }
    public int Total { get => (int)GetValue(TotalProperty); set => SetValue(TotalProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        var center = new Point(ActualWidth / 2, ActualHeight / 2); var radius = Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2 - 9);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(235, 239, 248)), 10), center, radius, radius);
        if (Total <= 0 || Done <= 0) return;
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(82, 102, 233)), 10) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (Done >= Total) { dc.DrawEllipse(null, pen, center, radius, radius); return; }
        var angle = Math.Clamp((double)Done / Total, 0, 1) * Math.PI * 2;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(center.X, center.Y - radius), false, false);
            ctx.ArcTo(new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)), new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, pen, geometry);
    }
}

public sealed class DistributionBar : FrameworkElement
{
    public static readonly DependencyProperty CountProperty = DependencyProperty.Register(nameof(Count), typeof(int), typeof(DistributionBar), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TotalProperty = DependencyProperty.Register(nameof(Total), typeof(int), typeof(DistributionBar), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(DistributionBar), new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public int Count { get => (int)GetValue(CountProperty); set => SetValue(CountProperty, value); }
    public int Total { get => (int)GetValue(TotalProperty); set => SetValue(TotalProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(238,242,248)), null, new Rect(0,0,ActualWidth,ActualHeight), 3,3);
        if (Total > 0 && Count > 0) dc.DrawRoundedRectangle(Fill, null, new Rect(0,0,ActualWidth * Math.Clamp((double)Count / Total,0,1),ActualHeight),3,3);
    }
}
