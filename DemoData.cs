namespace DevOpsPulse;
public static class DemoData
{
    public static DashboardData Create()
    {
        var now = DateTimeOffset.Now; const string me = "demo-jan";
        var rows = new (int Id, string Title, string Type, string State, string Project, int Priority, int Days)[]
        {
            (4218, "Forbedre validering av innsendte skjema", "Task", "Active", "HESA", 2, 0),
            (4192, "Feil ved innlogging etter utløpt sesjon", "Bug", "Active", "HESA", 1, 2),
            (4176, "Ny oversikt for nasjonale kvalitetsindikatorer", "Feature", "Active", "Blåresept", 2, 1),
            (4201, "Oppdatere dokumentasjon for tilgangsstyring", "Task", "New", "HESA", 3, 9),
            (4184, "Tabell mister sortering ved sideskift", "Bug", "New", "Blåresept", 2, 3),
            (4167, "Legge til tester for eksport av rapporter", "Task", "Active", "Blåresept", 2, 1),
            (4143, "Universell utforming av rapportvisning", "Feature", "New", "Blåresept", 2, 12),
            (4138, "Rydde opp i utdaterte avhengigheter", "Task", "New", "HESA", 3, 10),
            (4102, "Publisere nye API-beskrivelser", "Task", "Closed", "HESA", 2, 2),
            (4098, "Feil datoformat i nedlastet rapport", "Bug", "Closed", "Blåresept", 2, 3),
            (4086, "Overvåking av datakvalitet", "Feature", "Closed", "Blåresept", 2, 5),
            (4071, "Oppgradere byggepipeliner", "Task", "Closed", "HESA", 2, 6),
            (4223, "Som saksbehandler vil jeg se resepthistorikk", "Product Backlog Item", "Committed", "Blåresept", 2, 0),
            (4221, "Enklere oppfølging av innsendte søknader", "Product Backlog Item", "New", "HESA", 2, 1),
            (4068, "Vise kvittering etter innsending", "Product Backlog Item", "Done", "HESA", 2, 5)
        };
        var work = rows.Select(r => new WorkItem(r.Id, r.Title, r.Type, r.State, r.Project, r.Project + "\\Sprint 24", r.Priority, r.Priority == 1 ? "1 - Critical" : "3 - Medium", now.AddDays(-r.Days).AddHours(-2), "")
        {
            Description = $"Dette er en fiktiv eksempeloppgave: {r.Title}.\n\nAvklar ønsket oppførsel, gjennomfør endringen og verifiser resultatet sammen med teamet.",
            AssignedTo = "Jan Ole Skotterud", AssignedToId = "demo-jan", AssignedEmail = "jan.ole.skotterud@helsedir.no", AreaPath = r.Project + "\\Utvikling", Tags = "Demo; Eksempel",
            ReproSteps = r.Type == "Bug" ? "1. Åpne eksempelvisningen.\n2. Utfør handlingen som utløser feilen.\n3. Sammenlign forventet og faktisk resultat." : "",
            AcceptanceCriteria = "• Endringen er kontrollert.\n• Relevante feiltilfeller er håndtert.\n• Teamet har gjennomgått resultatet."
        }).ToArray();
        var prs = new[]
        {
            new PullRequest(842, "Bedre feilhåndtering ved innlogging", "HESA", "innbyggerportal", me, "Jan Ole Skotterud", "active", false, "succeeded", now.AddDays(-2), "refs/heads/fix/session", "refs/heads/main", new[]{new Reviewer("anna", "Anna Hansen", 10, true), new Reviewer("per", "Per Olsen", 0, true)}, me, ""),
            new PullRequest(839, "Ny eksport av kvalitetsindikatorer", "Blåresept", "rapportering", "anna", "Anna Hansen", "active", false, "succeeded", now.AddHours(-18), "refs/heads/feature/export", "refs/heads/main", new[]{new Reviewer(me, "Jan Ole Skotterud", 0, true)}, me, ""),
            new PullRequest(836, "Oppdatere komponentbibliotek", "HESA", "designsystem", me, "Jan Ole Skotterud", "active", false, "conflicts", now.AddDays(-4), "refs/heads/chore/components", "refs/heads/main", new[]{new Reviewer("per", "Per Olsen", 5, false)}, me, ""),
            new PullRequest(833, "Tilgangskontroll for rapporter", "Blåresept", "rapportering", "per", "Per Olsen", "active", false, "succeeded", now.AddDays(-3), "refs/heads/feature/access", "refs/heads/main", new[]{new Reviewer(me, "Jan Ole Skotterud", 0, true)}, me, ""),
            new PullRequest(828, "Enhetlig datoformat i tabeller", "Blåresept", "rapportering", me, "Jan Ole Skotterud", "completed", false, "succeeded", now.AddDays(-6), "refs/heads/fix/dates", "refs/heads/main", new[]{new Reviewer("anna", "Anna Hansen", 10, true)}, me, ""),
            new PullRequest(824, "Oppdatert dokumentasjon", "HESA", "innbyggerportal", "anna", "Anna Hansen", "active", true, "succeeded", now.AddDays(-1), "refs/heads/docs/setup", "refs/heads/main", new[]{new Reviewer(me, "Jan Ole Skotterud", 0, false)}, me, "")
        };
        var builds = new[]
        {
            new BuildInfo(1,"innbyggerportal · CI", "20261102.3", "HESA", "refs/heads/main", "completed", "succeeded", now.AddMinutes(-23), ""),
            new BuildInfo(2,"rapportering · CI", "20261102.2", "Blåresept", "refs/heads/main", "completed", "failed", now.AddMinutes(-42), ""),
            new BuildInfo(3,"designsystem · CI", "20261102.1", "HESA", "refs/heads/feature/components", "inProgress", "", now.AddMinutes(-4), ""),
            new BuildInfo(4,"datakvalitet · CI", "20261101.5", "Blåresept", "refs/heads/main", "completed", "succeeded", now.AddHours(-12), "")
        };
        return new DashboardData(work, prs, builds, ["Blåresept", "HESA"], [], now, true);
    }
}
