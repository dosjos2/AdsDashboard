using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DevOpsPulse;
public partial class BoardView : UserControl
{
    public BoardViewModel Model {get;}=new();
    private Func<AzureDevOpsClient>? factory;
    private Action<WorkItem>? openItem, updatedItem;
    private DashboardData? context;
    private string identity="";
    private bool changing, busy, dragging, requiresReload;
    private Point dragStart;
    private readonly CancellationTokenSource lifetime=new();
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(150)};
    public bool IsWorking=>busy;
    public BoardView()
    {
        InitializeComponent();DataContext=Model;
        timer.Tick+=async(_,_)=>{if(IsVisible && context!=null && !busy && !dragging && !requiresReload)await RunAsync(ReloadCore);};timer.Start();
    }
    public void Stop(){timer.Stop();lifetime.Cancel();}
    public void Clear()
    {
        context=null;identity="";factory=null;requiresReload=false;Model.Set(null);
        changing=true;try{BoardProject.ItemsSource=null;BoardTeam.ItemsSource=null;BoardChoice.ItemsSource=null;}finally{changing=false;}
        BoardStatus.Text="Koble til og velg et board.";
    }
    public async Task ConfigureAsync(DashboardData data,string organization,string email,Func<AzureDevOpsClient> createClient,Action<WorkItem> open,Action<WorkItem> updated)
    {
        factory=createClient;openItem=open;updatedItem=updated;Model.Email=email;Model.UserId=data.UserId;
        var next=$"{organization}|{email}|{data.IsDemo}";context=data;
        if(identity==next && BoardProject.Items.Count>0)return;
        if(busy)return; identity=next;
        await RunAsync(async()=>
        {
            changing=true;
            try{BoardProject.ItemsSource=data.Projects;BoardProject.SelectedItem=data.Projects.FirstOrDefault(p=>p.Equals("HESA",StringComparison.OrdinalIgnoreCase))??data.Projects.FirstOrDefault();BoardTeam.ItemsSource=null;BoardChoice.ItemsSource=null;Model.Set(null);}finally{changing=false;}
            await LoadTeamsCore();
        });
    }
    private async void ProjectChanged(object sender,SelectionChangedEventArgs e){if(!changing && !busy)await RunAsync(LoadTeamsCore);}
    private async void TeamChanged(object sender,SelectionChangedEventArgs e){if(!changing && !busy)await RunAsync(LoadBoardsCore);}
    private async void BoardChanged(object sender,SelectionChangedEventArgs e){if(!changing && !busy)await RunAsync(ReloadCore);}
    private async void ReloadClick(object sender,RoutedEventArgs e)=>await RefreshAsync();
    public Task RefreshAsync()=>RunAsync(BoardTeam.SelectedItem==null?LoadTeamsCore:BoardChoice.SelectedItem==null?LoadBoardsCore:ReloadCore);
    private async Task LoadTeamsCore()
    {
        changing=true;try{BoardTeam.ItemsSource=null;BoardChoice.ItemsSource=null;Model.Set(null);}finally{changing=false;}
        if(BoardProject.SelectedItem is not string project || context==null)return;
        IReadOnlyList<BoardReference> teams;
        if(context.IsDemo)teams=[new BoardReference("demo-team","Demoteam")];
        else{using var client=factory!();var id=context.ProjectIds.TryGetValue(project,out var projectId)?projectId:project;teams=await client.ListTeamsAsync(id,lifetime.Token);}
        var preferredTeam=project.Equals("hesa",StringComparison.OrdinalIgnoreCase)
            ? teams.FirstOrDefault(t=>t.Name.Trim().Equals("hesa team",StringComparison.OrdinalIgnoreCase))
            : null;
        changing=true;try{BoardTeam.ItemsSource=teams;BoardTeam.SelectedItem=preferredTeam??teams.FirstOrDefault();}finally{changing=false;}
        if(teams.Count==0){BoardStatus.Text="Ingen tilgjengelige team i prosjektet.";return;}
        await LoadBoardsCore();
    }
    private async Task LoadBoardsCore()
    {
        changing=true;try{BoardChoice.ItemsSource=null;Model.Set(null);}finally{changing=false;}
        if(BoardProject.SelectedItem is not string project || BoardTeam.SelectedItem is not BoardReference team || context==null)return;
        IReadOnlyList<BoardReference> boards;
        if(context.IsDemo)boards=[new BoardReference("demo-board","Produktboard")];
        else{using var client=factory!();boards=await client.ListBoardsAsync(project,team.Id,lifetime.Token);}
        changing=true;try{BoardChoice.ItemsSource=boards;BoardChoice.SelectedItem=boards.FirstOrDefault();}finally{changing=false;}
        if(boards.Count==0){BoardStatus.Text="Ingen tilgjengelige boards for teamet.";return;}
        await ReloadCore();
    }
    private async Task ReloadCore()
    {
        if(BoardProject.SelectedItem is not string project || BoardTeam.SelectedItem is not BoardReference team || BoardChoice.SelectedItem is not BoardReference board || context==null)return;
        BoardSnapshot snapshot;
        if(context.IsDemo)snapshot=Model.Snapshot is {Project:var current} existing && current==project ? existing:BoardSnapshot.Demo(project);
        else{using var client=factory!();snapshot=await client.LoadBoardAsync(project,team.Id,board.Id,lifetime.Token);}
        Model.Set(snapshot);requiresReload=false;
        WriteHint.Text=context.IsDemo?"DEMOVISNING · Flytting endrer bare eksempeldata. Ingen endringer sendes til Azure DevOps.":"Flytting lagres i Azure DevOps og krever Work Items: Read & write. Boardet bruker egne prosjekt- og teamvalg.";
        BoardStatus.Text=$"{(context.IsDemo?"Demo · ":"")}Oppdatert {DateTime.Now:HH:mm} · Automatisk hvert 150. sekund. {snapshot.Warning}";
    }
    private async Task RunAsync(Func<Task> action)
    {
        if(busy || lifetime.IsCancellationRequested)return;
        SetBusy(true);BoardStatus.Foreground=(Brush)FindResource("Muted");BoardStatus.Text="Henter board…";
        try{await action();}
        catch(Exception ex)when(!lifetime.IsCancellationRequested){Error(AzureDevOpsClient.Friendly(ex));}
        catch(Exception)when(lifetime.IsCancellationRequested){}
        finally{SetBusy(false);}
    }
    private void SetBusy(bool value){busy=value;Selectors.IsEnabled=!value;LaneItems.IsEnabled=!value;BoardBusy.Visibility=value?Visibility.Visible:Visibility.Collapsed;}
    private void Error(string message){BoardStatus.Foreground=new SolidColorBrush(Color.FromRgb(181,70,89));BoardStatus.Text=message;}
    private void CardOpen(object sender,RoutedEventArgs e){if(sender is FrameworkElement{DataContext:BoardCardView c})openItem?.Invoke(c.Item);}
    private async void CardMove(object sender,RoutedEventArgs e)
    {if(sender is FrameworkElement{DataContext:BoardCardView c}){if(c.SelectedTarget is {} target)await MoveAsync(c.Card,target);else Error("Velg en målkolonne i «Flytt til»-listen først.");}}
    public async Task MoveAsync(BoardCard card,BoardLane target)
    {
        if(busy || lifetime.IsCancellationRequested || Model.Snapshot is not {} snapshot)return;
        if(requiresReload){Error("Oppdater boardet før du flytter flere oppgaver.");return;}
        if(snapshot.LaneFor(card)?.Key==target.Key)return;
        SetBusy(true);BoardStatus.Text=$"Flytter #{card.Item.Id} til {target.Name}…";
        try
        {
            if(!target.Supports(card.Item.Type))throw new InvalidOperationException("Oppgavetypen støttes ikke i denne kolonnen.");
            BoardCard updated;
            if(context?.IsDemo==true)updated=card with{Revision=card.Revision+1,Column=target.ColumnName,Done=target.Done,Item=card.Item with{State=target.States[card.Item.Type],Changed=DateTimeOffset.Now}};
            else{using var client=factory!();updated=await client.MoveBoardCardAsync(snapshot,card,target,lifetime.Token);}
            Model.Set(snapshot with{Cards=snapshot.Cards.Select(c=>c.Item.Id==updated.Item.Id?updated:c).ToArray()});
            updatedItem?.Invoke(updated.Item);BoardStatus.Foreground=(Brush)FindResource("Muted");BoardStatus.Text=$"#{updated.Item.Id} er flyttet til {target.Name}{(context?.IsDemo==true?" (kun demo)":" og lagret i Azure DevOps")}.";
        }
        catch(Exception ex)when(!lifetime.IsCancellationRequested){requiresReload=true;Error(ex.Message+" Oppdater boardet før du prøver igjen.");}
        catch(Exception)when(lifetime.IsCancellationRequested){}
        finally{SetBusy(false);}
    }
    private void CardMouseDown(object sender,MouseButtonEventArgs e)=>dragStart=e.GetPosition(this);
    private void CardMouseMove(object sender,MouseEventArgs e)
    {
        if(busy || requiresReload || e.LeftButton!=MouseButtonState.Pressed || sender is not Border{DataContext:BoardCardView card})return;
        var point=e.GetPosition(this);if(Math.Abs(point.X-dragStart.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y-dragStart.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        if(e.OriginalSource is DependencyObject source)
            for(var current=source;current!=null && !ReferenceEquals(current,sender);current=current is Visual?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current))if(current is ComboBox or TextBox)return;
        dragging=true;try{DragDrop.DoDragDrop((DependencyObject)sender,new DataObject(typeof(BoardCard),card.Card),DragDropEffects.Move);e.Handled=true;}finally{dragging=false;}
    }
    private void LaneDragOver(object sender,DragEventArgs e)
    {
        var can=!busy && !requiresReload && e.Data.GetData(typeof(BoardCard)) is BoardCard card && sender is Border{DataContext:BoardLaneView lane} && lane.Lane.Supports(card.Item.Type) && Model.Snapshot?.Cards.Any(c=>c.Item.Id==card.Item.Id && c.Revision==card.Revision)==true;
        e.Effects=can?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;
        if(sender is Border border)border.BorderBrush=new SolidColorBrush(can?Color.FromRgb(82,102,233):Color.FromRgb(224,230,239));
    }
    private void LaneDragLeave(object sender,DragEventArgs e){if(sender is Border b)b.BorderBrush=new SolidColorBrush(Color.FromRgb(224,230,239));}
    private async void LaneDrop(object sender,DragEventArgs e)
    {
        LaneDragLeave(sender,e);e.Handled=true;
        if(e.Data.GetData(typeof(BoardCard)) is BoardCard card && sender is Border{DataContext:BoardLaneView lane} && Model.Snapshot?.Cards.Any(c=>c.Item.Id==card.Item.Id && c.Revision==card.Revision)==true)await MoveAsync(card,lane.Lane);
    }
}
