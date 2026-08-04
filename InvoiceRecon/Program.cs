using InvoiceRecon.Components;
using InvoiceRecon.Data;
using InvoiceRecon.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// A factory rather than a scoped DbContext: Blazor Server components outlive individual
// operations, so each unit of work gets its own short-lived context.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<ReconciliationService>();

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services, app.Logger);

var isE2E = app.Environment.IsEnvironment("E2E");

if (!app.Environment.IsDevelopment() && !isE2E)
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

if (!isE2E)
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
