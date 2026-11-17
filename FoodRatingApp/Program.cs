using FoodRatingApp.Middleware;
using FoodRatingApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMvc();
builder.Services.AddHttpClient();
builder.Services.ConfigureHttpClientDefaults(http => http.ConfigureHttpClient(client =>
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Fsa:TimeoutSeconds", 30))));
builder.Services.AddSingleton<IFsaClient, FsaClient>();

var app = builder.Build();

// First in the pipeline so it catches exceptions from everything after it, in every environment:
// clients always get a ProblemDetails body, never a stack trace.
app.UseMiddleware<GlobalExceptionMiddleware>();

if (!builder.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

app.Run();

public partial class Program { }
