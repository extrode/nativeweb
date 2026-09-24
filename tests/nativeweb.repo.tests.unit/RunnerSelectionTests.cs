using System.Text.RegularExpressions;
using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

public sealed partial class RunnerSelectionTests
{
    [Theory]
    [InlineData("ci.yml")]
    [InlineData("publish.yml")]
    public void EveryJob_PicksItsRunnerFromARepoVariable_FallingBackToAHostedOne(string file)
    {
        Assert.All(Jobs(file), job =>
        {
            Match runsOn = RunsOnPattern().Match(job.Body);
            Assert.True(runsOn.Success, $"{file} job '{job.Name}' has no vars-selected runs-on.");
            Assert.Equal(
                runsOn.Groups["os"].Value == "LINUX" ? "ubuntu" : "windows",
                runsOn.Groups["hosted"].Value);
        });
    }

    [Theory]
    [InlineData("ci.yml")]
    [InlineData("publish.yml")]
    public void EveryJob_RestoresIntoACacheTheRunnerEmptiesPerJob(string file)
    {
        Assert.All(Jobs(file), job =>
        {
            Match cold = ColdCachePattern().Match(job.Body);
            Assert.True(cold.Success, $"{file} job '{job.Name}' never points NUGET_PACKAGES at RUNNER_TEMP.");

            Match sdk = SdkCommandPattern().Match(job.Body);
            Assert.True(!sdk.Success || cold.Index < sdk.Index,
                $"{file} job '{job.Name}' runs the SDK before it points NUGET_PACKAGES at RUNNER_TEMP.");
        });
    }

    [Theory]
    [InlineData("ci.yml")]
    [InlineData("publish.yml")]
    public void EveryAddSource_FirstRemovesTheSameNameFromTheSameConfig(string file)
    {
        int adds = 0;
        foreach ((string name, string body) in Jobs(file))
        {
            foreach (Match add in AddSourcePattern().Matches(body))
            {
                adds++;
                string source = add.Groups["name"].Value;
                string config = add.Groups["config"].Value;
                string before = body[..add.Index];
                Assert.True(
                    before.Contains($"dotnet nuget remove source {source} --configfile \"{config}\"", StringComparison.Ordinal),
                    $"{file} job '{name}' adds '{source}' to {config} without removing it first.");
            }
        }

        Assert.True(adds > 0, $"{file} adds no source at all.");
    }

    [Theory]
    [InlineData("compose.yaml", "--labels nativeweb-linux")]
    [InlineData("install-windows-runner.ps1", "$labels = 'nativeweb-windows'")]
    public void TheRunnersRegisterToThisRepositoryUnderTheLabelsTheVariablesName(string file, string label)
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ci-runner", file));

        Assert.Contains("https://github.com/extrode/nativeweb", text, StringComparison.Ordinal);
        Assert.Contains(label, text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheComposeProjectIsNamed_SoItCannotMergeWithAnotherCiRunnerDirectory()
    {
        string compose = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ci-runner", "compose.yaml"));

        Assert.Matches(ProjectNamePattern(), compose);
    }

    private static List<(string Name, string Body)> Jobs(string file)
    {
        string workflow = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", file));
        Match jobsKey = JobsKeyPattern().Match(workflow);
        Assert.True(jobsKey.Success, $"{file} declares no jobs: block.");

        MatchCollection ids = JobIdPattern().Matches(workflow, jobsKey.Index);
        Assert.NotEmpty(ids);

        var jobs = new List<(string, string)>();
        for (int i = 0; i < ids.Count; i++)
        {
            int to = i + 1 < ids.Count ? ids[i + 1].Index : workflow.Length;
            jobs.Add((ids[i].Groups[1].Value, CommentLinePattern().Replace(workflow[ids[i].Index..to], string.Empty)));
        }

        return jobs;
    }

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NativeWeb.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException($"Could not locate NativeWeb.slnx above {AppContext.BaseDirectory}.");
    }

    [GeneratedRegex(@"(?m)^    runs-on: \$\{\{ vars\.CI_(?<os>LINUX|WINDOWS)_RUNNER \|\| '(?<hosted>ubuntu|windows)-latest' \}\}\r?$")]
    private static partial Regex RunsOnPattern();

    [GeneratedRegex(@"NUGET_PACKAGES=\$(?:env:)?RUNNER_TEMP[\\/]nuget-packages")]
    private static partial Regex ColdCachePattern();

    [GeneratedRegex(@"dotnet (?:restore|build|publish|pack|test|run)\b")]
    private static partial Regex SdkCommandPattern();

    [GeneratedRegex(@"dotnet nuget add source \S+ [\\`]\r?\n\s*--name (?<name>\S+)(?:[^\n]*[\\`]\r?\n)*?[^\n]*--configfile ""(?<config>[^""]+)""")]
    private static partial Regex AddSourcePattern();

    [GeneratedRegex(@"(?m)^name: nativeweb-ci-runner\r?$")]
    private static partial Regex ProjectNamePattern();

    [GeneratedRegex(@"(?m)^[ \t]*#.*$")]
    private static partial Regex CommentLinePattern();

    [GeneratedRegex(@"(?m)^jobs:[ \t]*$")]
    private static partial Regex JobsKeyPattern();

    [GeneratedRegex(@"(?m)^  ([A-Za-z0-9_-]+):[ \t]*\r?$")]
    private static partial Regex JobIdPattern();
}
