# V5 wiring helper (R23, R42): teaches the devmarks integration tests to post
# forms the way a browser does -- honeypot fields echoed untouched plus the
# page's signed canary -- via the shared FormFlow helper.
# Safe to delete after V5 merges.
import sys

changed = []


def rewrite(path, subs, replace_all=()):
    s = open(path, encoding='utf-8').read()
    orig = s
    for old, new in subs:
        n = s.count(old)
        if n == 0:
            print('MISS in %s: %s' % (path, old[:70].replace('\n', '\\n')))
            continue
        if n != 1 and (old, new) not in replace_all:
            print('AMBIGUOUS(%d) in %s: %s' % (n, path, old[:70].replace('\n', '\\n')))
            continue
        s = s.replace(old, new)
    if s != orig:
        open(path, 'w', encoding='utf-8', newline='\n').write(s)
        changed.append(path)


POST_OLD = """    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string path, string antiforgeryToken, Dictionary<string, string> fields)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(fields),
        };
        request.Headers.Add("RequestVerificationToken", antiforgeryToken);
        return await client.SendAsync(request);
    }"""

POST_NEW = """    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string path, FormTokens tokens, Dictionary<string, string> fields)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(FormFlow.WithTrapFields(fields, tokens.Canary)),
        };
        request.Headers.Add("RequestVerificationToken", tokens.Antiforgery);
        return await client.SendAsync(request);
    }"""

GET_OLD = """    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        HttpResponseMessage response = await client.GetAsync(path);
        string html = await response.Content.ReadAsStringAsync();
        Match match = AntiforgeryTokenPattern().Match(html);
        Assert.True(match.Success, $"Expected a request-verification-token <meta> tag at {path}.");
        return match.Groups[1].Value;
    }"""

GET_NEW = """    private static async Task<FormTokens> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        HttpResponseMessage response = await client.GetAsync(path);
        string html = await response.Content.ReadAsStringAsync();
        Match match = AntiforgeryTokenPattern().Match(html);
        Assert.True(match.Success, $"Expected a request-verification-token <meta> tag at {path}.");
        return new FormTokens(match.Groups[1].Value, FormFlow.CanaryFrom(html));
    }"""

DECL_TOKEN = ('string token = await GetAntiforgeryTokenAsync',
              'FormTokens token = await GetAntiforgeryTokenAsync')
DECL_AF = ('string antiforgery = await GetAntiforgeryTokenAsync',
           'FormTokens antiforgery = await GetAntiforgeryTokenAsync')

STANDARD = [POST_OLD and (POST_OLD, POST_NEW), (GET_OLD, GET_NEW), DECL_TOKEN, DECL_AF]

for f in ['AccountEndpointsTests', 'BookmarkEndpointsTests', 'PublicPagesTests',
          'SearchTests', 'TagEndpointsTests']:
    rewrite('tests/nativeweb.devmarks.tests.integration/%s.cs' % f,
            STANDARD, replace_all=(DECL_TOKEN, DECL_AF))

rewrite('tests/nativeweb.devmarks.tests.integration/TransferEndpointsTests.cs', [
    (POST_OLD, POST_NEW),
    (GET_OLD, GET_NEW),
    DECL_TOKEN,
    ("""            content.Add(file, "file", "bookmarks.html");
""",
     """            content.Add(file, "file", "bookmarks.html");
            foreach (KeyValuePair<string, string> trap in FormFlow.WithTrapFields([], token.Canary))
            {
                content.Add(new StringContent(trap.Value), trap.Key);
            }
"""),
    ("""            request.Headers.Add("RequestVerificationToken", token);
""",
     """            request.Headers.Add("RequestVerificationToken", token.Antiforgery);
"""),
], replace_all=(DECL_TOKEN,))

rewrite('tests/nativeweb.devmarks.tests.integration/RateLimitTests.cs', [
    (GET_OLD, GET_NEW),
    DECL_TOKEN,
    ("""            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = text }),
        };
        request.Headers.Add("RequestVerificationToken", token);
""",
     """            Content = new FormUrlEncodedContent(FormFlow.WithTrapFields(new Dictionary<string, string> { ["text"] = text }, token.Canary)),
        };
        request.Headers.Add("RequestVerificationToken", token.Antiforgery);
"""),
    ("""            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["email"] = $"{username}@example.com",
                ["passphrase"] = Passphrase,
                ["confirmPassphrase"] = Passphrase,
            }),
        };
        request.Headers.Add("RequestVerificationToken", token);
""",
     """            Content = new FormUrlEncodedContent(FormFlow.WithTrapFields(new Dictionary<string, string>
            {
                ["username"] = username,
                ["email"] = $"{username}@example.com",
                ["passphrase"] = Passphrase,
                ["confirmPassphrase"] = Passphrase,
            }, token.Canary)),
        };
        request.Headers.Add("RequestVerificationToken", token.Antiforgery);
"""),
], replace_all=(DECL_TOKEN,))

rewrite('tests/nativeweb.devmarks.tests.integration/HomePageTests.cs', [
    ("""        HttpResponseMessage tokenResponse = await client.GetAsync("/register");
        string token = AntiforgeryTokenPattern().Match(await tokenResponse.Content.ReadAsStringAsync()).Groups[1].Value;
""",
     """        HttpResponseMessage tokenResponse = await client.GetAsync("/register");
        string html = await tokenResponse.Content.ReadAsStringAsync();
        string token = AntiforgeryTokenPattern().Match(html).Groups[1].Value;
"""),
    ("""            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["email"] = $"{username}@example.com",
                ["passphrase"] = Passphrase,
                ["confirmPassphrase"] = Passphrase,
            }),
""",
     """            Content = new FormUrlEncodedContent(FormFlow.WithTrapFields(new Dictionary<string, string>
            {
                ["username"] = username,
                ["email"] = $"{username}@example.com",
                ["passphrase"] = Passphrase,
                ["confirmPassphrase"] = Passphrase,
            }, FormFlow.CanaryFrom(html))),
"""),
])

rewrite('tests/nativeweb.devmarks.tests.integration/DevMarksFactory.cs', [
    ("""        Environment.SetEnvironmentVariable("DevMarks__BreachCheck__Hibp", "false");
""",
     """        Environment.SetEnvironmentVariable("DevMarks__BreachCheck__Hibp", "false");

        // Bot-trap MinAge would trip on machine-fast submits; the five trap
        // fields themselves are still posted for real by FormFlow.
        Environment.SetEnvironmentVariable("DevMarks__BotTraps__MinAgeSeconds", "0");
"""),
])

print('changed:', changed)
if len(changed) != 9:
    sys.exit(1)
