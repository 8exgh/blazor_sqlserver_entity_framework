using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace InvoiceRecon.E2E;

/// <summary>
/// Drives the running app through a real browser. Requires the app (and its SQL Server) to be
/// up; point E2E_BASE_URL at it. When the variable is not set the tests are skipped, so a plain
/// `dotnet test` on the solution still needs nothing but the unit-test SQLite setup.
/// </summary>
[TestFixture]
public class ReconcileE2ETests : PageTest
{
    private static readonly string? BaseUrl =
        Environment.GetEnvironmentVariable("E2E_BASE_URL");

    private ILocator InvoiceRows => Page.Locator("[data-testid='invoices-table'] tbody tr");
    private ILocator Message => Page.GetByTestId("message");

    private ILocator Stat(string name) =>
        Page.GetByTestId($"stat-{name}").Locator(".stat-value");

    [SetUp]
    public async Task OpenPageAndResetState()
    {
        if (BaseUrl is null)
        {
            Assert.Ignore("E2E_BASE_URL is not set; skipping browser tests.");
        }

        SetDefaultExpectTimeout(10_000);

        await Page.GotoAsync(BaseUrl!);
        await Expect(InvoiceRows).ToHaveCountAsync(10);

        // The page is prerendered before the Blazor Server circuit connects, and clicks made in
        // that window are silently dropped. Reset until the click demonstrably lands; this also
        // gives every test the same starting state.
        for (var attempt = 0; ; attempt++)
        {
            await Page.GetByTestId("reset-all").ClickAsync();
            try
            {
                await Expect(Message).ToHaveTextAsync("All matches cleared.",
                    new LocatorAssertionsToHaveTextOptions { Timeout = 2_000 });
                break;
            }
            catch (PlaywrightException) when (attempt < 9)
            {
            }
        }

        await Expect(Stat("unmatched")).ToHaveTextAsync("10");
    }

    [Test]
    public async Task PageShowsSeededInvoicesAndPayments()
    {
        await Expect(Page.Locator(".app-header h1")).ToHaveTextAsync("Invoice Reconciliation");
        await Expect(InvoiceRows).ToHaveCountAsync(10);
        await Expect(Page.Locator("[data-testid='payments-table'] tbody tr")).ToHaveCountAsync(10);
        await Expect(Stat("matched")).ToHaveTextAsync("0");
        await Expect(Stat("discrepancy")).ToHaveTextAsync("0");
        await Expect(Page.GetByTestId("progress-pct")).ToHaveTextAsync("0%");
    }

    [Test]
    public async Task AutoMatchProducesDocumentedOutcome()
    {
        await Page.GetByTestId("run-auto-match").ClickAsync();

        await Expect(Message).ToHaveTextAsync(
            "Matched 7: 4 on reference + amount, 2 on reference only, 1 on amount only.");
        await Expect(Stat("matched")).ToHaveTextAsync("5");
        await Expect(Stat("discrepancy")).ToHaveTextAsync("2");
        await Expect(Stat("unmatched")).ToHaveTextAsync("3");
        await Expect(Page.GetByTestId("progress-pct")).ToHaveTextAsync("70%");
        await Expect(Page.Locator(".badge.discrepancy")).ToHaveCountAsync(2);
    }

    [Test]
    public async Task AutoMatchIsIdempotent()
    {
        await Page.GetByTestId("run-auto-match").ClickAsync();
        await Expect(Stat("matched")).ToHaveTextAsync("5");

        await Page.GetByTestId("run-auto-match").ClickAsync();
        await Expect(Message).ToHaveTextAsync("No new matches found.");
        await Expect(Stat("matched")).ToHaveTextAsync("5");
    }

    [Test]
    public async Task ManualMatchThenUnmatch()
    {
        var firstRow = InvoiceRows.First;
        await Expect(firstRow.Locator(".badge")).ToHaveTextAsync("Unmatched");

        // Pick the first real payment in the dropdown (index 0 is the placeholder).
        var select = firstRow.Locator("select");
        var value = await select.Locator("option").Nth(1).GetAttributeAsync("value");
        await select.SelectOptionAsync(value!);
        await firstRow.GetByRole(AriaRole.Button, new() { Name = "Match" }).ClickAsync();

        await Expect(Message).ToHaveTextAsync("Payment applied.");
        await Expect(firstRow.Locator(".badge")).ToHaveTextAsync("Matched");
        await Expect(firstRow.Locator(".matched-payment .kind")).ToHaveTextAsync("Manual");

        await firstRow.GetByRole(AriaRole.Button, new() { Name = "Unmatch" }).ClickAsync();
        await Expect(Message).ToHaveTextAsync("Match removed.");
        await Expect(firstRow.Locator(".badge")).ToHaveTextAsync("Unmatched");
    }

    [Test]
    public async Task ResetClearsAutoMatches()
    {
        await Page.GetByTestId("run-auto-match").ClickAsync();
        await Expect(Stat("matched")).ToHaveTextAsync("5");

        await Page.GetByTestId("reset-all").ClickAsync();
        await Expect(Message).ToHaveTextAsync("All matches cleared.");
        await Expect(Stat("unmatched")).ToHaveTextAsync("10");
        await Expect(Page.Locator("[data-testid='payments-table'] tbody tr")).ToHaveCountAsync(10);
    }

    [Test]
    public async Task DiscrepancyRowsShowSignedDeltas()
    {
        await Page.GetByTestId("run-auto-match").ClickAsync();
        await Expect(Stat("discrepancy")).ToHaveTextAsync("2");

        await Expect(Page.Locator("td.delta.negative")).ToHaveTextAsync(new Regex(@"^-"));
        await Expect(Page.Locator("td.delta.positive")).ToHaveTextAsync(new Regex(@"^\+"));
    }
}
