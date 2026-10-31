using System.Globalization;
using Microsoft.AspNetCore.Localization;
using NetReporter.Designer.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<TemplateStore>();
builder.Services.AddSingleton<SampleStore>();

var app = builder.Build();

// UI language: ?culture=en → cookie (set by /Home/SetLanguage) → Accept-Language → Spanish (the neutral resources).
// Only the UI culture varies: the request culture stays invariant so form numbers ("12.5") and dates parse and
// print the same way whatever the user's language.
var uiCultures = new[] { new CultureInfo("es"), new CultureInfo("en") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture, new CultureInfo("es")),
    SupportedCultures = [CultureInfo.InvariantCulture],
    SupportedUICultures = uiCultures,
    FallBackToParentUICultures = true,
});

app.UseRouting();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
