using DevOpsPulse;
using System.Net.Http;

try
{
var checks = 0;
var skipped = 0;
void Check(bool ok, string description)
{
    if (!ok) throw new Exception("FAIL: " + description);
    checks++; Console.WriteLine("PASS: " + description);
}
if (args.Contains("--filters-only"))
{
    ProjectFilterChecks.Run(Check);
    Console.WriteLine($"\n{checks} project filter checks passed.");
    return;
}
const string email = "jan.ole.skotterud@helsedir.no";
var baseUri = new Uri(args.FirstOrDefault() ?? "http://127.0.0.1:4010/azure/");
using var control = new HttpClient { BaseAddress = baseUri };
async Task Mode(string mode) { using var r = await control.PostAsync("__control?mode=" + mode, null); r.EnsureSuccessStatusCode(); }
using var client = new AzureDevOpsClient("helsedir", "test-token", baseUri);
await Mode("normal");
await BoardChecks.Run(client,control,Check);
var discussion = await client.LoadCommentsAsync("Alpha", 1, CancellationToken.None);
Check(discussion.Count == 2 && discussion.Select(c => c.Id).SequenceEqual(new[]{10,12}), "Discussion pages are combined, duplicate and deleted comments excluded");
Check(discussion[0].Author == "Anna Demo" && discussion[0].Text == "@Jan & team" && discussion[1].Text == "Raw **markdown**", "Discussion preserves author, rendered HTML text and markdown fallback");
Check(discussion[0].MentionedIds.Contains("11111111-1111-1111-1111-111111111111"), "Comment mention metadata is retained for alerts");
Check((await client.LoadCommentsAsync("Alpha", 2, CancellationToken.None)).Count == 0, "Empty discussion is distinct from a load failure");
foreach (var id in new[]{3,4,5,6})
{
    var failed = false;
    try { await client.LoadCommentsAsync("Alpha", id, CancellationToken.None); }
    catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException) { failed = true; }
    Check(failed, $"Discussion failure {id} is reported rather than mistaken for an empty or complete discussion");
}
await Mode("activity");
var activity = await client.ScanMentionsAsync("11111111-1111-1111-1111-111111111111", email, DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow, CancellationToken.None);
Check(activity.Count == 2 && activity.All(a => a.Item.Id == 1), "Mention scan detects new rich-text mention and comment mention, ignoring existing mention on unrelated edit");
var feed = new ActivityFeed(); var example = DemoData.Create();
Check(feed.ObserveAssignments(example with { IsDemo = false }) == 0, "First assignment snapshot creates no historical alerts");
var addedItem = example.WorkItems[0] with { Id = 99999, Changed = DateTimeOffset.Now };
Check(feed.ObserveAssignments(example with { IsDemo = false, WorkItems = example.WorkItems.Concat([addedItem]).ToArray() }) == 1, "Newly assigned work creates one alert");
Check(feed.Add(activity) == 2 && feed.Add(activity) == 0 && feed.UnreadCount == 3, "Mention alerts are deduplicated and counted as unread");
feed.MarkRead(); Check(feed.UnreadCount == 0 && feed.Items.Count == 3, "Reading alerts clears badge and retains history");
feed.Reset(); Check(feed.Items.Count == 0 && feed.ObserveAssignments(example with {IsDemo=false}) == 0, "Account reset clears notifications and establishes a fresh baseline");
Check(MentionText.ContainsIdentity("<a data-vss-mention='version:2.0,11111111-1111-1111-1111-111111111111'>Jan</a>", "11111111-1111-1111-1111-111111111111", email) && !MentionText.ContainsIdentity("Write to " + email, "11111111-1111-1111-1111-111111111111", email), "Structured user tags match identity while ordinary email text is not a tag");
await Mode("normal");
var data = await client.LoadAsync(email, CancellationToken.None);
Check(!data.IsDemo, "HTTP data is never labeled demo");
Check(data.Projects.SequenceEqual(new[]{"Alpha", "Restricted"}), "Project continuation token is followed");
Check(data.WorkItems.Count == 200, "201 references fetched in batches <= 200, one omitted item reported");
var detailedWork = data.WorkItems.Single(w => w.Id == 1);
Check(detailedWork.Description == "First & second\nNext line\nFinal", "HTML descriptions preserve paragraphs and decode entities for native details window");
Check(detailedWork.AssignedTo == "Jan Demo" && detailedWork.AreaPath == "Alpha\\Team" && detailedWork.Tags == "UI; Test", "Work item detail metadata comes from actual batch response");
Check(detailedWork.AcceptanceCriteria == "• Works\n• Verified", "Acceptance criteria lists remain readable");
Check(data.WorkItems.Single(w => w.Id == 3).ReproSteps == "Open page\nSee error", "Bug reproduction steps are available independently of description");
Check(data.WorkItems.Single(w => w.Id == 2).Description == "" && data.WorkItems.Single(w => w.Id == 2).AssignedTo == "", "Missing optional detail fields do not break loading");
Check(WorkItemText.FromHtml("<script>alert('x')</script><style>body{}</style><p>Safe &lt;text&gt;</p><img src='https://invalid.example/x'>") == "Safe <text>", "HTML is converted to text without scripts, styles or external images");
Check(data.Warnings.Any(x => x.Contains("1 oppgaver")), "Omitted batch item produces visible warning");
Check(data.PullRequests.Count == 104, "PR pagination, 30-day history and duplicate roles merged");
Check(data.PullRequests.Single(p => p.Id == 1).IsMine && data.PullRequests.Single(p => p.Id == 1).IsReview, "PR belonging to both roles retains both roles");
Check(data.PullRequests.Single(p => p.Id == 1).Conflict, "Merge conflict detected");
Check(!data.PullRequests.Single(p => p.Id == 999).NeedsReview, "Draft PR does not count as waiting for review");
Check(data.Builds.Count == 2 && data.Builds.Single(b => b.Name == "Pipeline 1").Result == "succeeded", "Build pagination and latest-per-definition preserve latest success over older failure");
Check(data.Warnings.Count(w => w.Contains("Restricted:")) == 7, "Forbidden project preserves other data and warns on each unavailable section");
Check(data.WorkItems.Single(w => w.Id == 3).IsCritical, "Critical bug uses priority and severity");
Check(data.WorkItems.Single(w => w.Id == 1).IsStale && !data.WorkItems.Single(w => w.Id == 4).IsStale, "Stale warning applies only to unfinished work");
var vm = new DashboardViewModel(); vm.SetData(data); vm.Section = "Arbeidsoppgaver";
Check(data.WorkItems.Count(w => w.Type == "Product Backlog Item") == 2 && vm.PbiCount == 1, "Product Backlog Items are loaded and open PBIs counted separately");
Check(vm.OpenCount == vm.TaskCount + vm.FeatureCount + vm.BugCount + vm.PbiCount, "Overview distribution includes all four work item types");
vm.Type = "Product Backlog Item";
Check(vm.Types.Contains("Product Backlog Item") && vm.WorkItems.Count() == 1 && vm.WorkItems.Single().TypeLabel == "Product Backlog Item", "PBI filter and type label identify open PBIs correctly");
vm.IncludeClosed = true;
Check(vm.WorkItems.Count() == 2 && vm.WorkItems.Any(w => w.IsDone), "Completed PBI visibility follows completed work filter");
vm.Type = "Alle typer"; vm.IncludeClosed = false;
Check(vm.WorkItems.All(w => !w.IsDone), "Completed work hidden by default");
var open = vm.OpenCount; vm.Search = "no-such-title";
Check(vm.NoWork && vm.OpenCount == open, "Search filters list without changing project KPIs");
vm.Search = ""; vm.IncludeClosed = true; vm.Type = "Bug";
Check(vm.WorkItems.All(w => w.Type == "Bug") && vm.WorkItems.Any(w => w.IsDone), "Type and completed filters combine");
vm.Section = "Pull requests"; vm.PrRole = "Opprettet av meg"; vm.IncludeHistory = true;
Check(vm.PullRequests.Count() == 103 && vm.PullRequests.All(p => p.IsMine), "PR role and history filters combine");
vm.Project = "Restricted";
Check(vm.NoWork && vm.NoPr && vm.OpenCount == 0 && !vm.Builds.Any(), "Project filtering applies across lists and KPIs");
async Task ExpectFailure(string mode, string expected)
{
    await Mode(mode);
    try { await client.LoadAsync(email, CancellationToken.None); throw new Exception("Expected failure: " + mode); }
    catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
    { Check(AzureDevOpsClient.Friendly(ex).Contains(expected), mode + " yields useful error without fallback demo"); }
}
await ExpectFailure("auth", "401");
await ExpectFailure("ambiguous", "entydig");
await ExpectFailure("html", "Uventet svar");
await Mode("retry");
var retried = await client.LoadAsync(email, CancellationToken.None);
Check(retried.WorkItems.Count == 200, "429 Retry-After is honored and real load recovers");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await client.LoadAsync(email, cancelled.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { Check(true, "Cancellation stops refresh"); }
Check(PullRequest.VoteText(-5) == "Venter på forfatter" && PullRequest.VoteText(5) == "Godkjent med innspill", "Azure reviewer votes translated correctly");
Check(!Presentation.Done("Resolved") && Presentation.Done("Closed"), "Resolved work awaits closure and is not counted as finished");
vm.Project = "Alle prosjekter"; vm.Type = "Bug"; vm.IncludeClosed = true; vm.PrRole = "Opprettet av meg"; vm.IncludeHistory = true; vm.Section = "Oversikt";
Check(vm.WorkItems.All(w => !w.IsDone) && vm.WorkItems.Any(w => w.Type != "Bug") && vm.PullRequests.All(p => p.Active), "Overview remains open work and active PRs regardless of hidden detail filters");
var secretBytes = System.Text.Encoding.UTF8.GetBytes("synthetic-token-for-dpapi-roundtrip");
try
{
    var encrypted = SettingsStore.Protect(secretBytes, true);
    Check(!encrypted.SequenceEqual(secretBytes) && SettingsStore.Protect(encrypted, false).SequenceEqual(secretBytes), "Windows DPAPI protects and restores a synthetic token without saving credentials");
}
catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode is 2 or 5)
{
    skipped++;
    Console.WriteLine($"SKIP: DPAPI roundtrip cannot access the current Windows profile in this restricted process (Win32 {ex.NativeErrorCode}). Run outside the sandbox to verify credential persistence.");
}
await Mode("normal");
Console.WriteLine($"\n{checks} checks passed; {skipped} skipped. Local Postman simulation only. Live Helsedir access not tested.");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Environment.ExitCode = 1;
}
