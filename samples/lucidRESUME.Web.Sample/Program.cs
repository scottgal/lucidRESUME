using lucidRESUME.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLucidResumeCompiler(builder.Configuration);

var app = builder.Build();
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapGet("/", () => Results.Redirect("/lucidresume/"));
app.MapLucidResumeCompiler();
app.Run();

public partial class Program;
