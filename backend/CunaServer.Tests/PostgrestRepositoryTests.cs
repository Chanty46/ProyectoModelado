using System.Net;
using System.Text;
using System.Text.Json;
using CunaServer.Data;
using Microsoft.Extensions.Configuration;

namespace CunaServer.Tests;

public class PostgrestRepositoryTests
{
    private const string FakeSupabaseUrl = "https://postgrest-test.invalid";
    private const string FakeSecretKey = "sb_secret_unit_test_dummy_value";

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SUPABASE_URL"] = FakeSupabaseUrl,
                ["SUPABASE_SECRET_KEY"] = FakeSecretKey
            })
            .Build();

    [Fact]
    public async Task PostgrestFamily_ConfiguresAuthHeadersAndCreatesFamilyWithPrincipal()
    {
        var userId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
        var familyId = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");
        var profileId = Guid.Parse("cccccccc-3333-3333-3333-333333333333");
        var createdAt = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        var handler = new RecordingHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RelativePathAndQuery.StartsWith("usuarios?id=eq.", StringComparison.Ordinal))
            {
                return JsonResponse("[]");
            }

            if (req.Method == HttpMethod.Post && req.RelativePathAndQuery == "familias")
            {
                return JsonResponse(JsonSerializer.Serialize(new[]
                {
                    new { id = familyId, nombre = "Familia PostgREST", creado_en = createdAt }
                }));
            }

            if (req.Method == HttpMethod.Post && req.RelativePathAndQuery == "usuarios")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            if (req.Method == HttpMethod.Get && req.RelativePathAndQuery.StartsWith("perfiles_cuidador?family_id=eq.", StringComparison.Ordinal))
            {
                return JsonResponse("[]");
            }

            if (req.Method == HttpMethod.Post && req.RelativePathAndQuery == "perfiles_cuidador")
            {
                return JsonResponse(JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        id = profileId,
                        family_id = familyId,
                        nombre = "Mamá",
                        avatar = "M1",
                        activo = true,
                        eliminado_en = (DateTimeOffset?)null,
                        creado_en = createdAt
                    }
                }));
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RelativePathAndQuery}");
        });

        using var http = new HttpClient(handler);
        var repo = new PostgrestFamilyAccountRepository(http, BuildConfig());

        var family = await repo.CreateFamilyWithPrincipalAsync(
            userId,
            "mama@cuna.local",
            "Familia PostgREST",
            "Mamá",
            "M1",
            CancellationToken.None);

        Assert.Equal(familyId, family.Id);
        Assert.Equal("Familia PostgREST", family.Nombre);
        Assert.Equal(new Uri($"{FakeSupabaseUrl}/rest/v1/"), http.BaseAddress);

        Assert.All(handler.Requests, r =>
        {
            Assert.Equal(FakeSecretKey, r.ApiKeyHeader);
            Assert.Null(r.AuthorizationHeader);
        });

        var famPost = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.RelativePathAndQuery == "familias");
        Assert.Contains("return=representation", famPost.PreferHeaders);
        using (var famDoc = JsonDocument.Parse(famPost.Body!))
        {
            Assert.Equal("Familia PostgREST", famDoc.RootElement.GetProperty("nombre").GetString());
        }

        var usrPost = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post && r.RelativePathAndQuery == "usuarios");
        using var usrDoc = JsonDocument.Parse(usrPost.Body!);
        Assert.Equal(userId.ToString(), usrDoc.RootElement.GetProperty("id").GetString());
        Assert.Equal(familyId.ToString(), usrDoc.RootElement.GetProperty("family_id").GetString());
        Assert.Equal(UserRoles.Principal, usrDoc.RootElement.GetProperty("rol").GetString());
        Assert.Equal("mama@cuna.local", usrDoc.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task PostgrestFamily_RevokeSecondaryMember_SendsDeleteWithRepresentationAndReturnsMatch()
    {
        var familyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var memberId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var callCount = 0;

        var handler = new RecordingHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Delete, req.Method);
            Assert.Equal($"usuarios?id=eq.{memberId}&family_id=eq.{familyId}&rol=eq.secundario", req.RelativePathAndQuery);
            Assert.Contains("return=representation", req.PreferHeaders);

            callCount++;
            if (callCount == 1)
            {
                return JsonResponse(JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        id = memberId,
                        family_id = familyId,
                        rol = UserRoles.Secundario,
                        email = "sec@cuna.local",
                        creado_en = DateTimeOffset.UtcNow
                    }
                }));
            }

            return JsonResponse("[]");
        });

        using var http = new HttpClient(handler);
        var repo = new PostgrestFamilyAccountRepository(http, BuildConfig());

        Assert.True(await repo.RevokeSecondaryMemberAsync(familyId, memberId, CancellationToken.None));
        Assert.False(await repo.RevokeSecondaryMemberAsync(familyId, memberId, CancellationToken.None));
    }

    [Fact]
    public async Task PostgrestFamily_LogSecurityEventAndList_SerializesInfoJsonbContract()
    {
        var familyId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var userId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var ts = new DateTimeOffset(2026, 11, 14, 15, 30, 0, TimeSpan.Zero);

        var handler = new RecordingHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Post && req.RelativePathAndQuery == "bitacora_seguridad")
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            if (req.Method == HttpMethod.Get && req.RelativePathAndQuery.StartsWith("bitacora_seguridad?", StringComparison.Ordinal))
            {
                return JsonResponse(JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        family_id = familyId,
                        usuario_id = userId,
                        dispositivo_id = (Guid?)null,
                        evento = "acceso_denegado_rol",
                        ip_origen = "127.0.0.1",
                        detalle = new { info = "Intento de revocación por secundario" },
                        ts
                    }
                }));
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RelativePathAndQuery}");
        });

        using var http = new HttpClient(handler);
        var repo = new PostgrestFamilyAccountRepository(http, BuildConfig());

        await repo.LogSecurityEventAsync(
            new SecurityAuditEntry(familyId, userId, null, "acceso_denegado_rol", "127.0.0.1", "Intento de revocación por secundario", ts),
            CancellationToken.None);

        var post = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        using var bodyDoc = JsonDocument.Parse(post.Body!);
        Assert.Equal("Intento de revocación por secundario", bodyDoc.RootElement.GetProperty("detalle").GetProperty("info").GetString());

        var events = await repo.ListSecurityEventsAsync(familyId, CancellationToken.None);
        var item = Assert.Single(events);
        Assert.Equal("acceso_denegado_rol", item.Evento);
        Assert.Equal("Intento de revocación por secundario", item.Detalle);
    }

    [Fact]
    public async Task PostgrestFamily_CompleteTelegramLinkByCode_ClearsPreviousChatAndPatchesMatch()
    {
        var matchId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var userId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var now = new DateTimeOffset(2026, 11, 14, 16, 0, 0, TimeSpan.Zero);
        var candidateHash = CryptoTokenHelper.ComputeSha256Hex("TG9988");
        var otherHash = CryptoTokenHelper.ComputeSha256Hex("OTHER1");

        var handler = new RecordingHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RelativePathAndQuery == "telegram_vinculos?codigo_usado=is.false&intentos=lt.5")
            {
                return JsonResponse(JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        id = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                        usuario_id = Guid.NewGuid(),
                        chat_id = (long?)null,
                        hash_codigo = otherHash,
                        codigo_expira_en = now.AddMinutes(10),
                        codigo_usado = false,
                        intentos = 0,
                        vinculado_en = (DateTimeOffset?)null,
                        creado_en = now.AddMinutes(-2)
                    },
                    new
                    {
                        id = matchId,
                        usuario_id = userId,
                        chat_id = (long?)null,
                        hash_codigo = candidateHash,
                        codigo_expira_en = now.AddMinutes(10),
                        codigo_usado = false,
                        intentos = 0,
                        vinculado_en = (DateTimeOffset?)null,
                        creado_en = now.AddMinutes(-1)
                    }
                }));
            }

            if (req.Method == HttpMethod.Patch && req.RelativePathAndQuery == $"telegram_vinculos?chat_id=eq.987654321&id=neq.{matchId}")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (req.Method == HttpMethod.Patch && req.RelativePathAndQuery == $"telegram_vinculos?id=eq.{matchId}")
            {
                return JsonResponse(JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        id = matchId,
                        usuario_id = userId,
                        chat_id = (long?)987654321L,
                        hash_codigo = (string?)null,
                        codigo_expira_en = now.AddMinutes(10),
                        codigo_usado = true,
                        intentos = 0,
                        vinculado_en = now,
                        creado_en = now.AddMinutes(-1)
                    }
                }));
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RelativePathAndQuery}");
        });

        using var http = new HttpClient(handler);
        var repo = new PostgrestFamilyAccountRepository(http, BuildConfig());

        var linked = await repo.CompleteTelegramLinkByCodeAsync(candidateHash, 987654321L, now, CancellationToken.None);

        Assert.NotNull(linked);
        Assert.Equal(userId, linked.UsuarioId);
        Assert.Equal(987654321L, linked.ChatId);
        Assert.True(linked.CodigoUsado);
        Assert.Null(linked.HashCodigo);

        var clearPatch = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Patch && r.RelativePathAndQuery == $"telegram_vinculos?chat_id=eq.987654321&id=neq.{matchId}");
        using (var clearDoc = JsonDocument.Parse(clearPatch.Body!))
        {
            Assert.True(clearDoc.RootElement.TryGetProperty("chat_id", out var clearedChatProp));
            Assert.Equal(JsonValueKind.Null, clearedChatProp.ValueKind);
            Assert.True(clearDoc.RootElement.TryGetProperty("vinculado_en", out var clearedAtProp));
            Assert.Equal(JsonValueKind.Null, clearedAtProp.ValueKind);
        }

        var finalPatch = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Patch && r.RelativePathAndQuery == $"telegram_vinculos?id=eq.{matchId}");
        Assert.Contains("return=representation", finalPatch.PreferHeaders);
        using var patchDoc = JsonDocument.Parse(finalPatch.Body!);
        Assert.Equal(987654321L, patchDoc.RootElement.GetProperty("chat_id").GetInt64());
        Assert.True(patchDoc.RootElement.TryGetProperty("hash_codigo", out var hashProp));
        Assert.Equal(JsonValueKind.Null, hashProp.ValueKind);
        Assert.True(patchDoc.RootElement.GetProperty("codigo_usado").GetBoolean());
    }

    [Fact]
    public async Task PostgrestFamily_CompleteTelegramLinkByCode_IgnoresExpiredRows()
    {
        var now = new DateTimeOffset(2026, 11, 14, 16, 0, 0, TimeSpan.Zero);
        var candidateHash = CryptoTokenHelper.ComputeSha256Hex("EXPIRE1");

        var handler = new RecordingHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            return JsonResponse(JsonSerializer.Serialize(new[]
            {
                new
                {
                    id = Guid.NewGuid(),
                    usuario_id = Guid.NewGuid(),
                    chat_id = (long?)null,
                    hash_codigo = candidateHash,
                    codigo_expira_en = now.AddSeconds(-1),
                    codigo_usado = false,
                    intentos = 0,
                    vinculado_en = (DateTimeOffset?)null,
                    creado_en = now.AddMinutes(-20)
                }
            }));
        });

        using var http = new HttpClient(handler);
        var repo = new PostgrestFamilyAccountRepository(http, BuildConfig());

        var result = await repo.CompleteTelegramLinkByCodeAsync(candidateHash, 111222L, now, CancellationToken.None);

        Assert.Null(result);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PostgrestDevice_GetDeviceByDeviceId_EscapesIdAndResolvesCribFamily()
    {
        var deviceRowId = Guid.Parse("77777777-1111-1111-1111-111111111111");
        var cunaId = Guid.Parse("77777777-2222-2222-2222-222222222222");
        var familyId = Guid.Parse("77777777-3333-3333-3333-333333333333");
        const string deviceId = "pico central#1";
        var encoded = Uri.EscapeDataString(deviceId);

        var handler = new RecordingHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RelativePathAndQuery.StartsWith($"dispositivos?device_id=eq.{encoded}&", StringComparison.Ordinal))
            {
                return JsonResponse(JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        id = deviceRowId,
                        device_id = deviceId,
                        cuna_id = cunaId,
                        tipo = DeviceTypes.Central,
                        hash_token = "tok_hash_abc",
                        estado = "en_linea",
                        bateria_pct = 98,
                        ultimo_seq = 42L,
                        ultimo_heartbeat = (DateTimeOffset?)null,
                        fw_version = "1.0.0"
                    }
                }));
            }

            if (req.Method == HttpMethod.Get && req.RelativePathAndQuery.StartsWith($"cunas?id=eq.{cunaId}&", StringComparison.Ordinal))
            {
                return JsonResponse(JsonSerializer.Serialize(new[]
                {
                    new { id = cunaId, family_id = familyId, escala_masa = 0.25m }
                }));
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RelativePathAndQuery}");
        });

        using var http = new HttpClient(handler);
        var repo = new PostgrestDeviceTelemetryRepository(http, BuildConfig());

        var device = await repo.GetDeviceByDeviceIdAsync(deviceId, CancellationToken.None);

        Assert.NotNull(device);
        Assert.Equal(deviceRowId, device.Id);
        Assert.Equal(cunaId, device.CunaId);
        Assert.Equal(familyId, device.FamilyId);
        Assert.Equal(42L, device.UltimoSeq);
        Assert.Equal("tok_hash_abc", device.HashToken);
    }

    [Fact]
    public async Task PostgrestDevice_TryAdvanceSeq_RejectsNonPositiveAndChecksRowUpdated()
    {
        var deviceRowId = Guid.Parse("88888888-1111-1111-1111-111111111111");
        var callCount = 0;

        var handler = new RecordingHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Patch, req.Method);
            Assert.Contains("return=representation", req.PreferHeaders);
            callCount++;

            if (req.RelativePathAndQuery == $"dispositivos?id=eq.{deviceRowId}&ultimo_seq=lt.10")
            {
                return JsonResponse(JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        id = deviceRowId,
                        device_id = "pico-central-1",
                        cuna_id = (Guid?)Guid.NewGuid(),
                        tipo = DeviceTypes.Central,
                        hash_token = "h",
                        estado = "en_linea",
                        bateria_pct = (int?)null,
                        ultimo_seq = 10L,
                        ultimo_heartbeat = (DateTimeOffset?)null,
                        fw_version = "1.0.0"
                    }
                }));
            }

            return JsonResponse("[]");
        });

        using var http = new HttpClient(handler);
        var repo = new PostgrestDeviceTelemetryRepository(http, BuildConfig());

        Assert.False(await repo.TryAdvanceSeqAsync(deviceRowId, 0, CancellationToken.None));
        Assert.False(await repo.TryAdvanceSeqAsync(deviceRowId, -5, CancellationToken.None));
        Assert.Equal(0, callCount);

        Assert.True(await repo.TryAdvanceSeqAsync(deviceRowId, 10, CancellationToken.None));
        Assert.False(await repo.TryAdvanceSeqAsync(deviceRowId, 9, CancellationToken.None));
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task PostgrestDevice_SaveTelemetry_UsesIgnoreDuplicatesAndSnakeCasePayload()
    {
        var cunaId = Guid.Parse("99999999-1111-1111-1111-111111111111");
        var dispId = Guid.Parse("99999999-2222-2222-2222-222222222222");
        var ts = new DateTimeOffset(2026, 11, 14, 17, 0, 0, TimeSpan.Zero);

        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        using var http = new HttpClient(handler);
        var repo = new PostgrestDeviceTelemetryRepository(http, BuildConfig());

        await repo.SaveTelemetryAsync(
            new TelemetryRecord(cunaId, dispId, ts, 4.25m, 0.045m, 23.4m, 48.0m, 180.5m, 36.8m, 92, 77L),
            CancellationToken.None);

        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("telemetria", req.RelativePathAndQuery);
        Assert.Contains("resolution=ignore-duplicates", req.PreferHeaders);

        using var doc = JsonDocument.Parse(req.Body!);
        var root = doc.RootElement;
        Assert.Equal(cunaId.ToString(), root.GetProperty("cuna_id").GetString());
        Assert.Equal(dispId.ToString(), root.GetProperty("dispositivo_id").GetString());
        Assert.Equal(4.25m, root.GetProperty("masa_kg").GetDecimal());
        Assert.Equal(0.045m, root.GetProperty("indice_movimiento").GetDecimal());
        Assert.Equal(23.4m, root.GetProperty("temp_c").GetDecimal());
        Assert.Equal(48.0m, root.GetProperty("humedad").GetDecimal());
        Assert.Equal(180.5m, root.GetProperty("masa_biberon_g").GetDecimal());
        Assert.Equal(36.8m, root.GetProperty("temp_biberon_c").GetDecimal());
        Assert.Equal(92, root.GetProperty("bateria_pct").GetInt32());
        Assert.Equal(77L, root.GetProperty("seq").GetInt64());
    }

    [Fact]
    public async Task PostgrestDevice_UpdateCribConnectionState_CascadesOfflineToDevicesOnlyWhenDesconectada()
    {
        var cunaId = Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222");
        var familyId = Guid.Parse("bbbbbbbb-3333-3333-3333-333333333333");
        var now = new DateTimeOffset(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

        var handler = new RecordingHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Patch && req.RelativePathAndQuery == $"cunas?id=eq.{cunaId}")
            {
                using var bodyDoc = JsonDocument.Parse(req.Body!);
                var conn = bodyDoc.RootElement.GetProperty("estado_conexion").GetString()!;
                return JsonResponse(JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        id = cunaId,
                        family_id = familyId,
                        nombre = "Cuna Sala",
                        estado_conexion = conn,
                        modo_operacion = "monitoreo",
                        modo_atencion_expira_en = (DateTimeOffset?)null,
                        luz_encendida = false,
                        luz_nivel = 0,
                        escala_masa = 0.20m,
                        ultimo_heartbeat = now,
                        creado_en = now.AddDays(-1)
                    }
                }));
            }

            if (req.Method == HttpMethod.Patch && req.RelativePathAndQuery == $"dispositivos?cuna_id=eq.{cunaId}")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException($"Unexpected request: {req.Method} {req.RelativePathAndQuery}");
        });

        using var http = new HttpClient(handler);
        var repo = new PostgrestDeviceTelemetryRepository(http, BuildConfig());

        var online = await repo.UpdateCribConnectionStateAsync(cunaId, "en_linea", CancellationToken.None);
        Assert.NotNull(online);
        Assert.Equal("en_linea", online.EstadoConexion);
        Assert.Single(handler.Requests);

        var offline = await repo.UpdateCribConnectionStateAsync(cunaId, "desconectada", CancellationToken.None);
        Assert.NotNull(offline);
        Assert.Equal("desconectada", offline.EstadoConexion);
        Assert.Equal(3, handler.Requests.Count);

        var cascadePatch = handler.Requests[2];
        Assert.Equal($"dispositivos?cuna_id=eq.{cunaId}", cascadePatch.RelativePathAndQuery);
        using var cascadeDoc = JsonDocument.Parse(cascadePatch.Body!);
        Assert.Equal("desconectado", cascadeDoc.RootElement.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task PostgrestDevice_CompleteFeedingSession_SendsPatchAndMapsSnakeCaseResponse()
    {
        var sessionId = Guid.Parse("cccccccc-4444-4444-4444-444444444444");
        var bebeId = Guid.Parse("cccccccc-5555-5555-5555-555555555555");
        var start = new DateTimeOffset(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(12);

        var handler = new RecordingHttpMessageHandler(req =>
        {
            Assert.Equal(HttpMethod.Patch, req.Method);
            Assert.Equal($"sesiones_alimentacion?id=eq.{sessionId}", req.RelativePathAndQuery);
            Assert.Contains("return=representation", req.PreferHeaders);

            return JsonResponse(JsonSerializer.Serialize(new[]
            {
                new
                {
                    id = sessionId,
                    bebe_id = bebeId,
                    perfil_id = (Guid?)null,
                    m_inicial = 216.3m,
                    m_final = 113.3m,
                    ml = 100.0m,
                    temp_c = 36.8m,
                    alerta_temperatura = false,
                    inicio = start,
                    fin = end
                }
            }));
        });

        using var http = new HttpClient(handler);
        var repo = new PostgrestDeviceTelemetryRepository(http, BuildConfig());

        var completed = await repo.CompleteFeedingSessionAsync(sessionId, 113.3m, 100.0m, end, CancellationToken.None);

        Assert.NotNull(completed);
        Assert.Equal(113.3m, completed.MasaFinalG);
        Assert.Equal(100.0m, completed.MlConsumidos);
        Assert.Equal(end, completed.Fin);

        var req = Assert.Single(handler.Requests);
        using var doc = JsonDocument.Parse(req.Body!);
        Assert.Equal(113.3m, doc.RootElement.GetProperty("m_final").GetDecimal());
        Assert.Equal(100.0m, doc.RootElement.GetProperty("ml").GetDecimal());
    }

    [Fact]
    public async Task PostgrestDevice_LogSecurityEvent_SerializesInfoJsonbContract()
    {
        var deviceRowId = Guid.Parse("dddddddd-6666-6666-6666-666666666666");
        var ts = new DateTimeOffset(2026, 11, 14, 19, 0, 0, TimeSpan.Zero);

        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        using var http = new HttpClient(handler);
        var repo = new PostgrestDeviceTelemetryRepository(http, BuildConfig());

        await repo.LogSecurityEventAsync(
            new SecurityAuditEntry(null, null, deviceRowId, "replay_seq_detectado", "192.168.1.55", "seq=5 <= ultimo_seq=8", ts),
            CancellationToken.None);

        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("bitacora_seguridad", req.RelativePathAndQuery);
        using var doc = JsonDocument.Parse(req.Body!);
        Assert.Equal("replay_seq_detectado", doc.RootElement.GetProperty("evento").GetString());
        Assert.Equal("seq=5 <= ultimo_seq=8", doc.RootElement.GetProperty("detalle").GetProperty("info").GetString());
    }

    [Fact]
    public async Task DeviceRepo_GetAndUpdateCribThresholds_FilterFamilyAndSerializeSnakeCase()
    {
        var cunaId = Guid.Parse("eeeeeeee-1111-1111-1111-111111111111");
        var familyId = Guid.Parse("eeeeeeee-2222-2222-2222-222222222222");

        var handler = new RecordingHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RelativePathAndQuery.StartsWith("cunas?", StringComparison.Ordinal))
            {
                return JsonResponse($$"""
                    [{"id":"{{cunaId}}","family_id":"{{familyId}}","escala_masa":0.22}]
                    """);
            }
            if (req.Method == HttpMethod.Get && req.RelativePathAndQuery.StartsWith("umbrales?", StringComparison.Ordinal))
            {
                return JsonResponse("""[]""");
            }
            return JsonResponse("[]");
        });

        using var http = new HttpClient(handler);
        var repo = new PostgrestDeviceTelemetryRepository(http, BuildConfig());

        var cfg = await repo.GetCribConfigAsync(cunaId, familyId, CancellationToken.None);
        Assert.Equal(0.22m, cfg.EscalaMasa);
        Assert.Equal(18.0m, cfg.TempMin);
        Assert.Equal(26.0m, cfg.TempMax);

        var updated = await repo.UpdateCribThresholdsAsync(
            cunaId,
            familyId,
            new UpdateCribThresholdsRequest(19.5m, 25.5m, 42.0m, 58.0m, 0.120m, false, 0.012m, 0.25m, null),
            CancellationToken.None);

        Assert.Equal(19.5m, updated.TempMin);
        Assert.False(updated.MovimientoAlertaActiva);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Contains($"cunas?id=eq.{cunaId}&family_id=eq.{familyId}&select=id,family_id,escala_masa&limit=1", handler.Requests[0].RelativePathAndQuery);
        Assert.Equal(HttpMethod.Patch, handler.Requests[2].Method);
        Assert.Equal($"cunas?id=eq.{cunaId}&family_id=eq.{familyId}", handler.Requests[2].RelativePathAndQuery);
        Assert.Equal(HttpMethod.Patch, handler.Requests[3].Method);
        Assert.Equal($"umbrales?cuna_id=eq.{cunaId}", handler.Requests[3].RelativePathAndQuery);
        using var patchDoc = JsonDocument.Parse(handler.Requests[3].Body!);
        Assert.Equal(19.5m, patchDoc.RootElement.GetProperty("temp_min").GetDecimal());
        Assert.False(patchDoc.RootElement.GetProperty("movimiento_alerta_activa").GetBoolean());
    }

    [Fact]
    public async Task AccountRepo_ListLinkedTelegramChatsByFamilyAsync_HandlesEmptyAndMultipleDistinctMembers()
    {
        var familyEmpty = Guid.Parse("ffffffff-1111-1111-1111-111111111111");
        var familyFull = Guid.Parse("ffffffff-2222-2222-2222-222222222222");
        var userA = Guid.Parse("ffffffff-3333-3333-3333-333333333333");
        var userB = Guid.Parse("ffffffff-4444-4444-4444-444444444444");

        var handler = new RecordingHttpMessageHandler(req =>
        {
            if (req.RelativePathAndQuery.Contains(familyEmpty.ToString(), StringComparison.Ordinal))
            {
                return JsonResponse("[]");
            }
            if (req.RelativePathAndQuery.StartsWith("usuarios?", StringComparison.Ordinal))
            {
                return JsonResponse($$"""
                    [
                      {"id":"{{userA}}","family_id":"{{familyFull}}","email":"a@cuna.local","rol":"principal"},
                      {"id":"{{userB}}","family_id":"{{familyFull}}","email":"b@cuna.local","rol":"secundario"}
                    ]
                    """);
            }
            return JsonResponse("""
                [
                  {"chat_id":777001},
                  {"chat_id":null},
                  {"chat_id":777001},
                  {"chat_id":777002}
                ]
                """);
        });

        using var http = new HttpClient(handler);
        var repo = new PostgrestFamilyAccountRepository(http, BuildConfig());

        var emptyChats = await repo.ListLinkedTelegramChatsByFamilyAsync(familyEmpty, CancellationToken.None);
        Assert.Empty(emptyChats);
        Assert.Single(handler.Requests);

        var chats = await repo.ListLinkedTelegramChatsByFamilyAsync(familyFull, CancellationToken.None);
        Assert.Equal(new long[] { 777001, 777002 }, chats);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(
            $"telegram_vinculos?usuario_id=in.({userA},{userB})&chat_id=not.is.null&select=chat_id",
            handler.Requests[2].RelativePathAndQuery);
    }

    [Fact]
    public async Task DeviceRepo_LogCaregiverActionAsync_SerializesAuditPayload()
    {
        var familyId = Guid.Parse("12121212-1111-1111-1111-111111111111");
        var userId = Guid.Parse("12121212-2222-2222-2222-222222222222");
        var profileId = Guid.Parse("12121212-3333-3333-3333-333333333333");
        var ts = new DateTimeOffset(2026, 11, 14, 19, 35, 0, TimeSpan.Zero);

        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        using var http = new HttpClient(handler);
        var repo = new PostgrestDeviceTelemetryRepository(http, BuildConfig());

        await repo.LogCaregiverActionAsync(
            new CaregiverAuditLogEntry(familyId, userId, profileId, "exportar_historial_rf32", "{\"format\":\"pdf\"}", ts),
            CancellationToken.None);

        var req = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("bitacora", req.RelativePathAndQuery);
        using var doc = JsonDocument.Parse(req.Body!);
        Assert.Equal("exportar_historial_rf32", doc.RootElement.GetProperty("accion").GetString());
        Assert.Equal("pdf", doc.RootElement.GetProperty("detalle").GetProperty("format").GetString());
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed record RecordedRequest(
        HttpMethod Method,
        string RelativePathAndQuery,
        string? ApiKeyHeader,
        string? AuthorizationHeader,
        IReadOnlyList<string> PreferHeaders,
        string? Body);

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<RecordedRequest, HttpResponseMessage> _responder;
        public List<RecordedRequest> Requests { get; } = [];

        public RecordingHttpMessageHandler(Func<RecordedRequest, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var apiKey = request.Headers.TryGetValues("apikey", out var apiValues)
                ? apiValues.FirstOrDefault()
                : null;
            var auth = request.Headers.Authorization is not null
                ? $"{request.Headers.Authorization.Scheme} {request.Headers.Authorization.Parameter}"
                : null;
            var prefer = request.Headers.TryGetValues("Prefer", out var preferValues)
                ? preferValues.ToArray()
                : Array.Empty<string>();

            var pathAndQuery = request.RequestUri?.PathAndQuery ?? "";
            const string restPrefix = "/rest/v1/";
            if (pathAndQuery.StartsWith(restPrefix, StringComparison.Ordinal))
            {
                pathAndQuery = pathAndQuery[restPrefix.Length..];
            }

            var recorded = new RecordedRequest(
                request.Method,
                pathAndQuery,
                apiKey,
                auth,
                prefer,
                body);
            Requests.Add(recorded);

            return _responder(recorded);
        }
    }
}
