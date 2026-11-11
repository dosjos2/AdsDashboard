namespace DevOpsPulse;

public static class Presentation
{
    public static bool Done(string state) => new[] { "closed", "done", "removed", "completed" }.Contains(state.ToLowerInvariant());
    public static string Age(DateTimeOffset date)
    {
        var age = DateTimeOffset.Now - date;
        return age.TotalMinutes < 60 ? $"{Math.Max(0, (int)age.TotalMinutes)} min" : age.TotalHours < 24 ? $"{(int)age.TotalHours} t" : $"{(int)age.TotalDays} d";
    }
}
public sealed record WorkItem(int Id, string Title, string Type, string State, string Project, string Iteration, int Priority, string Severity, DateTimeOffset Changed, string Url)
{
    public string Description { get; init; } = "";
    public string ReproSteps { get; init; } = "";
    public string AcceptanceCriteria { get; init; } = "";
    public string AssignedTo { get; init; } = "";
    public string AssignedToId { get; init; } = "";
    public string AssignedEmail { get; init; } = "";
    public string AreaPath { get; init; } = "";
    public string Tags { get; init; } = "";
    public bool IsDone => Presentation.Done(State);
    public bool IsStale => !IsDone && Changed < DateTimeOffset.Now.AddDays(-7);
    public bool IsCritical => !IsDone && Type == "Bug" && (Priority == 1 || Severity.StartsWith("1"));
    public string Color => Type switch { "Bug" => "#DB6178", "Feature" => "#9B72D7", "Product Backlog Item" => "#28A28B", _ => "#4B91D2" };
    public string Symbol => Type switch { "Bug" => "●", "Feature" => "◆", "Product Backlog Item" => "▤", _ => "▣" };
    public string TypeLabel => Type;
    public string PriorityLabel => Priority > 0 ? $"P{Priority}" : "–";
    public string ChangedLabel => $"{Presentation.Age(Changed)} siden";
    public string Detail => $"#{Id}  ·  {Project}  ·  {Iteration.Split('\\').LastOrDefault()}";
    public string StateColor => IsDone ? "#188778" : "#5266D5";
    public string StateBackground => IsDone ? "#E8F6F0" : "#EFF1FD";
}
public sealed record Reviewer(string Id, string Name, int Vote, bool Required, bool IsContainer = false);
public sealed record WorkItemComment(int Id, string Author, DateTimeOffset Created, DateTimeOffset Modified, string Text)
{
    public string Metadata => $"{(Created == DateTimeOffset.MinValue ? "Ukjent tidspunkt" : Created.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))}{(Modified > Created.AddSeconds(1) ? $" · Redigert {Modified.ToLocalTime():dd.MM.yyyy HH:mm}" : "")}";
    public IReadOnlyList<string> MentionedIds { get; init; } = [];
}
public sealed record PullRequest(int Id, string Title, string Project, string Repository, string CreatorId, string Creator, string Status, bool Draft, string MergeStatus, DateTimeOffset Created, string Source, string Target, IReadOnlyList<Reviewer> Reviewers, string MyId, string Url)
{
    public bool IsMine => string.Equals(CreatorId, MyId, StringComparison.OrdinalIgnoreCase);
    public bool IsReview => Reviewers.Any(r => string.Equals(r.Id, MyId, StringComparison.OrdinalIgnoreCase));
    public bool Active => Status.Equals("active", StringComparison.OrdinalIgnoreCase);
    public int? MyVote => Reviewers.FirstOrDefault(r => string.Equals(r.Id, MyId, StringComparison.OrdinalIgnoreCase))?.Vote;
    public bool NeedsReview => Active && !Draft && IsReview && MyVote is 0;
    public bool Conflict => Active && MergeStatus.Equals("conflicts", StringComparison.OrdinalIgnoreCase);
    public string StateLabel => !Active ? (Status == "completed" ? "Fullført" : "Avbrutt") : Draft ? "Utkast" : Conflict ? "Konflikt" : "Aktiv";
    public string StateColor => Conflict ? "#BD4D61" : !Active ? "#168773" : "#5266D5";
    public string StateBackground => Conflict ? "#FCEEF1" : !Active ? "#E8F6F0" : "#EFF1FD";
    public string Role => IsMine && IsReview ? "Opprettet av meg · Reviewer" : IsMine ? "Opprettet av meg" : "Jeg er reviewer";
    public string Detail => $"!{Id}  ·  {Project} / {Repository}  ·  {Creator}";
    public string Branch => $"{Source.Replace("refs/heads/", "")}  →  {Target.Replace("refs/heads/", "")}";
    public string AgeLabel => $"Opprettet for {Presentation.Age(Created)} siden";
    public string ReviewLabel
    {
        get
        {
            var individual = Reviewers.Where(r => !r.IsContainer).ToArray();
            var approved = individual.Count(r => r.Vote > 0);
            var changes = individual.Count(r => r.Vote < 0);
            var own = IsReview ? $" · Min stemme: {VoteText(MyVote ?? 0)}" : "";
            return $"{approved}/{individual.Length} positive stemmer{(changes > 0 ? $" · {changes} ber om endringer/avviser" : "")}{own}";
        }
    }
    public string ReviewTooltip => string.Join("\n", Reviewers.Select(r => $"{r.Name}{(r.Required ? " (påkrevd)" : "")}: {VoteText(r.Vote)}"));
    public static string VoteText(int vote) => vote switch { 10 => "Godkjent", 5 => "Godkjent med innspill", -5 => "Venter på forfatter", -10 => "Avvist", _ => "Ikke svart" };
}
public sealed record BuildInfo(int Id, string Name, string Number, string Project, string Branch, string Status, string Result, DateTimeOffset Queued, string Url)
{
    public bool Failed => Result == "failed";
    public string Label => Status switch { "inProgress" => "Kjører", "notStarted" => "I kø", "cancelling" => "Avbryter", _ => Result switch { "succeeded" => "Vellykket", "failed" => "Feilet", "partiallySucceeded" => "Delvis vellykket", "canceled" => "Avbrutt", _ => Status } };
    public string Color => Failed ? "#DB6178" : Status == "inProgress" ? "#5266E9" : Result == "succeeded" ? "#28A28B" : "#C29336";
    public string Detail => $"{Project}  ·  {Branch.Replace("refs/heads/", "")}  ·  #{Number}";
    public string AgeLabel => $"Kølagt for {Presentation.Age(Queued)} siden";
}
public sealed record Insight(string Symbol, string Title, string Detail, string Color, string Url = "");
public sealed record DashboardData(IReadOnlyList<WorkItem> WorkItems, IReadOnlyList<PullRequest> PullRequests, IReadOnlyList<BuildInfo> Builds, IReadOnlyList<string> Projects, IReadOnlyList<string> Warnings, DateTimeOffset LoadedAt, bool IsDemo)
{
    public string UserId { get; init; } = "";
    public IReadOnlyDictionary<string,string> ProjectIds { get; init; } = new Dictionary<string,string>();
}
public sealed record AppSettings(string Organization = "helsedir", string Email = "jan.ole.skotterud@helsedir.no", bool RememberToken = false);
