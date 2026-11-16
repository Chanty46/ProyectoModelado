import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { supabase } from "../lib/supabase";
import { accountApi, ApiError, type LiveCribStatusResponse } from "../lib/api";

export interface ThresholdFormFields {
  tempMin: string;
  tempMax: string;
  humMin: string;
  humMax: string;
  movUmbral: string;
  movAlertaActiva: boolean;
  ruidoPiso: string;
  escalaMasa: string;
}

interface UseLiveCribTelemetryOptions {
  accessToken: string;
  cunaId: string;
  activeProfileId: string | null;
  onError: (msg: string | null) => void;
  onCribUpdated: () => void;
}

/**
 * Hook que sincroniza en tiempo real la telemetría, alertas, modo operativo,
 * iluminación nocturna PWM y umbrales configurables de una cuna (IU-01, RF04–RF09, RF29).
 * Combina Supabase Realtime (< 1 s) con polling adaptativo (10 s con Realtime o 2 s sin él).
 */
export function useLiveCribTelemetry({
  accessToken,
  cunaId,
  activeProfileId,
  onError,
  onCribUpdated,
}: UseLiveCribTelemetryOptions) {
  const [live, setLive] = useState<LiveCribStatusResponse | null>(null);
  const [lightLevel, setLightLevel] = useState<number>(50);
  const [showThresholdsForm, setShowThresholdsForm] = useState<boolean>(false);
  const [countdownNowMs, setCountdownNowMs] = useState<number>(() => Date.now());

  // Refs para proteger entradas en edición contra sobreescrituras de polling/Realtime en segundo plano
  const isDirtyLightRef = useRef<boolean>(false);
  const showThresholdsFormRef = useRef<boolean>(false);

  const [thresholds, setThresholds] = useState<ThresholdFormFields>({
    tempMin: "18.0",
    tempMax: "26.0",
    humMin: "40.0",
    humMax: "60.0",
    movUmbral: "0.150",
    movAlertaActiva: true,
    ruidoPiso: "0.010",
    escalaMasa: "0.20",
  });

  const syncThresholdInputsFromData = useCallback((data: LiveCribStatusResponse) => {
    setThresholds({
      tempMin: data.umbrales.tempMin.toString(),
      tempMax: data.umbrales.tempMax.toString(),
      humMin: data.umbrales.humMin.toString(),
      humMax: data.umbrales.humMax.toString(),
      movUmbral: data.umbrales.movimientoUmbral.toString(),
      movAlertaActiva: data.umbrales.movimientoAlertaActiva,
      ruidoPiso: data.umbrales.ruidoMasaPiso.toString(),
      escalaMasa: data.umbrales.escalaMasa.toString(),
    });
  }, []);

  useEffect(() => {
    showThresholdsFormRef.current = showThresholdsForm;
  }, [showThresholdsForm]);

  const fetchLiveStatus = useCallback(
    async (signal?: AbortSignal) => {
      try {
        const data = await accountApi.getLiveCribStatus(accessToken, cunaId, signal);
        setLive(data);
        // Solo sincroniza el deslizador de luz si el cuidador no lo está moviendo en este instante
        if (!isDirtyLightRef.current) {
          setLightLevel(data.cuna.luzEncendida ? data.cuna.luzNivel : 50);
        }
        // Evita sobreescribir los inputs mientras el formulario de umbrales está abierto
        if (!showThresholdsFormRef.current) {
          syncThresholdInputsFromData(data);
        }
      } catch (err) {
        if ((err as Error)?.name === "AbortError") {
          return;
        }
        if (err instanceof ApiError) {
          onError(err.message);
        }
      }
    },
    [accessToken, cunaId, onError, syncThresholdInputsFromData]
  );

  // Reinicia estados locales transitorios al cambiar de cuna seleccionada
  useEffect(() => {
    isDirtyLightRef.current = false;
    showThresholdsFormRef.current = false;
    setShowThresholdsForm(false);
  }, [cunaId]);

  useEffect(() => {
    const controller = new AbortController();
    void fetchLiveStatus(controller.signal);
    return () => controller.abort();
  }, [cunaId, fetchLiveStatus]);

  // Cuenta regresiva cada segundo mientras esté activo el Modo Atención (RF05)
  useEffect(() => {
    if (live?.cuna.modoOperacion !== "atencion") {
      return;
    }
    const timer = window.setInterval(() => setCountdownNowMs(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [live?.cuna.modoOperacion]);

  // IU-01: Suscripción Supabase Realtime (< 1 s) + fallback por polling cada 2 s si no hay Supabase
  useEffect(() => {
    const sbClient = supabase;
    const pollIntervalMs = sbClient ? 10_000 : 2_000;
    const pollTimer = window.setInterval(() => {
      void fetchLiveStatus();
    }, pollIntervalMs);

    if (!sbClient) {
      return () => window.clearInterval(pollTimer);
    }

    const channel = sbClient
      .channel(`crib-live-${cunaId}`)
      .on(
        "postgres_changes",
        { event: "*", schema: "public", table: "cunas", filter: `id=eq.${cunaId}` },
        () => {
          void fetchLiveStatus();
          onCribUpdated();
        }
      )
      .on(
        "postgres_changes",
        { event: "*", schema: "public", table: "telemetria", filter: `cuna_id=eq.${cunaId}` },
        () => {
          void fetchLiveStatus();
        }
      )
      .on(
        "postgres_changes",
        { event: "*", schema: "public", table: "eventos_alertas", filter: `cuna_id=eq.${cunaId}` },
        () => {
          void fetchLiveStatus();
        }
      )
      .on(
        "postgres_changes",
        { event: "*", schema: "public", table: "umbrales", filter: `cuna_id=eq.${cunaId}` },
        () => {
          void fetchLiveStatus();
        }
      )
      .subscribe();

    return () => {
      window.clearInterval(pollTimer);
      void sbClient.removeChannel(channel);
    };
  }, [cunaId, fetchLiveStatus, onCribUpdated]);

  const handleToggleMode = useCallback(
    async (targetMode: "monitoreo" | "atencion") => {
      onError(null);
      try {
        await accountApi.setCribMode(accessToken, cunaId, {
          modo: targetMode,
          perfilId: activeProfileId,
        });
        await fetchLiveStatus();
        onCribUpdated();
      } catch (err) {
        if (err instanceof ApiError) {
          onError(err.message);
        }
      }
    },
    [accessToken, activeProfileId, cunaId, fetchLiveStatus, onCribUpdated, onError]
  );

  const handleLightSliderChange = useCallback((nextLevel: number) => {
    isDirtyLightRef.current = true;
    setLightLevel(nextLevel);
  }, []);

  const handleApplyLight = useCallback(
    async (encendida: boolean, nivel: number) => {
      isDirtyLightRef.current = false;
      onError(null);
      try {
        await accountApi.setCribLight(accessToken, cunaId, {
          encendida,
          nivel,
          perfilId: activeProfileId,
        });
        await fetchLiveStatus();
        onCribUpdated();
      } catch (err) {
        if (err instanceof ApiError) {
          onError(err.message);
        }
      }
    },
    [accessToken, activeProfileId, cunaId, fetchLiveStatus, onCribUpdated, onError]
  );

  const handleToggleThresholdsForm = useCallback(() => {
    const opening = !showThresholdsForm;
    showThresholdsFormRef.current = opening;
    setShowThresholdsForm(opening);
    if (opening && live) {
      syncThresholdInputsFromData(live);
    }
  }, [live, showThresholdsForm, syncThresholdInputsFromData]);

  const handleSaveThresholds = useCallback(
    async (e: FormEvent) => {
      e.preventDefault();
      onError(null);
      try {
        await accountApi.updateCribThresholds(accessToken, cunaId, {
          tempMin: Number(thresholds.tempMin),
          tempMax: Number(thresholds.tempMax),
          humMin: Number(thresholds.humMin),
          humMax: Number(thresholds.humMax),
          movimientoUmbral: Number(thresholds.movUmbral),
          movimientoAlertaActiva: thresholds.movAlertaActiva,
          ruidoMasaPiso: Number(thresholds.ruidoPiso),
          escalaMasa: Number(thresholds.escalaMasa),
          perfilId: activeProfileId,
        });
        showThresholdsFormRef.current = false;
        setShowThresholdsForm(false);
        await fetchLiveStatus();
      } catch (err) {
        if (err instanceof ApiError) {
          onError(err.message);
        }
      }
    },
    [accessToken, activeProfileId, cunaId, fetchLiveStatus, onError, thresholds]
  );

  return {
    live,
    lightLevel,
    countdownNowMs,
    showThresholdsForm,
    thresholds,
    setThresholds,
    handleToggleMode,
    handleLightSliderChange,
    handleApplyLight,
    handleToggleThresholdsForm,
    handleSaveThresholds,
  };
}
