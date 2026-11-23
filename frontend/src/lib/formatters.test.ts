import test from "node:test";
import assert from "node:assert/strict";
import {
  formatRemainingCountdown,
  resolveRgbLedStatus,
  canManagePrincipalActions,
  buildTelegramDeepLink,
  downsampleFloatToPcm8,
  evaluateComfortBand,
  estimateMilkMl,
  computeMobileLedFrame,
  parseOptionalFiniteNumber,
} from "./formatters.ts";
import { buildMobileOnPayload, isCribOnline, parseMobileDuration } from "./mobileCommands.ts";

test("formatRemainingCountdown formats MM:SS cleanly and returns null when expired", () => {
  const now = Date.parse("2026-11-14T12:00:00Z");
  assert.equal(formatRemainingCountdown("2026-11-14T12:04:05Z", now), "04:05");
  assert.equal(formatRemainingCountdown("2026-11-14T12:00:00Z", now), null);
  assert.equal(formatRemainingCountdown("2026-11-14T11:59:00Z", now), null);
  assert.equal(formatRemainingCountdown(null, now), null);
  assert.equal(formatRemainingCountdown("invalid-date", now), null);
});

test("resolveRgbLedStatus maps physical LED colors for every crib mode", () => {
  const alarma = resolveRgbLedStatus("alarma");
  assert.match(alarma.label, /Rojo/);
  assert.match(alarma.dotClass, /bg-red-500/);

  const atencion = resolveRgbLedStatus("atencion");
  assert.match(atencion.label, /Amarillo/);
  assert.match(atencion.dotClass, /bg-amber-400/);

  const monitoreo = resolveRgbLedStatus("monitoreo");
  assert.match(monitoreo.label, /Verde/);
  assert.match(monitoreo.dotClass, /bg-emerald-500/);
});

test("canManagePrincipalActions allows only principal role", () => {
  assert.equal(canManagePrincipalActions("principal"), true);
  assert.equal(canManagePrincipalActions("secundario"), false);
  assert.equal(canManagePrincipalActions(null), false);
});

test("buildTelegramDeepLink strips leading @ and encodes verification code", () => {
  assert.equal(
    buildTelegramDeepLink("@CunasInteligentes_Modelado_Bot", " AB12CD "),
    "https://t.me/CunasInteligentes_Modelado_Bot?start=AB12CD"
  );
  assert.equal(
    buildTelegramDeepLink("CunasInteligentes_Modelado_Bot", "XYZ999"),
    "https://t.me/CunasInteligentes_Modelado_Bot?start=XYZ999"
  );
});

test("downsampleFloatToPcm8 downsamples audio safely to unsigned 8-bit PCM", () => {
  assert.equal(downsampleFloatToPcm8(new Float32Array(0), 48000).length, 0);

  const silence = new Float32Array(4800); // 100 ms @ 48 kHz -> ~800 samples @ 8 kHz
  const pcmSilence = downsampleFloatToPcm8(silence, 48000, 8000, 1200);
  assert.equal(pcmSilence.length, 800);
  assert.equal(pcmSilence[0], 128);

  const clipHigh = new Float32Array([2.5, 2.5, 2.5, 2.5, 2.5, 2.5]);
  const pcmHigh = downsampleFloatToPcm8(clipHigh, 48000, 8000, 1200);
  assert.equal(pcmHigh[0], 238); // 128 + 110
});

test("evaluateComfortBand classifies safe, low, high and unknown values accurately", () => {
  assert.equal(evaluateComfortBand(null, 18, 26).status, "unknown");
  assert.equal(evaluateComfortBand(undefined, 18, 26).status, "unknown");
  assert.equal(evaluateComfortBand(22, 18, 26).status, "ok");
  assert.equal(evaluateComfortBand(16.5, 18, 26).status, "low");
  assert.equal(evaluateComfortBand(28.0, 18, 26).status, "high");
  assert.equal(evaluateComfortBand(36.5, 35.0, 38.5).status, "ok");
});

test("estimateMilkMl converts gram difference using 1.03 g/ml density without negative values", () => {
  assert.equal(estimateMilkMl(216.3, 113.3), 100);
  assert.equal(estimateMilkMl(100, 120), 0);
  assert.equal(estimateMilkMl(Number.NaN, 100), 0);
});

test("computeMobileLedFrame previews secuencia, parpadeo, columnas, filas and espiral on a 3x3 grid", () => {
  const lit = (frame: boolean[]) => frame.map((on, i) => (on ? i : -1)).filter((i) => i >= 0);

  const seq = computeMobileLedFrame("secuencia", 4, 9);
  assert.equal(seq.length, 9);
  assert.deepEqual(lit(seq), [4]);

  assert.equal(computeMobileLedFrame("parpadeo", 0, 9).every(Boolean), true);
  assert.equal(computeMobileLedFrame("parpadeo", 1, 9).some(Boolean), false);

  assert.deepEqual(lit(computeMobileLedFrame("columnas", 1, 9)), [1, 4, 7]);
  assert.deepEqual(lit(computeMobileLedFrame("filas", 2, 9)), [6, 7, 8]);
  assert.deepEqual(lit(computeMobileLedFrame("espiral", 4, 9)), [0, 1, 2, 5]);
  assert.equal(computeMobileLedFrame("espiral", 9, 9).every(Boolean), true);
});

test("parseMobileDuration only accepts whole minutes from 1 to 30", () => {
  assert.deepEqual(parseMobileDuration("1"), { ok: true, value: 1 });
  assert.deepEqual(parseMobileDuration(" 30 "), { ok: true, value: 30 });
  for (const bad of ["0", "31", "2.5", "-3", "", "abc", "1e1"]) {
    assert.equal(parseMobileDuration(bad).ok, false, `debería rechazar "${bad}"`);
  }
});

test("buildMobileOnPayload uses the exact keys the firmware expects", () => {
  assert.deepEqual(buildMobileOnPayload("espiral", "rapida", 10), {
    patron: "espiral",
    velocidad: "rapida",
    duracion_min: 10,
  });
});

test("isCribOnline treats heartbeats older than 60 s as offline", () => {
  const now = Date.parse("2026-11-19T12:00:00Z");
  assert.equal(isCribOnline("2026-11-19T11:59:30Z", now), true);
  assert.equal(isCribOnline("2026-11-19T11:59:00Z", now), true);
  assert.equal(isCribOnline("2026-11-19T11:58:59Z", now), false);
  assert.equal(isCribOnline(null, now), false);
  assert.equal(isCribOnline("no-es-fecha", now), false);
});

test("parseOptionalFiniteNumber avoids converting empty strings into zero", () => {
  assert.equal(parseOptionalFiniteNumber(""), null);
  assert.equal(parseOptionalFiniteNumber("   "), null);
  assert.equal(parseOptionalFiniteNumber("abc"), null);
  assert.equal(parseOptionalFiniteNumber("Infinity"), null);
  assert.equal(parseOptionalFiniteNumber("0"), 0);
  assert.equal(parseOptionalFiniteNumber(" 36.5 "), 36.5);
});
