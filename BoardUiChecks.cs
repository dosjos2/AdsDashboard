using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace DevOpsPulse;
public partial class MainWindow
{
    private async Task VerifyBoardUiAsync()
    {
        // Explicit test mode: every read and write goes to the synthetic local simulation.
        var endpoint=new Uri("http://127.0.0.1:4010/azure/");
        using var control=new HttpClient{BaseAddress=endpoint};
        async Task Mode(string mode){using var r=await control.PostAsync("__control?mode="+mode,null);r.EnsureSuccessStatusCode();}
        async Task<int> Attempts(){using var doc=JsonDocument.Parse(await control.GetStringAsync("__control"));return doc.RootElement.GetProperty("boardPatchAttempts").GetInt32();}
        var results=new List<string>();
        void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);results.Add("PASS: "+label);}
        await Mode("board");
        var data=new DashboardData([],[],[],["Alpha"],[],DateTimeOffset.Now,false){UserId="11111111-1111-1111-1111-111111111111",ProjectIds=new Dictionary<string,string>{{"Alpha","project-a"}}};
        vm.SetData(data);vm.Section="Board";
        await WorkBoard.ConfigureAsync(data,"helsedir","jan.ole.skotterud@helsedir.no",()=>new AzureDevOpsClient("helsedir","test-token",endpoint),_=>{},_=>{});
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
        Check(WorkBoard.Model.Snapshot?.Cards.Count==3,"Live board UI loads configured team scope through HTTP");
        var snapshot=WorkBoard.Model.Snapshot!;var card=snapshot.Cards.Single(c=>c.Item.Id==501);var target=snapshot.Lanes.First(l=>l.ColumnName=="In progress"&&!l.Done);
        await Mode("boardForbidden");await WorkBoard.MoveAsync(card,target);
        Check(WorkBoard.Model.Snapshot!.Cards.Single(c=>c.Item.Id==501).Column=="New" && WorkBoard.BoardStatus.Text.Contains("Read & write"),"Rejected move leaves card in original column and exposes permission guidance");
        await WorkBoard.MoveAsync(card,target);
        Check(await Attempts()==1 && WorkBoard.BoardStatus.Text.Contains("Oppdater boardet"),"UI prevents a second mutation until the board is reloaded");
        await Mode("board");await WorkBoard.RefreshAsync();
        card=WorkBoard.Model.Snapshot!.Cards.Single(c=>c.Item.Id==501);
        await Mode("boardLost");await WorkBoard.MoveAsync(card,target);
        Check(WorkBoard.Model.Snapshot!.Cards.Single(c=>c.Item.Id==501).Column=="New" && WorkBoard.BoardStatus.Text.Contains("ukjent"),"Uncertain move does not present an unconfirmed local success");
        await WorkBoard.RefreshAsync();
        Check(WorkBoard.Model.Snapshot!.Cards.Single(c=>c.Item.Id==501).Column=="In progress" && await Attempts()==1,"Reload discovers applied write without sending another PATCH");
        card=WorkBoard.Model.Snapshot!.Cards.Single(c=>c.Item.Id==501);
        var review=WorkBoard.Model.Snapshot.Lanes.Single(l=>l.ColumnName=="Review");await Mode("normal");
        await WorkBoard.MoveAsync(card,review);
        Check(WorkBoard.Model.Snapshot!.Cards.Single(c=>c.Item.Id==501).Column=="Review" && WorkBoard.BoardStatus.Text.Contains("lagret i Azure DevOps"),"Confirmed move updates visible board after server acknowledgment");
        await Mode("board");await WorkBoard.RefreshAsync();card=WorkBoard.Model.Snapshot!.Cards.Single(c=>c.Item.Id==501);
        await Mode("boardConflict");await WorkBoard.MoveAsync(card,target);await WorkBoard.RefreshAsync();
        Check(WorkBoard.Model.Snapshot!.Cards.Single(c=>c.Item.Id==501).Item.Title=="Changed externally","Concurrent editor's data survives a rejected UI move and reload");
        card=WorkBoard.Model.Snapshot!.Cards.Single(c=>c.Item.Id==501);await Mode("normal");
        var pendingMove=WorkBoard.MoveAsync(card,target);WorkBoard.Stop();await pendingMove;
        Check(!WorkBoard.IsWorking,"Closing during a pending move completes cleanly without an unhandled cancellation");
        WorkBoard.Clear();Check(WorkBoard.Model.Snapshot==null && WorkBoard.BoardChoice.Items.Count==0,"Clearing account context removes stale board cards and choices");
        await Mode("board");await Mode("normal");
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"board-ui-checks.txt"),results);
    }
}
