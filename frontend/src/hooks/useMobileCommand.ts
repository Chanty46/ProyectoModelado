import { useCallback, useEffect, useRef, useState } from "react";
import { supabase } from "../lib/supabase";
import {
  COMMAND_TIMEOUT_MS,
  type MobileCommandPayload,
  type MobileCommandType,
} from "../lib/mobileCommands";

export type MobileCommandStatus =
  | { kind: "idle" }
  | { kind: "sending"; tipo: MobileCommandType }
  | { kind: "confirmed"; tipo: MobileCommandType }
  | { kind: "error"; message: string }
  | { kind: "timeout" };

const SUBSCRIBE_WAIT_MS = 3_000;

function describeInsertError(error: { code?: string; message?: string }): string {
  if (error.code === "42501") {
    const detail = error.message ? ` (detalle: ${error.message})` : "";
    return `No tienes permiso para controlar esta cuna${detail}.`;
  }
  return error.message ? `No se pudo enviar el comando: ${error.message}` : "No se pudo enviar el comando.";
}

/**
 * Envía un comando del móvil colgante insertando una fila 'pendiente' en `public.comandos`
 * y escucha esa misma fila con Supabase Realtime hasta que la Pico la marque
 * `confirmado` o `error`. Si en 10 s no hay respuesta, el estado pasa a `timeout`.
 *
 * El id se genera en el navegador para suscribirse ANTES de insertar: así no se pierde
 * una confirmación que llegue muy rápido. Al vencer el plazo se consulta la fila una vez
 * más por si el evento de Realtime no llegó.
 */
export function useMobileCommand(cunaId: string) {
  const [status, setStatus] = useState<MobileCommandStatus>({ kind: "idle" });
  const cancelRef = useRef<(() => void) | null>(null);

  // Al cambiar de cuna o desmontar: dejar de escuchar el comando anterior
  useEffect(() => {
    setStatus({ kind: "idle" });
    return () => {
      cancelRef.current?.();
      cancelRef.current = null;
    };
  }, [cunaId]);

  const send = useCallback(
    async (tipo: MobileCommandType, payload: MobileCommandPayload) => {
      cancelRef.current?.();
      cancelRef.current = null;

      const client = supabase;
      if (!client) {
        setStatus({ kind: "error", message: "Supabase no está configurado en esta web." });
        return;
      }

      const commandId = crypto.randomUUID();
      setStatus({ kind: "sending", tipo });

      let settled = false;
      let timeoutId: number | undefined;
      const channel = client.channel(`comando-${commandId}`);

      const cleanup = () => {
        if (timeoutId !== undefined) {
          window.clearTimeout(timeoutId);
        }
        void client.removeChannel(channel);
      };
      const finish = (next: MobileCommandStatus) => {
        if (settled) return;
        settled = true;
        cleanup();
        cancelRef.current = null;
        setStatus(next);
      };
      const applyEstado = (estado: unknown): boolean => {
        if (estado === "confirmado") {
          finish({ kind: "confirmed", tipo });
          return true;
        }
        if (estado === "error") {
          finish({ kind: "error", message: "La cuna recibió el comando pero no pudo ejecutarlo." });
          return true;
        }
        return false;
      };

      cancelRef.current = () => {
        settled = true;
        cleanup();
      };

      channel.on(
        "postgres_changes",
        { event: "UPDATE", schema: "public", table: "comandos", filter: `id=eq.${commandId}` },
        (change) => {
          applyEstado((change.new as { estado?: unknown }).estado);
        }
      );

      // Esperar a que la suscripción quede activa (con tope) antes de insertar
      await new Promise<void>((resolve) => {
        const waitTimer = window.setTimeout(resolve, SUBSCRIBE_WAIT_MS);
        channel.subscribe((subscriptionStatus) => {
          if (subscriptionStatus !== "SUBSCRIBED" && subscriptionStatus !== "CHANNEL_ERROR" && subscriptionStatus !== "TIMED_OUT") {
            return;
          }
          window.clearTimeout(waitTimer);
          resolve();
        });
      });
      if (settled) return;

      const { error } = await client.from("comandos").insert({
        id: commandId,
        cuna_id: cunaId,
        tipo,
        payload,
        estado: "pendiente",
      });
      if (error) {
        finish({ kind: "error", message: describeInsertError(error) });
        return;
      }
      if (settled) return;

      timeoutId = window.setTimeout(() => {
        void (async () => {
          if (settled) return;
          const { data } = await client.from("comandos").select("estado").eq("id", commandId).maybeSingle();
          if (!applyEstado((data as { estado?: unknown } | null)?.estado)) {
            finish({ kind: "timeout" });
          }
        })();
      }, COMMAND_TIMEOUT_MS);
    },
    [cunaId]
  );

  return { status, send };
}
