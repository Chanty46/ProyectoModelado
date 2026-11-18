import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { FamilyAdminPanel } from "./family/FamilyAdminPanel";
import { NightLightControlCard } from "./live/NightLightControlCard";
import { AcceptInvitationPage } from "../pages/AcceptInvitationPage";
import { accountApi, type AccountContextResponse } from "../lib/api";

const baseAccount: AccountContextResponse = {
  userId: "user-1",
  email: "mama@cuna.local",
  familia: {
    id: "fam-1",
    nombre: "Familia Vega",
    creadoEn: "2026-11-10T00:00:00Z",
  },
  rol: "principal",
  perfiles: [
    {
      id: "prof-1",
      familyId: "fam-1",
      nombre: "Mamá",
      avatar: "M1",
      activo: true,
      eliminadoEn: null,
      creadoEn: "2026-11-10T00:00:00Z",
    },
  ],
  miembros: [
    {
      id: "user-1",
      familyId: "fam-1",
      email: "mama@cuna.local",
      rol: "principal",
      creadoEn: "2026-11-10T00:00:00Z",
    },
    {
      id: "user-2",
      familyId: "fam-1",
      email: "papa@cuna.local",
      rol: "secundario",
      creadoEn: "2026-11-11T00:00:00Z",
    },
  ],
};

describe("FamilyAdminPanel Component", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.spyOn(accountApi, "listSecurityAudit").mockResolvedValue([]);
  });

  it("creates caregiver profile, generates/copies invite link and revokes secondary member", async () => {
    const refreshSpy = vi.fn().mockResolvedValue(undefined);
    const createProfileSpy = vi.spyOn(accountApi, "createProfile").mockResolvedValue({
      id: "prof-2",
      familyId: "fam-1",
      nombre: "Abuela",
      avatar: "A1",
      activo: true,
      eliminadoEn: null,
      creadoEn: "2026-11-17T00:00:00Z",
    });
    const createInviteSpy = vi.spyOn(accountApi, "createInvitation").mockResolvedValue({
      invitationId: "inv-1",
      token: "token-unico-123",
      expiraEn: "2026-11-18T00:00:00Z",
      inviteUrl: "http://localhost:5173/invitacion/token-unico-123",
    });
    const revokeSpy = vi.spyOn(accountApi, "revokeMember").mockResolvedValue(true);

    render(
      <FamilyAdminPanel
        accessToken="jwt-principal"
        account={baseAccount}
        isPrincipal={true}
        onRefreshAccount={refreshSpy}
        onError={vi.fn()}
      />
    );

    const profileInput = screen.getByPlaceholderText(/Nuevo perfil/i);
    fireEvent.change(profileInput, { target: { value: "Abuela" } });
    fireEvent.click(screen.getByRole("button", { name: /Agregar/i }));

    await waitFor(() => {
      expect(createProfileSpy).toHaveBeenCalledWith("jwt-principal", {
        nombre: "Abuela",
        avatar: "F1",
      });
    });

    fireEvent.click(screen.getByRole("button", { name: /Generar enlace de invitación/i }));
    await waitFor(() => {
      expect(createInviteSpy).toHaveBeenCalledWith("jwt-principal");
      expect(
        screen.getByDisplayValue("http://localhost:5173/invitacion/token-unico-123")
      ).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole("button", { name: /Copiar/i }));
    await waitFor(() => {
      expect(navigator.clipboard.writeText).toHaveBeenCalledWith(
        "http://localhost:5173/invitacion/token-unico-123"
      );
    });

    fireEvent.click(screen.getByRole("button", { name: /Revocar acceso/i }));
    const confirmBtn = await screen.findByRole("button", { name: /Sí, revocar/i });
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(revokeSpy).toHaveBeenCalledWith("jwt-principal", "user-2");
      expect(refreshSpy).toHaveBeenCalledWith("jwt-principal");
    });
  });

  it("enforces read-only mode when current user is secundario", () => {
    render(
      <FamilyAdminPanel
        accessToken="jwt-sec"
        account={{ ...baseAccount, rol: "secundario" }}
        isPrincipal={false}
        onRefreshAccount={vi.fn()}
        onError={vi.fn()}
      />
    );

    expect(screen.getByText(/únicamente el Usuario Principal/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Generar enlace de invitación/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Revocar acceso/i })).not.toBeInTheDocument();
  });
});

describe("NightLightControlCard Presets", () => {
  it("applies preset level and turns night light off", () => {
    const onSliderChange = vi.fn();
    const onApplyLight = vi.fn();

    render(
      <NightLightControlCard
        luzEncendida={true}
        luzNivel={50}
        lightLevel={50}
        onSliderChange={onSliderChange}
        onApplyLight={onApplyLight}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "80%" }));
    expect(onSliderChange).toHaveBeenCalledWith(80);
    expect(onApplyLight).toHaveBeenCalledWith(true, 80);

    fireEvent.click(screen.getByRole("button", { name: /Apagar/i }));
    expect(onApplyLight).toHaveBeenCalledWith(false, 0);
  });
});

describe("AcceptInvitationPage Component", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("prompts Google login when user arrives without active session", () => {
    const loginSpy = vi.fn();
    render(
      <MemoryRouter initialEntries={["/invitacion/tok-123"]}>
        <Routes>
          <Route
            path="/invitacion/:token"
            element={
              <AcceptInvitationPage
                accessToken={null}
                onAccepted={vi.fn()}
                onRequestGoogleLogin={loginSpy}
              />
            }
          />
        </Routes>
      </MemoryRouter>
    );

    fireEvent.click(screen.getByRole("button", { name: /Continuar con Google/i }));
    expect(loginSpy).toHaveBeenCalledTimes(1);
  });

  it("submits profile name and accepts family invitation when authenticated", async () => {
    const acceptedSpy = vi.fn();
    const acceptApiSpy = vi
      .spyOn(accountApi, "acceptInvitation")
      .mockResolvedValue({ ...baseAccount, rol: "secundario" });

    render(
      <MemoryRouter initialEntries={["/invitacion/tok-abc"]}>
        <Routes>
          <Route
            path="/invitacion/:token"
            element={
              <AcceptInvitationPage
                accessToken="jwt-invited"
                onAccepted={acceptedSpy}
                onRequestGoogleLogin={vi.fn()}
              />
            }
          />
        </Routes>
      </MemoryRouter>
    );

    fireEvent.change(screen.getByLabelText(/Nombre de tu perfil de cuidador/i), {
      target: { value: "Abuela Rosa" },
    });
    fireEvent.click(screen.getByRole("button", { name: /Aceptar invitación y unirme/i }));

    await waitFor(() => {
      expect(acceptApiSpy).toHaveBeenCalledWith("jwt-invited", {
        token: "tok-abc",
        nombrePerfil: "Abuela Rosa",
        avatar: "C1",
      });
      expect(acceptedSpy).toHaveBeenCalledTimes(1);
      expect(screen.getByText(/Cuenta vinculada correctamente/i)).toBeInTheDocument();
    });
  });
});
