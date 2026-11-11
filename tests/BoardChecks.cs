using DevOpsPulse;
using System.Net.Http;
using System.Text.Json;

internal static class BoardChecks
{
    public static async Task Run(AzureDevOpsClient client,HttpClient control,Action<bool,string> check)
    {
        async Task Mode(string name){using var r=await control.PostAsync("__control?mode="+name,null);r.EnsureSuccessStatusCode();}
        async Task<int> Attempts(){using var doc=JsonDocument.Parse(await control.GetStringAsync("__control"));return doc.RootElement.GetProperty("boardPatchAttempts").GetInt32();}
        await Mode("board");
        check((await client.ListTeamsAsync("project-a",CancellationToken.None)).Count==101,"Board team discovery follows skip/top pagination");
        check((await client.ListBoardsAsync("Alpha","team-main",CancellationToken.None)).Single().Id=="board-a","Actual team boards are discovered");
        async Task<BoardSnapshot> Load()=>await client.LoadBoardAsync("Alpha","team-main","board-a",CancellationToken.None);
        var snapshot=await Load();
        check(snapshot.Cards.Count==3 && snapshot.Lanes.Count==5,"Configured board loads team-scoped items and expands split columns");
        check(snapshot.ColumnField=="WEF_Team_Kanban.Column" && snapshot.DoneField=="WEF_Team_Kanban.Column.Done","Board uses server-provided field references");
        check(snapshot.LaneFor(snapshot.Cards.Single(c=>c.Item.Id==502))?.Done==true,"Split done half uses the board-specific boolean");
        check(snapshot.LaneFor(snapshot.Cards.Single(c=>c.Item.Id==503))==null,"Ambiguous state is not silently assigned to a guessed column");
        var vm=new BoardViewModel{Email="jan.ole.skotterud@helsedir.no",UserId="11111111-1111-1111-1111-111111111111"};vm.Set(snapshot);vm.OnlyMine=true;
        check(vm.Columns.Sum(c=>c.Cards.Count)==2 && vm.Columns.Any(c=>c.Lane.Key=="unmapped"),"Board personal filter retains unplaced own work without hiding it");
        vm.Search="503";check(vm.Columns.Sum(c=>c.Cards.Count)==1,"Board search combines with personal filter");
        var card=snapshot.Cards.Single(c=>c.Item.Id==501);var active=snapshot.Lanes.Single(l=>l.ColumnName=="In progress"&&!l.Done);
        async Task<BoardCard> Move(BoardLane target){var result=await client.MoveBoardCardAsync(snapshot,card,target,CancellationToken.None);snapshot=snapshot with{Cards=snapshot.Cards.Select(c=>c.Item.Id==result.Item.Id?result:c).ToArray()};card=result;return result;}
        var moved=await Move(active);
        check(moved.Revision==2 && moved.Item.State=="Committed" && moved.Column=="In progress" && moved.Row=="Expedite","Move writes mapped state and configured column with revision check, preserving swimlane");
        moved=await Move(snapshot.Lanes.Single(l=>l.ColumnName=="In progress"&&l.Done));
        check(moved.Done && moved.Item.State=="Committed","Move within split column changes done flag without inventing a workflow state");
        moved=await Move(snapshot.Lanes.Single(l=>l.ColumnName=="Review"));
        check(!moved.Done && moved.Item.State=="Committed" && moved.Column=="Review","Move across columns sharing a state updates column and clears done flag");
        moved=await Move(snapshot.Lanes.Single(l=>l.ColumnName=="Done"));
        check(moved.Item.State=="Done" && (await Load()).Cards.Single(c=>c.Item.Id==501).Revision==moved.Revision,"Completed move is observed by a subsequent real HTTP board read");
        var attempts=await Attempts();await client.MoveBoardCardAsync(snapshot,card,snapshot.Lanes.Single(l=>l.ColumnName=="Done"),CancellationToken.None);
        check(await Attempts()==attempts,"Dropping a card in its current column performs no write");
        foreach(var mode in new[]{"boardForbidden","boardRules","boardRateLimit","boardConflict","boardLost","boardUnconfirmed"})
        {
            await Mode("board");snapshot=await Load();card=snapshot.Cards.Single(c=>c.Item.Id==501);await Mode(mode);
            BoardMoveException? failure=null;try{await client.MoveBoardCardAsync(snapshot,card,active,CancellationToken.None);}catch(BoardMoveException ex){failure=ex;}
            check(failure!=null && await Attempts()==1,$"{mode}: failed move is surfaced with exactly one PATCH attempt");
            var actual=(await Load()).Cards.Single(c=>c.Item.Id==501);
            if(mode is "boardLost" or "boardUnconfirmed")check(failure!.Uncertain && actual.Column=="In progress" && actual.Revision==2,mode+": ambiguous response does not imply rollback; reload reveals applied write");
            else check(actual.Column=="New" && (mode!="boardConflict"||actual.Item.Title=="Changed externally"),mode+": rejected move does not overwrite server data");
        }
        await Mode("board");await Mode("boardMissingField");snapshot=await Load();card=snapshot.Cards[0];var blocked=false;
        try{await client.MoveBoardCardAsync(snapshot,card,active,CancellationToken.None);}catch(ArgumentException){blocked=true;}
        check(blocked && await Attempts()==0,"Missing split-column field blocks mutation before HTTP");
        foreach(var mode in new[]{"boardNoAreas","boardInvalid"})
        {await Mode(mode);var failed=false;try{await Load();}catch(InvalidOperationException){failed=true;}check(failed,mode+": invalid scope/configuration fails instead of broadening board membership");}
        await Mode("boardMany");check((await Load()).Cards.Count==501,"Large board follows WIQL ID pages and 200-item batches");
        await Mode("board");await Mode("normal");
    }
}
