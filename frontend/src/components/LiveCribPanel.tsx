import { canManagePrincipalActions } from "../lib/formatters";
import { useLiveCribTelemetry } from "../hooks/useLiveCribTelemetry";
import { LivePanelSkeleton } from "./live/LivePanelSkeleton";
import { CribStatusBanner } from "./live/CribStatusBanner";
import { TelemetryMetricCards } from "./live/TelemetryMetricCards";
import { NightLightControlCard } from "./live/NightLightControlCard";
import { ThresholdsConfigCard } from "./live/ThresholdsConfigCard";
import { TelemetryTrendChart } from "./live/TelemetryTrendChart";
import { RecentAlertsList } from "./live/RecentAlertsList";

interface LiveCribPanelProps {
  accessToken: string;
  cunaId: string;
  activeProfileId: string | null;
  userRole: "principal" | "secundario" | null;
  refreshSignal?: number;
  onError: (msg: string | null) => void;
  onCribUpdated: () => void;
}

/**
 * Panel reactivo de supervisión en tiempo real de una cuna (M5: IU-01, RF04–RF09, RF20, RF29).
 * Coordina el estado vivo vía `useLiveCribTelemetry` y delega la presentación a tarjetas modulares.
 */
export function LiveCribPanel({
  accessToken,
  cunaId,
  activeProfileId,
  userRole,
  refreshSignal = 0,
  onError,
  onCribUpdated,
}: LiveCribPanelProps) {
  const {
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
  } = useLiveCribTelemetry({
    accessToken,
    cunaId,
    activeProfileId,
    refreshSignal,
    onError,
    onCribUpdated,
  });

  if (!live) {
    return <LivePanelSkeleton />;
  }

  const latest = live.ultimaTelemetria;
  const presenceThresholdKg = Number((1.0 * live.umbrales.escalaMasa).toFixed(2));
  const babyPresent =
    latest?.masaKg !== null &&
    latest?.masaKg !== undefined &&
    latest.masaKg > presenceThresholdKg;

  return (
    <div className="space-y-4 rounded-xl border border-slate-200 bg-slate-50/70 p-4">
      <CribStatusBanner
        crib={live.cuna}
        babyPresent={babyPresent}
        enVentanaGraciaRetiro={live.enVentanaGraciaRetiro}
        countdownNowMs={countdownNowMs}
        onToggleMode={(mode) => void handleToggleMode(mode)}
      />

      <TelemetryMetricCards
        latest={latest}
        thresholds={live.umbrales}
        babyPresent={babyPresent}
        presenceThresholdKg={presenceThresholdKg}
      />

      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <NightLightControlCard
          luzEncendida={live.cuna.luzEncendida}
          luzNivel={live.cuna.luzNivel}
          lightLevel={lightLevel}
          onSliderChange={handleLightSliderChange}
          onApplyLight={(encendida, nivel) => void handleApplyLight(encendida, nivel)}
        />

        <ThresholdsConfigCard
          currentThresholds={live.umbrales}
          isPrincipal={canManagePrincipalActions(userRole)}
          showForm={showThresholdsForm}
          formValues={thresholds}
          onToggleForm={handleToggleThresholdsForm}
          onChangeField={(patch) => setThresholds((prev) => ({ ...prev, ...patch }))}
          onSubmit={(e) => void handleSaveThresholds(e)}
        />
      </div>

      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <TelemetryTrendChart samples={live.telemetriaReciente} />
        <RecentAlertsList alerts={live.alertasRecientes} />
      </div>
    </div>
  );
}
