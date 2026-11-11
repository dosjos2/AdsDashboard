using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace DevOpsPulse;
public sealed record ActivityEvent(string Key, string Title, string Detail, WorkItem Item, DateTimeOffset Occurred)
{
    public string Timestamp => Occurred.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}
public sealed class ActivityFeed : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<ActivityEvent> Items { get; } = [];
    private readonly HashSet<string> seen = [];
    private HashSet<int>? assigned;
    private int unread;
    private string status = "Varsler sjekkes hvert 2,5. minutt mens appen er åpen. Første henting etablerer utgangspunktet.";
    public int UnreadCount => unread;
    public string ButtonLabel => $"Varsler ({unread})";
    public string Status { get => status; set { status = value; Changed(); } }
    public bool DesktopAlertsEnabled { get; set; } = true;
    public int ObserveAssignments(DashboardData data)
    {
        if (data.IsDemo || data.Warnings.Any(w => w.Contains("oppgaver", StringComparison.OrdinalIgnoreCase))) return 0;
        var previous = assigned; assigned = data.WorkItems.Select(w => w.Id).ToHashSet();
        if (previous == null) return 0;
        return Add(data.WorkItems.Where(w => !previous.Contains(w.Id)).Select(w => new ActivityEvent($"assigned:{w.Id}:{w.Changed:O}", "Ny oppgave tildelt deg", $"#{w.Id} · {w.Title} · {w.Project}", w, data.LoadedAt)));
    }
    public int Add(IEnumerable<ActivityEvent> events)
    {
        var count = 0;
        foreach (var entry in events.OrderBy(e => e.Occurred))
        {
            if (!seen.Add(entry.Key)) continue;
            Items.Insert(0, entry); unread++; count++;
            if (Items.Count > 200) Items.RemoveAt(Items.Count - 1);
        }
        unread = Math.Min(unread, Items.Count); Changed(); return count;
    }
    public void MarkRead() { unread = 0; Changed(); }
    public void Reset() { Items.Clear(); seen.Clear(); assigned = null; unread = 0; Status = "Koble til for å overvåke nye tildelinger og @omtaler."; Changed(); }
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
}
public static class MentionText
{
    public static bool ContainsIdentity(string text, string userId, string email)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (Guid.TryParse(userId, out _))
            foreach (Match match in Regex.Matches(text, "data-vss-mention\\s*=\\s*([\"'])(.*?)\\1", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                if (match.Groups[2].Value.Contains(userId, StringComparison.OrdinalIgnoreCase)) return true;
        return !string.IsNullOrWhiteSpace(email) && Regex.IsMatch(text, @"(?<![\w.])@" + Regex.Escape(email) + @"(?![\w.])", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }
}
