using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace __PRODUCT__;

// Startup-time configuration handling that must be drivable by the unit tier.
// Both members here fail by *not* firing, so a guard that silently returns is
// indistinguishable from a correct deployment -- which is why they are pure
// functions rather than inline in Program.cs's top-level statements: the tests
// call them directly instead of trying to observe a host that refused to start.
//
// Skeleton copy -- the product owns this file after stamping, and a product with
// its own boot-time invariants (a required secret, a row that must exist) adds
// them here beside these two.
internal static class StartupConfigGuards
{
    // Exit code for a refused start. Distinct from 0 is all an orchestrator
    // needs; 78 is sysexits.h's EX_CONFIG, which costs nothing and reads
    // correctly in a service log.
    internal const int ConfigurationErrorExitCode = 78;

    // Deliberately not `throw`. An unhandled exception out of Program.Main is a
    // *crash*: under NativeAOT it fail-fasts with STATUS_STACK_BUFFER_OVERRUN
    // (0xC0000409), which on Windows pops a modal "stack-based buffer overrun"
    // error box and blocks the process on it until someone clicks OK -- so a
    // mistyped configuration value hangs the service instead of failing it, and a
    // supervisor sees a hung process rather than a clean non-zero exit. Under a
    // container it produces a crash dump for what is not a bug. A misconfigured
    // deployment is an operator error with a known cause and a known remedy, so
    // report it and exit.
    [DoesNotReturn]
    internal static void FailStartup(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.Flush();
        Environment.Exit(ConfigurationErrorExitCode);
        throw new UnreachableException();
    }

    // Demotes the sources added at or after <paramref name="addedFrom"/> so they sit
    // just below the last environment-variables source, preserving their order
    // relative to each other.
    //
    // AddUserSecrets appends, which makes the store outrank a real environment
    // variable. That is backwards -- the store is a developer convenience and the
    // variable is what a deployment (or CI, or a test fixture) actually configured --
    // and it is silent: the app starts, reads the wrong connection string, and every
    // symptom points somewhere else. This is a pure list rewrite so the unit tier can
    // drive it; observing it through a booted host would mean owning a user-secrets
    // store on the machine running the tests.
    //
    // No-ops when nothing was added, or when no environment-variables source precedes
    // the addition -- in both cases the existing order is already correct.
    internal static void DemoteBelowEnvironmentVariables(IList<IConfigurationSource> sources, int addedFrom)
    {
        ArgumentNullException.ThrowIfNull(sources);

        int lastEnvironment = -1;
        for (int i = Math.Min(addedFrom, sources.Count) - 1; i >= 0; i--)
        {
            if (sources[i] is EnvironmentVariablesConfigurationSource)
            {
                lastEnvironment = i;
                break;
            }
        }

        if (lastEnvironment < 0)
        {
            return;
        }

        if (addedFrom >= sources.Count)
        {
            return;
        }

        // Lift the whole added range out before putting any of it back. Moving one at a
        // time back-to-front looks equivalent and is not: each RemoveAt/Insert pair
        // shifts everything between lastEnvironment and i up by one, so the next index
        // the loop visits is no longer the next added source. With two added sources it
        // walks onto the environment source itself and demotes THAT -- leaving an added
        // source above the environment variables, which is precisely the bug this
        // method exists to prevent. It only ever worked because AddUserSecrets adds
        // exactly one source.
        var added = new List<IConfigurationSource>(sources.Count - addedFrom);
        for (int i = addedFrom; i < sources.Count; i++)
        {
            added.Add(sources[i]);
        }

        for (int i = sources.Count - 1; i >= addedFrom; i--)
        {
            sources.RemoveAt(i);
        }

        for (int i = 0; i < added.Count; i++)
        {
            sources.Insert(lastEnvironment + i, added[i]);
        }
    }
}
