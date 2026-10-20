# V6 (R42): the e2e tier now runs the published exe over https (the __Host-
# session cookie is Secure-always, and Chromium refuses the __Host- prefix on
# an http origin -- verified empirically, localhost trustworthiness covers the
# Secure attribute but not the prefix). The fixture's cert is self-signed, so
# every browser context ignores https errors and every raw HttpClient gets a
# validation-bypass handler (E2eHttp.CreateClient). Safe to delete after V6.
import glob

def rewrite(pattern, old, new, exclude=()):
    total = 0
    for name in glob.glob(pattern):
        base = name.replace('\\', '/').rsplit('/', 1)[-1]
        if base in exclude:
            continue
        s = open(name, encoding='utf-8').read()
        n = s.count(old)
        if n:
            open(name, 'w', encoding='utf-8', newline='\n').write(s.replace(old, new))
            total += n
            print('%s: %d x %s' % (name, n, old.strip()))
    return total

for d in ('tests/nativeweb.web.tests.e2e', 'tests/nativeweb.devmarks.tests.e2e'):
    p = d + '/*.cs'
    rewrite(p, 'Browser.NewContextAsync(new() { JavaScriptEnabled = false })',
               'Browser.NewContextAsync(new() { JavaScriptEnabled = false, IgnoreHTTPSErrors = true })')
    rewrite(p, 'Browser.NewContextAsync()',
               'Browser.NewContextAsync(new() { IgnoreHTTPSErrors = true })')
    rewrite(p, 'Browser.NewPageAsync()',
               'Browser.NewPageAsync(new() { IgnoreHTTPSErrors = true })')
    # Fixtures build their own clients (healthz poll) and are edited by hand.
    rewrite(p, 'new HttpClient()', 'E2eHttp.CreateClient()',
            exclude=('AppProcessFixture.cs', 'DevMarksProcessFixture.cs'))
