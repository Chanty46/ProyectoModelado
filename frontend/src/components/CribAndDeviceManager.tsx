import { useCallback, useEffect, useState } from "react";
import {
  accountApi,
  ApiError,
  type BabyRecord,
  type CribSummaryResponse,
} from "../lib/api";
import { canManagePrincipalActions } from "../lib/formatters";
import { IntercomPushToTalk } from "./IntercomPushToTalk";
import { LiveCribPanel } from "./LiveCribPanel";
import { FeedingToysAndHistoryPanel } from "./FeedingToysAndHistoryPanel";
import { CribOverviewHeader } from "./cribs/CribOverviewHeader";
import { DeviceNodeGrid } from "./cribs/DeviceNodeGrid";
import { DevicePairingPanel } from "./cribs/DevicePairingPanel";
import { BabyProfilePanel } from "./cribs/BabyProfilePanel";
import { DemoSimulatorCard } from "./live/DemoSimulatorCard";

interface CribAndDeviceManagerProps {
  accessToken: string;
  activeProfileId: string | null;
  userRole: "principal" | "secundario" | null;
  localLoginEnabled?: boolean;
  onError: (msg: string | null) => void;
}

/**
 * Orquestador principal de cunas, nodos Pico W, telemetría en vivo,
 * alimentación/juguetes, intercomunicador, emparejamiento (RF24) y perfiles de bebé (RF28).
 */
export function CribAndDeviceManager({
  accessToken,
  activeProfileId,
  userRole,
  localLoginEnabled = false,
  onError,
}: CribAndDeviceManagerProps) {
  const [cribs, setCribs] = useState<CribSummaryResponse[]>([]);
  const [babies, setBabies] = useState<BabyRecord[]>([]);
  const [selectedCunaId, setSelectedCunaId] = useState<string | null>(null);
  const [demoRefreshTick, setDemoRefreshTick] = useState<number>(0);

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
      } else {
        setSelectedCunaId(null);
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

  const isPrincipal = canManagePrincipalActions(userRole);
  const activeCribSummary = cribs.find((c) => c.cuna.id === selectedCunaId) ?? null;

  return (
    <div className="space-y-6">
      {/* RF25 / RF26 / RF27: Cunas y Dispositivos Asociados */}
      <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-5">
        <CribOverviewHeader
          accessToken={accessToken}
          cribs={cribs}
          selectedCunaId={selectedCunaId}
          activeCribSummary={activeCribSummary}
          isPrincipal={isPrincipal}
          onSelectCrib={setSelectedCunaId}
          onDataChanged={loadData}
          onError={onError}
        />

        {activeCribSummary && (
          <div className="space-y-4">
            <DeviceNodeGrid devices={activeCribSummary.dispositivos} />

            {/* M5: Panel reactivo de Cuna en Vivo (< 1 s, IU-01, RF04–RF09, RF20, RF29) */}
            <LiveCribPanel
              accessToken={accessToken}
              cunaId={activeCribSummary.cuna.id}
              activeProfileId={activeProfileId}
              userRole={userRole}
              refreshSignal={demoRefreshTick}
              onError={onError}
              onCribUpdated={loadData}
            />

            {localLoginEnabled && (
              <DemoSimulatorCard
                accessToken={accessToken}
                cunaId={activeCribSummary.cuna.id}
                onSimulated={() => {
                  setDemoRefreshTick((t) => t + 1);
                  void loadData();
                }}
                onError={onError}
              />
            )}

            {/* M7: Alimentación (RF15–RF17, RF37), Canciones (RF12), Móvil (RF13) e Historiales (RF30–RF32) */}
            <FeedingToysAndHistoryPanel
              accessToken={accessToken}
              cunaId={activeCribSummary.cuna.id}
              bebeId={activeCribSummary.bebe?.id ?? babies[0]?.id ?? null}
              activeProfileId={activeProfileId}
              refreshSignal={demoRefreshTick}
              onError={onError}
            />

            {/* RF11: Intercomunicador Push-to-Talk para la cuna seleccionada */}
            <IntercomPushToTalk
              accessToken={accessToken}
              cunaId={activeCribSummary.cuna.id}
            />
          </div>
        )}
      </section>

      <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
        <DevicePairingPanel
          accessToken={accessToken}
          cribs={cribs}
          isPrincipal={isPrincipal}
          onClaimed={loadData}
          onError={onError}
        />

        <BabyProfilePanel
          accessToken={accessToken}
          babies={babies}
          cribs={cribs}
          selectedCunaId={selectedCunaId}
          isPrincipal={isPrincipal}
          onSaved={loadData}
          onError={onError}
        />
      </div>
    </div>
  );
}
