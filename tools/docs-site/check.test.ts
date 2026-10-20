import { assertEquals } from '@std/assert';
import { join } from '@std/path';
import { cdnReferences, manifestProblems, siteProblems } from './check.ts';

async function site(
  build: (root: string) => Promise<void>,
  assert: (root: string) => Promise<void>,
): Promise<void> {
  const root = await Deno.makeTempDir({ prefix: 'docs-site-check-' });
  try {
    await build(root);
    await assert(root);
  } finally {
    await Deno.remove(root, { recursive: true });
  }
}

async function write(root: string, rel: string, text: string): Promise<void> {
  const path = join(root, rel);
  await Deno.mkdir(join(path, '..'), { recursive: true });
  await Deno.writeTextFile(path, text);
}

async function intact(root: string): Promise<void> {
  await write(root, 'index.html', '<html></html>');
  await write(
    root,
    'versions.json',
    JSON.stringify({
      versions: [{ prefix: 'v1.0.0', tag: 'v1.0.0', release: true, built: '2026-09-01T00:00:00Z' }],
      latest: 'v1.0.0',
    }),
  );
  await write(root, 'v1.0.0/index.html', '<html></html>');
  await write(root, 'v1.0.0/site.css', 'body{}');
}

Deno.test('an intact site has no problems', async () => {
  await site(intact, async (root) => assertEquals(await siteProblems(root), []));
});

Deno.test('a missing manifest is the only problem reported', async () => {
  await site(
    async (root) => await write(root, 'index.html', '<html></html>'),
    async (root) => {
      const problems = await siteProblems(root);
      assertEquals(problems.length, 1);
      assertEquals(problems[0].includes('versions.json'), true);
    },
  );
});

Deno.test('a version listed in the manifest but absent on disk is reported', async () => {
  await site(
    async (root) => {
      await intact(root);
      await Deno.remove(join(root, 'v1.0.0', 'site.css'));
    },
    async (root) => {
      const problems = await siteProblems(root);
      assertEquals(problems.length, 1);
      assertEquals(problems[0].includes('site.css'), true);
    },
  );
});

Deno.test('a missing root index is reported', async () => {
  await site(
    async (root) => {
      await intact(root);
      await Deno.remove(join(root, 'index.html'));
    },
    async (root) => {
      const problems = await siteProblems(root);
      assertEquals(problems.some((p) => p.includes('index.html is missing')), true);
    },
  );
});

Deno.test('a CDN reference in a page is reported', async () => {
  await site(
    async (root) => {
      await intact(root);
      await write(root, 'v1.0.0/page.html', '<script src="https://cdn.jsdelivr.net/x.js">');
    },
    async (root) => {
      const problems = await siteProblems(root);
      assertEquals(problems.some((p) => p.includes('references a CDN')), true);
    },
  );
});

Deno.test('a font whose bytes spell a CDN host is not scanned', async () => {
  await site(
    async (root) => {
      await intact(root);
      await Deno.writeFile(
        join(root, 'v1.0.0', 'x.woff2'),
        new TextEncoder().encode('jsdelivr'),
      );
    },
    async (root) => assertEquals(await cdnReferences(root), []),
  );
});

Deno.test('a CDN host in a webmanifest is scanned', async () => {
  await site(
    async (root) => {
      await intact(root);
      await write(root, 'v1.0.0/app.webmanifest', '{"icons":[{"src":"https://unpkg.com/i.png"}]}');
    },
    async (root) => assertEquals((await cdnReferences(root)).length, 1),
  );
});

Deno.test('invalid JSON is the only problem reported', () => {
  const problems = manifestProblems('{not json');
  assertEquals(problems.length, 1);
  assertEquals(problems[0].startsWith('versions.json is not valid JSON'), true);
});

Deno.test('a manifest of null is reported, not thrown', () => {
  assertEquals(manifestProblems('null'), ['versions.json is not a JSON object']);
});

Deno.test('a manifest that is an array is reported, not thrown', () => {
  assertEquals(manifestProblems('[]'), ['versions.json is not a JSON object']);
});

Deno.test('a non-array versions is reported, not thrown', () => {
  const problems = manifestProblems('{"versions": 5, "latest": "x"}');
  assertEquals(problems, ['versions.json has no versions array']);
});

Deno.test('a duplicated prefix is reported', () => {
  const entry = { prefix: 'v1.0.0', tag: 'v1.0.0', release: true, built: 'now' };
  const text = JSON.stringify({ versions: [entry, entry], latest: 'v1.0.0' });
  assertEquals(manifestProblems(text), ['versions.json lists "v1.0.0" more than once']);
});

Deno.test('a non-boolean release flag is reported', () => {
  const text = JSON.stringify({
    versions: [{ prefix: 'v1.0.0', tag: null, release: 'true', built: 'now' }],
    latest: 'v1.0.0',
  });
  assertEquals(manifestProblems(text), ['version "v1.0.0" has a non-boolean release flag']);
});

Deno.test('siteProblems survives a manifest of null', async () => {
  await site(
    async (root) => {
      await intact(root);
      await write(root, 'versions.json', 'null');
    },
    async (root) => assertEquals(await siteProblems(root), ['versions.json is not a JSON object']),
  );
});

Deno.test('siteProblems survives a non-array versions', async () => {
  await site(
    async (root) => {
      await intact(root);
      await write(root, 'versions.json', '{"versions": 5, "latest": "x"}');
    },
    async (root) => assertEquals(await siteProblems(root), ['versions.json has no versions array']),
  );
});

Deno.test('siteProblems survives invalid JSON', async () => {
  await site(
    async (root) => {
      await intact(root);
      await write(root, 'versions.json', '{not json');
    },
    async (root) => {
      const problems = await siteProblems(root);
      assertEquals(problems.length, 1);
      assertEquals(problems[0].startsWith('versions.json is not valid JSON'), true);
    },
  );
});

Deno.test('a malformed manifest still gets the versions it can read walked', async () => {
  await site(
    async (root) => {
      await intact(root);
      await Deno.remove(join(root, 'v1.0.0', 'site.css'));
      await write(
        root,
        'versions.json',
        JSON.stringify({
          versions: [{ prefix: 'v1.0.0', tag: 'v1.0.0', release: true, built: 'now' }],
          latest: 'v2.0.0',
        }),
      );
    },
    async (root) => {
      const problems = await siteProblems(root);
      assertEquals(problems.some((p) => p.includes('not one of its versions')), true);
      assertEquals(problems.some((p) => p.includes('site.css')), true);
    },
  );
});

Deno.test('a latest that names no version is reported', () => {
  const text = JSON.stringify({
    versions: [{ prefix: 'v1.0.0', tag: 'v1.0.0', release: true, built: 'now' }],
    latest: 'v2.0.0',
  });
  assertEquals(manifestProblems(text), [
    'versions.json names latest "v2.0.0", which is not one of its versions',
  ]);
});

Deno.test('a release entry without a tag is reported', () => {
  const text = JSON.stringify({
    versions: [{ prefix: 'v1.0.0', tag: null, release: true, built: 'now' }],
    latest: 'v1.0.0',
  });
  assertEquals(manifestProblems(text), [
    'version "v1.0.0" claims to be a release but carries no tag',
  ]);
});

Deno.test('an unversioned entry needs no tag', () => {
  const text = JSON.stringify({
    versions: [{ prefix: 'unversioned', tag: null, release: false, built: 'now' }],
    latest: 'unversioned',
  });
  assertEquals(manifestProblems(text), []);
});
