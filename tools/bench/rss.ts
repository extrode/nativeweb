// Peak resident set of the app process, sampled while load runs.
//
// Section 14's memory comparison was RSS under load (htmx-skeleton 49MB against the Deno
// process's 186MB), so that is the figure the baseline keeps. Sampled rather than read once at
// the end: the interesting number is the peak the server reached while serving, and a
// post-run read misses whatever the GC has already given back.
//
// `tasklist` on Windows, not `(Get-Process).WorkingSet64` -- PowerShell start-up is several
// hundred milliseconds of CPU per sample, on the same machine as the thing being measured.
// tasklist is a plain console tool and costs a fraction of that.

/** Parse one `tasklist /FO CSV /NH` line into bytes. Null when the PID has no row. */
export function parseTasklist(output: string): number | null {
  const line = output.split("\n").map((l) => l.trim()).find((l) => l.startsWith('"'));
  if (line === undefined) return null;

  const fields = line.split('","');
  const memory = fields.at(-1);
  if (memory === undefined) return null;

  // `"51,234 K"` -- thousands separators are locale-dependent, so strip everything that is
  // not a digit rather than assuming a comma.
  const digits = memory.replace(/[^0-9]/g, "");
  if (digits.length === 0) return null;
  return Number(digits) * 1024;
}

/** Parse `ps -o rss= -p N` output (kilobytes) into bytes. Null when the PID is gone. */
export function parsePs(output: string): number | null {
  const digits = output.trim();
  if (!/^[0-9]+$/.test(digits)) return null;
  return Number(digits) * 1024;
}

/** One RSS reading for a live process, or null if it cannot be read. */
export async function sampleRssBytes(pid: number): Promise<number | null> {
  try {
    if (Deno.build.os === "windows") {
      const command = new Deno.Command("tasklist", {
        args: ["/FI", `PID eq ${pid}`, "/FO", "CSV", "/NH"],
        stdout: "piped",
        stderr: "null",
      });
      const { stdout } = await command.output();
      return parseTasklist(new TextDecoder().decode(stdout));
    }

    const command = new Deno.Command("ps", {
      args: ["-o", "rss=", "-p", String(pid)],
      stdout: "piped",
      stderr: "null",
    });
    const { stdout } = await command.output();
    return parsePs(new TextDecoder().decode(stdout));
  } catch {
    return null;
  }
}

export interface RssSampler {
  /** Stop sampling and return the highest reading seen, 0 if none succeeded. */
  stop(): Promise<number>;
}

/**
 * Start sampling until stopped. A failed sample is skipped, not fatal: the process can exit
 * between the load finishing and the sampler stopping, and losing the last reading is not
 * worth failing a benchmark over. A run where EVERY sample failed reports 0, which the
 * baseline comparison treats as a value below any ceiling -- so `run.ts` refuses to record a
 * baseline with a zero RSS rather than committing a figure nothing measured.
 */
export function startRssSampler(
  pid: number,
  intervalMs: number,
  sample: (pid: number) => Promise<number | null> = sampleRssBytes,
): RssSampler {
  let running = true;
  let peak = 0;

  const loop = (async () => {
    while (running) {
      const bytes = await sample(pid);
      if (bytes !== null && bytes > peak) peak = bytes;
      if (!running) break;
      await new Promise((resolve) => setTimeout(resolve, intervalMs));
    }
  })();

  return {
    async stop(): Promise<number> {
      running = false;
      await loop;
      return peak;
    },
  };
}
