using DevOpsPulse;

internal static class ProjectFilterChecks
{
    public static void Run(Action<bool, string> check)
    {
        var sample = DemoData.Create();
        var data = sample with
        {
            WorkItems = sample.WorkItems.Concat([sample.WorkItems[0] with { Id = 9999, Project = "Annet" }]).ToArray(),
            PullRequests = sample.PullRequests.Concat([sample.PullRequests[0] with { Id = 9999, Project = "Annet" }]).ToArray(),
            Builds = sample.Builds.Concat([sample.Builds[0] with { Id = 9999, Project = "Annet", Result = "failed" }]).ToArray(),
            Projects = ["HESA", "Blåresept", "Annet"]
        };
        var vm = new DashboardViewModel(); vm.SetData(data);
        check(vm.PipelineProject == DashboardViewModel.FocusProjects && vm.ScopedBuilds.Count() == sample.Builds.Count, "Pipelines default to HESA and Blåresept and exclude other projects");
        check(vm.TotalCount == data.WorkItems.Count, "Work remains across all projects until a project filter is chosen");
        check(vm.Projects.Contains(DashboardViewModel.FocusProjects) && vm.PipelineProjects.Contains(DashboardViewModel.AllProjects), "Combined project shortcut and pipeline all-projects option are available");
        vm.Project = DashboardViewModel.FocusProjects;
        check(vm.TotalCount == sample.WorkItems.Count && vm.ScopedPr.Count() == sample.PullRequests.Count && vm.ScopedBuilds.Count() == sample.Builds.Count, "Combined project filter applies to work, PRs and pipelines");
        vm.Project = "hesa";
        check(vm.ScopedWork.All(w => w.Project == "HESA") && vm.ScopedWork.Any() && vm.ScopedPr.All(p => p.Project == "HESA") && vm.ScopedBuilds.All(b => b.Project == "HESA"), "Individual project matching ignores case across sections");
        vm.SetData(data);
        check(vm.Project == "HESA", "Refresh restores selected project's current casing");
        vm.PipelineProject = "Blåresept";
        check(!vm.ScopedBuilds.Any() && vm.ScopedWork.Any(), "Pipeline filter intersects global filter without filtering work");
        vm.Project = DashboardViewModel.AllProjects;
        check(vm.ScopedBuilds.Any() && vm.ScopedBuilds.All(b => b.Project == "Blåresept") && vm.TotalCount == data.WorkItems.Count, "Pipeline-only project selection leaves work scope unchanged");
        vm.PipelineProject = DashboardViewModel.AllProjects;
        check(vm.ScopedBuilds.Count() == data.Builds.Count, "All projects can be explicitly restored for pipelines");
        vm.Project = DashboardViewModel.FocusProjects; vm.PipelineProject = DashboardViewModel.FocusProjects;
        vm.Search = "innlogging"; vm.SetData(data);
        check(vm.Project == DashboardViewModel.FocusProjects && vm.PipelineProject == DashboardViewModel.FocusProjects, "Combined filters survive search and data refresh");
        vm.SetData(data with { Projects = ["Annet"], WorkItems = [], PullRequests = [], Builds = [data.Builds.Last()] });
        check(vm.NoBuilds && vm.NoWork && vm.NoPr && vm.Project == DashboardViewModel.FocusProjects, "Missing focus projects yield empty selection without silently including other projects");
        vm.SetData(data with { Projects = ["hesa", "Blaaresept"], Builds = [sample.Builds[0] with { Project = "hesa" }, sample.Builds[1] with { Project = "Blaaresept" }] });
        check(vm.ScopedBuilds.Count() == 2, "Focus shortcut accepts case variations and ASCII spelling of Blåresept");
    }
}
