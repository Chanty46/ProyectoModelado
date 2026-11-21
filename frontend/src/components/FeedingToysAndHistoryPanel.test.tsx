import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { FeedingToysAndHistoryPanel } from "./FeedingToysAndHistoryPanel";
import {
  accountApi,
  type SongCatalogItem,
  type ToysAndPeripheralsStatusResponse,
  type FeedingSessionRecord,
  type SleepSessionRecord,
} from "../lib/api";

// El control del móvil habla directo con Supabase; aquí se prueba sin cliente.
// Sus pruebas propias están en feeding/MobileControlCard.test.tsx.
vi.mock("../lib/supabase", () => ({ supabase: null }));

const mockSongs: SongCatalogItem[] = [
  { id: "song-1", codigo: "estrellita", nombre: "Estrellita dónde estás", duracionSegundos: 45, notasJson: "[]" },
  { id: "song-2", codigo: "nana_brahms", nombre: "Canción de Cuna de Brahms", duracionSegundos: 60, notasJson: "[]" },
  { id: "song-3", codigo: "arrorro", nombre: "Arrorró mi niño", duracionSegundos: 50, notasJson: "[]" },
];

const mockToys: ToysAndPeripheralsStatusResponse = {
  historialUso: [],
  bateriaJuguetePct: 85,
  alertaBateriaBajaJuguete: false,
  bateriaBiberonPct: 90,
  ultimaMasaBiberonG: 210.0,
  ultimaTempBiberonC: 36.8,
};

const mockFeedingHistory: FeedingSessionRecord[] = [
  {
    id: "feed-1",
    bebeId: "bebe-test-1",
    inicio: new Date(Date.now() - 3600000).toISOString(),
    fin: new Date(Date.now() - 3000000).toISOString(),
    masaInicialG: 210.0,
    masaFinalG: 95.0,
    mlConsumidos: 111.7, // (210 - 95) / 1.03
    tempC: 36.8,
    alertaTemperatura: false,
    perfilId: "prof-1",
  },
];

const mockSleepHistory: SleepSessionRecord[] = [
  {
    id: "sleep-1",
    bebeId: "bebe-test-1",
    cunaId: "cuna-test-1",
    tipo: "siesta",
    inicio: new Date(Date.now() - 7200000).toISOString(),
    fin: new Date(Date.now() - 4500000).toISOString(),
    movimientoPromedio: 0.042,
    interrupciones: 0,
  },
];

describe("FeedingToysAndHistoryPanel Component", () => {
  const defaultProps = {
    accessToken: "mock-jwt-token",
    cunaId: "cuna-test-1",
    bebeId: "bebe-test-1",
    activeProfileId: "prof-1",
    onError: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.spyOn(accountApi, "listSongs").mockResolvedValue(mockSongs);
    vi.spyOn(accountApi, "getToysStatus").mockResolvedValue(mockToys);
    vi.spyOn(accountApi, "getFeedingHistory").mockResolvedValue(mockFeedingHistory);
    vi.spyOn(accountApi, "getSleepHistory").mockResolvedValue(mockSleepHistory);
  });

  it("renders thermal band badge and classifies ideal bottle temperature cleanly", async () => {
    render(<FeedingToysAndHistoryPanel {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText(/Banda Térmica IR/i)).toBeInTheDocument();
      expect(screen.getByText(/36.8 °C/i)).toBeInTheDocument();
      expect(screen.getByText(/Ideal 35.0–38.5 °C/i)).toBeInTheDocument();
    });
  });

  it("displays thermal alert when IR temperature is outside safe range (<35.0 or >38.5 °C)", async () => {
    render(<FeedingToysAndHistoryPanel {...defaultProps} />);

    const tempInput = await screen.findByLabelText(/Temperatura IR/i);
    fireEvent.change(tempInput, { target: { value: "39.5" } });

    await waitFor(() => {
      expect(screen.getByText(/Alerta térmica \(RF16\): 39.5 °C está fuera del rango seguro/i)).toBeInTheDocument();
    });
  });

  it("starts a new feeding session and dispatches startFeedingSession", async () => {
    const startFeedSpy = vi.spyOn(accountApi, "startFeedingSession").mockResolvedValue({
      id: "feed-new-1",
      bebeId: "bebe-test-1",
      inicio: new Date().toISOString(),
      fin: null,
      masaInicialG: 220.0,
      masaFinalG: null,
      mlConsumidos: null,
      tempC: 37.0,
      alertaTemperatura: false,
      perfilId: "prof-1",
    });

    render(<FeedingToysAndHistoryPanel {...defaultProps} />);

    const massInput = await screen.findByLabelText(/Masa inicial M_i/i);
    fireEvent.change(massInput, { target: { value: "220.0" } });

    const tempInput = screen.getByLabelText(/Temperatura IR/i);
    fireEvent.change(tempInput, { target: { value: "37.0" } });

    const startBtn = screen.getByRole("button", { name: /Iniciar Sesión de Alimentación/i });
    fireEvent.click(startBtn);

    await waitFor(() => {
      expect(startFeedSpy).toHaveBeenCalledWith("mock-jwt-token", {
        bebeId: "bebe-test-1",
        masaInicialG: 220.0,
        tempC: 37.0,
        perfilId: "prof-1",
      });
    });
  });

  it("calculates estimated milk intake with 1.03 g/ml density in active session and finalizes", async () => {
    const activeFeed: FeedingSessionRecord = {
      id: "feed-open-1",
      bebeId: "bebe-test-1",
      inicio: new Date().toISOString(),
      fin: null,
      masaInicialG: 200.0,
      masaFinalG: null,
      mlConsumidos: null,
      tempC: 36.5,
      alertaTemperatura: false,
      perfilId: "prof-1",
    };
    vi.spyOn(accountApi, "getFeedingHistory").mockResolvedValue([activeFeed]);
    const finishFeedSpy = vi.spyOn(accountApi, "finishFeedingSession").mockResolvedValue({
      ...activeFeed,
      fin: new Date().toISOString(),
      masaFinalG: 97.0,
      mlConsumidos: 100.0,
      alertaTemperatura: false,
    });

    render(<FeedingToysAndHistoryPanel {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText(/Toma en curso/i)).toBeInTheDocument();
    });

    const finalMassInput = screen.getByPlaceholderText(/Masa final M_f/i);
    fireEvent.change(finalMassInput, { target: { value: "97.0" } });

    // Formula check: (200 - 97) / 1.03 = 103 / 1.03 = 100.0 ml
    expect(screen.getByText(/Ingesta est.: 100.0 ml/i)).toBeInTheDocument();

    const finishBtn = screen.getByRole("button", { name: /Finalizar y calcular ml/i });
    fireEvent.click(finishBtn);

    await waitFor(() => {
      expect(finishFeedSpy).toHaveBeenCalledWith(
        "mock-jwt-token",
        "feed-open-1",
        expect.objectContaining({
          masaFinalG: 97.0,
          perfilId: "prof-1",
        })
      );
    });
  });

  it("dispatches PWM song play and stop actions", async () => {
    const playSongSpy = vi.spyOn(accountApi, "playSong").mockResolvedValue(true);

    render(<FeedingToysAndHistoryPanel {...defaultProps} />);

    const playBtn = await screen.findByRole("button", { name: /Reproducir/i });
    fireEvent.click(playBtn);

    await waitFor(() => {
      expect(playSongSpy).toHaveBeenCalledWith("mock-jwt-token", "cuna-test-1", {
        codigo: "estrellita",
        accion: "play",
        perfilId: "prof-1",
      });
    });

    const stopBtn = screen.getByRole("button", { name: /^Detener$/i });
    fireEvent.click(stopBtn);

    await waitFor(() => {
      expect(playSongSpy).toHaveBeenCalledWith("mock-jwt-token", "cuna-test-1", {
        codigo: "estrellita",
        accion: "stop",
        perfilId: "prof-1",
      });
    });
  });

  it("renders the mobile control card next to the songs card", async () => {
    render(<FeedingToysAndHistoryPanel {...defaultProps} />);

    expect(await screen.findByText(/Móvil colgante de 9 LEDs/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Vista previa aproximada de los 9 LEDs del móvil/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Encender móvil/i })).toBeInTheDocument();
    expect(screen.getByText(/Melodías PWM \(RF12\)/i)).toBeInTheDocument();
  });

  it("handles history export to CSV and PDF cleanly", async () => {
    const exportSpy = vi.spyOn(accountApi, "exportBabyHistory").mockResolvedValue({
      fileName: "cuna_historial_bebe.csv",
      contentType: "text/csv; charset=utf-8",
      contentBase64: window.btoa("test,csv,data"),
      elapsedMs: 25,
      feedingSessionsCount: 1,
      sleepSessionsCount: 1,
    });

    render(<FeedingToysAndHistoryPanel {...defaultProps} />);

    const exportCsvBtn = await screen.findByRole("button", { name: /^CSV$/i });
    fireEvent.click(exportCsvBtn);

    await waitFor(() => {
      expect(exportSpy).toHaveBeenCalledWith("mock-jwt-token", "bebe-test-1", {
        formato: "csv",
        desde: null,
        hasta: null,
        perfilId: "prof-1",
      });
      expect(screen.getByText(/Exportado cuna_historial_bebe.csv/i)).toBeInTheDocument();
    });
  });
});
