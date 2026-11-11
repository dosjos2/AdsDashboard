using System.Windows;

namespace DevOpsPulse;
public partial class NotificationsWindow : Window
{
    private readonly Action<WorkItem> open;
    public NotificationsWindow(ActivityFeed feed, Action<WorkItem> openItem)
    {
        InitializeComponent(); DataContext = feed; open = openItem;
        feed.MarkRead();
    }
    private void OpenNotification(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ActivityEvent activity }) open(activity.Item);
    }
}
