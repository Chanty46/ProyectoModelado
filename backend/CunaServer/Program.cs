using CunaServer.Api;
using CunaServer.Data;
using CunaServer.Services;
using CunaServer.Tcp;
using CunaServer.Telegram;

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
    builder.Services.AddSingleton<IDeviceTelemetryRepository>(sp =>
    {
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var config = sp.GetRequiredService<IConfiguration>();
        return new PostgrestDeviceTelemetryRepository(httpFactory.CreateClient("PostgrestTelemetryClient"), config);
    });
}
else
{
    builder.Services.AddSingleton<IFamilyAccountRepository, InMemoryFamilyAccountRepository>();
    builder.Services.AddSingleton<IDeviceTelemetryRepository, InMemoryDeviceTelemetryRepository>();
}

var telegramBotToken = builder.Configuration["TELEGRAM_BOT_TOKEN"];
if (!string.IsNullOrWhiteSpace(telegramBotToken))
{
    builder.Services.AddSingleton<ITelegramBotTransport>(sp =>
    {
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var logger = sp.GetRequiredService<ILogger<HttpTelegramBotTransport>>();
        return new HttpTelegramBotTransport(httpFactory.CreateClient("TelegramBotClient"), telegramBotToken, logger);
    });
}
else
{
    builder.Services.AddSingleton<ITelegramBotTransport, InMemoryTelegramBotTransport>();
}

builder.Services.AddSingleton<FamilyAccountService>();
builder.Services.AddSingleton<DeviceTcpServer>();
builder.Services.AddSingleton<IntercomAudioService>();
builder.Services.AddSingleton<PairingAndCribService>();
builder.Services.AddSingleton<TelegramBotService>();
builder.Services.AddSingleton<CribLiveControlService>();
builder.Services.AddSingleton<FeedingHistoryAndToysService>();
builder.Services.AddHostedService<CribWatchdogBackgroundService>();
builder.Services.AddHostedService<TelegramPollingAndRemindersBackgroundService>();
if (!string.Equals(builder.Configuration["TCP_SERVER_ENABLED"], "false", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService(sp => sp.GetRequiredService<DeviceTcpServer>());
}

var app = builder.Build();

if (string.IsNullOrWhiteSpace(telegramBotToken))
{
    app.Logger.LogWarning(
        "Telegram: TELEGRAM_BOT_TOKEN no configurado (ni en entorno ni en backend/.env). Bot activo solo en modo InMemory (no responderá mensajes reales en Telegram).");
}
else
{
    app.Logger.LogInformation(
        "Telegram: transporte HTTP Bot API habilitado para @{BotHandle}.",
        (app.Configuration["TELEGRAM_BOT_USERNAME"] ?? "CunasInteligentes_Modelado_Bot").Trim().TrimStart('@'));
}

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error no controlado procesando la petición HTTP {Path}", context.Request.Path);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new { error = "Ocurrió un error interno en el servidor." });
        }
    }
});

// Asegurar que el interceptor de eventos TCP de CribLiveControlService quede registrado al iniciar
_ = app.Services.GetRequiredService<CribLiveControlService>();

app.UseCors("WebFrontend");

app.MapHealthEndpoints();
app.MapAccountAndFamilyEndpoints();
app.MapIntercomEndpoints();
app.MapPairingAndCribEndpoints();
app.MapCribLiveControlEndpoints();
app.MapTelegramAndRemindersEndpoints();
app.MapFeedingHistoryAndToysEndpoints();

await app.RunAsync();

static void LoadDotEnvIfExists()
{
    var rawCandidates = new List<string>
    {
        Path.Combine(Directory.GetCurrentDirectory(), "backend", ".env"),
        Path.Combine(Directory.GetCurrentDirectory(), ".env"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "backend", ".env"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", ".env")
    };

    var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
    for (var depth = 0; depth < 6 && baseDir is not null; depth++, baseDir = baseDir.Parent)
    {
        rawCandidates.Add(Path.Combine(baseDir.FullName, "backend", ".env"));
        rawCandidates.Add(Path.Combine(baseDir.FullName, ".env"));
    }

    foreach (var candidate in rawCandidates.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal))
    {
        if (!File.Exists(candidate))
        {
            continue;
        }

        foreach (var rawLine in File.ReadAllLines(candidate))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1].Trim();
            }

            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        break;
    }
}

public partial class Program;
