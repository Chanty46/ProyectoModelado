import { useCallback, useEffect, useState } from "react";
import {
  accountApi,
  ApiError,
  type FeedingSessionRecord,
  type SleepSessionRecord,
  type SongCatalogItem,
  type ToysAndPeripheralsStatusResponse,
} from "../lib/api";
import { FeedingSessionCard } from "./feeding/FeedingSessionCard";
import { SongsCard } from "./feeding/SongsCard";
import { MobileControlCard } from "./feeding/MobileControlCard";
import { HistoryAndExportCard } from "./feeding/HistoryAndExportCard";

interface FeedingToysAndHistoryPanelProps {
  accessToken: string;
  cunaId: string;
  bebeId: string | null;
  activeProfileId: string | null;
  refreshSignal?: number;
  onError: (msg: string | null) => void;
}

/**
 * Panel integrador de nutrición infantil (RF15–RF17, RF37), estímulos sensoriales
 * (RF12, RF13, RF33–RF36) e historiales exportables (RF30–RF32).
 */
export function FeedingToysAndHistoryPanel({
  accessToken,
  cunaId,
  bebeId,
  activeProfileId,
  refreshSignal = 0,
  onError,
}: FeedingToysAndHistoryPanelProps) {
  const [songs, setSongs] = useState<SongCatalogItem[]>([]);
  const [selectedSongCode, setSelectedSongCode] = useState<string>("estrellita");
  const [toysStatus, setToysStatus] = useState<ToysAndPeripheralsStatusResponse | null>(null);

  const [initialMassG, setInitialMassG] = useState<string>("210.0");
  const [foodTempC, setFoodTempC] = useState<string>("36.8");
  const [activeFeedingSession, setActiveFeedingSession] = useState<FeedingSessionRecord | null>(null);

  const [feedingHistory, setFeedingHistory] = useState<FeedingSessionRecord[]>([]);
  const [sleepHistory, setSleepHistory] = useState<SleepSessionRecord[]>([]);
  const [filterFrom, setFilterFrom] = useState<string>("");
  const [filterTo, setFilterTo] = useState<string>("");

  const loadCribPeripherals = useCallback(
    async (syncBottleInputs = false) => {
      try {
        const [songList, toys] = await Promise.all([
          accountApi.listSongs(accessToken, cunaId),
          accountApi.getToysStatus(accessToken, cunaId),
        ]);
        setSongs(songList);
        if (songList.length > 0) {
          setSelectedSongCode((prev) =>
            songList.some((s) => s.codigo === prev) ? prev : songList[0].codigo
          );
        }
        setToysStatus(toys);
        // Solo pre-carga la última lectura de biberón al montar o cambiar de cuna para no pisar la edición manual
        if (syncBottleInputs) {
          if (toys.ultimaMasaBiberonG !== null) {
            setInitialMassG(toys.ultimaMasaBiberonG.toString());
          }
          if (toys.ultimaTempBiberonC !== null) {
            setFoodTempC(toys.ultimaTempBiberonC.toString());
          }
        }
      } catch (err) {
        if (err instanceof ApiError) {
          onError(err.message);
        }
      }
    },
    [accessToken, cunaId, onError]
  );

  const loadBabyHistories = useCallback(async () => {
    if (!bebeId) {
      setFeedingHistory([]);
      setSleepHistory([]);
      return;
    }
    try {
      const [feeds, sleeps] = await Promise.all([
        accountApi.getFeedingHistory(
          accessToken,
          bebeId,
          filterFrom || undefined,
          filterTo || undefined
        ),
        accountApi.getSleepHistory(
          accessToken,
          bebeId,
          filterFrom || undefined,
          filterTo || undefined
        ),
      ]);
      setFeedingHistory(feeds);
      setSleepHistory(sleeps);
      const openSession = feeds.find((f) => f.fin === null) ?? null;
      setActiveFeedingSession(openSession);
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }, [accessToken, bebeId, filterFrom, filterTo, onError]);

  useEffect(() => {
    void loadCribPeripherals(true);
    const timer = window.setInterval(() => {
      void loadCribPeripherals(false);
    }, 4000);
    return () => window.clearInterval(timer);
  }, [loadCribPeripherals]);

  useEffect(() => {
    void loadCribPeripherals(false);
    void loadBabyHistories();
  }, [refreshSignal, loadCribPeripherals, loadBabyHistories]);

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <FeedingSessionCard
          accessToken={accessToken}
          bebeId={bebeId}
          activeProfileId={activeProfileId}
          initialMassG={initialMassG}
          foodTempC={foodTempC}
          activeFeedingSession={activeFeedingSession}
          onInitialMassChange={setInitialMassG}
          onFoodTempChange={setFoodTempC}
          onSessionStarted={async (created) => {
            setActiveFeedingSession(created);
            await loadBabyHistories();
          }}
          onSessionFinished={async () => {
            setActiveFeedingSession(null);
            await loadBabyHistories();
          }}
          onError={onError}
        />

        <div className="space-y-4">
          <MobileControlCard cunaId={cunaId} />
          <SongsCard
            accessToken={accessToken}
            cunaId={cunaId}
            activeProfileId={activeProfileId}
            songs={songs}
            selectedSongCode={selectedSongCode}
            toysStatus={toysStatus}
            onSelectSong={setSelectedSongCode}
            onError={onError}
          />
        </div>
      </div>

      <HistoryAndExportCard
        accessToken={accessToken}
        cunaId={cunaId}
        bebeId={bebeId}
        activeProfileId={activeProfileId}
        filterFrom={filterFrom}
        filterTo={filterTo}
        feedingHistory={feedingHistory}
        sleepHistory={sleepHistory}
        toyUsageHistory={toysStatus?.historialUso ?? []}
        onFilterFromChange={setFilterFrom}
        onFilterToChange={setFilterTo}
        onHistoryChanged={loadBabyHistories}
        onError={onError}
      />
    </div>
  );
}
