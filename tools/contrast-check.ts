#!/usr/bin/env -S deno run --allow-read
//
// Plan 009. The sample overrides the framework's design tokens in its own layer,
// and a palette is the one kind of change that fails silently: nothing throws,
// no test goes red, and the page still renders -- it is only unreadable.
//
// So every foreground/background pair the sample's chrome actually puts together
// is measured here against WCAG 2.1 AA (1.4.3 Contrast Minimum): 4.5:1 for body
// text, 3:1 for large text and for the non-text boundaries under 1.4.11. The
// framework's own tokens.css carries two divergences from its source prototype
// won in an a11y pass, both documented in its header; overriding the palette
// without re-measuring would discard them without saying so.
//
// Reads the values out of the stylesheet rather than taking a copy, so a token
// edited in CSS and not here is measured, not missed.
//
//   deno run --allow-read tools/contrast-check.ts
//
// Exit 0 when every pair clears its threshold, 1 otherwise, with the failures
// listed. No arguments, no network, no write.

const CSS = new URL("../apps/sample/wwwroot/css/sample.css", import.meta.url);

type Pair = { fg: string; bg: string; min: number; what: string };

// Each entry names a place the chrome puts the first colour on the second, and
// the threshold that place is held to. A pair nobody renders is not listed --
// a passing ratio for a combination the site never shows is noise.
const PAIRS: Pair[] = [
  { fg: "--t1", bg: "--bg", min: 4.5, what: "body text on the page" },
  { fg: "--t1", bg: "--s1", min: 4.5, what: "card title on a card" },
  { fg: "--t2", bg: "--bg", min: 4.5, what: "lede on the page" },
  { fg: "--t2", bg: "--s1", min: 4.5, what: "card body on a card" },
  { fg: "--t3", bg: "--bg", min: 4.5, what: "footer and meta on the page" },
  { fg: "--t3", bg: "--s1", min: 4.5, what: "meta line on a card" },
  { fg: "--on-ac", bg: "--ac", min: 4.5, what: "primary button label" },
  { fg: "--ac", bg: "--bg", min: 4.5, what: "link on the page" },
  { fg: "--ac", bg: "--s1", min: 4.5, what: "link on a card" },
  { fg: "--bd2", bg: "--s1", min: 3.0, what: "input border on a card (1.4.11)" },
  { fg: "--t1", bg: "--s2", min: 4.5, what: "text on the raised surface" },
  { fg: "--t2", bg: "--s3", min: 4.5, what: "text on the sunken surface" },
];

// Two themes, two blocks. The selectors are what theme.js sets on <html>.
const THEMES: Record<string, string[]> = {
  light: [":root", '[data-theme="light"]'],
  dark: ['[data-theme="dark"]'],
};

// Every declaration block whose selector list contains this selector. Scanned
// rather than matched with a regex: a selector like [data-theme="dark"] is most
// of a character class already, and escaping it into one is how this stops
// finding a block it should have found.
function blocks(css: string, selector: string): string[] {
  const found: string[] = [];
  let from = 0;
  for (;;) {
    const at = css.indexOf(selector, from);
    if (at < 0) return found;
    from = at + selector.length;

    const open = css.indexOf("{", at);
    const close = css.indexOf("}", open);
    if (open < 0 || close < 0) return found;

    // The selector must be part of THIS block's selector list, not of a
    // declaration inside an earlier block that happens to name it.
    const heading = css.slice(css.lastIndexOf("}", at) + 1, open);
    if (heading.includes(selector) && !heading.includes(";")) {
      found.push(css.slice(open + 1, close));
    }
  }
}

function tokens(css: string, selectors: string[]): Map<string, string> {
  const values = new Map<string, string>();
  for (const selector of selectors) {
    for (const body of blocks(css, selector)) {
      for (const line of body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) {
        values.set(line[1], line[2].trim());
      }
    }
  }
  return values;
}

function channel(value: number): number {
  const c = value / 255;
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
}

// Hex, or rgba() over a known backdrop. A token written as rgba() is composited
// against its background before measuring, which is what the eye sees; anything
// else is refused rather than guessed at.
function rgb(value: string, over: [number, number, number] | null): [number, number, number] {
  const hex = value.match(/^#([0-9a-f]{3}|[0-9a-f]{6})$/i);
  if (hex) {
    const h = hex[1].length === 3 ? [...hex[1]].map((c) => c + c).join("") : hex[1];
    return [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16)) as [number, number, number];
  }

  const rgba = value.match(/^rgba?\(([^)]+)\)$/i);
  if (rgba) {
    const parts = rgba[1].split(",").map((p) => Number(p.trim()));
    const alpha = parts.length > 3 ? parts[3] : 1;
    if (!over) throw new Error(`${value} is translucent and has no backdrop to composite over`);
    return [0, 1, 2].map((i) => parts[i] * alpha + over[i] * (1 - alpha)) as [number, number, number];
  }

  throw new Error(`cannot read colour ${value}`);
}

function luminance([r, g, b]: [number, number, number]): number {
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

function ratio(fg: [number, number, number], bg: [number, number, number]): number {
  const [a, b] = [luminance(fg), luminance(bg)].sort((x, y) => y - x);
  return (a + 0.05) / (b + 0.05);
}

const css = await Deno.readTextFile(CSS);
const failures: string[] = [];
let checked = 0;

for (const [theme, selectors] of Object.entries(THEMES)) {
  const values = tokens(css, selectors);
  for (const pair of PAIRS) {
    const fgValue = values.get(pair.fg);
    const bgValue = values.get(pair.bg);
    if (!fgValue || !bgValue) {
      failures.push(`${theme}: ${pair.fg} or ${pair.bg} is not declared in the sample's overrides`);
      continue;
    }

    const bg = rgb(bgValue, null);
    const measured = ratio(rgb(fgValue, bg), bg);
    checked++;
    if (measured < pair.min) {
      failures.push(
        `${theme}: ${pair.fg} on ${pair.bg} is ${measured.toFixed(2)}:1, below ${pair.min}:1 — ${pair.what}`,
      );
    }
  }
}

if (failures.length > 0) {
  console.error(`${failures.length} of ${checked + failures.length} pairs fail:`);
  for (const failure of failures) console.error(`  ${failure}`);
  Deno.exit(1);
}

console.log(`${checked} foreground/background pairs clear WCAG AA in both themes.`);
