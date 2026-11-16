import { useEffect, useState } from "react";
import { accountApi, type HealthStatusResponse } from "../lib/api";

/**
 * Hook de sondeo periódico al endpoint `/health` del backend local (cada 15 s).
 * Utiliza `AbortController` para cancelar consultas pendientes si el componente se desmonta
 * o si llega el siguiente ciclo antes de completarse el anterior.
 */
export function useBackendHealth(pollIntervalMs: number = 15_000) {
  const [health, setHealth] = useState<HealthStatusResponse | null>(null);
  const [healthOffline, setHealthOffline] = useState<boolean>(false);

  useEffect(() => {
    let active = true;
    let controller: AbortController | null = null;

    async function pollHealth() {
      // Aborta la petición anterior antes de lanzar una nueva para evitar condiciones de carrera
      controller?.abort();
      controller = new AbortController();
      try {
        const res = await accountApi.getHealthStatus(controller.signal);
        if (active) {
          setHealth(res);
          setHealthOffline(false);
        }
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") {
          return;
        }
        if (active) {
          setHealthOffline(true);
        }
      }
    }

    void pollHealth();
    const timer = window.setInterval(() => void pollHealth(), pollIntervalMs);
    return () => {
      active = false;
      controller?.abort();
      window.clearInterval(timer);
    };
  }, [pollIntervalMs]);

  return { health, healthOffline };
}
