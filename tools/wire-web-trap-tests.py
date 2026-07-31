# V5 (R42): route every browser-shaped form POST in the IdP integration tier
# through TrapForm.ContentAsync. Machine-endpoint posts (token/introspect/
# device -- ResponseHardeningTests and OAuthTokenEndpointsTests.CallAsync) are
# untrapped endpoints and stay as-is. Safe to delete after V5 merges.
import os

os.chdir('tests/nativeweb.web.tests.integration')

FILES = [
    'AdminEndpointsTests.cs', 'OAuthEndpointsTests.cs', 'PassphraseEndpointsTests.cs',
    'RegisterEndpointsTests.cs', 'SettingsEndpointsTests.cs', 'SignInEndpointsTests.cs',
    'TwoFactorEndpointsTests.cs', 'WebAuthnEndpointsTests.cs',
]

for name in FILES:
    s = open(name, encoding='utf-8').read()
    n = s.count('new FormUrlEncodedContent(')
    s = s.replace('new FormUrlEncodedContent(', 'await TrapForm.ContentAsync(client, ')
    open(name, 'w', encoding='utf-8', newline='\n').write(s)
    print('%s: %d converted' % (name, n))

# OAuthTokenEndpointsTests: convert only PostFormAsync (authorize consent), not CallAsync.
name = 'OAuthTokenEndpointsTests.cs'
s = open(name, encoding='utf-8').read()
old = """            Content = new FormUrlEncodedContent(fields),"""
assert s.count(old) == 1
s = s.replace(old, """            Content = await TrapForm.ContentAsync(client, fields),""")
open(name, 'w', encoding='utf-8', newline='\n').write(s)
print('%s: 1 converted (PostFormAsync only)' % name)
