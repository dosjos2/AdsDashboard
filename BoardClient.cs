using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevOpsPulse;
public sealed class BoardMoveException(string message, bool uncertain = false) : Exception(message)
{
    public bool Uncertain { get; } = uncertain;
}
public sealed partial class AzureDevOpsClient
{
    public async Task<IReadOnlyList<BoardReference>> ListTeamsAsync(string project, CancellationToken ct)
    {
        var all = new List<BoardReference>();
        for(var skip=0;;skip+=100)
        {
            var values=Values((await RequestAsync($"{root}/_apis/projects/{E(project)}/teams?api-version=7.1&$top=100&$skip={skip}",null,ct)).Json);
            all.AddRange(values.Select(t=>new BoardReference(Str(t,"id"),Str(t,"name"))));
            if(values.Length<100) break;
        }
        return all.DistinctBy(t=>t.Id).OrderBy(t=>t.Name).ToArray();
    }
    public async Task<IReadOnlyList<BoardReference>> ListBoardsAsync(string project,string team,CancellationToken ct)
    {
        var values=Values((await RequestAsync($"{root}/{E(project)}/{E(team)}/_apis/work/boards?api-version=7.1",null,ct)).Json);
        return values.Select(b=>new BoardReference(Str(b,"id"),Str(b,"name"))).ToArray();
    }
    public async Task<BoardSnapshot> LoadBoardAsync(string project,string team,string boardId,CancellationToken ct)
    {
        var prefix=$"{root}/{E(project)}/{E(team)}/_apis/work";
        var board=(await RequestAsync($"{prefix}/boards/{E(boardId)}?api-version=7.1",null,ct)).Json;
        if(board.TryGetProperty("isValid",out var valid) && valid.ValueKind==JsonValueKind.False) throw new InvalidOperationException("Azure DevOps melder at board-oppsettet er ugyldig.");
        var fields=Obj(board,"fields");
        var columnField=Str(Obj(fields,"columnField"),"referenceName"); var doneField=Str(Obj(fields,"doneField"),"referenceName"); var rowField=Str(Obj(fields,"rowField"),"referenceName");
        foreach(var name in new[]{columnField,doneField,rowField}.Where(s=>s.Length>0)) ValidateField(name);
        if(!board.TryGetProperty("columns",out var cs) || cs.ValueKind!=JsonValueKind.Array) throw new JsonException("Boardet mangler kolonner.");
        var columns=cs.EnumerateArray().Select(c=>new BoardColumn(Str(c,"id"),Str(c,"name"),Bool(c,"isSplit"),Int(c,"itemLimit"),Str(c,"columnType"),
            Obj(c,"stateMappings").ValueKind==JsonValueKind.Object ? Obj(c,"stateMappings").EnumerateObject().Where(p=>p.Value.ValueKind==JsonValueKind.String).ToDictionary(p=>p.Name,p=>p.Value.GetString()??"") : new Dictionary<string,string>())).ToArray();
        var types=columns.SelectMany(c=>c.States.Keys).Distinct().ToArray();
        if(columns.Length==0 || types.Length==0) throw new InvalidOperationException("Boardet mangler kolonner eller statuskoblinger for oppgavetyper.");
        var area=(await RequestAsync(prefix+"/teamsettings/teamfieldvalues?api-version=7.1",null,ct)).Json;
        var settings=(await RequestAsync(prefix+"/teamsettings?api-version=7.1",null,ct)).Json;
        var areaField=Str(Obj(area,"field"),"referenceName"); ValidateField(areaField);
        if(!area.TryGetProperty("values",out var areaValues) || areaValues.ValueKind!=JsonValueKind.Array) throw new JsonException("Teamet mangler områdeliste.");
        var areaClauses=areaValues.EnumerateArray().Select(a=>$"[{areaField}] {(areaField=="System.AreaPath" && Bool(a,"includeChildren") ? "UNDER" : "=")} '{Q(Str(a,"value"))}'").ToArray();
        if(areaClauses.Length==0) throw new InvalidOperationException("Teamet har ingen konfigurerte områder. Kontroller teamoppsettet i Azure DevOps.");
        var iteration=Str(Obj(settings,"backlogIteration"),"path");
        var typeClauses=types.Select(t=>
        {
            var mappedStates=columns.Where(c=>c.States.ContainsKey(t)).Select(c=>c.States[t]).Distinct().ToArray();
            var terminal=columns.Where(c=>c.Kind=="outgoing" && c.States.ContainsKey(t)).Select(c=>c.States[t]).Distinct().ToArray();
            return $"([System.WorkItemType] = '{Q(t)}' AND [System.State] IN ({string.Join(",",mappedStates.Select(s=>"'"+Q(s)+"'"))})"+(terminal.Length==0 ? "" : $" AND ([System.State] NOT IN ({string.Join(",",terminal.Select(s=>"'"+Q(s)+"'"))}) OR [System.ChangedDate] >= @Today - 30)")+")";
        }).ToArray();
        var scope=$"[System.TeamProject] = '{Q(project)}' AND ({string.Join(" OR ",areaClauses)}) AND ({string.Join(" OR ",typeClauses)})"+(iteration.Length>0?$" AND [System.IterationPath] UNDER '{Q(iteration)}'":"");
        var items=new List<JsonElement>();var after=0;var omitted=0;
        while(true)
        {
            var query=$"SELECT [System.Id] FROM WorkItems WHERE {scope} AND [System.Id] > {after} ORDER BY [System.Id] ASC";
            var ids=(await RequestAsync(root+"/_apis/wit/wiql?api-version=7.1&$top=500",new{query},ct)).Json.GetProperty("workItems").EnumerateArray().Select(i=>Int(i,"id")).ToArray();
            if(ids.Length==0) break;
            foreach(var chunk in ids.Chunk(200))
            {
                var values=Values((await RequestAsync(root+"/_apis/wit/workitemsbatch?api-version=7.1",new{ids=chunk,errorPolicy="Omit"},ct)).Json);
                items.AddRange(values);omitted+=chunk.Length-values.Length;
            }
            var next=ids.Max();if(next<=after) throw new InvalidOperationException("Gjentatt side ved henting av board.");after=next;
            if(ids.Length<500) break;
        }
        var snapshot=new BoardSnapshot(project,team,boardId,Str(board,"name"),columnField,doneField,rowField,columns,[],omitted>0?$"{omitted} oppgaver kunne ikke leses. Boardet er ufullstendig.":"");
        return snapshot with{Cards=items.DistinctBy(i=>Int(i,"id")).Select(i=>ParseBoardCard(i,snapshot)).ToArray()};
    }
    private BoardCard ParseBoardCard(JsonElement item,BoardSnapshot board)
    {
        var f=item.GetProperty("fields");var rank=Obj(f,"Microsoft.VSTS.Common.StackRank");
        if(rank.ValueKind!=JsonValueKind.Number)rank=Obj(f,"Microsoft.VSTS.Common.BacklogPriority");
        return new BoardCard(ParseWorkItem(item),Int(item,"rev"),Str(f,board.ColumnField),Bool(f,board.DoneField),Str(f,board.RowField),rank.ValueKind==JsonValueKind.Number && rank.TryGetDouble(out var n)?n:0);
    }
    public async Task<BoardCard> MoveBoardCardAsync(BoardSnapshot board,BoardCard card,BoardLane target,CancellationToken ct)
    {
        var lane=board.Lanes.FirstOrDefault(l=>l.Key==target.Key)??throw new ArgumentException("Kolonnen tilhører ikke valgt board.");
        if(!board.Cards.Any(c=>c.Item.Id==card.Item.Id && c.Revision==card.Revision) || card.Item.Project!=board.Project)throw new ArgumentException("Oppgaven tilhører ikke det lastede boardet.");
        if(!lane.States.TryGetValue(card.Item.Type,out var state) || string.IsNullOrWhiteSpace(state))throw new ArgumentException("Oppgavetypen kan ikke flyttes til denne kolonnen.");
        if(board.LaneFor(card)?.Key==lane.Key) return card;
        if(card.Revision<=0)throw new ArgumentException("Oppgaven mangler revisjon. Oppdater boardet først.");
        ValidateField(board.ColumnField);
        if(board.Columns.Any(c=>c.IsSplit) && board.DoneField.Length==0)throw new ArgumentException("Boardet mangler feltet som styrer delte kolonner.");
        var operations=new List<object>{new{op="test",path="/rev",value=(object)card.Revision},new{op="add",path="/fields/System.State",value=(object)state},new{op="add",path="/fields/"+board.ColumnField,value=(object)lane.ColumnName}};
        if(board.DoneField.Length>0){ValidateField(board.DoneField);operations.Add(new{op="add",path="/fields/"+board.DoneField,value=(object)lane.Done});}
        using var request=new HttpRequestMessage(HttpMethod.Patch,$"{root}/{E(board.Project)}/_apis/wit/workitems/{card.Item.Id}?api-version=7.1") {Content=new StringContent(JsonSerializer.Serialize(operations),Encoding.UTF8,"application/json-patch+json")};
        try
        {
            // A write may have reached the service even if the response is lost. Never retry it automatically.
            using var response=await http.SendAsync(request,ct);
            var body=await response.Content.ReadAsStringAsync(ct);
            if(response.StatusCode==HttpStatusCode.Forbidden)throw new BoardMoveException("Flytting krever Work Items (Read & write) på tokenet og redigeringstilgang til oppgaven.");
            if(response.StatusCode==HttpStatusCode.Unauthorized)throw new BoardMoveException("Tokenet er ugyldig eller utløpt. Åpne Tilkobling.");
            if((int)response.StatusCode>=500)throw new BoardMoveException($"Serveren eller gatewayen svarte HTTP {(int)response.StatusCode}. Resultatet av flyttingen er ukjent; endringen kan være lagret. Oppdater boardet før du prøver igjen.",true);
            if(!response.IsSuccessStatusCode)
            {
                var message="";try{using var error=JsonDocument.Parse(body);message=Str(error.RootElement,"message");}catch(JsonException){}
                if(response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed || message.Contains("revision",StringComparison.OrdinalIgnoreCase))
                    throw new BoardMoveException("Oppgaven er endret av noen andre. Oppdater boardet før du flytter den igjen.");
                throw new BoardMoveException($"Flyttingen ble avvist (HTTP {(int)response.StatusCode}). "+(message.Length>0?message[..Math.Min(350,message.Length)]:"Kontroller arbeidsflyt og påkrevde felter."));
            }
            using var document=JsonDocument.Parse(body);var updated=ParseBoardCard(document.RootElement,board);
            if(updated.Item.Id!=card.Item.Id || updated.Revision<=card.Revision || updated.Column!=lane.ColumnName || updated.Item.State!=state || (board.DoneField.Length>0 && updated.Done!=lane.Done))
                throw new BoardMoveException("Serverens svar bekrefter ikke ønsket plassering. Oppdater boardet for å se hvor oppgaven ligger.",true);
            return updated;
        }
        catch(Exception ex)when(ex is HttpRequestException or OperationCanceledException or JsonException)
        {throw new BoardMoveException("Kunne ikke bekrefte resultatet av flyttingen. Oppdater boardet før du prøver igjen; endringen kan allerede være lagret.",true);}
    }
    private static string Q(string value)=>value.Replace("'","''");
    private static void ValidateField(string field)
    {if(!Regex.IsMatch(field,@"^[A-Za-z][A-Za-z0-9_.]*$"))throw new InvalidOperationException("Boardet returnerte et manglende eller ugyldig felt. Oppdater boardet eller kontroller oppsettet i Azure DevOps.");}
}
