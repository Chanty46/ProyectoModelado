import { useState, useRef, useEffect, type KeyboardEvent } from "react";
import { Mic, Volume2, Radio } from "lucide-react";
import { accountApi, ApiError } from "../lib/api";
import { downsampleFloatToPcm8 } from "../lib/formatters";

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
 * Captura micrófono real con Web Audio API cuando el navegador lo permite y ofrece tono de
 * prueba 440 Hz como respaldo para dispositivos/navegadores sin micrófono.
 */
export function IntercomPushToTalk({ accessToken, cunaId }: IntercomPushToTalkProps) {
  const [transmitting, setTransmitting] = useState(false);
  const [usingMic, setUsingMic] = useState(false);
  const [statusMsg, setStatusMsg] = useState<string | null>(null);
  const seqRef = useRef(0);
  const activeStreamRef = useRef<MediaStream | null>(null);
  const audioCtxRef = useRef<AudioContext | null>(null);
  const processorRef = useRef<ScriptProcessorNode | null>(null);
  const holdingRef = useRef(false);

  const stopMicCapture = () => {
    holdingRef.current = false;
    setUsingMic(false);
    setTransmitting(false);

    if (processorRef.current) {
      processorRef.current.disconnect();
      processorRef.current.onaudioprocess = null;
      processorRef.current = null;
    }
    if (audioCtxRef.current) {
      void audioCtxRef.current.close();
      audioCtxRef.current = null;
    }
    if (activeStreamRef.current) {
      for (const track of activeStreamRef.current.getTracks()) {
        track.stop();
      }
      activeStreamRef.current = null;
    }
  };

  useEffect(() => {
    return () => {
      stopMicCapture();
    };
  }, []);

  async function dispatchPcmChunk(pcm: Uint8Array, sourceLabel: string) {
    if (pcm.length === 0) return;
    seqRef.current += 1;
    const res = await accountApi.sendIntercomChunk(accessToken, cunaId, {
      sampleRateHz: 8000,
      seq: seqRef.current,
      pcmBase64: uint8ToBase64(pcm),
    });

    setStatusMsg(
      res.delivered
        ? `${sourceLabel} transmitido (${res.sampleCount} muestras @ ${res.sampleRateHz} Hz, tope ≤ 65 dBA).`
        : "Bloque procesado (la Unidad Central está desconectada actualmente)."
    );
  }

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
      await dispatchPcmChunk(pcm, "Tono 440 Hz");
    } catch (err) {
      setStatusMsg(err instanceof ApiError ? err.message : "Error al transmitir bloque de voz.");
    } finally {
      setTransmitting(false);
    }
  }

  async function startHoldToTalk() {
    if (holdingRef.current || transmitting) return;
    holdingRef.current = true;
    setStatusMsg(null);

    if (!navigator.mediaDevices?.getUserMedia || typeof AudioContext === "undefined") {
      holdingRef.current = false;
      await sendVoiceTestChunk();
      return;
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      if (!holdingRef.current) {
        for (const t of stream.getTracks()) t.stop();
        return;
      }

      activeStreamRef.current = stream;
      const ctx = new AudioContext();
      audioCtxRef.current = ctx;
      const source = ctx.createMediaStreamSource(stream);
      const processor = ctx.createScriptProcessor(4096, 1, 1);
      processorRef.current = processor;

      setUsingMic(true);
      setTransmitting(true);
      setStatusMsg("Micrófono activo: transmitiendo voz en bloques 8 kHz...");

      processor.onaudioprocess = (event) => {
        if (!holdingRef.current) return;
        const floatSamples = event.inputBuffer.getChannelData(0);
        const pcm = downsampleFloatToPcm8(floatSamples, ctx.sampleRate, 8000, 1000);
        void dispatchPcmChunk(pcm, "Voz micrófono").catch((err) => {
          setStatusMsg(err instanceof ApiError ? err.message : "Error enviando audio de micrófono.");
          stopMicCapture();
        });
      };

      source.connect(processor);
      processor.connect(ctx.destination);
    } catch {
      // Sin micrófono o permiso denegado -> respaldo limpio con tono 440 Hz
      holdingRef.current = false;
      setUsingMic(false);
      await sendVoiceTestChunk();
    }
  }

  function handleKeyDown(e: KeyboardEvent<HTMLButtonElement>) {
    if (e.key === " " || e.key === "Enter") {
      e.preventDefault();
      void sendVoiceTestChunk();
    }
  }

  return (
    <div className="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-2.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Volume2 className="h-4 w-4 text-teal-600" aria-hidden="true" />
          <span className="text-xs font-semibold text-slate-800">
            Intercomunicador de Voz (RF11 — Spike M8)
          </span>
        </div>
        <span className="text-[11px] text-slate-500">PCM 8-bit 8 kHz · Tope ≤ 65 dBA</span>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <button
          type="button"
          onMouseDown={() => void startHoldToTalk()}
          onMouseUp={stopMicCapture}
          onMouseLeave={() => {
            if (usingMic) stopMicCapture();
          }}
          onTouchStart={(e) => {
            e.preventDefault();
            void startHoldToTalk();
          }}
          onTouchEnd={stopMicCapture}
          onKeyDown={handleKeyDown}
          aria-label="Transmitir bloque de voz por intercomunicador push-to-talk"
          className="inline-flex items-center gap-2 rounded-lg bg-teal-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors focus:outline-none focus:ring-2 focus:ring-teal-500 focus:ring-offset-1"
        >
          <Mic className={`h-3.5 w-3.5 ${transmitting ? "animate-pulse text-amber-300" : ""}`} aria-hidden="true" />
          {usingMic
            ? "Hablando... (suelta para detener)"
            : transmitting
            ? "Transmitiendo..."
            : "Mantener para hablar (Mic / Respaldo)"}
        </button>

        <button
          type="button"
          disabled={transmitting}
          onClick={() => void sendVoiceTestChunk()}
          className="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-100 disabled:opacity-50"
        >
          <Radio className="h-3.5 w-3.5 text-teal-600" aria-hidden="true" />
          Tono prueba 440 Hz
        </button>
      </div>

      {statusMsg && (
        <p className="text-[11px] text-slate-600" role="status" aria-live="polite">
          {statusMsg}
        </p>
      )}
    </div>
  );
}
