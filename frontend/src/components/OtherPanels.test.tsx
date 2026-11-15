import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { ProfileSelectorBar } from "./ProfileSelectorBar";
import { IntercomPushToTalk } from "./IntercomPushToTalk";
import { ErrorBoundary } from "./ErrorBoundary";
import { accountApi, type CaregiverProfileRecord } from "../lib/api";

const mockProfiles: CaregiverProfileRecord[] = [
  {
    id: "prof-1",
    familyId: "fam-1",
    nombre: "Mamá Andrea",
    avatar: "MA",
    activo: true,
    eliminadoEn: null,
    creadoEn: "2026-11-15T00:00:00Z",
  },
  {
    id: "prof-2",
    familyId: "fam-1",
    nombre: "Papá Carlos",
    avatar: "PC",
    activo: true,
    eliminadoEn: null,
    creadoEn: "2026-11-15T00:00:00Z",
  },
  {
    id: "prof-3",
    familyId: "fam-1",
    nombre: "Abuela Elena",
    avatar: "AE",
    activo: true,
    eliminadoEn: null,
    creadoEn: "2026-11-15T00:00:00Z",
  },
];

describe("ProfileSelectorBar Component", () => {
  it("renders null when profiles list is empty", () => {
    const { container } = render(
      <ProfileSelectorBar profiles={[]} activeProfileId={null} onSelectProfile={vi.fn()} />
    );
    expect(container.firstChild).toBeNull();
  });

  it("renders all caregiver profiles and highlights active with aria-pressed", () => {
    render(
      <ProfileSelectorBar
        profiles={mockProfiles}
        activeProfileId="prof-2"
        onSelectProfile={vi.fn()}
      />
    );

    expect(screen.getByText("Mamá Andrea")).toBeInTheDocument();
    expect(screen.getByText("Papá Carlos")).toBeInTheDocument();
    expect(screen.getByText("Abuela Elena")).toBeInTheDocument();

    const mamaBtn = screen.getByRole("button", { name: /Mamá Andrea/i });
    const papaBtn = screen.getByRole("button", { name: /Papá Carlos/i });

    expect(mamaBtn).toHaveAttribute("aria-pressed", "false");
    expect(papaBtn).toHaveAttribute("aria-pressed", "true");
  });

  it("triggers onSelectProfile in 1 direct click (IU-02)", () => {
    const onSelectSpy = vi.fn();
    render(
      <ProfileSelectorBar
        profiles={mockProfiles}
        activeProfileId="prof-1"
        onSelectProfile={onSelectSpy}
      />
    );

    const abuelaBtn = screen.getByRole("button", { name: /Abuela Elena/i });
    fireEvent.click(abuelaBtn);

    expect(onSelectSpy).toHaveBeenCalledWith("prof-3");
    expect(onSelectSpy).toHaveBeenCalledTimes(1);
  });
});

describe("IntercomPushToTalk Component", () => {
  const defaultProps = {
    accessToken: "mock-jwt-token",
    cunaId: "cuna-test-1",
  };

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders voice intercom controls with max dBA indicator", () => {
    render(<IntercomPushToTalk {...defaultProps} />);

    expect(screen.getByText(/Intercomunicador de Voz \(RF11 — Spike M8\)/i)).toBeInTheDocument();
    expect(screen.getByText(/PCM 8-bit 8 kHz · Tope ≤ 65 dBA/i)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /Transmitir bloque de voz por intercomunicador/i })
    ).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Tono prueba 440 Hz/i })).toBeInTheDocument();
  });

  it("sends 440 Hz backup test tone chunk and displays feedback status", async () => {
    const sendChunkSpy = vi.spyOn(accountApi, "sendIntercomChunk").mockResolvedValue({
      delivered: true,
      sampleCount: 800,
      sampleRateHz: 8000,
      maxSafeGainApplied: 1.0,
    });

    render(<IntercomPushToTalk {...defaultProps} />);

    const testToneBtn = screen.getByRole("button", { name: /Tono prueba 440 Hz/i });
    fireEvent.click(testToneBtn);

    await waitFor(() => {
      expect(sendChunkSpy).toHaveBeenCalledWith("mock-jwt-token", "cuna-test-1", {
        sampleRateHz: 8000,
        seq: 1,
        pcmBase64: expect.any(String),
      });
      expect(
        screen.getByText(/Tono 440 Hz transmitido \(800 muestras @ 8000 Hz, tope ≤ 65 dBA\)/i)
      ).toBeInTheDocument();
    });
  });

  it("handles offline central node gracefully when chunk cannot be delivered", async () => {
    vi.spyOn(accountApi, "sendIntercomChunk").mockResolvedValue({
      delivered: false,
      sampleCount: 800,
      sampleRateHz: 8000,
      maxSafeGainApplied: 1.0,
    });

    render(<IntercomPushToTalk {...defaultProps} />);

    const testToneBtn = screen.getByRole("button", { name: /Tono prueba 440 Hz/i });
    fireEvent.click(testToneBtn);

    await waitFor(() => {
      expect(
        screen.getByText(/Bloque procesado \(la Unidad Central está desconectada actualmente\)/i)
      ).toBeInTheDocument();
    });
  });
});

describe("ErrorBoundary Component", () => {
  function Bomb({ shouldThrow }: { shouldThrow: boolean }) {
    if (shouldThrow) {
      throw new Error("Error fatal simulado en renderizado React");
    }
    return <div>Componente renderizado sin errores</div>;
  }

  it("renders children normally when no error occurs", () => {
    render(
      <ErrorBoundary>
        <Bomb shouldThrow={false} />
      </ErrorBoundary>
    );

    expect(screen.getByText("Componente renderizado sin errores")).toBeInTheDocument();
  });

  it("catches thrown errors and displays recovery screen with reload button", () => {
    // Suppress console.error in test output for intentional throw
    const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});

    render(
      <ErrorBoundary>
        <Bomb shouldThrow={true} />
      </ErrorBoundary>
    );

    expect(screen.getByText("Algo salió mal en la aplicación")).toBeInTheDocument();
    expect(screen.getByText(/Error fatal simulado en renderizado React/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Recargar aplicación/i })).toBeInTheDocument();

    consoleSpy.mockRestore();
  });
});
