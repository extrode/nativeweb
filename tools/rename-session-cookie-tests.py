# V6 (R42): the session cookie gained the __Host- prefix
# (apps/web/Auth/SessionAuth.cs). The web test tiers hardcode the old literal
# (devmarks tests use the const and need no change); rewrite every occurrence.
# ".NativeWeb.ReturnUrl" / ".NativeWeb.TotpChallenge" / ".NativeWeb.WebAuthnChallenge"
# are different cookies and deliberately untouched. Safe to delete after V6 merges.
import glob

OLD = '.NativeWeb.Session'
NEW = '__Host-NativeWeb.Session'

total = 0
for name in glob.glob('tests/nativeweb.web.tests.integration/*.cs') + glob.glob('tests/nativeweb.web.tests.e2e/*.cs'):
    s = open(name, encoding='utf-8').read()
    n = s.count(OLD)
    if n == 0:
        continue
    s = s.replace(OLD, NEW)
    open(name, 'w', encoding='utf-8', newline='\n').write(s)
    total += n
    print('%s: %d' % (name, n))
print('total: %d' % total)
