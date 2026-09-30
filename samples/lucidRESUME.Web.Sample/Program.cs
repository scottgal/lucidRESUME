using lucidRESUME.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLucidResumeCompiler(builder.Configuration);
builder.Services.Configure<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>(options =>
    options.Cookie.Name = "lucidresume.csrf");

var app = builder.Build();
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapGet("/", () => Results.Redirect("/resume/"));
app.MapLucidResumeCompiler();
app.Run();

public partial class Program;
