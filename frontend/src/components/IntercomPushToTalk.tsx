import { useState, useRef, type KeyboardEvent } from "react";
import { Mic, Volume2 } from "lucide-react";
import { accountApi, ApiError } from "../lib/api";

interface IntercomPushToTalkProps {
  accessToken: string;
  cunaId: string;
}

function uint8ToBase64(bytes: Uint8Array): string {
  let binary = "";
  for (let i = 0; i < bytes.length; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return window.btoa(binary);
}

/**
 * RF11 / Spike M8: Intercomunicador unidireccional Push-to-Talk (PCM 8-bit 8 kHz <= 65 dBA).
 * Soporta eventos de ratón, táctiles y teclado (Espacio/Enter) con estado de accesibilidad.
 */
export function IntercomPushToTalk({ accessToken, cunaId }: IntercomPushToTalkProps) {
  const [transmitting, setTransmitting] = useState(false);
  const [statusMsg, setStatusMsg] = useState<string | null>(null);
  const seqRef = useRef(0);

  async function sendVoiceTestChunk() {
    if (transmitting) return;
    setTransmitting(true);
    setStatusMsg(null);
    try {
      const sampleRateHz = 8000;
      const sampleCount = 800; // 100 ms por bloque (< 1280 muestras)
      const pcm = new Uint8Array(sampleCount);
      for (let i = 0; i < sampleCount; i++) {
        const wave = Math.sin((2 * Math.PI * 440 * i) / sampleRateHz);
        pcm[i] = Math.round(128 + wave * 60);
      }

      seqRef.current += 1;
      const res = await accountApi.sendIntercomChunk(accessToken, cunaId, {
        sampleRateHz,
        seq: seqRef.current,
        pcmBase64: uint8ToBase64(pcm),
      });

      setStatusMsg(
        res.delivered
          ? `Audio enviado a la cuna (${res.sampleCount} muestras a ${res.sampleRateHz} Hz, límite ≤ 65 dBA aplicado).`
          : "Bloque procesado (la Unidad Central está desconectada actualmente)."
      );
    } catch (err) {
      if (err instanceof ApiError) {
        setStatusMsg(err.message);
      } else {
        setStatusMsg("Error al transmitir bloque de voz.");
      }
    } finally {
      setTransmitting(false);
    }
  }

  function handleKeyDown(e: KeyboardEvent<HTMLButtonElement>) {
    if (e.key === " " || e.key === "Enter") {
      e.preventDefault();
      void sendVoiceTestChunk();
    }
  }

  return (
    <div className="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-2">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Volume2 className="h-4 w-4 text-teal-600" aria-hidden="true" />
          <span className="text-xs font-semibold text-slate-800">
            Intercomunicador de Voz (RF11 — Spike M8)
          </span>
        </div>
        <span className="text-[11px] text-slate-500">PCM 8 kHz · Tope ≤ 65 dBA</span>
      </div>
      <button
        type="button"
        disabled={transmitting}
        onMouseDown={sendVoiceTestChunk}
        onTouchStart={(e) => {
          e.preventDefault();
          void sendVoiceTestChunk();
        }}
        onKeyDown={handleKeyDown}
        aria-label="Transmitir bloque de voz por intercomunicador push-to-talk"
        className="inline-flex items-center gap-2 rounded-lg bg-teal-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors focus:outline-none focus:ring-2 focus:ring-teal-500 focus:ring-offset-1"
      >
        <Mic className={`h-3.5 w-3.5 ${transmitting ? "animate-pulse text-amber-300" : ""}`} aria-hidden="true" />
        {transmitting ? "Transmitiendo..." : "Mantener para hablar (Push-to-Talk)"}
      </button>
      {statusMsg && (
        <p className="text-[11px] text-slate-600" role="status" aria-live="polite">
          {statusMsg}
        </p>
      )}
    </div>
  );
}
