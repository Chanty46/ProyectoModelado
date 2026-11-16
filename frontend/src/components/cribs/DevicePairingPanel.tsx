import { useEffect, useState, type FormEvent } from "react";
import { Check, Copy, KeyRound } from "lucide-react";
import {
  accountApi,
  ApiError,
  type ClaimDeviceResponse,
  type CribSummaryResponse,
  type OpenPairingWindowResponse,
} from "../../lib/api";

interface DevicePairingPanelProps {
  accessToken: string;
  cribs: CribSummaryResponse[];
  isPrincipal: boolean;
  onClaimed: () => Promise<void>;
  onError: (msg: string | null) => void;
}

/**
 * Panel de emparejamiento seguro de dispositivos Pico W (RF24).
 * Permite al Usuario Principal abrir una ventana de 10 minutos, reclamar nodos por
 * `device_id` + código de fábrica y copiar el bloque `secrets.py` mostrado una única vez.
 */
export function DevicePairingPanel({
  accessToken,
  cribs,
  isPrincipal,
  onClaimed,
  onError,
}: DevicePairingPanelProps) {
  const [pairingWindow, setPairingWindow] = useState<OpenPairingWindowResponse | null>(null);
  const [claimResult, setClaimResult] = useState<ClaimDeviceResponse | null>(null);
  const [copiedSecrets, setCopiedSecrets] = useState(false);

  const [claimDeviceId, setClaimDeviceId] = useState("pico-central-nuevo");
  const [claimCode, setClaimCode] = useState("");
  const [claimCribName, setClaimCribName] = useState("Cuna Principal");
  const [claimTargetCunaId, setClaimTargetCunaId] = useState<string>("");
  const [isClaiming, setIsClaiming] = useState(false);

  useEffect(() => {
    if (cribs.length > 0) {
      setClaimTargetCunaId((prev) =>
        prev && cribs.some((c) => c.cuna.id === prev) ? prev : cribs[0].cuna.id
      );
    } else {
      setClaimTargetCunaId("");
    }
  }, [cribs]);

  async function handleOpenPairingWindow() {
    onError(null);
    try {
      const win = await accountApi.openPairingWindow(accessToken);
      setPairingWindow(win);
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  async function handleClaimDevice(e: FormEvent) {
    e.preventDefault();
    onError(null);
    setIsClaiming(true);
    try {
      const res = await accountApi.claimDevice(accessToken, {
        deviceId: claimDeviceId.trim(),
        codigoReclamo: claimCode.trim(),
        cunaId: claimTargetCunaId || null,
        nombreCuna: claimCribName.trim() || "Cuna Principal",
      });
      setClaimResult(res);
      setCopiedSecrets(false);
      setClaimCode("");
      await onClaimed();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsClaiming(false);
    }
  }

  async function handleCopySecretsSnippet() {
    if (!claimResult) {
      return;
    }
    const snippet = `DEVICE_ID = "${claimResult.deviceId}"\nDEVICE_TYPE = "${claimResult.tipo}"\nDEVICE_TOKEN = "${claimResult.deviceTokenOnce}"\n`;
    try {
      await navigator.clipboard.writeText(snippet);
      setCopiedSecrets(true);
    } catch {
      onError("No se pudo copiar automáticamente al portapapeles; copia las credenciales manualmente.");
    }
  }

  return (
    <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <KeyRound className="h-5 w-5 text-teal-600" aria-hidden="true" />
          <h2 className="text-base font-semibold text-slate-900">
            Emparejar Dispositivo Pico W (RF24)
          </h2>
        </div>
      </div>

      {isPrincipal ? (
        <>
          <p className="text-xs text-slate-600">
            El Usuario Principal abre una ventana de 10 minutos e introduce el identificador de fábrica y código de reclamo del dispositivo (máximo 5 intentos).
          </p>

          <button
            type="button"
            onClick={() => void handleOpenPairingWindow()}
            className="w-full rounded-xl border border-teal-600 bg-teal-50 px-4 py-2 text-xs font-medium text-teal-800 hover:bg-teal-100"
          >
            Abrir ventana de emparejamiento (10 min)
          </button>

          {pairingWindow && (
            <p className="rounded-lg bg-emerald-50 px-3 py-1.5 text-[11px] font-medium text-emerald-800">
              Ventana activa hasta: {new Date(pairingWindow.expiraEn).toLocaleTimeString()}
            </p>
          )}

          <form onSubmit={(e) => void handleClaimDevice(e)} className="space-y-3 pt-2 border-t border-slate-100">
            <div>
              <label htmlFor="claim-device-id" className="block text-xs font-medium text-slate-700 mb-1">
                ID de fábrica del dispositivo (device_id)
              </label>
              <input
                id="claim-device-id"
                type="text"
                required
                value={claimDeviceId}
                onChange={(e) => setClaimDeviceId(e.target.value)}
                placeholder="Ej. pico-central-nuevo"
                className="w-full rounded-lg border border-slate-300 px-3 py-1.5 text-xs font-mono"
              />
            </div>

            <div>
              <label htmlFor="claim-code" className="block text-xs font-medium text-slate-700 mb-1">
                Código de reclamo de fábrica (un solo uso)
              </label>
              <input
                id="claim-code"
                type="text"
                required
                value={claimCode}
                onChange={(e) => setClaimCode(e.target.value)}
                placeholder="Ej. CLAIM-CENTRAL-2026"
                className="w-full rounded-lg border border-slate-300 px-3 py-1.5 text-xs font-mono"
              />
            </div>

            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
              <div>
                <label htmlFor="claim-crib-name" className="block text-xs font-medium text-slate-700 mb-1">
                  Nombre de cuna (si es Central)
                </label>
                <input
                  id="claim-crib-name"
                  type="text"
                  maxLength={30}
                  value={claimCribName}
                  onChange={(e) => setClaimCribName(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-3 py-1.5 text-xs"
                />
              </div>

              <div>
                <label htmlFor="claim-target-cuna" className="block text-xs font-medium text-slate-700 mb-1">
                  Cuna destino (si es periférico)
                </label>
                <select
                  id="claim-target-cuna"
                  value={claimTargetCunaId}
                  onChange={(e) => setClaimTargetCunaId(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-2.5 py-1.5 text-xs"
                >
                  <option value="">(Crear nueva cuna con Central)</option>
                  {cribs.map((c) => (
                    <option key={c.cuna.id} value={c.cuna.id}>
                      {c.cuna.nombre}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            <button
              type="submit"
              disabled={isClaiming}
              className="w-full rounded-xl bg-teal-600 px-4 py-2 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors"
            >
              {isClaiming ? "Reclamando dispositivo..." : "Reclamar y generar token de dispositivo"}
            </button>
          </form>
        </>
      ) : (
        <p className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 text-xs text-slate-600">
          Solo el Usuario Principal puede abrir ventanas de emparejamiento o reclamar nodos Pico W (RF03 / RF24).
        </p>
      )}

      {claimResult && (
        <div className="rounded-xl border border-amber-300 bg-amber-50 p-3 space-y-2">
          <p className="text-xs font-semibold text-amber-900">
            Token generado para {claimResult.deviceId} (se muestra una sola vez):
          </p>
          <pre className="overflow-x-auto rounded border border-amber-200 bg-white p-2 text-[11px] font-mono text-slate-800">
            {`DEVICE_ID = "${claimResult.deviceId}"\nDEVICE_TYPE = "${claimResult.tipo}"\nDEVICE_TOKEN = "${claimResult.deviceTokenOnce}"`}
          </pre>
          <div className="flex items-center justify-between">
            <button
              type="button"
              onClick={() => void handleCopySecretsSnippet()}
              className="inline-flex items-center gap-1 rounded bg-slate-900 px-2.5 py-1 text-xs text-white hover:bg-slate-800"
            >
              {copiedSecrets ? (
                <>
                  <Check className="h-3.5 w-3.5" aria-hidden="true" />
                  Copiado para secrets.py
                </>
              ) : (
                <>
                  <Copy className="h-3.5 w-3.5" aria-hidden="true" />
                  Copiar bloque secrets.py
                </>
              )}
            </button>
            <button
              type="button"
              onClick={() => setClaimResult(null)}
              className="text-xs text-amber-900 underline"
            >
              Ya lo guardé, ocultar
            </button>
          </div>
        </div>
      )}
    </section>
  );
}
