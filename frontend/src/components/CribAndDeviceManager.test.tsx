import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { CribAndDeviceManager } from "./CribAndDeviceManager";
import {
  accountApi,
  type CribSummaryResponse,
  type BabyRecord,
  type ClaimableDeviceRecord,
} from "../lib/api";

// Stub child panels to isolate CribAndDeviceManager functionality
vi.mock("./LiveCribPanel", () => ({
  LiveCribPanel: () => <div data-testid="mock-live-crib-panel">LiveCribPanelStub</div>,
}));

vi.mock("./FeedingToysAndHistoryPanel", () => ({
  FeedingToysAndHistoryPanel: () => <div data-testid="mock-feeding-toys-panel">FeedingToysStub</div>,
}));

vi.mock("./IntercomPushToTalk", () => ({
  IntercomPushToTalk: () => <div data-testid="mock-intercom">IntercomStub</div>,
}));

const mockDevices: ClaimableDeviceRecord[] = [
  {
    id: "dev-central",
    deviceId: "pico-central-01",
    cunaId: "cuna-test-1",
    familyId: "fam-test-1",
    tipo: "central",
    estado: "en_linea",
    bateriaPct: null,
    fwVersion: "1.0.4",
  },
  {
    id: "dev-colchon",
    deviceId: "pico-colchon-01",
    cunaId: "cuna-test-1",
    familyId: "fam-test-1",
    tipo: "colchon",
    estado: "en_linea",
    bateriaPct: null,
    fwVersion: "1.0.2",
  },
  {
    id: "dev-movil",
    deviceId: "pico-movil-01",
    cunaId: "cuna-test-1",
    familyId: "fam-test-1",
    tipo: "movil",
    estado: "en_linea",
    bateriaPct: null,
    fwVersion: "1.0.1",
  },
  {
    id: "dev-biberon",
    deviceId: "pico-biberon-01",
    cunaId: "cuna-test-1",
    familyId: "fam-test-1",
    tipo: "biberon",
    estado: "en_linea",
    bateriaPct: 92,
    fwVersion: "1.0.0",
  },
  {
    id: "dev-juguete",
    deviceId: "pico-juguete-01",
    cunaId: "cuna-test-1",
    familyId: "fam-test-1",
    tipo: "juguete",
    estado: "desconectado",
    bateriaPct: 45,
    fwVersion: "1.0.0",
  },
];

const mockBabies: BabyRecord[] = [
  {
    id: "bebe-test-1",
    familyId: "fam-test-1",
    cunaId: "cuna-test-1",
    nombre: "Sofía Valentina",
    fechaNacimiento: "2026-02-14",
    notas: "Alergia leve a la proteína de leche de vaca",
    creadoEn: new Date().toISOString(),
  },
];

const mockCribs: CribSummaryResponse[] = [
  {
    cuna: {
      id: "cuna-test-1",
      familyId: "fam-test-1",
      nombre: "Cuna Habitación Principal",
      estadoConexion: "en_linea",
      modoOperacion: "monitoreo",
      modoAtencionExpiraEn: null,
      luzEncendida: false,
      luzNivel: 50,
      escalaMasa: 0.2,
      ultimoHeartbeat: new Date().toISOString(),
      creadoEn: new Date().toISOString(),
    },
    dispositivos: mockDevices,
    bebe: mockBabies[0],
  },
];

describe("CribAndDeviceManager Component", () => {
  const defaultProps = {
    accessToken: "mock-jwt-token",
    activeProfileId: "prof-1",
    userRole: "principal" as const,
    onError: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.spyOn(accountApi, "listCribs").mockResolvedValue(mockCribs);
    vi.spyOn(accountApi, "listBabies").mockResolvedValue(mockBabies);
  });

  it("renders 5-device node status grid with node labels, FW versions and connection states", async () => {
    render(<CribAndDeviceManager {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText("Unidad Central")).toBeInTheDocument();
      expect(screen.getByText("Colchón Sensor")).toBeInTheDocument();
      expect(screen.getByText("Móvil Colgante")).toBeInTheDocument();
      expect(screen.getByText("Base Biberón")).toBeInTheDocument();
      expect(screen.getByText("Juguete Cognitivo")).toBeInTheDocument();

      expect(screen.getByText(/pico-central-01 · v1.0.4/i)).toBeInTheDocument();
      expect(screen.getByText(/pico-biberon-01 · v1.0.0/i)).toBeInTheDocument();
      expect(screen.getByText("92%")).toBeInTheDocument();
      expect(screen.getByText("45%")).toBeInTheDocument();
    });
  });

  it("opens 10-minute pairing window upon clicking pairing button", async () => {
    const futureWindow = new Date(Date.now() + 10 * 60 * 1000).toISOString();
    const openPairingSpy = vi.spyOn(accountApi, "openPairingWindow").mockResolvedValue({
      expiraEn: futureWindow,
      ventanaMinutos: 10,
    });

    render(<CribAndDeviceManager {...defaultProps} />);

    const openPairingBtn = await screen.findByRole("button", {
      name: /Abrir ventana de emparejamiento/i,
    });
    fireEvent.click(openPairingBtn);

    await waitFor(() => {
      expect(openPairingSpy).toHaveBeenCalledWith("mock-jwt-token");
      expect(screen.getByText(/Ventana activa hasta:/i)).toBeInTheDocument();
    });
  });

  it("submits device claim form and renders generated secrets.py block with copy action", async () => {
    const claimSpy = vi.spyOn(accountApi, "claimDevice").mockResolvedValue({
      deviceRowId: "row-central-1",
      deviceId: "pico-central-nuevo",
      tipo: "central",
      deviceTokenOnce: "token_secret_1234567890abcdef",
      cunaId: "cuna-test-1",
      nombreCuna: "Cuna Habitación Principal",
    });

    render(<CribAndDeviceManager {...defaultProps} />);

    const deviceIdInput = await screen.findByLabelText(/ID de fábrica del dispositivo/i);
    fireEvent.change(deviceIdInput, { target: { value: "pico-central-nuevo" } });

    const claimCodeInput = screen.getByLabelText(/Código de reclamo de fábrica/i);
    fireEvent.change(claimCodeInput, { target: { value: "CLAIM-SECRET-2026" } });

    const submitClaimBtn = screen.getByRole("button", {
      name: /Reclamar y generar token de dispositivo/i,
    });
    fireEvent.click(submitClaimBtn);

    await waitFor(() => {
      expect(claimSpy).toHaveBeenCalledWith("mock-jwt-token", {
        deviceId: "pico-central-nuevo",
        codigoReclamo: "CLAIM-SECRET-2026",
        cunaId: "cuna-test-1",
        nombreCuna: "Cuna Principal",
      });
      expect(screen.getByText(/Token generado para pico-central-nuevo/i)).toBeInTheDocument();
      expect(screen.getByText(/DEVICE_TOKEN = "token_secret_1234567890abcdef"/i)).toBeInTheDocument();
    });

    const copyBtn = screen.getByRole("button", { name: /Copiar bloque secrets.py/i });
    fireEvent.click(copyBtn);

    await waitFor(() => {
      expect(navigator.clipboard.writeText).toHaveBeenCalledWith(
        expect.stringContaining("DEVICE_TOKEN = \"token_secret_1234567890abcdef\"")
      );
      expect(screen.getByText(/Copiado para secrets.py/i)).toBeInTheDocument();
    });
  });

  it("handles crib renaming flow with validation", async () => {
    const renameSpy = vi.spyOn(accountApi, "renameCrib").mockResolvedValue({
      ...mockCribs[0].cuna,
      nombre: "Cuna Bebé Guardería",
    });

    render(<CribAndDeviceManager {...defaultProps} />);

    const renameBtn = await screen.findByRole("button", { name: /Renombrar \(RF26\)/i });
    fireEvent.click(renameBtn);

    const renameInput = screen.getByPlaceholderText(/Nuevo nombre de la cuna/i);
    fireEvent.change(renameInput, { target: { value: "Cuna Bebé Guardería" } });

    const saveRenameBtn = screen.getByRole("button", { name: /Guardar nombre/i });
    fireEvent.click(saveRenameBtn);

    await waitFor(() => {
      expect(renameSpy).toHaveBeenCalledWith(
        "mock-jwt-token",
        "cuna-test-1",
        "Cuna Bebé Guardería"
      );
    });
  });

  it("requires typing DESVINCULAR to enable unlink confirmation and dispatches unlinkCrib", async () => {
    const unlinkSpy = vi.spyOn(accountApi, "unlinkCrib").mockResolvedValue(true);

    render(<CribAndDeviceManager {...defaultProps} />);

    const unlinkBtn = await screen.findByRole("button", { name: /Desvincular \(RF27\)/i });
    fireEvent.click(unlinkBtn);

    expect(screen.getByText(/Confirmación explícita de desvinculación \(RF27\)/i)).toBeInTheDocument();

    const confirmBtn = screen.getByRole("button", { name: /Confirmar desvinculación/i });
    expect(confirmBtn).toBeDisabled();

    const unlinkInput = screen.getByPlaceholderText(/Escribe DESVINCULAR/i);
    fireEvent.change(unlinkInput, { target: { value: "DESVINCULAR" } });

    expect(confirmBtn).not.toBeDisabled();
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(unlinkSpy).toHaveBeenCalledWith("mock-jwt-token", "cuna-test-1", "DESVINCULAR");
    });
  });

  it("restricts administrative actions when userRole is secundario", async () => {
    render(<CribAndDeviceManager {...defaultProps} userRole="secundario" />);

    await waitFor(() => {
      expect(screen.getByRole("tab", { name: "Cuna Habitación Principal" })).toBeInTheDocument();
    });

    expect(screen.queryByRole("button", { name: /Renombrar \(RF26\)/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Desvincular \(RF27\)/i })).not.toBeInTheDocument();
    expect(screen.getByText(/Solo el Usuario Principal puede abrir ventanas de emparejamiento/i)).toBeInTheDocument();
  });
});
