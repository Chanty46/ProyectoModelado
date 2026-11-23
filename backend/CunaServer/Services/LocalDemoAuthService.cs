using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CunaServer.Data;

namespace CunaServer.Services;

public sealed record AuthModeResponse(
    bool LocalLoginEnabled,
    bool SupabaseConfigured,
    string TelegramBotUsername);

public sealed record LocalLoginRequest(
    string Username,
    string Password);

public sealed record LocalLoginResponse(
    string AccessToken,
    Guid UserId,
    string Email,
    string Username,
    DateTimeOffset ExpiresAt);

public sealed record DemoSimulateRequest(string Action);

public sealed record DemoSimulateResponse(
    string Action,
    string Message,
    DateTimeOffset ExecutedAt);

/// <summary>
/// Autenticación local temporal y disparadores de eventos sin hardware para pruebas y presentación preliminar.
/// Se habilita automáticamente cuando no hay <c>SUPABASE_SECRET_KEY</c> (modo InMemory) o cuando
/// <c>LOCAL_DEMO_AUTH_ENABLED=true</c>.
/// </summary>
public sealed class LocalDemoAuthService
{
    public static readonly Guid DemoUserId = Guid.Parse("aaaaaaaa-0001-4000-8000-000000000001");
    public static readonly Guid DemoFamilyId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    public static readonly Guid DemoCribId = Guid.Parse("aaaaaaaa-c001-4000-8000-000000000001");
    public const string DemoEmail = "padre@cuna.local";
    public const int DefaultTokenLifetimeHours = 24;

    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(32);
    private readonly IFamilyAccountRepository _accountRepo;
    private readonly IDeviceTelemetryRepository _deviceRepo;
    private readonly CribLiveControlService _liveService;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<LocalDemoAuthService>? _logger;
    private readonly string _supabaseUrl;
    private readonly string _supabaseSecretKey;
    private readonly string _expectedUsername;
    private readonly string _expectedPasswordHash;
    private readonly string _botUsername;
    private readonly bool _supabaseConfigured;
    private readonly int _tokenLifetimeHours;
    private long _demoSeq = 9000;

    public bool IsEnabled { get; }

    public LocalDemoAuthService(
        IFamilyAccountRepository accountRepo,
        IDeviceTelemetryRepository deviceRepo,
        CribLiveControlService liveService,
        IConfiguration configuration,
        IHttpClientFactory? httpClientFactory = null,
        ILogger<LocalDemoAuthService>? logger = null)
    {
        _accountRepo = accountRepo;
        _deviceRepo = deviceRepo;
        _liveService = liveService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _supabaseUrl = (configuration["SUPABASE_URL"] ?? "").Trim().TrimEnd('/');
        _supabaseSecretKey = (configuration["SUPABASE_SECRET_KEY"] ?? "").Trim();
        _supabaseConfigured = !string.IsNullOrWhiteSpace(_supabaseSecretKey);

        var explicitFlag = configuration["LOCAL_DEMO_AUTH_ENABLED"];
        IsEnabled = string.Equals(explicitFlag, "true", StringComparison.OrdinalIgnoreCase) ||
                    (!string.Equals(explicitFlag, "false", StringComparison.OrdinalIgnoreCase) && !_supabaseConfigured);

        _expectedUsername = string.IsNullOrWhiteSpace(configuration["LOCAL_AUTH_USERNAME"])
            ? "Padre"
            : configuration["LOCAL_AUTH_USERNAME"]!.Trim();

        var configuredPass = configuration["LOCAL_AUTH_PASSWORD"];
        var rawPassword = string.IsNullOrEmpty(configuredPass) ? "123" : configuredPass;
        _expectedPasswordHash = CryptoTokenHelper.ComputeSha256Hex(rawPassword);

        var handle = (configuration["TELEGRAM_BOT_USERNAME"] ?? "CunaIntBot").Trim().TrimStart('@');
        _botUsername = $"@{handle}";
        _tokenLifetimeHours = int.TryParse(configuration["LOCAL_DEMO_TOKEN_HOURS"], out var hours)
            ? Math.Clamp(hours, 1, 168)
            : DefaultTokenLifetimeHours;
    }

    public AuthModeResponse GetAuthMode()
        => new(IsEnabled, _supabaseConfigured, _botUsername);

    public async Task<ServiceResult<LocalLoginResponse>> LoginAsync(
        LocalLoginRequest? request,
        string? clientIp,
        CancellationToken ct)
    {
        if (!IsEnabled)
        {
            return ServiceResult<LocalLoginResponse>.Forbidden(
                "El acceso local de pruebas está deshabilitado en este entorno.");
        }

        if (request is null ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrEmpty(request.Password))
        {
            return ServiceResult<LocalLoginResponse>.BadRequest("Ingresa usuario y contraseña.");
        }

        var candidateUser = request.Username.Trim();
        var userMatches = string.Equals(candidateUser, _expectedUsername, StringComparison.OrdinalIgnoreCase);
        var candidateHash = CryptoTokenHelper.ComputeSha256Hex(request.Password);
        var passMatches = CryptoTokenHelper.ConstantTimeHashEquals(_expectedPasswordHash, candidateHash);

        if (!userMatches || !passMatches)
        {
            await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
                null,
                null,
                null,
                "login_local_fallido",
                clientIp,
                $"usuario={candidateUser}",
                DateTimeOffset.UtcNow), ct);
            return new ServiceResult<LocalLoginResponse>(
                ServiceStatus.Unauthorized,
                default,
                "Usuario o contraseña incorrectos.");
        }

        await EnsureDemoWorkspaceSeededAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddHours(_tokenLifetimeHours);
        var token = IssueToken(DemoUserId, DemoEmail, expiresAt);

        await _accountRepo.LogSecurityEventAsync(new SecurityAuditEntry(
            DemoFamilyId,
            DemoUserId,
            null,
            "login_local_demo",
            clientIp,
            $"usuario={_expectedUsername}",
            now), ct);

        return ServiceResult<LocalLoginResponse>.Ok(
            new LocalLoginResponse(token, DemoUserId, DemoEmail, _expectedUsername, expiresAt));
    }

    public AuthenticatedIdentity? ValidateToken(string rawJwt)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(rawJwt))
        {
            return null;
        }

        var parts = rawJwt.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var headerJson = Encoding.UTF8.GetString(CryptoTokenHelper.Base64UrlDecode(parts[0]));
            using var headerDoc = JsonDocument.Parse(headerJson);
            var alg = headerDoc.RootElement.TryGetProperty("alg", out var algProp) ? algProp.GetString() : null;
            if (!string.Equals(alg, "HS256", StringComparison.Ordinal))
            {
                return null;
            }

            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var expectedSig = HMACSHA256.HashData(_signingKey, signingInput);
            var actualSig = CryptoTokenHelper.Base64UrlDecode(parts[2]);

            if (actualSig.Length != expectedSig.Length ||
                !CryptographicOperations.FixedTimeEquals(expectedSig, actualSig))
            {
                return null;
            }

            var payloadJson = Encoding.UTF8.GetString(CryptoTokenHelper.Base64UrlDecode(parts[1]));
            using var payloadDoc = JsonDocument.Parse(payloadJson);
            var root = payloadDoc.RootElement;

            if (!root.TryGetProperty("iss", out var issProp) ||
                !string.Equals(issProp.GetString(), "cuna-local-demo", StringComparison.Ordinal))
            {
                return null;
            }

            if (!root.TryGetProperty("exp", out var expProp) ||
                !expProp.TryGetInt64(out var exp) ||
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= exp)
            {
                return null;
            }

            if (!root.TryGetProperty("sub", out var subProp) ||
                !Guid.TryParse(subProp.GetString(), out var userId))
            {
                return null;
            }

            var email = root.TryGetProperty("email", out var emailProp)
                ? emailProp.GetString() ?? DemoEmail
                : DemoEmail;

            return new AuthenticatedIdentity(userId, email);
        }
        catch
        {
            return null;
        }
    }

    public async Task<ServiceResult<DemoSimulateResponse>> SimulateCribEventAsync(
        AuthenticatedIdentity identity,
        Guid cunaId,
        DemoSimulateRequest? request,
        string? clientIp,
        CancellationToken ct)
    {
        if (!IsEnabled)
        {
            return ServiceResult<DemoSimulateResponse>.Forbidden(
                "Los disparadores de demostración están deshabilitados.");
        }

        var (membership, crib, forbidden) = await FamilyAuthorizationHelper.RequireFamilyCribAccessAsync(
            _accountRepo,
            _deviceRepo,
            identity,
            cunaId,
            "simular_evento_presentacion",
            requirePrincipal: false,
            clientIp,
            ct);
        if (forbidden is not null || membership is null || crib is null)
        {
            return ServiceResult<DemoSimulateResponse>.Forbidden(forbidden ?? "Acceso denegado.");
        }

        var action = (request?.Action ?? "").Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var devices = await _deviceRepo.ListActiveDevicesForCribAsync(cunaId, ct);
        var central = devices.FirstOrDefault(d => d.Tipo == DeviceTypes.Central)
            ?? CreateVirtualDemoDevice(cunaId, membership.FamilyId, DeviceTypes.Central);
        var colchon = devices.FirstOrDefault(d => d.Tipo == DeviceTypes.Colchon)
            ?? CreateVirtualDemoDevice(cunaId, membership.FamilyId, DeviceTypes.Colchon);
        var biberon = devices.FirstOrDefault(d => d.Tipo == DeviceTypes.Biberon)
            ?? CreateVirtualDemoDevice(cunaId, membership.FamilyId, DeviceTypes.Biberon);
        var seq = Interlocked.Increment(ref _demoSeq);

        switch (action)
        {
            case "normal_telemetry":
                await SaveSimulatedTelemetryAsync(cunaId, central.Id, colchon.Id, biberon.Id,
                    masaKg: 0.660m, mov: 0.025m, tempC: 22.4m, hum: 49.0m, bottleMassG: 155.0m, bottleTempC: 36.8m, batteryPct: 92, seq, now, ct);
                return ServiceResult<DemoSimulateResponse>.Ok(new DemoSimulateResponse(
                    action, "Telemetría normal enviada (22.4 °C, 49 %, colchón 0.66 kg, biberón 36.8 °C).", now));

            case "removal_alarm":
                await SaveSimulatedTelemetryAsync(cunaId, central.Id, colchon.Id, biberon.Id,
                    masaKg: 0.040m, mov: 0.420m, tempC: 22.4m, hum: 49.0m, bottleMassG: 155.0m, bottleTempC: 36.8m, batteryPct: 92, seq, now, ct);
                await DispatchSimulatedEventAsync(colchon, "removal", "critica", "Retiro imprevisto simulado (sin hardware)",
                    JsonSerializer.Serialize(new { kind = "removal", simulated = true }), now, ct);
                return ServiceResult<DemoSimulateResponse>.Ok(new DemoSimulateResponse(
                    action, "Retiro súbito simulado: ventana de gracia de 5 s iniciada (pasará a ALARMA y notificará por Telegram si no activas Atención).", now));

            case "button_attention":
                await DispatchSimulatedEventAsync(central, "button_attention", "info", "Botón táctil central simulado (RF05)",
                    JsonSerializer.Serialize(new { kind = "button_attention", simulated = true }), now, ct);
                return ServiceResult<DemoSimulateResponse>.Ok(new DemoSimulateResponse(
                    action, "Botón físico de Modo Atención simulado en la Unidad Central.", now));

            case "env_alert":
                await SaveSimulatedTelemetryAsync(cunaId, central.Id, colchon.Id, biberon.Id,
                    masaKg: 0.660m, mov: 0.030m, tempC: 31.8m, hum: 74.0m, bottleMassG: 155.0m, bottleTempC: 36.8m, batteryPct: 92, seq, now, ct);
                await DispatchSimulatedEventAsync(central, "env_out_of_range", "advertencia", "Temperatura/humedad fuera de rango (simulación)",
                    JsonSerializer.Serialize(new { temp_c = 31.8m, hum_pct = 74.0m, simulated = true }), now, ct);
                return ServiceResult<DemoSimulateResponse>.Ok(new DemoSimulateResponse(
                    action, "Alerta ambiental enviada (31.8 °C · 74 % humedad) hacia web y Telegram.", now));

            case "movement_alert":
                await SaveSimulatedTelemetryAsync(cunaId, central.Id, colchon.Id, biberon.Id,
                    masaKg: 0.675m, mov: 0.390m, tempC: 22.5m, hum: 50.0m, bottleMassG: 155.0m, bottleTempC: 36.8m, batteryPct: 92, seq, now, ct);
                await DispatchSimulatedEventAsync(colchon, "movement_high", "advertencia", "Movimiento anómalo > 30 s (simulación)",
                    JsonSerializer.Serialize(new { movement_index = 0.390m, simulated = true }), now, ct);
                return ServiceResult<DemoSimulateResponse>.Ok(new DemoSimulateResponse(
                    action, "Alerta de movimiento elevado (0.390 kg/min) enviada.", now));

            case "bottle_low_battery":
                await SaveSimulatedTelemetryAsync(cunaId, central.Id, colchon.Id, biberon.Id,
                    masaKg: 0.660m, mov: 0.025m, tempC: 22.4m, hum: 49.0m, bottleMassG: 140.0m, bottleTempC: 36.7m, batteryPct: 8, seq, now, ct);
                await DispatchSimulatedEventAsync(biberon, "battery_low", "advertencia", "Batería baja en base de biberón (8 %)",
                    JsonSerializer.Serialize(new { battery_pct = 8, simulated = true }), now, ct);
                return ServiceResult<DemoSimulateResponse>.Ok(new DemoSimulateResponse(
                    action, "Alerta de batería baja (8 %) enviada hacia web y Telegram.", now));

            case "toy_game":
                await _deviceRepo.SaveToyUsageAsync(new ToyUsageRecord(
                    Guid.NewGuid(),
                    cunaId,
                    "botones",
                    null,
                    now,
                    24,
                    7,
                    null,
                    null), ct);
                return ServiceResult<DemoSimulateResponse>.Ok(new DemoSimulateResponse(
                    action, "Partida del juguete de 5 botones registrada (7 aciertos en 24 s).", now));

            case "reset_normal":
                await _deviceRepo.UpdateCribModeAsync(cunaId, "monitoreo", null, ct);
                await SaveSimulatedTelemetryAsync(cunaId, central.Id, colchon.Id, biberon.Id,
                    masaKg: 0.660m, mov: 0.020m, tempC: 22.4m, hum: 49.0m, bottleMassG: 155.0m, bottleTempC: 36.8m, batteryPct: 95, seq, now, ct);
                return ServiceResult<DemoSimulateResponse>.Ok(new DemoSimulateResponse(
                    action, "Cuna restaurada a Modo Monitoreo (Verde) con lecturas normales.", now));

            default:
                return ServiceResult<DemoSimulateResponse>.BadRequest(
                    "Acción de simulación no reconocida.");
        }
    }

    private async Task DispatchSimulatedEventAsync(
        RegisteredDevice device,
        string kind,
        string severity,
        string msg,
        string datosJson,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var alertRecord = new EventAlertRecord(
            Guid.NewGuid(),
            device.CunaId,
            device.Id,
            kind,
            severity,
            msg,
            datosJson,
            now);

        var shouldSave = await _liveService.ProcessIncomingDeviceEventAsync(device, alertRecord, ct);
        if (shouldSave)
        {
            await _deviceRepo.SaveEventAlertAsync(alertRecord, ct);
        }
    }

    private async Task SaveSimulatedTelemetryAsync(
        Guid cunaId,
        Guid centralId,
        Guid colchonId,
        Guid biberonId,
        decimal masaKg,
        decimal mov,
        decimal tempC,
        decimal hum,
        decimal bottleMassG,
        decimal bottleTempC,
        int batteryPct,
        long seq,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await _deviceRepo.SaveTelemetryAsync(new TelemetryRecord(
            cunaId, centralId, now, null, null, tempC, hum, null, null, null, seq), ct);
        await _deviceRepo.SaveTelemetryAsync(new TelemetryRecord(
            cunaId, colchonId, now.AddMilliseconds(50), masaKg, mov, null, null, null, null, null, seq), ct);
        await _deviceRepo.SaveTelemetryAsync(new TelemetryRecord(
            cunaId, biberonId, now.AddMilliseconds(100), null, null, null, null, bottleMassG, bottleTempC, batteryPct, seq), ct);
    }

    private static RegisteredDevice CreateVirtualDemoDevice(Guid cunaId, Guid familyId, string tipo)
        => new(Guid.Empty, $"sim-demo-{tipo}", cunaId, familyId, tipo, "", "en_linea", null, 0, DateTimeOffset.UtcNow, "demo");

    private async Task EnsureSupabaseAuthDemoUserAsync(CancellationToken ct)
    {
        if (!_supabaseConfigured ||
            string.IsNullOrWhiteSpace(_supabaseUrl) ||
            _httpClientFactory is null)
        {
            return;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient("SupabaseAuthAdmin");
            client.BaseAddress = new Uri($"{_supabaseUrl}/auth/v1/");
            client.DefaultRequestHeaders.Add("apikey", _supabaseSecretKey);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _supabaseSecretKey);

            using var checkRes = await client.GetAsync($"admin/users/{DemoUserId}", ct);
            if (checkRes.IsSuccessStatusCode)
            {
                return;
            }

            var randomAdminPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            using var createRes = await client.PostAsJsonAsync(
                "admin/users",
                new
                {
                    id = DemoUserId,
                    email = DemoEmail,
                    password = randomAdminPassword,
                    email_confirm = true,
                    user_metadata = new { full_name = _expectedUsername, demo = true }
                },
                ct);

            if (!createRes.IsSuccessStatusCode &&
                createRes.StatusCode != System.Net.HttpStatusCode.UnprocessableEntity)
            {
                var body = await createRes.Content.ReadAsStringAsync(ct);
                _logger?.LogWarning("No se pudo crear usuario demo en Supabase Auth Admin ({Status}): {Body}",
                    createRes.StatusCode, body);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Error verificando usuario demo en Supabase Auth Admin.");
        }
    }

    private async Task EnsureDemoWorkspaceSeededAsync(CancellationToken ct)
    {
        Guid activeFamilyId;
        Guid? activeCribId = null;

        if (_accountRepo is InMemoryFamilyAccountRepository inMemAccount)
        {
            var fam = inMemAccount.EnsureSeededPrincipalFamily(
                DemoUserId,
                DemoFamilyId,
                DemoEmail,
                "Familia Demo",
                _expectedUsername,
                "P1");
            activeFamilyId = fam.Id;
        }
        else
        {
            var existingMembership = await _accountRepo.GetUserMembershipAsync(DemoUserId, ct);
            if (existingMembership is null)
            {
                await EnsureSupabaseAuthDemoUserAsync(ct);
                var createdFamily = await _accountRepo.CreateFamilyWithPrincipalAsync(
                    DemoUserId,
                    DemoEmail,
                    "Familia Demo",
                    _expectedUsername,
                    "P1",
                    ct);
                activeFamilyId = createdFamily.Id;
            }
            else
            {
                activeFamilyId = existingMembership.FamilyId;
            }

            var existingCribs = await _deviceRepo.ListCribsByFamilyAsync(activeFamilyId, ct);
            if (existingCribs.Count == 0)
            {
                var createdCrib = await _deviceRepo.CreateCribWithDefaultsAsync(activeFamilyId, "Cuna Habitación A", ct);
                activeCribId = createdCrib.Id;
            }
            else
            {
                activeCribId = existingCribs[0].Id;
            }
        }

        if (_deviceRepo is InMemoryDeviceTelemetryRepository)
        {
            activeCribId ??= DemoCribId;
        }

        if (!activeCribId.HasValue)
        {
            return;
        }

        var targetCribId = activeCribId.Value;
        var existingBabies = await _deviceRepo.ListBabiesByFamilyAsync(activeFamilyId, ct);
        var baby = existingBabies.FirstOrDefault();
        if (baby is null)
        {
            baby = await _deviceRepo.CreateBabyAsync(
                activeFamilyId,
                targetCribId,
                "Sofía (Demo)",
                new DateOnly(2026, 6, 10),
                "Perfil de prueba para presentación preliminar",
                ct);

            var feedStart = DateTimeOffset.UtcNow.AddHours(-2);
            var feed = await _deviceRepo.CreateFeedingSessionAsync(
                baby.Id,
                null,
                180.0m,
                36.8m,
                false,
                feedStart,
                ct);
            await _deviceRepo.CompleteFeedingSessionAsync(
                feed.Id,
                80.0m,
                FeedingHistoryAndToysService.ComputeVolumeMl(180.0m, 80.0m),
                feedStart.AddMinutes(14),
                ct);

            var sleepEnd = DateTimeOffset.UtcNow.AddMinutes(-35);
            await _deviceRepo.CreateSleepSessionAsync(
                baby.Id,
                targetCribId,
                "siesta",
                sleepEnd.AddMinutes(-50),
                sleepEnd,
                0.032m,
                0,
                ct);
        }

        var existingTelemetry = await _deviceRepo.ListTelemetryForCribAsync(targetCribId, ct);
        if (existingTelemetry.Count == 0)
        {
            var now = DateTimeOffset.UtcNow;
            await _deviceRepo.SaveTelemetryAsync(new TelemetryRecord(
                targetCribId,
                Guid.Empty,
                now.AddSeconds(-10),
                0.660m,
                0.024m,
                22.4m,
                49.0m,
                150.0m,
                36.8m,
                92,
                1), ct);
        }
    }

    private string IssueToken(Guid userId, string email, DateTimeOffset expiresAt)
    {
        var headerJson = JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" });
        var payloadJson = JsonSerializer.Serialize(new
        {
            iss = "cuna-local-demo",
            sub = userId.ToString(),
            email,
            exp = expiresAt.ToUnixTimeSeconds()
        });

        var headerB64 = CryptoTokenHelper.Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadB64 = CryptoTokenHelper.Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = $"{headerB64}.{payloadB64}";
        var sig = HMACSHA256.HashData(_signingKey, Encoding.ASCII.GetBytes(signingInput));
        return $"{signingInput}.{CryptoTokenHelper.Base64UrlEncode(sig)}";
    }
}
