using System.ComponentModel;

namespace DevOpsPulse;
public sealed record BoardReference(string Id, string Name);
public sealed record BoardColumn(string Id, string Name, bool IsSplit, int Limit, string Kind, IReadOnlyDictionary<string,string> States);
public sealed record BoardLane(string Key, string Name, string ColumnName, bool Done, int Limit, IReadOnlyDictionary<string,string> States)
{
    public bool Supports(string type) => States.ContainsKey(type);
}
public sealed record BoardCard(WorkItem Item, int Revision, string Column, bool Done, string Row, double Rank);
public sealed record BoardSnapshot(string Project, string Team, string Id, string Name, string ColumnField, string DoneField, string RowField, IReadOnlyList<BoardColumn> Columns, IReadOnlyList<BoardCard> Cards, string Warning = "")
{
    public IReadOnlyList<BoardLane> Lanes => Columns.SelectMany(c => c.IsSplit
        ? new[] { new BoardLane(c.Id+":doing", c.Name+" · Pågår",c.Name,false,c.Limit,c.States), new BoardLane(c.Id+":done",c.Name+" · Ferdig",c.Name,true,c.Limit,c.States) }
        : new[] { new BoardLane(c.Id,c.Name,c.Name,false,c.Limit,c.States) }).ToArray();
    public BoardLane? LaneFor(BoardCard card)
    {
        var sameColumn = Lanes.Where(l => l.ColumnName == card.Column).ToArray();
        var exact = sameColumn.Length == 1 ? sameColumn[0] : sameColumn.FirstOrDefault(l=>l.Done==card.Done);
        if (exact != null) return exact;
        // State alone is insufficient when several board columns share a workflow state.
        if (card.Column.Length > 0) return null;
        var candidates = Lanes.Where(l => !l.Done && l.States.TryGetValue(card.Item.Type, out var state) && state == card.Item.State).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }
    public static BoardSnapshot Demo(string project)
    {
        var types = new[]{"Task","Feature","Bug","Product Backlog Item"};
        var initial = types.ToDictionary(t=>t,_=>"New");
        var active = types.ToDictionary(t=>t,t=>t=="Product Backlog Item"?"Committed":"Active");
        var done = types.ToDictionary(t=>t,t=>t=="Product Backlog Item"?"Done":"Closed");
        var columns = new[] {new BoardColumn("new","Klar",false,0,"incoming",initial),new BoardColumn("active","Utvikling",true,4,"inProgress",active),new BoardColumn("review","Til review",false,3,"inProgress",active),new BoardColumn("done","Fullført",false,0,"outgoing",done)};
        var cards = DemoData.Create().WorkItems.Where(w=>w.Project==project).Select((w,i)=>new BoardCard(w,1,w.IsDone?"Fullført":w.State=="New"?"Klar":"Utvikling",false,i==0?"Haster":"Standard",i)).ToArray();
        return new BoardSnapshot(project,"Demoteam","demo-board","Produktboard","WEF_Demo_Kanban.Column","WEF_Demo_Kanban.Column.Done","WEF_Demo_Kanban.Lane",columns,cards);
    }
}
public sealed class BoardCardView
{
    public BoardCard Card { get; }
    public WorkItem Item => Card.Item;
    public IReadOnlyList<BoardLane> Targets { get; }
    public BoardLane? SelectedTarget { get; set; }
    public string Assignee => string.IsNullOrWhiteSpace(Item.AssignedTo) ? "Ikke tildelt" : Item.AssignedTo;
    public string Metadata => $"#{Item.Id} · {Item.Type} · {Item.PriorityLabel}";
    public string Row => string.IsNullOrWhiteSpace(Card.Row) ? "" : Card.Row;
    public BoardCardView(BoardCard card, BoardSnapshot snapshot) { Card=card; var current=snapshot.LaneFor(card)?.Key; Targets=snapshot.Lanes.Where(l=>l.Key!=current && l.Supports(card.Item.Type)).ToArray(); }
}
public sealed record BoardLaneView(BoardLane Lane, IReadOnlyList<BoardCardView> Cards, string Wip)
{
    public string Count => Cards.Count.ToString();
}
public sealed class BoardViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public BoardSnapshot? Snapshot { get; private set; }
    private string search="";
    private bool onlyMine;
    public string Email { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Search {get=>search;set{search=value;Rebuild();}}
    public bool OnlyMine {get=>onlyMine;set{onlyMine=value;Rebuild();}}
    public IReadOnlyList<BoardLaneView> Columns {get;private set;}=[];
    public string Summary => Snapshot == null ? "Velg prosjekt, team og board." : $"{Columns.Sum(c=>c.Cards.Count)} av {Snapshot.Cards.Count} oppgaver · Ferdige oppgaver endret siste 30 dager";
    public void Set(BoardSnapshot? snapshot) { Snapshot=snapshot;Rebuild(); }
    private void Rebuild()
    {
        if (Snapshot is not { } s) {Columns=[];PropertyChanged?.Invoke(this,new(""));return;}
        var cards=s.Cards.Where(c=>(!onlyMine || (UserId.Length>0 && c.Item.AssignedToId.Equals(UserId,StringComparison.OrdinalIgnoreCase)) || (Email.Length>0 && c.Item.AssignedEmail.Equals(Email,StringComparison.OrdinalIgnoreCase))) && (string.IsNullOrWhiteSpace(search) || $"{c.Item.Id} {c.Item.Title} {c.Item.Type} {c.Item.AssignedTo} {c.Row}".Contains(search.Trim(),StringComparison.OrdinalIgnoreCase))).ToArray();
        var lanes=s.Lanes.ToList();
        if(cards.Any(c=>s.LaneFor(c)==null)) lanes.Add(new BoardLane("unmapped","Uten kolonne","",false,0,new Dictionary<string,string>()));
        Columns=lanes.Select(l=>new BoardLaneView(l,cards.Where(c=>(s.LaneFor(c)?.Key??"unmapped")==l.Key).OrderBy(c=>c.Rank).ThenBy(c=>c.Item.Id).Select(c=>new BoardCardView(c,s)).ToArray(),l.Limit>0?$"WIP {s.Cards.Count(c=>c.Column==l.ColumnName)}/{l.Limit}":"")).ToArray();
        PropertyChanged?.Invoke(this,new(""));
    }
}
