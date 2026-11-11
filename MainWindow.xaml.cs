using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DevOpsPulse;
public partial class MainWindow : Window
{
    private readonly DashboardViewModel vm = new();
    private AppSettings settings;
    private string token;
    private bool busy;
    private DateTimeOffset? lastMentionCheck;
    private ActivityToast? toast;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(150) };
    public MainWindow()
    {
        settings = SettingsStore.Load(); token = SettingsStore.LoadToken(settings);
        InitializeComponent(); DataContext = vm;
        OrgLabel.Text = "dev.azure.com/" + settings.Organization;
        DateLabel.Text = DateTime.Now.ToString("dddd d. MMMM yyyy");
        refreshTimer.Tick += async (_, _) => { if (!busy && token.Length > 0) await RefreshAsync(); };
        Loaded += OnLoaded;
        Closed += (_, _) => { refreshTimer.Stop(); WorkBoard.Stop(); lifetime.Cancel(); toast?.Close(); token = ""; lifetime.Dispose(); };
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var args = Environment.GetCommandLineArgs();
        if(args.Contains("--board-checks"))
        {
            try{await VerifyBoardUiAsync();Close();}
            catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"board-ui-checks.txt"),"FAIL: "+ex.Message);Application.Current.Shutdown(1);}
            return;
        }
        if (args.Contains("--preview")) { await SavePreviews(); return; }
        refreshTimer.Start();
        if (token.Length > 0) await RefreshAsync();
    }
    private async void Navigate(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton button && button.Tag is string name)
        { vm.Section = name; MainScroll?.ScrollToTop(); if(name=="Board" && WorkBoard!=null) await ConfigureBoardAsync(); }
    }
    private Task ConfigureBoardAsync()=>WorkBoard.ConfigureAsync(vm.Data,settings.Organization,settings.Email,()=>new AzureDevOpsClient(settings.Organization,token),item=>ShowWorkItem(item),item=>
    {
        vm.SetData(vm.Data with{WorkItems=vm.Data.WorkItems.Select(w=>w.Id==item.Id?item:w).ToArray()});
    });
    private void ShowSection(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string name)
        {
            foreach (var radio in Descendants<RadioButton>(this).Where(r => r.GroupName == "Nav"))
                if (Equals(radio.Tag, name)) radio.IsChecked = true;
        }
    }
    private async void RefreshClick(object sender, RoutedEventArgs e)
    {
        if (token.Length == 0) { OpenConnection(); return; }
        await RefreshAsync();
    }
    private void ConnectionClick(object sender, RoutedEventArgs e) => OpenConnection();
    private async void OpenConnection()
    {
        if (busy) return;
        if(WorkBoard.IsWorking){ShowError("Vent til boardet er ferdig med oppdateringen før du endrer tilkobling.");return;}
        var dialog = new ConnectionWindow(settings, token) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            // Clear the old account's data before connecting to a different account.
            var changed = dialog.Settings.Email != settings.Email || dialog.Settings.Organization != settings.Organization;
            settings = dialog.Settings; token = dialog.Token;
            if (changed) { WorkBoard.Clear(); vm.SetData(new DashboardData([], [], [], [], [], DateTimeOffset.Now, false)); vm.Notifications.Reset(); lastMentionCheck = null; }
            OrgLabel.Text = "dev.azure.com/" + settings.Organization;
            await RefreshAsync(saveSettings: true);
        }
        else if (dialog.ForgetRequested)
        {
            token = ""; settings = settings with { RememberToken = false };
            WorkBoard.Clear();
            vm.Notifications.Reset(); lastMentionCheck = null;
            vm.SetData(DemoData.Create()); ErrorBanner.Visibility = Visibility.Collapsed;
            if(vm.IsBoard)await ConfigureBoardAsync();
        }
    }
    private async Task RefreshAsync(bool saveSettings = false)
    {
        if (busy) return;
        busy = true; RefreshButton.IsEnabled = false; BusyBar.Visibility = Visibility.Visible;
        ErrorBanner.Visibility = Visibility.Collapsed;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var scanUntil = DateTimeOffset.UtcNow;
        try
        {
            using var client = new AzureDevOpsClient(settings.Organization, token);
            var result = await client.LoadAsync(settings.Email, timeout.Token);
            vm.SetData(result);
            if(vm.IsBoard)await ConfigureBoardAsync();
            var newAlerts = vm.Notifications.ObserveAssignments(result);
            if (saveSettings)
            {
                try { SettingsStore.Save(settings, token); }
                catch (Exception ex) { ShowError("Dataene er hentet, men tilkoblingen kunne ikke lagres: " + ex.Message); }
            }
            try
            {
                if (lastMentionCheck is { } since)
                {
                    vm.Notifications.Status = "Sjekker @omtaler i endrede arbeidsoppgaver…";
                    newAlerts += vm.Notifications.Add(await client.ScanMentionsAsync(result.UserId, settings.Email, since, scanUntil, timeout.Token));
                }
                lastMentionCheck = scanUntil;
                vm.Notifications.Status = $"Sist sjekket {DateTime.Now:HH:mm}. Automatisk hvert 2,5. minutt mens appen er åpen. Varsler gjelder tilgjengelige arbeidsoppgaver i alle prosjekter.";
            }
            catch (Exception ex) when (!lifetime.IsCancellationRequested)
            { vm.Notifications.Status = "@omtaler kunne ikke sjekkes fullstendig. " + AzureDevOpsClient.Friendly(ex) + " Prøver igjen ved neste oppdatering."; }
            if (newAlerts > 0 && vm.Notifications.DesktopAlertsEnabled && !lifetime.IsCancellationRequested)
            {
                toast?.Close(); toast = new ActivityToast(newAlerts, vm.Notifications.Items[0].Detail, OpenNotifications); toast.Show();
            }
        }
        catch (Exception ex) when (!lifetime.IsCancellationRequested)
        {
            var context = vm.IsDemo ? "Demodata vises fortsatt." : $"Viser data sist hentet {vm.Data.LoadedAt:dd.MM HH:mm}.";
            ShowError("Oppdatering mislyktes. " + AzureDevOpsClient.Friendly(ex) + " " + context);
        }
        catch (OperationCanceledException) { }
        finally { busy = false; RefreshButton.IsEnabled = true; BusyBar.Visibility = Visibility.Collapsed; }
    }
    private void ShowError(string message) { ErrorText.Text = message; ErrorBanner.Visibility = Visibility.Visible; }
    private void NotificationsClick(object sender, RoutedEventArgs e) => OpenNotifications();
    private void OpenNotifications()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        var existing = OwnedWindows.OfType<NotificationsWindow>().FirstOrDefault();
        if (existing != null) { vm.Notifications.MarkRead(); existing.Activate(); return; }
        new NotificationsWindow(vm.Notifications, item => ShowWorkItem(item)) { Owner = this }.Show();
    }
    private void OpenWorkItem(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkItem item }) ShowWorkItem(item);
    }
    private void WorkRowClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenWorkItem(sender, e);
        e.Handled = true;
    }
    private WorkItemWindow ShowWorkItem(WorkItem item)
    {
        var org = settings.Organization; var currentToken = token;
        if (!vm.IsDemo && string.IsNullOrWhiteSpace(item.Url))
            item = item with { Url = $"https://dev.azure.com/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(item.Project)}/_workitems/edit/{item.Id}" };
        async Task<IReadOnlyList<WorkItemComment>> LoadDiscussion(CancellationToken ct)
        {
            using var client = new AzureDevOpsClient(org, currentToken);
            return await client.LoadCommentsAsync(item.Project, item.Id, ct);
        }
        var window = new WorkItemWindow(item, vm.Data.LoadedAt, vm.IsDemo, !vm.IsDemo && currentToken.Length > 0 ? LoadDiscussion : null) { Owner = this };
        window.Show();
        return window;
    }
    private void OpenLink(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string workUrl } && !string.IsNullOrWhiteSpace(workUrl))
        {
            var workItem = vm.Data.WorkItems.FirstOrDefault(w => w.Url == workUrl);
            if (workItem != null) { ShowWorkItem(workItem); return; }
        }
        if (sender is not Button { Tag: string url } || url.Length == 0)
        { ShowError("Dette er eksempeldata. Koble til Azure DevOps for å åpne dine oppgaver, PR-er og bygg."); return; }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "dev.azure.com") return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { ShowError("Kunne ikke åpne nettleseren."); }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private async Task SavePreviews()
    {
        // Render our own WPF content for repeatable visual QA; no desktop capture.
        var folder = Path.Combine(AppContext.BaseDirectory, "previews"); Directory.CreateDirectory(folder);
        vm.SetData(DemoData.Create());
        foreach (var (name, file) in new[] { ("Oversikt", "overview"), ("Arbeidsoppgaver", "work"), ("Pull requests", "pull-requests"), ("Pipelines", "pipelines"), ("Board","board") })
        {
            foreach (var radio in Descendants<RadioButton>(this).Where(r => r.GroupName == "Nav"))
                if (Equals(radio.Tag, name)) radio.IsChecked = true;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            if(WorkBoard.IsVisible!=(name=="Board") || MainScroll.IsVisible==(name=="Board"))
                throw new InvalidOperationException("Board-filtrene eller sideinnholdet har feil synlighet på "+name+".");
            UpdateLayout(); MainScroll.ScrollToTop();
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var fileStream = File.Create(Path.Combine(folder, file + ".png")); encoder.Save(fileStream);
        }
        if(WorkBoard.Model.Snapshot is not {} demoBoard || demoBoard.Cards.Count==0)throw new InvalidOperationException("Boardet lastet ingen demokort.");
        var moveCard=demoBoard.Cards.First(c=>c.Column=="Utvikling");
        var moveTarget=demoBoard.Lanes.First(l=>l.ColumnName=="Til review");
        var moveSelector=Descendants<ComboBox>(WorkBoard).First(c=>c.DataContext is BoardCardView v && v.Item.Id==moveCard.Item.Id);
        moveSelector.SelectedItem=((BoardCardView)moveSelector.DataContext).Targets.First(t=>t.Key==moveTarget.Key);
        var moveButton=Descendants<Button>(WorkBoard).First(b=>Equals(b.Content,"Flytt") && b.DataContext is BoardCardView v && v.Item.Id==moveCard.Item.Id);
        moveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        var movedCard=WorkBoard.Model.Snapshot!.Cards.Single(c=>c.Item.Id==moveCard.Item.Id);
        if(movedCard.Column!="Til review" || movedCard.Revision!=moveCard.Revision+1 || movedCard.Item.State!=moveCard.Item.State || movedCard.Row!=moveCard.Row)
            throw new InvalidOperationException("Flytt-knappen oppdaterte ikke riktig kolonne eller bevarte ikke status og svømmebane.");
        foreach(var radio in Descendants<RadioButton>(this).Where(r=>r.GroupName=="Nav" && Equals(r.Tag,"Oversikt")))radio.IsChecked=true;
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        if(WorkBoard.IsVisible || !MainScroll.IsVisible)throw new InvalidOperationException("Board-filtrene blir liggende etter navigasjon tilbake til oversikten.");
        ProjectBox.SelectedItem = "Blåresept";
        vm.Search = "rapport";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (vm.Project != "Blåresept" || !Equals(ProjectBox.SelectedItem, "Blåresept")) throw new InvalidOperationException("Prosjektfilteret mistet valgt prosjekt etter søk.");
        vm.Search = ""; vm.Section = "Oversikt";
        vm.SetData(vm.Data);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (vm.Project != "Blåresept") throw new InvalidOperationException("Prosjektfilteret mistet valgt prosjekt etter oppdatering.");
        ProjectBox.SelectedItem = DashboardViewModel.FocusProjects;
        PipelineProjectBox.SelectedItem = "Blåresept";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        vm.SetData(vm.Data);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (vm.Project != DashboardViewModel.FocusProjects || vm.PipelineProject != "Blåresept" || !Equals(PipelineProjectBox.SelectedItem, "Blåresept"))
            throw new InvalidOperationException("Prosjektgruppen eller pipelinefilteret mistet valget etter oppdatering.");
        var connection = new ConnectionWindow(new AppSettings(), "") { Owner = this };
        connection.Show();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        connection.UpdateLayout();
        var dialogBitmap = new RenderTargetBitmap((int)connection.ActualWidth, (int)connection.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        dialogBitmap.Render(connection);
        var dialogEncoder = new PngBitmapEncoder(); dialogEncoder.Frames.Add(BitmapFrame.Create(dialogBitmap));
        using (var dialogFile = File.Create(Path.Combine(folder, "connection.png"))) dialogEncoder.Save(dialogFile);
        connection.Close();
        vm.Project = DashboardViewModel.AllProjects;
        vm.Section = "Arbeidsoppgaver";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var workButton = Descendants<Button>(this).First(b => b.DataContext is WorkItem && b.Content is string);
        var clickedItem = (WorkItem)workButton.DataContext;
        workButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var details = OwnedWindows.OfType<WorkItemWindow>().Single();
        if (details.OpenWebButton.IsEnabled) throw new InvalidOperationException("Demooppgave skal ikke åpne en nettside.");
        var linkedDetails = new WorkItemWindow(clickedItem with { Url = $"https://dev.azure.com/helsedir/HESA/_workitems/edit/{clickedItem.Id}" }, vm.Data.LoadedAt, false);
        if (!linkedDetails.OpenWebButton.IsEnabled) throw new InvalidOperationException("Oppgave med gyldig lenke mangler nettknapp.");
        linkedDetails.Close();
        if (details.Item.Id != clickedItem.Id || !IsVisible || !details.IsVisible)
            throw new InvalidOperationException("Klikk på arbeidsoppgave åpnet ikke riktig detaljvindu.");
        details.UpdateLayout();
        var detailsBitmap = new RenderTargetBitmap((int)details.ActualWidth, (int)details.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        detailsBitmap.Render(details);
        var detailsEncoder = new PngBitmapEncoder(); detailsEncoder.Frames.Add(BitmapFrame.Create(detailsBitmap));
        using (var detailsFile = File.Create(Path.Combine(folder, "work-item.png"))) detailsEncoder.Save(detailsFile);
        if (details.DiscussionItems.Items.Count != 2) throw new InvalidOperationException("Demo-diskusjonen mangler ved første åpning.");
        details.DiscussionTab.IsSelected = true;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        details.UpdateLayout();
        var discussionBitmap = new RenderTargetBitmap((int)details.ActualWidth, (int)details.ActualHeight, 96,96,PixelFormats.Pbgra32);
        discussionBitmap.Render(details); var discussionEncoder = new PngBitmapEncoder(); discussionEncoder.Frames.Add(BitmapFrame.Create(discussionBitmap));
        using (var discussionFile = File.Create(Path.Combine(folder, "discussion.png"))) discussionEncoder.Save(discussionFile);
        var firstLoad = new TaskCompletionSource<IReadOnlyList<WorkItemComment>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstOpen = new WorkItemWindow(clickedItem with { Url = $"https://dev.azure.com/helsedir/HESA/_workitems/edit/{clickedItem.Id}" }, vm.Data.LoadedAt, false, _ => firstLoad.Task) { Owner = this };
        firstOpen.Show();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (!firstOpen.OpenWebButton.IsEnabled || firstOpen.ReloadDiscussionButton.IsEnabled) throw new InvalidOperationException("Nettknappen er deaktivert mens diskusjonen lastes første gang.");
        firstLoad.SetResult([new WorkItemComment(77,"UI-test",DateTimeOffset.Now,DateTimeOffset.Now,"Første kommentar")]);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (firstOpen.DiscussionItems.Items.Count != 1) throw new InvalidOperationException("Diskusjonen ble ikke vist etter første henting.");
        firstOpen.Close();
        var loadAttempts = 0;
        var retryWindow = new WorkItemWindow(clickedItem, vm.Data.LoadedAt, false, _ => ++loadAttempts == 1
            ? Task.FromException<IReadOnlyList<WorkItemComment>>(new InvalidOperationException("403 test"))
            : Task.FromResult<IReadOnlyList<WorkItemComment>>([])) { Owner = this };
        retryWindow.Show(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (!retryWindow.DiscussionStatus.Text.Contains("403 test") || !retryWindow.ReloadDiscussionButton.IsEnabled) throw new InvalidOperationException("Diskusjonsfeil viser ikke feil og mulighet til å prøve igjen.");
        retryWindow.ReloadDiscussionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (retryWindow.DiscussionStatus.Text != "Ingen kommentarer ennå.") throw new InvalidOperationException("Tom diskusjon etter nytt forsøk vises feil.");
        retryWindow.Close();
        var wasCancelled = false;
        var cancelWindow = new WorkItemWindow(clickedItem, vm.Data.LoadedAt, false, async ct => { ct.Register(() => wasCancelled = true); await Task.Delay(Timeout.Infinite,ct); return Array.Empty<WorkItemComment>(); }) { Owner = this };
        cancelWindow.Show(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); cancelWindow.Close();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (!wasCancelled) throw new InvalidOperationException("Lukking av oppgaven avbryter ikke diskusjonshentingen.");
        var otherDetails = ShowWorkItem(vm.Data.WorkItems.First(w => w.Id != clickedItem.Id));
        if (OwnedWindows.OfType<WorkItemWindow>().Count() != 2) throw new InvalidOperationException("Flere oppgaver kunne ikke åpnes samtidig.");
        var closeButton = Descendants<Button>(otherDetails).Single(b => Equals(b.Content, "Lukk"));
        closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (otherDetails.IsVisible || !details.IsVisible || !IsVisible)
            throw new InvalidOperationException("Lukking av ett detaljvindu påvirket andre vinduer.");
        details.Close();
        var emptyDetails = ShowWorkItem(clickedItem with { Description = "", ReproSteps = "", AcceptanceCriteria = "", Tags = "", AssignedTo = "", AreaPath = "" });
        if (emptyDetails.DescriptionValue.Text != "Ingen beskrivelse oppgitt." || emptyDetails.ReproSection.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Manglende oppgavedetaljer ble ikke håndtert.");
        emptyDetails.Close();
        vm.Notifications.Add([new ActivityEvent("ui-notification", "Du er tagget i en oppgave", $"#{clickedItem.Id} · {clickedItem.Title}", clickedItem, DateTimeOffset.Now)]);
        var notificationsWindow = new NotificationsWindow(vm.Notifications, item => ShowWorkItem(item)) {Owner=this};
        notificationsWindow.Show(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        if (vm.Notifications.UnreadCount != 0) throw new InvalidOperationException("Åpning av varsler nullstiller ikke ulest-telleren.");
        notificationsWindow.UpdateLayout();
        var notificationsBitmap = new RenderTargetBitmap((int)notificationsWindow.ActualWidth, (int)notificationsWindow.ActualHeight, 96,96,PixelFormats.Pbgra32);
        notificationsBitmap.Render(notificationsWindow); var notificationsEncoder = new PngBitmapEncoder(); notificationsEncoder.Frames.Add(BitmapFrame.Create(notificationsBitmap));
        using (var notificationsFile = File.Create(Path.Combine(folder, "notifications.png"))) notificationsEncoder.Save(notificationsFile);
        notificationsWindow.Close();
        File.WriteAllText(Path.Combine(folder, "ui-checks.txt"), "Four navigation actions rendered. Project selection survives search and refresh. Combined project group and independent pipeline selection survive refresh. Connection dialog rendered. Actual task button opens correct modeless details window; two work item windows coexist. Browser button enabled before first discussion load completes. Discussion loads on first open, errors are visible and retry recovers, empty comments distinguished, closing cancels pending load. Notification inbox renders and clears unread badge.\n");
        Close();
    }
}
