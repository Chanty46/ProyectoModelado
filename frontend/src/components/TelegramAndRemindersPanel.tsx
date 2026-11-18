import { useState, useEffect, useCallback, type FormEvent } from "react";
import {
  accountApi,
  ApiError,
  type TelegramLinkStatusResponse,
  type GenerateTelegramCodeResponse,
  type TelegramBotStatusResponse,
  type TelegramTestMessageResponse,
  type FeedingReminderRecord,
} from "../lib/api";
import { TelegramLinkCard } from "./telegram/TelegramLinkCard";
import { FeedingReminderCard } from "./telegram/FeedingReminderCard";

interface TelegramAndRemindersPanelProps {
  accessToken: string;
  activeProfileId: string | null;
  onError: (msg: string | null) => void;
}

/**
 * Pestaña de comunicación y avisos: vinculación con el bot de Telegram
 * y recordatorios dinámicos de alimentación (RF18, RF19, RF38, RF39).
 */
export function TelegramAndRemindersPanel({
  accessToken,
  activeProfileId,
  onError,
}: TelegramAndRemindersPanelProps) {
  const [linkStatus, setLinkStatus] = useState<TelegramLinkStatusResponse | null>(null);
  const [botStatus, setBotStatus] = useState<TelegramBotStatusResponse | null>(null);
  const [generatedCode, setGeneratedCode] = useState<GenerateTelegramCodeResponse | null>(null);
  const [copiedCommand, setCopiedCommand] = useState(false);
  const [testResult, setTestResult] = useState<TelegramTestMessageResponse | null>(null);
  const [isSendingTest, setIsSendingTest] = useState(false);

  const [reminder, setReminder] = useState<FeedingReminderRecord | null>(null);
  const [intervalHours, setIntervalHours] = useState<number>(3);
  const [reminderActive, setReminderActive] = useState<boolean>(true);
  const [isGeneratingCode, setIsGeneratingCode] = useState(false);
  const [isUnlinking, setIsUnlinking] = useState(false);
  const [isSavingReminder, setIsSavingReminder] = useState(false);
  const [savedNotice, setSavedNotice] = useState(false);

  const loadData = useCallback(async () => {
    try {
      const [status, botStat, rem] = await Promise.all([
        accountApi.getTelegramLink(accessToken),
        accountApi.getTelegramStatus(accessToken),
        accountApi.getFeedingReminder(accessToken),
      ]);
      setLinkStatus(status);
      setBotStatus(botStat);
      setReminder(rem);
      setIntervalHours(rem.intervaloHoras);
      setReminderActive(rem.activo);
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }, [accessToken, onError]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  async function handleGenerateCode() {
    onError(null);
    setTestResult(null);
    setIsGeneratingCode(true);
    try {
      const res = await accountApi.generateTelegramLinkCode(accessToken);
      setGeneratedCode(res);
      setCopiedCommand(false);
      await loadData();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsGeneratingCode(false);
    }
  }

  async function handleSendTestMessage() {
    onError(null);
    setTestResult(null);
    setIsSendingTest(true);
    try {
      const res = await accountApi.sendTelegramTest(accessToken);
      setTestResult(res);
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsSendingTest(false);
    }
  }

  async function handleCopyCommand() {
    if (!generatedCode) {
      return;
    }
    try {
      await navigator.clipboard.writeText(generatedCode.comandoEjemplo);
      setCopiedCommand(true);
    } catch {
      onError("No se pudo copiar el comando automáticamente al portapapeles.");
    }
  }

  async function handleUnlinkTelegram() {
    onError(null);
    setIsUnlinking(true);
    try {
      await accountApi.unlinkTelegram(accessToken);
      setGeneratedCode(null);
      await loadData();
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsUnlinking(false);
    }
  }

  async function handleSaveReminder(e: FormEvent) {
    e.preventDefault();
    onError(null);
    setSavedNotice(false);
    setIsSavingReminder(true);
    try {
      const updated = await accountApi.updateFeedingReminder(accessToken, {
        intervaloHoras: intervalHours,
        activo: reminderActive,
        perfilId: activeProfileId,
      });
      setReminder(updated);
      setSavedNotice(true);
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    } finally {
      setIsSavingReminder(false);
    }
  }

  return (
    <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
      <TelegramLinkCard
        linkStatus={linkStatus}
        botStatus={botStatus}
        generatedCode={generatedCode}
        copiedCommand={copiedCommand}
        testResult={testResult}
        isSendingTest={isSendingTest}
        isGeneratingCode={isGeneratingCode}
        isUnlinking={isUnlinking}
        onGenerateCode={() => void handleGenerateCode()}
        onSendTestMessage={() => void handleSendTestMessage()}
        onCopyCommand={() => void handleCopyCommand()}
        onUnlinkTelegram={() => void handleUnlinkTelegram()}
      />

      <FeedingReminderCard
        reminder={reminder}
        intervalHours={intervalHours}
        reminderActive={reminderActive}
        isSaving={isSavingReminder}
        savedNotice={savedNotice}
        onIntervalChange={(h) => {
          setIntervalHours(h);
          setSavedNotice(false);
        }}
        onActiveChange={(active) => {
          setReminderActive(active);
          setSavedNotice(false);
        }}
        onSave={(e) => void handleSaveReminder(e)}
      />
    </div>
  );
}
