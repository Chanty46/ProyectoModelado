import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { TelegramAndRemindersPanel } from "./TelegramAndRemindersPanel";
import {
  accountApi,
  type TelegramLinkStatusResponse,
  type TelegramBotStatusResponse,
  type FeedingReminderRecord,
} from "../lib/api";

const mockUnlinkedStatus: TelegramLinkStatusResponse = {
  usuarioId: "user-test-1",
  vinculado: false,
  chatId: null,
  vinculadoEn: null,
  codigoPendienteExpiraEn: null,
};

const mockLinkedStatus: TelegramLinkStatusResponse = {
  usuarioId: "user-test-1",
  vinculado: true,
  chatId: 987654321,
  vinculadoEn: "2026-11-15T02:00:00Z",
  codigoPendienteExpiraEn: null,
};

const mockBotStatus: TelegramBotStatusResponse = {
  tokenConfigured: true,
  mode: "polling",
  botReachable: true,
  botUsername: "CunaInteligenteBot",
  resolvedUsername: "CunaInteligenteBot",
  userLinked: false,
  userChatId: null,
  note: "Operativo",
};

const mockReminder: FeedingReminderRecord = {
  id: "rem-1",
  familyId: "fam-test-1",
  intervaloHoras: 3,
  activo: true,
  ultimoEnvio: null,
  actualizadoEn: new Date().toISOString(),
};

describe("TelegramAndRemindersPanel Component", () => {
  const defaultProps = {
    accessToken: "mock-jwt-token",
    activeProfileId: "prof-1",
    onError: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.spyOn(accountApi, "getTelegramLink").mockResolvedValue(mockUnlinkedStatus);
    vi.spyOn(accountApi, "getTelegramStatus").mockResolvedValue(mockBotStatus);
    vi.spyOn(accountApi, "getFeedingReminder").mockResolvedValue(mockReminder);
  });

  it("renders bot status indicator and simulation vs reachable state", async () => {
    render(<TelegramAndRemindersPanel {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText("Bot Telegram HTTP activo")).toBeInTheDocument();
      expect(screen.getByText(/Chat ID, no teléfono/i)).toBeInTheDocument();
    });
  });

  it("generates 10-minute link code and enables clipboard copy of start command", async () => {
    const futureExp = new Date(Date.now() + 10 * 60 * 1000).toISOString();
    const generateCodeSpy = vi.spyOn(accountApi, "generateTelegramLinkCode").mockResolvedValue({
      codigo: "TG-987654",
      expiraEn: futureExp,
      botUsername: "CunaInteligenteBot",
      comandoEjemplo: "/start TG-987654",
      deepLinkUrl: "https://t.me/CunaInteligenteBot?start=TG-987654",
    });

    render(<TelegramAndRemindersPanel {...defaultProps} />);

    const generateBtn = await screen.findByRole("button", {
      name: /Generar código de vinculación/i,
    });
    fireEvent.click(generateBtn);

    await waitFor(() => {
      expect(generateCodeSpy).toHaveBeenCalledWith("mock-jwt-token");
      expect(screen.getByDisplayValue("/start TG-987654")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: /Abrir en Telegram/i })).toHaveAttribute(
        "href",
        "https://t.me/CunaInteligenteBot?start=TG-987654"
      );
    });

    const copyBtn = screen.getByRole("button", { name: /Copiar/i });
    fireEvent.click(copyBtn);

    await waitFor(() => {
      expect(navigator.clipboard.writeText).toHaveBeenCalledWith("/start TG-987654");
      expect(screen.getByText("Copiado")).toBeInTheDocument();
    });
  });

  it("renders active chat ID when linked and sends test alert message", async () => {
    vi.spyOn(accountApi, "getTelegramLink").mockResolvedValue(mockLinkedStatus);
    const sendTestSpy = vi.spyOn(accountApi, "sendTelegramTest").mockResolvedValue({
      sent: true,
      chatId: 987654321,
      mode: "polling",
      message: "Mensaje de prueba despachado con éxito",
    });

    render(<TelegramAndRemindersPanel {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText(/Chat ID #987654321 activo/i)).toBeInTheDocument();
    });

    const sendTestBtn = screen.getByRole("button", { name: /Enviar mensaje de prueba/i });
    fireEvent.click(sendTestBtn);

    await waitFor(() => {
      expect(sendTestSpy).toHaveBeenCalledWith("mock-jwt-token");
      expect(screen.getByText(/Mensaje de prueba despachado con éxito/i)).toBeInTheDocument();
    });
  });

  it("handles unlinking Telegram account cleanly", async () => {
    vi.spyOn(accountApi, "getTelegramLink").mockResolvedValue(mockLinkedStatus);
    const unlinkSpy = vi.spyOn(accountApi, "unlinkTelegram").mockResolvedValue(true);

    render(<TelegramAndRemindersPanel {...defaultProps} />);

    const unlinkBtn = await screen.findByRole("button", { name: /Desvincular/i });
    fireEvent.click(unlinkBtn);

    await waitFor(() => {
      expect(unlinkSpy).toHaveBeenCalledWith("mock-jwt-token");
    });
  });

  it("selects and saves feeding reminder interval between 1 and 8 hours", async () => {
    const updateReminderSpy = vi.spyOn(accountApi, "updateFeedingReminder").mockResolvedValue({
      ...mockReminder,
      intervaloHoras: 4,
    });

    render(<TelegramAndRemindersPanel {...defaultProps} />);

    const quickBtn4h = await screen.findByRole("button", { name: "4h" });
    fireEvent.click(quickBtn4h);

    const saveReminderBtn = screen.getByRole("button", { name: /Guardar configuración de recordatorio/i });
    fireEvent.click(saveReminderBtn);

    await waitFor(() => {
      expect(updateReminderSpy).toHaveBeenCalledWith("mock-jwt-token", {
        intervaloHoras: 4,
        activo: true,
        perfilId: "prof-1",
      });
      expect(screen.getByText("Configuración de recordatorio guardada.")).toBeInTheDocument();
    });
  });
});
