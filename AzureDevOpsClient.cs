using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevOpsPulse;

public sealed partial class AzureDevOpsClient : IDisposable
{
    private readonly HttpClient http;
    private readonly string root;
    private readonly string identityRoot;
    private readonly string organization;
    public AzureDevOpsClient(string organization, string token, Uri? testEndpoint = null)
    {
        if (!Regex.IsMatch(organization, "^[A-Za-z0-9][A-Za-z0-9-]{0,49}$"))
            throw new ArgumentException("Organisasjonen må være navnet fra dev.azure.com, for eksempel helsedir.");
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Legg inn et Personal Access Token.");
        if (testEndpoint != null && (!testEndpoint.IsLoopback || testEndpoint.Scheme != "http"))
            throw new ArgumentException("Testendepunkt må være lokal HTTP.");
        this.organization = organization;
        root = testEndpoint?.AbsoluteUri.TrimEnd('/') ?? $"https://dev.azure.com/{organization}";
        identityRoot = testEndpoint?.AbsoluteUri.TrimEnd('/') ?? $"https://vssps.dev.azure.com/{organization}";
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + token.Trim())));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DevOpsPulse/1.0");
    }
    public async Task<DashboardData> LoadAsync(string email, CancellationToken ct)
    {
        var warnings = new List<string>();
        var identity = await RequestAsync(identityRoot + "/_apis/identities?searchFilter=General&filterValue=" + E(email) + "&queryMembership=None&api-version=7.1", null, ct);
        var candidates = Values(identity.Json).Where(x => !Bool(x, "isContainer") && (!x.TryGetProperty("isActive", out var active) || active.ValueKind != JsonValueKind.False)).ToArray();
        var matches = candidates.Where(x => IdentityMatches(x, email)).ToArray();
        // Never silently select a fuzzy identity: a reviewer ID must belong to this email.
        if (matches.Length != 1) throw new InvalidOperationException($"Kunne ikke finne én entydig DevOps-identitet for {email}. Kontroller e-postadressen og Identity (Read)-tilgangen.");
        var myId = Str(matches[0], "id");
        if (!Guid.TryParse(myId, out _)) throw new InvalidOperationException("DevOps returnerte en ugyldig identitets-ID.");
        var projects = new List<(string Id, string Name)>();
        string continuation = "";
        var seenPages = new HashSet<string>();
        do
        {
            var page = await RequestAsync(root + "/_apis/projects?api-version=7.1&$top=100&continuationToken=" + E(continuation), null, ct);
            projects.AddRange(Values(page.Json).Select(p => (Str(p, "id"), Str(p, "name"))));
            continuation = page.Continuation;
            if (continuation.Length > 0 && !seenPages.Add(continuation)) throw new InvalidOperationException("DevOps gjentok samme prosjektside. Prøv å oppdatere igjen.");
        } while (continuation.Length > 0);

        var escapedEmail = email.Replace("'", "''");
        var wiql = new { query = $"SELECT [System.Id] FROM WorkItems WHERE [System.AssignedTo] = '{escapedEmail}' AND [System.WorkItemType] IN ('Task', 'Feature', 'Bug', 'Product Backlog Item') ORDER BY [System.ChangedDate] DESC" };
        var ids = await RequestAsync(root + "/_apis/wit/wiql?api-version=7.1", wiql, ct);
        var work = new List<WorkItem>();
        if (!ids.Json.TryGetProperty("workItems", out var references) || references.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("DevOps returnerte et uventet svar på oppgavesøket.");
        foreach (var chunk in references.EnumerateArray().Select(i => Int(i, "id")).Chunk(200))
        {
            // No fields list: optional process-specific fields must not break the whole batch.
            var batch = await RequestAsync(root + "/_apis/wit/workitemsbatch?api-version=7.1", new { ids = chunk, errorPolicy = "Omit" }, ct);
            var items = Values(batch.Json);
            if (items.Length != chunk.Length) warnings.Add($"{chunk.Length - items.Length} oppgaver kunne ikke leses i en batch (slettet eller manglende tilgang).");
            foreach (var item in items)
            {
                work.Add(ParseWorkItem(item));
            }
        }
        var pulls = new List<PullRequest>(); var builds = new List<BuildInfo>();
        // Bounded concurrency: a large organization must not flood DevOps.
        using var gate = new SemaphoreSlim(4);
        var jobs = projects.Select(async p =>
        {
            await gate.WaitAsync(ct);
            var localPulls = new List<PullRequest>(); var localBuilds = new List<BuildInfo>(); var localWarnings = new List<string>();
            try
            {
                foreach (var role in new[] { "creatorId", "reviewerId" })
                foreach (var status in new[] { "active", "completed", "abandoned" })
                {
                    try
                    {
                        var since = status == "active" ? "" : "&searchCriteria.queryTimeRangeType=closed&searchCriteria.minTime=" + E(DateTimeOffset.UtcNow.AddDays(-30).ToString("O"));
                        for (var skip = 0; ; skip += 100)
                        {
                            var page = await RequestAsync($"{root}/{E(p.Id)}/_apis/git/pullrequests?api-version=7.1&searchCriteria.{role}={E(myId)}&searchCriteria.status={status}&$top=100&$skip={skip}{since}", null, ct);
                            var values = Values(page.Json);
                            localPulls.AddRange(values.Select(x => ParsePull(x, p.Name, myId)));
                            if (values.Length < 100) break;
                        }
                    }
                    catch (Exception ex) when (Recoverable(ex, ct))
                    { localWarnings.Add($"{p.Name}: PR ({(role == "creatorId" ? "mine" : "review")}, {status}) kunne ikke hentes. {Friendly(ex)}"); }
                }
                try
                {
                    var allBuilds = new List<JsonElement>(); var buildToken = ""; var seenBuildPages = new HashSet<string>();
                    do
                    {
                        var page = await RequestAsync($"{root}/{E(p.Id)}/_apis/build/builds?api-version=7.1&$top=100&queryOrder=queueTimeDescending&minTime={E(DateTimeOffset.UtcNow.AddDays(-7).ToString("O"))}&continuationToken={E(buildToken)}", null, ct);
                        allBuilds.AddRange(Values(page.Json)); buildToken = page.Continuation;
                        if (buildToken.Length > 0 && !seenBuildPages.Add(buildToken)) throw new InvalidOperationException("Gjentatt side ved henting av bygg.");
                    } while (buildToken.Length > 0);
                    foreach (var b in allBuilds.GroupBy(b => Int(Obj(b, "definition"), "id")).Select(g => g.OrderByDescending(b => Date(b, "queueTime")).First()))
                    {
                        var id = Int(b, "id");
                        localBuilds.Add(new BuildInfo(id, Str(Obj(b, "definition"), "name"), Str(b, "buildNumber"), p.Name, Str(b, "sourceBranch"), Str(b, "status"), Str(b, "result"), Date(b, "queueTime"), $"https://dev.azure.com/{organization}/{E(p.Name)}/_build/results?buildId={id}"));
                    }
                }
                catch (Exception ex) when (Recoverable(ex, ct)) { localWarnings.Add($"{p.Name}: Bygg kunne ikke hentes. {Friendly(ex)}"); }
                lock (pulls) { pulls.AddRange(localPulls); builds.AddRange(localBuilds); warnings.AddRange(localWarnings); }
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(jobs);
        return new DashboardData(work, pulls.DistinctBy(p => (p.Project, p.Repository, p.Id)).OrderByDescending(p => p.Created).ToArray(), builds.OrderByDescending(b => b.Queued).ToArray(), projects.Select(p => p.Name).Order().ToArray(), warnings, DateTimeOffset.Now, false) { UserId = myId, ProjectIds = projects.DistinctBy(p=>p.Name).ToDictionary(p=>p.Name,p=>p.Id,StringComparer.OrdinalIgnoreCase) };
    }

    private WorkItem ParseWorkItem(JsonElement item)
    {
        var f = item.GetProperty("fields"); var id = Int(item, "id"); var project = Str(f, "System.TeamProject");
        return new WorkItem(id, Str(f, "System.Title"), Str(f, "System.WorkItemType"), Str(f, "System.State"), project,
            Str(f, "System.IterationPath"), Int(f, "Microsoft.VSTS.Common.Priority"), Str(f, "Microsoft.VSTS.Common.Severity"),
            Date(f, "System.ChangedDate"), $"https://dev.azure.com/{organization}/{E(project)}/_workitems/edit/{id}")
        {
            Description = WorkItemText.FromHtml(Str(f, "System.Description")), ReproSteps = WorkItemText.FromHtml(Str(f, "Microsoft.VSTS.TCM.ReproSteps")),
            AcceptanceCriteria = WorkItemText.FromHtml(Str(f, "Microsoft.VSTS.Common.AcceptanceCriteria")),
            AssignedTo = Str(Obj(f, "System.AssignedTo"), "displayName"), AssignedToId = Str(Obj(f,"System.AssignedTo"),"id"), AssignedEmail = Str(Obj(f,"System.AssignedTo"),"uniqueName"), AreaPath = Str(f, "System.AreaPath"), Tags = Str(f, "System.Tags")
        };
    }
    public async Task<IReadOnlyList<ActivityEvent>> ScanMentionsAsync(string userId, string email, DateTimeOffset since, DateTimeOffset until, CancellationToken ct)
    {
        if (!Guid.TryParse(userId, out _)) throw new ArgumentException("Mangler identitet for @omtaler.");
        var events = new List<ActivityEvent>(); var afterId = 0;
        while (true)
        {
            var query = $"SELECT [System.Id] FROM WorkItems WHERE [System.ChangedDate] >= '{since.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ}' AND [System.ChangedDate] <= '{until.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ}' AND [System.Id] > {afterId} ORDER BY [System.Id] ASC";
            var result = await RequestAsync(root + "/_apis/wit/wiql?api-version=7.1&$top=500&timePrecision=true", new { query }, ct);
            var ids = result.Json.GetProperty("workItems").EnumerateArray().Select(i => Int(i, "id")).ToArray();
            if (ids.Length == 0) break;
            foreach (var chunk in ids.Chunk(200))
            {
                var current = Values((await RequestAsync(root + "/_apis/wit/workitemsbatch?api-version=7.1", new { ids = chunk, errorPolicy = "Omit", asOf = until.ToString("O") }, ct)).Json);
                if (current.Length != chunk.Length) throw new InvalidOperationException("Noen endrede oppgaver kunne ikke leses. Varsler er ikke fullstendig sjekket.");
                var before = Values((await RequestAsync(root + "/_apis/wit/workitemsbatch?api-version=7.1", new { ids = chunk, errorPolicy = "Omit", asOf = since.ToString("O") }, ct)).Json).ToDictionary(i => Int(i, "id"));
                foreach (var raw in current)
                {
                    var item = ParseWorkItem(raw); var fields = raw.GetProperty("fields");
                    var old = before.TryGetValue(item.Id, out var previous) ? Obj(previous, "fields") : default;
                    foreach (var name in new[] { "System.Title", "System.Description", "Microsoft.VSTS.TCM.ReproSteps", "Microsoft.VSTS.Common.AcceptanceCriteria" })
                        if (MentionText.ContainsIdentity(Str(fields, name), userId, email) && !MentionText.ContainsIdentity(Str(old, name), userId, email))
                            events.Add(new ActivityEvent($"mention:{item.Id}:{name}:{item.Changed:O}", "Du er tagget i en oppgave", $"#{item.Id} · {item.Title} · {item.Project}", item, item.Changed));
                    if (Int(fields, "System.CommentCount") <= 0) continue;
                    foreach (var comment in await LoadCommentsAsync(item.Project, item.Id, ct))
                        if ((comment.MentionedIds.Contains(userId, StringComparer.OrdinalIgnoreCase) || MentionText.ContainsIdentity(comment.Text, userId, email)) && (comment.Created >= since || comment.Modified >= since) && comment.Created <= until)
                            events.Add(new ActivityEvent($"mention:{item.Id}:comment:{comment.Id}", $"{comment.Author} har tagget deg", $"#{item.Id} · {item.Title}\n{comment.Text}", item, comment.Modified > comment.Created ? comment.Modified : comment.Created));
                }
            }
            var next = ids.Max(); if (next <= afterId) throw new InvalidOperationException("Gjentatt side ved varslingssjekk.");
            afterId = next; if (ids.Length < 500) break;
        }
        return events;
    }

    public async Task<IReadOnlyList<WorkItemComment>> LoadCommentsAsync(string project, int workItemId, CancellationToken ct)
    {
        if (workItemId <= 0 || string.IsNullOrWhiteSpace(project)) throw new ArgumentException("Ugyldig oppgave eller prosjekt.");
        var comments = new List<WorkItemComment>(); var continuation = ""; var seen = new HashSet<string>();
        do
        {
            var page = await RequestAsync($"{root}/{E(project)}/_apis/wit/workItems/{workItemId}/comments?api-version=7.1-preview.4&$top=100&$expand=renderedText&includeDeleted=false&order=asc&continuationToken={E(continuation)}", null, ct);
            if (!page.Json.TryGetProperty("comments", out var values) || values.ValueKind != JsonValueKind.Array)
                throw new JsonException("Missing comments array");
            foreach (var comment in values.EnumerateArray().Where(c => !Bool(c, "isDeleted")))
            {
                var rendered = Str(comment, "renderedText");
                var text = rendered.Length > 0 ? WorkItemText.FromHtml(rendered) : Str(comment, "format") == "markdown" ? Str(comment, "text") : WorkItemText.FromHtml(Str(comment, "text"));
                var author = Str(Obj(comment, "createdBy"), "displayName");
                comments.Add(new WorkItemComment(Int(comment, "id") is var id && id > 0 ? id : Int(comment, "commentId"),
                    author.Length > 0 ? author : "Ukjent forfatter", Date(comment, "createdDate"), Date(comment, "modifiedDate"), text)
                {
                    MentionedIds = comment.TryGetProperty("mentions", out var mentions) && mentions.ValueKind == JsonValueKind.Array
                        ? mentions.EnumerateArray().Select(m => Str(m, "targetId")).Where(s => s.Length > 0).ToArray() : []
                });
            }
            continuation = Str(page.Json, "continuationToken");
            if (continuation.Length > 0 && !seen.Add(continuation)) throw new InvalidOperationException("Gjentatt side ved henting av diskusjon.");
        } while (continuation.Length > 0);
        return comments.DistinctBy(c => c.Id).OrderBy(c => c.Created).ThenBy(c => c.Id).ToArray();
    }
    private PullRequest ParsePull(JsonElement x, string project, string myId)
    {
        var repo = Obj(x, "repository"); var creator = Obj(x, "createdBy"); var id = Int(x, "pullRequestId");
        var reviewers = x.TryGetProperty("reviewers", out var rs) && rs.ValueKind == JsonValueKind.Array
            ? rs.EnumerateArray().Select(r => new Reviewer(Str(r, "id"), Str(r, "displayName"), Int(r, "vote"), Bool(r, "isRequired"), Bool(r, "isContainer"))).ToArray() : [];
        return new PullRequest(id, Str(x, "title"), project, Str(repo, "name"), Str(creator, "id"), Str(creator, "displayName"), Str(x, "status"), Bool(x, "isDraft"), Str(x, "mergeStatus"), Date(x, "creationDate"), Str(x, "sourceRefName"), Str(x, "targetRefName"), reviewers, myId,
            $"https://dev.azure.com/{organization}/{E(project)}/_git/{E(Str(repo, "id"))}/pullrequest/{id}");
    }
    private async Task<(JsonElement Json, string Continuation)> RequestAsync(string url, object? body, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, url);
            if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request, ct);
            if ((response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable) && attempt < 2)
            {
                var seconds = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 2 * (attempt + 1);
                if (seconds > 30) throw new InvalidOperationException("DevOps ber om en lengre pause. Vent litt og oppdater igjen.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 30)), ct); continue;
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidOperationException("Tokenet er ugyldig eller utløpt (401). Åpne Tilkobling og legg inn et nytt token.");
            if (response.StatusCode == HttpStatusCode.Forbidden) throw new InvalidOperationException("Manglende lesetilgang (403). Kontroller tokenets scopes og prosjekttilgang.");
            if ((int)response.StatusCode is >= 300 and < 400) throw new InvalidOperationException("DevOps krever innlogging. Kontroller tokenet under Tilkobling.");
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"DevOps svarte HTTP {(int)response.StatusCode}. Kontroller tilgangen og prøv igjen.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var continuation = response.Headers.TryGetValues("x-ms-continuationtoken", out var tokens) ? tokens.FirstOrDefault() ?? "" : "";
            return (document.RootElement.Clone(), continuation);
        }
    }
    private static bool Recoverable(Exception ex, CancellationToken ct) => !ct.IsCancellationRequested && ex is InvalidOperationException or HttpRequestException or JsonException or TaskCanceledException;
    public static string Friendly(Exception ex) => ex switch { HttpRequestException => "Kan ikke nå Azure DevOps. Kontroller nettverk/VPN.", TaskCanceledException or OperationCanceledException => "Hentingen tok for lang tid eller ble avbrutt.", JsonException => "Uventet svar fra DevOps. Kontroller innlogging og nettverk.", _ => ex.Message };
    private static bool IdentityMatches(JsonElement x, string email)
    {
        var properties = Obj(x, "properties");
        foreach (var name in new[] { "Mail", "Account", "DirectoryAlias", "Email" })
        { if (string.Equals(Str(Obj(properties, name), "$value"), email, StringComparison.OrdinalIgnoreCase)) return true; }
        return Str(x, "descriptor").EndsWith("\\" + email, StringComparison.OrdinalIgnoreCase);
    }
    private static string E(string value) => Uri.EscapeDataString(value);
    private static JsonElement[] Values(JsonElement x) => x.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToArray() : throw new JsonException("Missing value array");
    private static JsonElement Obj(JsonElement x, string key) => x.ValueKind == JsonValueKind.Object && x.TryGetProperty(key, out var v) ? v : default;
    private static string Str(JsonElement x, string key) => Obj(x, key) is var v && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement x, string key) => Obj(x, key) is var v && v.TryGetInt32Safe(out var n) ? n : 0;
    private static bool Bool(JsonElement x, string key) => Obj(x, key).ValueKind == JsonValueKind.True;
    private static DateTimeOffset Date(JsonElement x, string key) => DateTimeOffset.TryParse(Str(x, key), out var d) ? d : DateTimeOffset.MinValue;
    public void Dispose() => http.Dispose();
}
internal static class JsonExtensions
{
    public static bool TryGetInt32Safe(this JsonElement value, out int result) { result = 0; return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result); }
}
