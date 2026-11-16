import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { LiveCribPanel } from "./LiveCribPanel";
import { accountApi, type LiveCribStatusResponse } from "../lib/api";

function createMockLiveStatus(overrides?: Partial<LiveCribStatusResponse>): LiveCribStatusResponse {
  return {
    cuna: {
      id: "cuna-test-1",
      familyId: "fam-test-1",
      nombre: "Cuna Principal Bebé",
      estadoConexion: "en_linea",
      modoOperacion: "monitoreo",
      modoAtencionExpiraEn: null,
      luzEncendida: true,
      luzNivel: 60,
      escalaMasa: 0.2,
      ultimoHeartbeat: new Date().toISOString(),
      creadoEn: new Date().toISOString(),
    },
    umbrales: {
      cunaId: "cuna-test-1",
      familyId: "fam-test-1",
      escalaMasa: 0.2,
      tempMin: 18.0,
      tempMax: 26.0,
      humMin: 40.0,
      humMax: 60.0,
      movimientoUmbral: 0.15,
      movimientoAlertaActiva: true,
      ruidoMasaPiso: 0.01,
    },
    ultimaTelemetria: {
      cunaId: "cuna-test-1",
      dispositivoId: "pico-central-1",
      ts: new Date().toISOString(),
      masaKg: 3.85,
      indiceMovimiento: 0.045,
      tempC: 22.4,
      humedad: 48.5,
      masaBiberonG: 180.0,
      tempBiberonC: 36.5,
      bateriaPct: 95,
      seq: 101,
    },
    telemetriaReciente: [
      {
        cunaId: "cuna-test-1",
        dispositivoId: "pico-central-1",
        ts: new Date().toISOString(),
        masaKg: 3.85,
        indiceMovimiento: 0.045,
        tempC: 22.4,
        humedad: 48.5,
        masaBiberonG: null,
        tempBiberonC: null,
        bateriaPct: 95,
        seq: 101,
      },
    ],
    alertasRecientes: [
      {
        id: "alert-1",
        cunaId: "cuna-test-1",
        dispositivoId: "pico-central-1",
        tipo: "movimiento_brusco",
        severidad: "advertencia",
        mensaje: "Movimiento continuo detectado en cuna",
        datosJson: null,
        ts: new Date().toISOString(),
      },
      {
        id: "alert-2",
        cunaId: "cuna-test-1",
        dispositivoId: "pico-central-1",
        tipo: "retiro_imprevisto",
        severidad: "critica",
        mensaje: "Retiro sin confirmación previa de cuidador",
        datosJson: null,
        ts: new Date().toISOString(),
      },
    ],
    dispositivos: [],
    enVentanaGraciaRetiro: false,
    ...overrides,
  };
}

describe("LiveCribPanel Component", () => {
  const defaultProps = {
    accessToken: "mock-jwt-token",
    cunaId: "cuna-test-1",
    activeProfileId: "prof-1",
    userRole: "principal" as const,
    onError: vi.fn(),
    onCribUpdated: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders animated skeleton loader during initial fetch", () => {
    // Return pending promise
    vi.spyOn(accountApi, "getLiveCribStatus").mockReturnValue(new Promise(() => {}));

    render(<LiveCribPanel {...defaultProps} />);

    expect(screen.getByRole("status", { name: /cargando telemetría/i })).toBeInTheDocument();
  });

  it("renders telemetry values, comfort bands, and baby presence badge cleanly", async () => {
    const mockData = createMockLiveStatus();
    vi.spyOn(accountApi, "getLiveCribStatus").mockResolvedValue(mockData);

    render(<LiveCribPanel {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText("22.4 °C")).toBeInTheDocument();
      expect(screen.getByText("48.5 %")).toBeInTheDocument();
      expect(screen.getByText("3.850 kg")).toBeInTheDocument();
      expect(screen.getByText("0.045 kg/m")).toBeInTheDocument();
      expect(screen.getByText("Presencia detectada")).toBeInTheDocument();
      expect(screen.getByText("Supervisión Perimetral Activa (Monitoreo)")).toBeInTheDocument();
    });
  });

  it("classifies temperature comfort band as low or high outside thresholds", async () => {
    const mockData = createMockLiveStatus({
      ultimaTelemetria: {
        cunaId: "cuna-test-1",
        dispositivoId: "pico-central-1",
        ts: new Date().toISOString(),
        masaKg: 0.05,
        indiceMovimiento: 0.01,
        tempC: 15.2, // Below 18.0 min
        humedad: 75.0, // Above 60.0 max
        masaBiberonG: null,
        tempBiberonC: null,
        bateriaPct: 90,
        seq: 102,
      },
    });
    vi.spyOn(accountApi, "getLiveCribStatus").mockResolvedValue(mockData);

    render(<LiveCribPanel {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText("15.2 °C")).toBeInTheDocument();
      expect(screen.getByText("75.0 %")).toBeInTheDocument();
      expect(screen.getByText(/Sin presencia/i)).toBeInTheDocument();
    });
  });

  it("renders attention mode with active 15-minute countdown and reanudar button", async () => {
    const futureExp = new Date(Date.now() + 14 * 60 * 1000 + 45 * 1000).toISOString();
    const mockData = createMockLiveStatus({
      cuna: {
        id: "cuna-test-1",
        familyId: "fam-test-1",
        nombre: "Cuna Principal",
        estadoConexion: "en_linea",
        modoOperacion: "atencion",
        modoAtencionExpiraEn: futureExp,
        luzEncendida: false,
        luzNivel: 0,
        escalaMasa: 0.2,
        ultimoHeartbeat: new Date().toISOString(),
        creadoEn: new Date().toISOString(),
      },
    });
    vi.spyOn(accountApi, "getLiveCribStatus").mockResolvedValue(mockData);

    render(<LiveCribPanel {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText(/Pausa de Seguridad — Modo Atención/i)).toBeInTheDocument();
      expect(screen.getByText(/Restante: 14:/i)).toBeInTheDocument();
      expect(screen.getByRole("button", { name: /Reanudar Monitoreo/i })).toBeInTheDocument();
    });
  });

  it("handles mode toggle click to switch between monitoreo and atencion", async () => {
    const mockData = createMockLiveStatus({
      cuna: {
        id: "cuna-test-1",
        familyId: "fam-test-1",
        nombre: "Cuna Principal",
        estadoConexion: "en_linea",
        modoOperacion: "monitoreo",
        modoAtencionExpiraEn: null,
        luzEncendida: true,
        luzNivel: 50,
        escalaMasa: 0.2,
        ultimoHeartbeat: new Date().toISOString(),
        creadoEn: new Date().toISOString(),
      },
    });
    vi.spyOn(accountApi, "getLiveCribStatus").mockResolvedValue(mockData);
    const setModeSpy = vi.spyOn(accountApi, "setCribMode").mockResolvedValue({
      ...mockData.cuna,
      modoOperacion: "atencion",
      modoAtencionExpiraEn: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
    });

    render(<LiveCribPanel {...defaultProps} />);

    const atencionBtn = await screen.findByRole("button", { name: /Activar Modo Atención/i });
    fireEvent.click(atencionBtn);

    await waitFor(() => {
      expect(setModeSpy).toHaveBeenCalledWith("mock-jwt-token", "cuna-test-1", {
        modo: "atencion",
        perfilId: "prof-1",
      });
      expect(defaultProps.onCribUpdated).toHaveBeenCalled();
    });
  });

  it("updates night light slider and dispatches setCribLight upon application", async () => {
    const mockData = createMockLiveStatus();
    vi.spyOn(accountApi, "getLiveCribStatus").mockResolvedValue(mockData);
    const setLightSpy = vi.spyOn(accountApi, "setCribLight").mockResolvedValue({
      ...mockData.cuna,
      luzEncendida: true,
      luzNivel: 80,
    });

    render(<LiveCribPanel {...defaultProps} />);

    const slider = await screen.findByLabelText(/Intensidad de luz nocturna/i);
    fireEvent.change(slider, { target: { value: "80" } });

    const applyBtn = screen.getByRole("button", { name: /Aplicar 80 %/i });
    fireEvent.click(applyBtn);

    await waitFor(() => {
      expect(setLightSpy).toHaveBeenCalledWith("mock-jwt-token", "cuna-test-1", {
        encendida: true,
        nivel: 80,
        perfilId: "prof-1",
      });
    });
  });

  it("renders alerts feed with severity badges", async () => {
    const mockData = createMockLiveStatus();
    vi.spyOn(accountApi, "getLiveCribStatus").mockResolvedValue(mockData);

    render(<LiveCribPanel {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText("Movimiento continuo detectado en cuna")).toBeInTheDocument();
      expect(screen.getByText("ADVERTENCIA")).toBeInTheDocument();
      expect(screen.getByText("Retiro sin confirmación previa de cuidador")).toBeInTheDocument();
      expect(screen.getByText("CRITICA")).toBeInTheDocument();
    });
  });

  it("opens thresholds configuration form and submits updated thresholds", async () => {
    const mockData = createMockLiveStatus();
    vi.spyOn(accountApi, "getLiveCribStatus").mockResolvedValue(mockData);
    const updateThresholdsSpy = vi.spyOn(accountApi, "updateCribThresholds").mockResolvedValue({
      ...mockData.umbrales,
      tempMin: 19.5,
    });

    render(<LiveCribPanel {...defaultProps} />);

    const configBtn = await screen.findByRole("button", { name: /Configurar/i });
    fireEvent.click(configBtn);

    const tempMinInput = screen.getByDisplayValue("18");
    fireEvent.change(tempMinInput, { target: { value: "19.5" } });

    const saveBtn = screen.getByRole("button", { name: /Guardar y enviar por TCP/i });
    fireEvent.click(saveBtn);

    await waitFor(() => {
      expect(updateThresholdsSpy).toHaveBeenCalledWith(
        "mock-jwt-token",
        "cuna-test-1",
        expect.objectContaining({
          tempMin: 19.5,
          perfilId: "prof-1",
        })
      );
    });
  });
});
