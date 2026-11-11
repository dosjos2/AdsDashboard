using System.ComponentModel;

namespace DevOpsPulse;
public sealed class DashboardViewModel : INotifyPropertyChanged
{
    public const string FocusProjects = "HESA og Blåresept";
    public const string AllProjects = "Alle prosjekter";
    public event PropertyChangedEventHandler? PropertyChanged;
    public ActivityFeed Notifications { get; } = new();
    private DashboardData data = DemoData.Create();
    private string[] projects = [AllProjects, FocusProjects, "Blåresept", "HESA"];
    private string[] pipelineProjects = [FocusProjects, AllProjects, "Blåresept", "HESA"];
    private string pipelineProject = FocusProjects;
    private static readonly string[] TypeChoices = ["Alle typer", "Task", "Feature", "Bug", "Product Backlog Item"];
    private static readonly string[] RoleChoices = ["Alle roller", "Opprettet av meg", "Jeg er reviewer", "Venter på min review"];
    private string search = "", project = "Alle prosjekter", type = "Alle typer", prRole = "Alle roller";
    private bool includeClosed, includeHistory;
    private string section = "Oversikt";
    public DashboardData Data => data;
    public string Search { get => search; set { search = value; Refresh(); } }
    public string Project { get => project; set { if (value == null) return; project = value; Refresh(); } }
    public string PipelineProject { get => pipelineProject; set { if (value == null) return; pipelineProject = value; Refresh(); } }
    public string Type { get => type; set { if (value == null) return; type = value; Refresh(); } }
    public string PrRole { get => prRole; set { if (value == null) return; prRole = value; Refresh(); } }
    public bool IncludeClosed { get => includeClosed; set { includeClosed = value; Refresh(); } }
    public bool IncludeHistory { get => includeHistory; set { includeHistory = value; Refresh(); } }
    public string Section { get => section; set { section = value; Refresh(); } }
    public bool IsOverview => section == "Oversikt";
    public bool IsBoard => section == "Board";
    public bool ShowDashboard => !IsBoard;
    public bool ShowWork => IsOverview || section == "Arbeidsoppgaver";
    public bool ShowPr => IsOverview || section == "Pull requests";
    public bool ShowBuilds => IsOverview || section == "Pipelines";
    public bool IsWorkPage => section == "Arbeidsoppgaver";
    public bool IsPrPage => section == "Pull requests";
    public string Title => IsOverview ? "Din DevOps-hverdag, samlet." : section;
    public string Subtitle => IsOverview ? "Arbeidet ditt, kode som venter og det som trenger oppmerksomhet." : section switch { "Arbeidsoppgaver" => "Tasks, features, bugs og Product Backlog Items som er tildelt deg, på tvers av prosjekter.", "Pull requests" => "Dine PR-er og de du er angitt som reviewer på. Historikk: siste 30 dager.", _ => "Siste kjøring per pipeline blant bygg kølagt de siste 7 dagene. Velg prosjektene du vil følge under." };
    public string[] Projects => projects;
    public string[] PipelineProjects => pipelineProjects;
    public string PipelineCaption => $"Siste kjøring per pipeline · kølagt siste 7 dager · {pipelineProject}";
    public string[] Types => TypeChoices;
    public string[] PrRoles => RoleChoices;
    public IEnumerable<WorkItem> ScopedWork => data.WorkItems.Where(w => InProject(w.Project));
    public IEnumerable<PullRequest> ScopedPr => data.PullRequests.Where(p => InProject(p.Project));
    public IEnumerable<BuildInfo> ScopedBuilds => data.Builds.Where(b => InProject(b.Project) && InSelection(b.Project, pipelineProject));
    public IEnumerable<WorkItem> WorkItems => ScopedWork.Where(w => ((!IsOverview && includeClosed) || !w.IsDone) && (IsOverview || type == "Alle typer" || w.Type == type) && Matches($"{w.Id} {w.Title} {w.Type} {w.State} {w.Project} {w.Iteration}"))
        .OrderBy(w => w.IsDone).ThenByDescending(w => w.IsCritical).ThenBy(w => w.Priority == 0 ? 9 : w.Priority).ThenByDescending(w => w.Changed).Take(IsOverview ? 5 : int.MaxValue);
    public IEnumerable<PullRequest> PullRequests => ScopedPr.Where(p => ((!IsOverview && includeHistory) || p.Active) && (IsOverview || (prRole switch { "Opprettet av meg" => p.IsMine, "Jeg er reviewer" => p.IsReview, "Venter på min review" => p.NeedsReview, _ => true })) && Matches($"{p.Id} {p.Title} {p.Project} {p.Repository} {p.Creator}"))
        .OrderByDescending(p => p.Active).ThenByDescending(p => p.NeedsReview).ThenByDescending(p => p.Created).Take(IsOverview ? 4 : int.MaxValue);
    public IEnumerable<BuildInfo> Builds => ScopedBuilds.Where(b => Matches($"{b.Name} {b.Project} {b.Branch} {b.Label}")).OrderByDescending(b => b.Failed).ThenByDescending(b => b.Queued).Take(IsOverview ? 3 : int.MaxValue);
    public bool NoWork => !WorkItems.Any(); public bool NoPr => !PullRequests.Any(); public bool NoBuilds => !Builds.Any();
    public int OpenCount => ScopedWork.Count(w => !w.IsDone);
    public int BugCount => ScopedWork.Count(w => !w.IsDone && w.Type == "Bug");
    public int MineCount => ScopedPr.Count(p => p.Active && p.IsMine);
    public int ReviewCount => ScopedPr.Count(p => p.NeedsReview);
    public int TaskCount => ScopedWork.Count(w => !w.IsDone && w.Type == "Task");
    public int FeatureCount => ScopedWork.Count(w => !w.IsDone && w.Type == "Feature");
    public int PbiCount => ScopedWork.Count(w => !w.IsDone && w.Type == "Product Backlog Item");
    public int DoneCount => ScopedWork.Count(w => w.IsDone);
    public int TotalCount => ScopedWork.Count();
    public string Completion => TotalCount == 0 ? "0 %" : $"{(double)DoneCount / TotalCount:P0}";
    public string WorkSummary => $"{OpenCount} åpne · {DoneCount} ferdige · {TotalCount} tildelt";
    public string WorkListCaption => IsOverview ? "Prioriterte oppgaver · viser inntil 5" : $"{WorkItems.Count()} oppgaver i utvalget";
    public string PrListCaption => IsOverview ? "Venter på deg og siste aktivitet · viser inntil 4" : $"{PullRequests.Count()} pull requests i utvalget";
    public string ScopeNote => IsBoard ? "Boardet bruker egne prosjekt-, team- og søkefiltre. Flytting lagres når Azure DevOps bekrefter endringen." : "Prosjektfilteret gjelder alle visninger. Pipelines har eget tilleggsfilter. Søk filtrerer listene.";
    public IEnumerable<Insight> Insights
    {
        get
        {
            var list = new List<Insight>();
            if (ReviewCount > 0) list.Add(new("↗", $"{ReviewCount} PR-er venter på din review", "Aktive PR-er uten din stemme. Utkast er utelatt.", "#5266E9", ScopedPr.First(p => p.NeedsReview).Url));
            foreach (var w in ScopedWork.Where(w => w.IsCritical).Take(1)) list.Add(new("!", "En kritisk bug trenger oppmerksomhet", $"#{w.Id} · {w.Title}", "#DB6178", w.Url));
            var conflicts = ScopedPr.Where(p => p.Conflict).ToArray();
            if (conflicts.Length > 0) list.Add(new("⑂", $"{conflicts.Length} PR-er har mergekonflikter", "Må løses før endringene kan flettes.", "#CE8B3C", conflicts[0].Url));
            var stale = ScopedWork.Where(w => w.IsStale).ToArray();
            if (stale.Length > 0) list.Add(new("◷", $"{stale.Length} oppgaver har stått stille i over 7 dager", "Vurder å oppdatere status eller avklare neste steg.", "#9B72D7", stale[0].Url));
            var failed = ScopedBuilds.Where(b => b.Failed).ToArray();
            if (failed.Length > 0) list.Add(new("×", $"{failed.Length} pipelines har siste bygg med feil", "Siste kjøring per pipeline i de siste 7 dagene.", "#DB6178", failed[0].Url));
            if (list.Count == 0) list.Add(new("✓", "Ingen varsler i de hentede dataene", "Her vises reviews, kritiske bugs, konflikter, gamle oppgaver og byggfeil.", "#28A28B"));
            return list;
        }
    }
    public string Status => data.IsDemo ? "DEMO · Eksempeldata" : $"Oppdatert {data.LoadedAt:dd.MM HH:mm} · automatisk hvert 2,5. min";
    public string Mode => data.IsDemo ? "Demomodus" : data.Warnings.Count > 0 ? "Delvis tilkoblet" : "Azure DevOps tilkoblet";
    public string WarningText => string.Join("\n", data.Warnings);
    public bool HasWarnings => data.Warnings.Count > 0;
    public bool IsDemo => data.IsDemo;
    public bool HasData => TotalCount > 0;
    public void SetData(DashboardData value)
    {
        data = value;
        var available = data.Projects.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        projects = new[] { AllProjects, FocusProjects }.Concat(available).ToArray();
        pipelineProjects = new[] { FocusProjects, AllProjects }.Concat(available).ToArray();
        project = RestoreSelection(project, available, AllProjects);
        pipelineProject = RestoreSelection(pipelineProject, available, FocusProjects);
        Refresh();
    }
    private static string RestoreSelection(string selection, string[] available, string fallback) => selection == AllProjects || selection == FocusProjects
        ? selection : available.FirstOrDefault(p => p.Equals(selection, StringComparison.OrdinalIgnoreCase)) ?? fallback;
    private bool InProject(string name) => InSelection(name, project);
    private static bool InSelection(string name, string selection) => selection == AllProjects || (selection == FocusProjects
        ? IsFocusProject(name) : name.Equals(selection, StringComparison.OrdinalIgnoreCase));
    private static bool IsFocusProject(string name) => new[] { "hesa", "blåresept", "blaaresept", "blaresept" }.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);
    private bool Matches(string text) => string.IsNullOrWhiteSpace(search) || text.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
}
