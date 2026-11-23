import { useEffect, useState } from "react";
import { supabase } from "../lib/supabase";
import { isCribOnline } from "../lib/mobileCommands";

/**
 * Sigue `cunas.ultimo_heartbeat` de una cuna con Supabase Realtime y recalcula cada segundo
 * si está En línea (último heartbeat de hace 60 s o menos).
 */
export function useCribHeartbeat(cunaId: string) {
  const [ultimoHeartbeat, setUltimoHeartbeat] = useState<string | null>(null);
  const [loaded, setLoaded] = useState<boolean>(false);
  const [nowMs, setNowMs] = useState<number>(() => Date.now());

  useEffect(() => {
    const timer = window.setInterval(() => setNowMs(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    const client = supabase;
    setUltimoHeartbeat(null);
    setLoaded(false);
    if (!client) {
      setLoaded(true);
      return;
    }

    let active = true;
    void (async () => {
      const { data } = await client.from("cunas").select("ultimo_heartbeat").eq("id", cunaId).maybeSingle();
      if (!active) return;
      setUltimoHeartbeat((data as { ultimo_heartbeat?: string | null } | null)?.ultimo_heartbeat ?? null);
      setLoaded(true);
    })();

    const channel = client
      .channel(`cuna-heartbeat-${cunaId}`)
      .on(
        "postgres_changes",
        { event: "UPDATE", schema: "public", table: "cunas", filter: `id=eq.${cunaId}` },
        (change) => {
          const next = (change.new as { ultimo_heartbeat?: string | null }).ultimo_heartbeat;
          if (next !== undefined) {
            setUltimoHeartbeat(next);
          }
        }
      )
      .subscribe();

    return () => {
      active = false;
      void client.removeChannel(channel);
    };
  }, [cunaId]);

  return {
    loaded,
    ultimoHeartbeat,
    nowMs,
    online: isCribOnline(ultimoHeartbeat, nowMs),
  };
}
