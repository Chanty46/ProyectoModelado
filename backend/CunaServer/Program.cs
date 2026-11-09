using CunaServer.Api;
using CunaServer.Data;
using CunaServer.Services;

LoadDotEnvIfExists();

var builder = WebApplication.CreateBuilder(args);

var apiUrl = builder.Configuration["API_URL"] ?? "http://localhost:5080";
builder.WebHost.UseUrls(apiUrl);

var allowedOrigin = builder.Configuration["ALLOWED_WEB_ORIGIN"] ?? "http://localhost:5173";
builder.Services.AddCors(options =>
{
    options.AddPolicy("WebFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton<IJwtTokenValidator>(sp =>
{
    var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
    var config = sp.GetRequiredService<IConfiguration>();
    return new SupabaseJwksTokenValidator(httpFactory.CreateClient("JwksClient"), config);
});

var supabaseSecretKey = builder.Configuration["SUPABASE_SECRET_KEY"];
if (!string.IsNullOrWhiteSpace(supabaseSecretKey))
{
    builder.Services.AddSingleton<IFamilyAccountRepository>(sp =>
    {
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var config = sp.GetRequiredService<IConfiguration>();
        return new PostgrestFamilyAccountRepository(httpFactory.CreateClient("PostgrestClient"), config);
    });
}
else
{
    builder.Services.AddSingleton<IFamilyAccountRepository, InMemoryFamilyAccountRepository>();
}

builder.Services.AddSingleton<FamilyAccountService>();

var app = builder.Build();

app.UseCors("WebFrontend");

app.MapHealthEndpoints();
app.MapAccountAndFamilyEndpoints();

await app.RunAsync();

static void LoadDotEnvIfExists()
{
    var candidatePaths = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), ".env"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", ".env")
    };

    foreach (var path in candidatePaths)
    {
        if (!File.Exists(path))
        {
            continue;
        }

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        break;
    }
}

public partial class Program;
