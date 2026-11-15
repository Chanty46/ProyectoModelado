import { useState, useEffect, useCallback, type FormEvent } from "react";
import {
  Cpu,
  Baby,
  Wifi,
  WifiOff,
  KeyRound,
  Copy,
  Check,
  Edit3,
  Unlink,
  PlusCircle,
  Sparkles,
  Milk,
  Gamepad2,
  BedDouble,
  Battery,
} from "lucide-react";
import {
  accountApi,
  ApiError,
  type CribSummaryResponse,
  type BabyRecord,
  type OpenPairingWindowResponse,
  type ClaimDeviceResponse,
} from "../lib/api";
import { IntercomPushToTalk } from "./IntercomPushToTalk";
import { LiveCribPanel } from "./LiveCribPanel";
import { FeedingToysAndHistoryPanel } from "./FeedingToysAndHistoryPanel";
import { canManagePrincipalActions } from "../lib/formatters";

interface CribAndDeviceManagerProps {
  accessToken: string;
  activeProfileId: string | null;
  userRole: "principal" | "secundario" | null;
  onError: (msg: string | null) => void;
}

export function CribAndDeviceManager({
  accessToken,
  activeProfileId,
  userRole,
  onError,
}: CribAndDeviceManagerProps) {
  const [cribs, setCribs] = useState<CribSummaryResponse[]>([]);
  const [babies, setBabies] = useState<BabyRecord[]>([]);
  const [selectedCunaId, setSelectedCunaId] = useState<string | null>(null);
  const [pairingWindow, setPairingWindow] = useState<OpenPairingWindowResponse | null>(null);
  const [claimResult, setClaimResult] = useState<ClaimDeviceResponse | null>(null);
  const [copiedSecrets, setCopiedSecrets] = useState(false);

  // Formulario de reclamo RF24
  const [claimDeviceId, setClaimDeviceId] = useState("pico-central-nuevo");
  const [claimCode, setClaimCode] = useState("");
  const [claimCribName, setClaimCribName] = useState("Cuna Principal");
  const [claimTargetCunaId, setClaimTargetCunaId] = useState<string>("");

  // Renombrar y desvincular cuna RF26 / RF27
  const [editingCunaId, setEditingCunaId] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState("");
  const [unlinkingCunaId, setUnlinkingCunaId] = useState<string | null>(null);
  const [unlinkConfirmText, setUnlinkConfirmText] = useState("");

  // Formulario de bebé RF28
  const [editingBabyId, setEditingBabyId] = useState<string | null>(null);
  const [babyName, setBabyName] = useState("");
  const [babyBirthDate, setBabyBirthDate] = useState("2026-05-15");
  const [babyMedicalNotes, setBabyMedicalNotes] = useState("");
  const [babyCunaId, setBabyCunaId] = useState<string>("");

  // Estados de carga para operaciones asíncronas
  const [isClaiming, setIsClaiming] = useState(false);
  const [isRenaming, setIsRenaming] = useState(false);
  const [isUnlinking, setIsUnlinking] = useState(false);
  const [isSavingBaby, setIsSavingBaby] = useState(false);

  const loadData = useCallback(async () => {
    try {
      const [cribList, babyList] = await Promise.all([
        accountApi.listCribs(accessToken),
        accountApi.listBabies(accessToken),
      ]);
      setCribs(cribList);
      setBabies(babyList);
      if (cribList.length > 0) {
        setSelectedCunaId((prev) =>
          prev && cribList.some((c) => c.cuna.id === prev) ? prev : cribList[0].cuna.id
        );
        setClaimTargetCunaId((prev) =>
          prev && cribList.some((c) => c.cuna.id === prev) ? prev : cribList[0].cuna.id
        );
        setBabyCunaId((prev) =>
          prev && cribList.some((c) => c.cuna.id === prev) ? prev : cribList[0].cuna.id
        );
      } else {
        setSelectedCunaId(null);
        setClaimTargetCunaId("");
        setBabyCunaId("");
      }
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }, [accessToken, onError]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

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
      await loadData();
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

  async function handleRenameCrib(e: FormEvent, cunaId: string) {
    e.preventDefault();
    onError(null);
    setIsRenaming(true);
    try {
      await accountApi.renameCrib(accessToken, cunaId, renameValue.trim());
      setEditingCunaId(null);
      setRenameValue("");
      await loadData();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsRenaming(false);
    }
  }

  async function handleUnlinkCrib(e: FormEvent, cunaId: string) {
    e.preventDefault();
    onError(null);
    setIsUnlinking(true);
    try {
      await accountApi.unlinkCrib(accessToken, cunaId, unlinkConfirmText.trim());
      setUnlinkingCunaId(null);
      setUnlinkConfirmText("");
      await loadData();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsUnlinking(false);
    }
  }

  async function handleSaveBaby(e: FormEvent) {
    e.preventDefault();
    onError(null);
    const targetCuna = babyCunaId || selectedCunaId;
    if (!targetCuna) {
      onError("Debes registrar una cuna primero para asociar al bebé (RF28).");
      return;
    }
    setIsSavingBaby(true);
    try {
      const payload = {
        cunaId: targetCuna,
        nombre: babyName.trim(),
        fechaNacimiento: babyBirthDate,
        notas: babyMedicalNotes.trim() || null,
      };
      if (editingBabyId) {
        await accountApi.updateBaby(accessToken, editingBabyId, payload);
      } else {
        await accountApi.createBaby(accessToken, payload);
      }
      setEditingBabyId(null);
      setBabyName("");
      setBabyMedicalNotes("");
      await loadData();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsSavingBaby(false);
    }
  }

  function startEditBaby(baby: BabyRecord) {
    setEditingBabyId(baby.id);
    setBabyName(baby.nombre);
    setBabyBirthDate(baby.fechaNacimiento);
    setBabyMedicalNotes(baby.notas ?? "");
    setBabyCunaId(baby.cunaId ?? selectedCunaId ?? "");
  }

  const isPrincipal = canManagePrincipalActions(userRole);
  const activeCribSummary = cribs.find((c) => c.cuna.id === selectedCunaId) ?? null;

  return (
    <div className="space-y-6">
      {/* RF25 / RF26 / RF27: Cunas y Dispositivos Asociados */}
      <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-5">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <Cpu className="h-5 w-5 text-teal-600" aria-hidden="true" />
            <div>
              <h2 className="text-base font-semibold text-slate-900">
                Cunas y Dispositivos Vinculados (RF25 / RF26 / RF27)
              </h2>
              <p className="text-xs text-slate-500">
                Selecciona una cuna para supervisar sus nodos Pico W, renombrarla o usar el intercomunicador.
              </p>
            </div>
          </div>

          {cribs.length > 0 && (
            <div className="flex flex-wrap gap-2" role="tablist" aria-label="Selector de cuna">
              {cribs.map((item) => {
                const isSelected = item.cuna.id === selectedCunaId;
                return (
                  <button
                    key={item.cuna.id}
                    type="button"
                    role="tab"
                    aria-selected={isSelected}
                    onClick={() => setSelectedCunaId(item.cuna.id)}
                    className={`rounded-xl px-3.5 py-1.5 text-xs font-medium transition-colors ${
                      isSelected
                        ? "bg-teal-600 text-white shadow-sm"
                        : "border border-slate-200 bg-slate-50 text-slate-700 hover:bg-slate-100"
                    }`}
                  >
                    {item.cuna.nombre}
                  </button>
                );
              })}
            </div>
          )}
        </div>

        {cribs.length === 0 ? (
          <div className="rounded-xl border border-dashed border-slate-300 bg-slate-50 p-6 text-center text-xs text-slate-600">
            No hay cunas registradas aún. Usa el panel de Emparejamiento (RF24) abajo para vincular una Unidad Central y crear tu primera cuna.
          </div>
        ) : (
          activeCribSummary && (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-200 bg-slate-50 p-4">
                <div>
                  <div className="flex items-center gap-2">
                    <h3 className="text-sm font-semibold text-slate-900">
                      {activeCribSummary.cuna.nombre}
                    </h3>
                    <span
                      className={`rounded-full px-2.5 py-0.5 text-[11px] font-medium ${
                        activeCribSummary.cuna.modoOperacion === "alarma"
                          ? "bg-red-100 text-red-800"
                          : activeCribSummary.cuna.modoOperacion === "atencion"
                            ? "bg-amber-100 text-amber-800"
                            : "bg-emerald-100 text-emerald-800"
                      }`}
                    >
                      Estado: {activeCribSummary.cuna.modoOperacion.toUpperCase()}
                    </span>
                  </div>
                  <p className="text-xs text-slate-600 mt-1">
                    Bebé asociado:{" "}
                    <span className="font-medium text-slate-800">
                      {activeCribSummary.bebe ? activeCribSummary.bebe.nombre : "Sin bebé asignado"}
                    </span>
                  </p>
                </div>

                {userRole === "principal" && (
                  <div className="flex items-center gap-2">
                    <button
                      type="button"
                      onClick={() => {
                        setEditingCunaId(activeCribSummary.cuna.id);
                        setRenameValue(activeCribSummary.cuna.nombre);
                        setUnlinkingCunaId(null);
                      }}
                      className="inline-flex items-center gap-1 rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-100"
                    >
                      <Edit3 className="h-3.5 w-3.5" aria-hidden="true" />
                      Renombrar (RF26)
                    </button>
                    <button
                      type="button"
                      onClick={() => {
                        setUnlinkingCunaId(activeCribSummary.cuna.id);
                        setUnlinkConfirmText("");
                        setEditingCunaId(null);
                      }}
                      className="inline-flex items-center gap-1 rounded-lg border border-red-200 bg-red-50 px-2.5 py-1.5 text-xs font-medium text-red-700 hover:bg-red-100"
                    >
                      <Unlink className="h-3.5 w-3.5" aria-hidden="true" />
                      Desvincular (RF27)
                    </button>
                  </div>
                )}
              </div>

              {editingCunaId === activeCribSummary.cuna.id && (
                <form
                  onSubmit={(e) => handleRenameCrib(e, activeCribSummary.cuna.id)}
                  className="flex flex-wrap items-center gap-2 rounded-xl border border-teal-200 bg-teal-50/50 p-3"
                >
                  <input
                    type="text"
                    required
                    minLength={1}
                    maxLength={30}
                    value={renameValue}
                    onChange={(e) => setRenameValue(e.target.value)}
                    placeholder="Nuevo nombre de la cuna (1–30 caracteres)"
                    className="flex-1 rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs"
                  />
                  <button
                    type="submit"
                    disabled={isRenaming}
                    className="rounded-lg bg-teal-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors"
                  >
                    {isRenaming ? "Guardando..." : "Guardar nombre"}
                  </button>
                  <button
                    type="button"
                    onClick={() => setEditingCunaId(null)}
                    className="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs text-slate-600"
                  >
                    Cancelar
                  </button>
                </form>
              )}

              {unlinkingCunaId === activeCribSummary.cuna.id && (
                <form
                  onSubmit={(e) => handleUnlinkCrib(e, activeCribSummary.cuna.id)}
                  className="space-y-2 rounded-xl border border-red-300 bg-red-50 p-4"
                >
                  <p className="text-xs font-semibold text-red-900">
                    Confirmación explícita de desvinculación (RF27)
                  </p>
                  <p className="text-[11px] text-red-800">
                    Esta acción revoca los tokens de todos los dispositivos de la cuna, cierra sus sesiones TCP activas y conserva el perfil e historial del bebé. Escribe{" "}
                    <code className="font-mono font-bold">DESVINCULAR</code> para confirmar:
                  </p>
                  <div className="flex flex-wrap items-center gap-2">
                    <input
                      type="text"
                      required
                      value={unlinkConfirmText}
                      onChange={(e) => setUnlinkConfirmText(e.target.value)}
                      placeholder="Escribe DESVINCULAR"
                      className="rounded-lg border border-red-300 bg-white px-3 py-1.5 text-xs font-mono"
                    />
                    <button
                      type="submit"
                      disabled={unlinkConfirmText.trim().toUpperCase() !== "DESVINCULAR" || isUnlinking}
                      className="rounded-lg bg-red-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-red-700 disabled:opacity-50 transition-colors"
                    >
                      {isUnlinking ? "Desvinculando..." : "Confirmar desvinculación"}
                    </button>
                    <button
                      type="button"
                      onClick={() => setUnlinkingCunaId(null)}
                      className="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs text-slate-700"
                    >
                      Cancelar
                    </button>
                  </div>
                </form>
              )}

              {/* Lista visual de los 5 nodos Pico W asociados a esta cuna */}
              <div>
                <h4 className="text-xs font-semibold uppercase tracking-wider text-slate-500 mb-2">
                  Dispositivos de la cuna ({activeCribSummary.dispositivos.length})
                </h4>
                <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2 md:grid-cols-3">
                  {activeCribSummary.dispositivos.map((dev) => {
                    const enLinea = dev.estado === "en_linea";
                    const nodeMeta: Record<string, { label: string; badgeBg: string; Icon: typeof Cpu }> = {
                      central: { label: "Unidad Central", badgeBg: "bg-teal-50 text-teal-700", Icon: Cpu },
                      colchon: { label: "Colchón Sensor", badgeBg: "bg-indigo-50 text-indigo-700", Icon: BedDouble },
                      movil: { label: "Móvil Colgante", badgeBg: "bg-violet-50 text-violet-700", Icon: Sparkles },
                      biberon: { label: "Base Biberón", badgeBg: "bg-sky-50 text-sky-700", Icon: Milk },
                      juguete: { label: "Juguete Cognitivo", badgeBg: "bg-amber-50 text-amber-700", Icon: Gamepad2 },
                    };
                    const meta = nodeMeta[dev.tipo] ?? { label: dev.tipo, badgeBg: "bg-slate-100 text-slate-700", Icon: Cpu };
                    const NodeIcon = meta.Icon;
                    return (
                      <div
                        key={dev.id}
                        className="flex items-center justify-between gap-2 rounded-xl border border-slate-200 bg-white p-3 shadow-sm hover:border-slate-300 transition-colors"
                      >
                        <div className="flex items-center gap-2.5 min-w-0">
                          <div className={`flex h-9 w-9 shrink-0 items-center justify-center rounded-lg ${meta.badgeBg}`}>
                            <NodeIcon className="h-4 w-4" aria-hidden="true" />
                          </div>
                          <div className="min-w-0">
                            <p className="text-xs font-semibold text-slate-800 truncate">{meta.label}</p>
                            <p className="text-[11px] font-mono text-slate-500 truncate">
                              {dev.deviceId}
                              {dev.fwVersion ? ` · v${dev.fwVersion}` : ""}
                            </p>
                          </div>
                        </div>
                        <div className="flex flex-col items-end gap-1 shrink-0">
                          <span
                            className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-semibold ${
                              enLinea
                                ? "bg-emerald-50 text-emerald-700"
                                : "bg-slate-100 text-slate-600"
                            }`}
                          >
                            {enLinea ? (
                              <>
                                <Wifi className="h-3 w-3" aria-hidden="true" />
                                En línea
                              </>
                            ) : (
                              <>
                                <WifiOff className="h-3 w-3" aria-hidden="true" />
                                Offline
                              </>
                            )}
                          </span>
                          {dev.bateriaPct !== null && dev.bateriaPct !== undefined && (
                            <span
                              className={`inline-flex items-center gap-1 text-[10px] font-medium ${
                                dev.bateriaPct < 20 ? "text-red-600" : "text-slate-500"
                              }`}
                            >
                              <Battery className="h-3 w-3" aria-hidden="true" />
                              {dev.bateriaPct}%
                            </span>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>

              {/* M5: Panel reactivo de Cuna en Vivo (< 1 s, IU-01, RF04, RF05, RF06, RF07, RF08, RF09, RF20, RF29) */}
              <LiveCribPanel
                accessToken={accessToken}
                cunaId={activeCribSummary.cuna.id}
                activeProfileId={activeProfileId}
                userRole={userRole}
                onError={onError}
                onCribUpdated={loadData}
              />

              {/* M7: Alimentación (RF15–RF17, RF37), Canciones (RF12), Móvil y Juguetes (RF13, RF33–RF36) e Historiales/Exportación (RF30–RF32) */}
              <FeedingToysAndHistoryPanel
                accessToken={accessToken}
                cunaId={activeCribSummary.cuna.id}
                bebeId={activeCribSummary.bebe?.id ?? babies[0]?.id ?? null}
                activeProfileId={activeProfileId}
                onError={onError}
              />

              {/* RF11: Intercomunicador Push-to-Talk para la cuna seleccionada */}
              <IntercomPushToTalk
                accessToken={accessToken}
                cunaId={activeCribSummary.cuna.id}
              />
            </div>
          )
        )}
      </section>

      <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
        {/* RF24: Ventana de Emparejamiento y Reclamo de Dispositivos */}
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
                onClick={handleOpenPairingWindow}
                className="w-full rounded-xl border border-teal-600 bg-teal-50 px-4 py-2 text-xs font-medium text-teal-800 hover:bg-teal-100"
              >
                Abrir ventana de emparejamiento (10 min)
              </button>

              {pairingWindow && (
                <p className="rounded-lg bg-emerald-50 px-3 py-1.5 text-[11px] font-medium text-emerald-800">
                  Ventana activa hasta: {new Date(pairingWindow.expiraEn).toLocaleTimeString()}
                </p>
              )}

              <form onSubmit={handleClaimDevice} className="space-y-3 pt-2 border-t border-slate-100">
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
                  onClick={handleCopySecretsSnippet}
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

        {/* RF28: Perfil y Datos del Bebé */}
        <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Baby className="h-5 w-5 text-teal-600" aria-hidden="true" />
              <h2 className="text-base font-semibold text-slate-900">
                Perfil del Bebé (RF28)
              </h2>
            </div>
          </div>

          {babies.length > 0 && (
            <ul className="divide-y divide-slate-100">
              {babies.map((b) => {
                const cunaAsignada = cribs.find((c) => c.cuna.id === b.cunaId)?.cuna.nombre;
                return (
                  <li key={b.id} className="flex items-center justify-between py-2.5">
                    <div>
                      <p className="text-sm font-medium text-slate-800">{b.nombre}</p>
                      <p className="text-[11px] text-slate-500">
                        Nacimiento: {b.fechaNacimiento} · Cuna:{" "}
                        {cunaAsignada ?? "Desvinculada (historial conservado)"}
                      </p>
                      {b.notas && (
                        <p className="text-[11px] text-slate-600 mt-0.5">
                          Notas: {b.notas}
                        </p>
                      )}
                    </div>
                    {userRole === "principal" && (
                      <button
                        type="button"
                        onClick={() => startEditBaby(b)}
                        className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium text-teal-700 hover:bg-teal-50"
                      >
                        <Edit3 className="h-3.5 w-3.5" aria-hidden="true" />
                        Editar
                      </button>
                    )}
                  </li>
                );
              })}
            </ul>
          )}

          {isPrincipal ? (
            <form onSubmit={handleSaveBaby} className="space-y-3 pt-2 border-t border-slate-100">
              <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-500">
                {editingBabyId ? "Actualizar perfil del bebé" : "Registrar perfil de bebé"}
              </h3>

              <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                <div>
                  <label htmlFor="baby-name" className="block text-xs font-medium text-slate-700 mb-1">
                    Nombre del bebé (1–60 caracteres)
                  </label>
                  <input
                    id="baby-name"
                    type="text"
                    required
                    maxLength={60}
                    value={babyName}
                    onChange={(e) => setBabyName(e.target.value)}
                    placeholder="Ej. Bebé Sofía"
                    className="w-full rounded-lg border border-slate-300 px-3 py-1.5 text-xs"
                  />
                </div>

                <div>
                  <label htmlFor="baby-birthdate" className="block text-xs font-medium text-slate-700 mb-1">
                    Fecha de nacimiento
                  </label>
                  <input
                    id="baby-birthdate"
                    type="date"
                    required
                    value={babyBirthDate}
                    onChange={(e) => setBabyBirthDate(e.target.value)}
                    className="w-full rounded-lg border border-slate-300 px-3 py-1.5 text-xs"
                  />
                </div>
              </div>

              <div>
                <label htmlFor="baby-cuna" className="block text-xs font-medium text-slate-700 mb-1">
                  Asociar a cuna
                </label>
                <select
                  id="baby-cuna"
                  required={cribs.length > 0}
                  value={babyCunaId || selectedCunaId || cribs[0]?.cuna.id || ""}
                  onChange={(e) => setBabyCunaId(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-2.5 py-1.5 text-xs"
                >
                  {cribs.length === 0 ? (
                    <option value="">Registra primero una cuna para asociar</option>
                  ) : (
                    cribs.map((c) => (
                      <option key={c.cuna.id} value={c.cuna.id}>
                        {c.cuna.nombre}
                      </option>
                    ))
                  )}
                </select>
              </div>

              <div>
                <label htmlFor="baby-notes" className="block text-xs font-medium text-slate-700 mb-1">
                  Notas médicas u observaciones (opcional)
                </label>
                <input
                  id="baby-notes"
                  type="text"
                  maxLength={1000}
                  value={babyMedicalNotes}
                  onChange={(e) => setBabyMedicalNotes(e.target.value)}
                  placeholder="Alergias, pediatra, observaciones..."
                  className="w-full rounded-lg border border-slate-300 px-3 py-1.5 text-xs"
                />
              </div>

              <div className="flex items-center gap-2">
                <button
                  type="submit"
                  disabled={isSavingBaby || cribs.length === 0}
                  className="inline-flex items-center gap-1.5 rounded-xl bg-teal-600 px-4 py-2 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors"
                >
                  <PlusCircle className="h-3.5 w-3.5" aria-hidden="true" />
                  {isSavingBaby
                    ? "Guardando..."
                    : editingBabyId
                    ? "Guardar cambios del bebé"
                    : "Registrar bebé"}
                </button>
                {editingBabyId && (
                  <button
                    type="button"
                    onClick={() => {
                      setEditingBabyId(null);
                      setBabyName("");
                      setBabyMedicalNotes("");
                    }}
                    className="rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs text-slate-700"
                  >
                    Cancelar edición
                  </button>
                )}
              </div>
            </form>
          ) : (
            <p className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-600">
              Solo el Usuario Principal puede registrar o editar perfiles del bebé (RF03 / RF28).
            </p>
          )}
        </section>
      </div>
    </div>
  );
}
