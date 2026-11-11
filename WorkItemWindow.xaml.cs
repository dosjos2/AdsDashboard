using System.Windows;
using System.Diagnostics;

namespace DevOpsPulse;
public partial class WorkItemWindow : Window
{
    public WorkItem Item { get; }
    private readonly Uri? webUri;
    private readonly Func<CancellationToken, Task<IReadOnlyList<WorkItemComment>>>? loadDiscussion;
    private readonly CancellationTokenSource discussionLifetime = new();
    private readonly bool demo;
    private bool loadingDiscussion;
    public WorkItemWindow(WorkItem item, DateTimeOffset loadedAt, bool isDemo, Func<CancellationToken, Task<IReadOnlyList<WorkItemComment>>>? discussionLoader = null)
    {
        InitializeComponent(); Item = item; DataContext = item;
        demo = isDemo; loadDiscussion = discussionLoader;
        Loaded += async (_, _) => await LoadDiscussionAsync();
        Closed += (_, _) => { discussionLifetime.Cancel(); };
        if (Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "dev.azure.com")
            webUri = uri;
        OpenWebButton.IsEnabled = webUri != null;
        OpenWebButton.ToolTip = webUri?.AbsoluteUri ?? (isDemo ? "Eksempeloppgaver har ingen nettside. Koble til Azure DevOps for å åpne dine oppgaver på nett." : "Oppgaven mangler en gyldig Azure DevOps-lenke.");
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } };
        Title = $"#{item.Id} · {item.Title} – DevOps Pulse";
        SnapshotLabel.Text = $"{(isDemo ? "DEMOVISNING · Fiktive oppgavedata · " : "")}Hentet {loadedAt:dd.MM.yyyy HH:mm}. Oppdater dashboardet og åpne oppgaven på nytt for siste versjon.";
        AssignedValue.Text = string.IsNullOrWhiteSpace(item.AssignedTo) ? "Ikke oppgitt" : item.AssignedTo;
        AreaValue.Text = string.IsNullOrWhiteSpace(item.AreaPath) ? "Ikke oppgitt" : item.AreaPath;
        PriorityValue.Text = item.PriorityLabel + (string.IsNullOrWhiteSpace(item.Severity) ? "" : " · " + item.Severity);
        DescriptionValue.Text = string.IsNullOrWhiteSpace(item.Description) ? "Ingen beskrivelse oppgitt." : item.Description;
        ReproSection.Visibility = string.IsNullOrWhiteSpace(item.ReproSteps) ? Visibility.Collapsed : Visibility.Visible;
        AcceptanceSection.Visibility = string.IsNullOrWhiteSpace(item.AcceptanceCriteria) ? Visibility.Collapsed : Visibility.Visible;
        TagsSection.Visibility = string.IsNullOrWhiteSpace(item.Tags) ? Visibility.Collapsed : Visibility.Visible;
    }
    private async void ReloadDiscussionClick(object sender, RoutedEventArgs e) => await LoadDiscussionAsync();
    private async Task LoadDiscussionAsync()
    {
        if (loadingDiscussion || discussionLifetime.IsCancellationRequested) return;
        if (demo)
        {
            DiscussionItems.ItemsSource = new[] { new WorkItemComment(1, "Anna Hansen (demo)", DateTimeOffset.Now.AddHours(-3), DateTimeOffset.Now.AddHours(-3), "Kan du se på denne når du får anledning? Dette er en fiktiv eksempelkommentar."), new WorkItemComment(2, "Jan Ole Skotterud (demo)", DateTimeOffset.Now.AddHours(-1), DateTimeOffset.Now.AddHours(-1), "Jeg følger opp og avklarer neste steg med teamet.") }.OrderByDescending(c => c.Created).ThenByDescending(c => c.Id).ToArray();
            DiscussionTab.Header = "Diskusjon (2)";
            DiscussionStatus.Text = "DEMOVISNING · 2 fiktive kommentarer"; ReloadDiscussionButton.IsEnabled = false; return;
        }
        if (loadDiscussion == null) { DiscussionStatus.Text = "Koble til Azure DevOps for å hente diskusjonen."; ReloadDiscussionButton.IsEnabled = false; return; }
        loadingDiscussion = true; ReloadDiscussionButton.IsEnabled = false;
        DiscussionStatus.Text = "Henter diskusjon…";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(discussionLifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var comments = await loadDiscussion(timeout.Token);
            if (discussionLifetime.IsCancellationRequested) return;
            DiscussionItems.ItemsSource = comments.OrderByDescending(c => c.Created).ThenByDescending(c => c.Id).ToArray();
            DiscussionTab.Header = $"Diskusjon ({comments.Count})";
            DiscussionStatus.Text = comments.Count == 0 ? "Ingen kommentarer ennå." : $"{comments.Count} kommentarer · Oppdatert {DateTime.Now:HH:mm}";
        }
        catch (Exception ex) when (!discussionLifetime.IsCancellationRequested)
        { DiscussionStatus.Text = "Kunne ikke hente diskusjonen. " + AzureDevOpsClient.Friendly(ex) + (DiscussionItems.Items.Count > 0 ? " Tidligere kommentarer vises fortsatt." : ""); }
        catch (OperationCanceledException) { }
        finally { loadingDiscussion = false; if (!discussionLifetime.IsCancellationRequested) ReloadDiscussionButton.IsEnabled = true; }
    }
    private void OpenWebClick(object sender, RoutedEventArgs e)
    {
        if (webUri == null) return;
        BrowserError.Visibility = Visibility.Collapsed;
        try { Process.Start(new ProcessStartInfo(webUri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception)
        {
            BrowserError.Text = "Kunne ikke åpne nettleseren. Prøv igjen.";
            BrowserError.Visibility = Visibility.Visible;
        }
    }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
