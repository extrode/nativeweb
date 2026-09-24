using System.Diagnostics;

using Xunit;

namespace NativeWeb.Repo.Tests.Unit;

/// <remarks>
/// On 2026-09-24 the gate failed on the self-hosted runner with nothing in the log
/// but "Process completed with exit code 1": <c>dotnet list package</c> exited
/// non-zero inside <c>out=$(...)</c>, and <c>bash -e</c> ended the step before the
/// <c>echo</c> that would have shown why.
/// </remarks>
public sealed class VulnerableGateTests : IDisposable
{
    private readonly string _workspace = Directory.CreateTempSubdirectory("nw-gate-").FullName;

    public void Dispose() => Directory.Delete(_workspace, recursive: true);

    [Fact]
    public void AnAuditThatCannotRun_FailsTheStepAndSaysWhy()
    {
        (int exitCode, string output) = RunGate(
            "error: Response status code does not indicate success: 401 (Unauthorized).",
            exitCode: 1);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("401 (Unauthorized)", output);
    }

    [Fact]
    public void AVulnerablePackage_FailsTheStep()
    {
        (int exitCode, string output) = RunGate(
            "Project `app` has the following vulnerable packages\n   [net10.0]:\n   > Some.Package      1.0.0      1.0.0      High",
            exitCode: 0);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Vulnerable package(s) found", output);
    }

    [Fact]
    public void AnAuditWithNoConfirmation_FailsTheStep()
    {
        (int exitCode, string output) = RunGate("", exitCode: 0);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("no per-project confirmation", output);
    }

    [Fact]
    public void ACleanAudit_PassesTheStep()
    {
        (int exitCode, string output) = RunGate(
            "The given project `app` has no vulnerable packages given the current sources.",
            exitCode: 0);

        Assert.True(exitCode == 0, output);
    }

    private (int ExitCode, string Output) RunGate(string stdout, int exitCode)
    {
        string bin = Directory.CreateDirectory(Path.Combine(_workspace, "bin")).FullName;
        File.WriteAllText(Path.Combine(bin, "dotnet"), """
            #!/bin/sh
            echo "fake dotnet ran"
            [ -n "$FAKE_STDOUT" ] && printf '%s\n' "$FAKE_STDOUT"
            exit "$FAKE_EXIT"
            """.ReplaceLineEndings("\n"));

        string script = Path.Combine(_workspace, "gate.sh");
        File.WriteAllText(script, GateScript().ReplaceLineEndings("\n"));

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                Path.Combine(bin, "dotnet"),
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        ProcessStartInfo start = new(Bash())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("--noprofile");
        start.ArgumentList.Add("--norc");
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(script);
        start.Environment["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        start.Environment["FAKE_STDOUT"] = stdout;
        start.Environment["FAKE_EXIT"] = exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("bash did not start.");

        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.Contains("fake dotnet ran", output);

        return (process.ExitCode, output);
    }

    /// <summary>
    /// The step's <c>run: |</c> block, dedented the way the runner writes it to
    /// the script file it hands to bash.
    /// </summary>
    private static string GateScript()
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), ".github", "workflows", "ci.yml"));
        int step = Array.FindIndex(lines, l => l.Trim() == "- name: Vulnerable package gate");
        Assert.True(step >= 0, "ci.yml has no 'Vulnerable package gate' step.");

        int run = Array.FindIndex(lines, step, l => l.Trim() == "run: |");
        Assert.True(run > step, "The vulnerable package gate has no 'run: |' block.");

        string indent = new(' ', lines[run].IndexOf('r', StringComparison.Ordinal) + 2);
        IEnumerable<string> body = lines
            .Skip(run + 1)
            .TakeWhile(l => l.Length == 0 || l.StartsWith(indent, StringComparison.Ordinal))
            .Select(l => l.Length == 0 ? l : l[indent.Length..]);

        return string.Join('\n', body) + "\n";
    }

    /// <summary>
    /// Git's bash on Windows, found beside the git on PATH. Plain <c>bash</c> there
    /// can resolve to WSL's launcher, which runs in another filesystem.
    /// </summary>
    private static string Bash()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "bash";
        }

        string? git = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir, "git.exe"))
            .FirstOrDefault(File.Exists);
        Assert.True(git is not null, "No git.exe on PATH, so no Git bash to run the gate with.");

        DirectoryInfo? dir = new FileInfo(git!).Directory;
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "bin", "bash.exe")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, $"No bin\\bash.exe above {git}.");

        return Path.Combine(dir!.FullName, "bin", "bash.exe");
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
}
