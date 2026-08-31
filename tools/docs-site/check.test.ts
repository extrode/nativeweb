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

Deno.test('a font is not scanned for CDN hosts', async () => {
  await site(
    async (root) => {
      await intact(root);
      await Deno.writeFile(join(root, 'v1.0.0', 'x.woff2'), new Uint8Array([0xff, 0xfe, 0x00]));
    },
    async (root) => assertEquals(await cdnReferences(root), []),
  );
});

Deno.test('invalid JSON stops before the version walk', () => {
  const problems = manifestProblems('{not json');
  assertEquals(problems.length, 1);
  assertEquals(problems[0].startsWith('versions.json is not valid JSON'), true);
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
