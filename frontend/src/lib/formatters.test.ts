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

test("computeMobileLedFrame renders ola_circular, cascada_suave and destello_alterno across 9 LEDs", () => {
  const wave = computeMobileLedFrame("ola_circular", 0, 9);
  assert.equal(wave.length, 9);
  assert.equal(wave[0], true);
  assert.equal(wave[8], true);
  assert.equal(wave.filter(Boolean).length, 2);

  const cascade = computeMobileLedFrame("cascada_suave", 3, 9);
  assert.deepEqual(cascade.slice(0, 4), [true, true, true, false]);

  const blink = computeMobileLedFrame("destello_alterno", 1, 9);
  assert.equal(blink[0], false);
  assert.equal(blink[1], true);
});

test("parseOptionalFiniteNumber avoids converting empty strings into zero", () => {
  assert.equal(parseOptionalFiniteNumber(""), null);
  assert.equal(parseOptionalFiniteNumber("   "), null);
  assert.equal(parseOptionalFiniteNumber("abc"), null);
  assert.equal(parseOptionalFiniteNumber("Infinity"), null);
  assert.equal(parseOptionalFiniteNumber("0"), 0);
  assert.equal(parseOptionalFiniteNumber(" 36.5 "), 36.5);
});
