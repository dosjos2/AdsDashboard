using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DevOpsPulse;
public sealed class ActivityToast : Window
{
    public ActivityToast(int count, string summary, Action open)
    {
        Width = 370; SizeToContent = SizeToContent.Height; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowActivated = false; ShowInTaskbar = false; Topmost = true; Background = Brushes.Transparent; AllowsTransparency = true;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = count == 1 ? "DevOps Pulse · 1 nytt varsel" : $"DevOps Pulse · {count} nye varsler", FontWeight = FontWeights.SemiBold, FontSize = 15 });
        stack.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, MaxHeight = 55, Margin = new Thickness(0,10,0,12) });
        var button = new Button { Content = "Se varsler →", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12,6,12,6) };
        button.Click += (_, _) => { open(); Close(); }; stack.Children.Add(button);
        Content = new Border { Child = stack, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(82,102,233)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Margin = new Thickness(6) };
        Loaded += (_, _) => { Left = SystemParameters.WorkArea.Right - ActualWidth - 18; Top = SystemParameters.WorkArea.Bottom - ActualHeight - 18; };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) => { timer.Stop(); Close(); }; Closed += (_, _) => timer.Stop(); timer.Start();
    }
}
