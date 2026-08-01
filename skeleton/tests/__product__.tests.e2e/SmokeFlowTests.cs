using System.Text;

using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

using Xunit;

namespace __PRODUCT__.Tests.E2e;

// The stamped product's starting browser coverage: a real Chromium against the
// real published binary, plus the R18 accessibility gate on the two pages the
// skeleton ships. Every page the product adds to this suite gets scanned the
// same way.
[Collection(E2eFixture.Name)]
public class SmokeFlowTests(__PRODUCT__ProcessFixture app, BrowserFixture browser)
{
    [Fact]
    public async Task HomePage_LoadsAndShowsItsHeading()
    {
        IPage page = await browser.Browser.NewPageAsync(new() { IgnoreHTTPSErrors = true });
        try
        {
            await page.GotoAsync(app.BaseUrl);

            ILocator heading = page.Locator("h1");
            await heading.WaitForAsync();
            Assert.True(await heading.IsVisibleAsync());
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    // /about is the V8 static page (spec 003 R38); / is the rendered page.
    [Fact]
    public async Task SkeletonPages_PassAxe()
    {
        IPage page = await browser.Browser.NewPageAsync(new() { IgnoreHTTPSErrors = true });
        try
        {
            foreach (string path in new[] { "/", "/about" })
            {
                await AssertNoViolationsAsync(page, path);
            }
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    // Without a path, scans wherever the page already is (post-click states).
    private async Task AssertNoViolationsAsync(IPage page, string? path = null)
    {
        if (path is not null)
        {
            // GotoAsync does not throw on an error status, so a deleted or
            // renamed page would silently scan the (accessible) error shell
            // and keep the gate green -- assert the page actually loaded.
            IResponse? response = await page.GotoAsync($"{app.BaseUrl}{path}");
            Assert.Equal(200, response?.Status);
        }

        string scanned = path ?? new Uri(page.Url).PathAndQuery;
        AxeResult result = await page.RunAxe();
        if (result.Violations.Length == 0)
        {
            return;
        }

        var detail = new StringBuilder($"axe violations on {scanned}:");
        foreach (AxeResultItem violation in result.Violations)
        {
            detail.Append($"\n  [{violation.Impact}] {violation.Id}: {violation.Help}");
            foreach (AxeResultNode node in violation.Nodes)
            {
                detail.Append($"\n    {node.Target} {node.Html}");
            }
        }

        Assert.Fail(detail.ToString());
    }
}
