import { useState, useEffect, useCallback, type FormEvent } from "react";
import {
  MessageSquare,
  Clock,
  Copy,
  Check,
  Unlink,
  Send,
  ExternalLink,
} from "lucide-react";
import {
  accountApi,
  ApiError,
  type TelegramLinkStatusResponse,
  type GenerateTelegramCodeResponse,
  type TelegramBotStatusResponse,
  type TelegramTestMessageResponse,
  type FeedingReminderRecord,
} from "../lib/api";
import { buildTelegramDeepLink } from "../lib/formatters";

interface TelegramAndRemindersPanelProps {
  accessToken: string;
  activeProfileId: string | null;
  onError: (msg: string | null) => void;
}

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
    await navigator.clipboard.writeText(generatedCode.comandoEjemplo);
    setCopiedCommand(true);
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
    setIsSavingReminder(true);
    try {
      const updated = await accountApi.updateFeedingReminder(accessToken, {
        intervaloHoras: intervalHours,
        activo: reminderActive,
        perfilId: activeProfileId,
      });
      setReminder(updated);
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
      {/* RF38 / RF39 / RF19: Vinculación de Bot de Telegram por Usuario */}
      <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <MessageSquare className="h-5 w-5 text-teal-600" aria-hidden="true" />
            <h2 className="text-base font-semibold text-slate-900">
              Alertas por Telegram (RF19 / RF38 / RF39)
            </h2>
          </div>
          <span
            className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${
              linkStatus?.vinculado
                ? "bg-emerald-50 text-emerald-700"
                : "bg-slate-100 text-slate-600"
            }`}
          >
            {linkStatus?.vinculado ? "Chat vinculado" : "Sin vincular"}
          </span>
        </div>

        <div className="space-y-2">
          <p className="text-xs text-slate-600">
            Vincula tu cuenta de Telegram personal con{" "}
            <span className="font-mono font-semibold text-slate-800">
              {botStatus?.resolvedUsername ?? botStatus?.botUsername ?? "@CunasInteligentes_Modelado_Bot"}
            </span>{" "}
            para recibir alertas críticas redundantes (RF19) y consultar{" "}
            <code className="font-mono">/estado</code> en &lt; 2 s (RF39).
          </p>

          {botStatus && (
            <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-[11px]">
              <span
                className={`inline-flex items-center gap-1.5 font-medium ${
                  botStatus.tokenConfigured && botStatus.botReachable
                    ? "text-emerald-700"
                    : "text-amber-700"
                }`}
              >
                <span
                  className={`h-2 w-2 rounded-full ${
                    botStatus.tokenConfigured && botStatus.botReachable
                      ? "bg-emerald-500"
                      : "bg-amber-500"
                  }`}
                />
                {botStatus.tokenConfigured && botStatus.botReachable
                  ? "Bot Telegram HTTP activo"
                  : botStatus.tokenConfigured
                  ? "Token configurado (sin conexión HTTP)"
                  : "Modo simulación local (sin TELEGRAM_BOT_TOKEN)"}
              </span>
              <span className="text-slate-500">
                Requiere <code className="font-mono">/start</code> previo (Telegram Bot API usa Chat ID, no teléfono)
              </span>
            </div>
          )}
        </div>

        {linkStatus?.vinculado ? (
          <div className="space-y-2.5">
            <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-emerald-200 bg-emerald-50/70 p-3">
              <div>
                <p className="text-xs font-semibold text-emerald-900">
                  Chat ID #{linkStatus.chatId} activo
                </p>
                {linkStatus.vinculadoEn && (
                  <p className="text-[11px] text-emerald-800">
                    Vinculado el {new Date(linkStatus.vinculadoEn).toLocaleString()}
                  </p>
                )}
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  disabled={isSendingTest}
                  onClick={handleSendTestMessage}
                  className="inline-flex items-center gap-1 rounded-lg bg-teal-600 px-2.5 py-1.5 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors"
                >
                  <Send className="h-3.5 w-3.5" aria-hidden="true" />
                  {isSendingTest ? "Enviando..." : "Enviar mensaje de prueba"}
                </button>
                <button
                  type="button"
                  disabled={isUnlinking}
                  onClick={handleUnlinkTelegram}
                  className="inline-flex items-center gap-1 rounded-lg border border-red-200 bg-white px-2.5 py-1.5 text-xs font-medium text-red-600 hover:bg-red-50 disabled:opacity-50 transition-colors"
                >
                  <Unlink className="h-3.5 w-3.5" aria-hidden="true" />
                  {isUnlinking ? "Desvinculando..." : "Desvincular"}
                </button>
              </div>
            </div>

            {testResult && (
              <p
                className={`rounded-lg border px-3 py-2 text-xs ${
                  testResult.sent
                    ? "border-emerald-200 bg-emerald-50 text-emerald-800"
                    : "border-amber-200 bg-amber-50 text-amber-800"
                }`}
              >
                {testResult.message} (modo: <span className="font-mono">{testResult.mode}</span>)
              </p>
            )}
          </div>
        ) : (
          <button
            type="button"
            disabled={isGeneratingCode}
            onClick={handleGenerateCode}
            className="w-full rounded-xl border border-teal-600 bg-teal-50 px-4 py-2 text-xs font-medium text-teal-800 hover:bg-teal-100 disabled:opacity-50 transition-colors"
          >
            {isGeneratingCode
              ? "Generando código..."
              : "Generar código de vinculación de 1 uso (10 min)"}
          </button>
        )}

        {generatedCode && (
          <div className="rounded-xl border border-slate-200 bg-slate-50 p-3 space-y-2.5">
            <p className="text-[11px] font-medium text-slate-700">
              Envía este comando a <span className="font-mono">{generatedCode.botUsername}</span>{" "}
              antes de las {new Date(generatedCode.expiraEn).toLocaleTimeString()}:
            </p>
            <div className="flex flex-wrap items-center gap-2">
              <input
                type="text"
                readOnly
                value={generatedCode.comandoEjemplo}
                className="flex-1 min-w-[180px] rounded border border-slate-300 bg-white px-2.5 py-1.5 text-xs font-mono text-slate-900"
              />
              <button
                type="button"
                onClick={handleCopyCommand}
                className="inline-flex items-center gap-1 rounded bg-slate-800 px-2.5 py-1.5 text-xs text-white hover:bg-slate-700"
              >
                {copiedCommand ? (
                  <>
                    <Check className="h-3.5 w-3.5" aria-hidden="true" />
                    Copiado
                  </>
                ) : (
                  <>
                    <Copy className="h-3.5 w-3.5" aria-hidden="true" />
                    Copiar
                  </>
                )}
              </button>
              <a
                href={
                  generatedCode.deepLinkUrl ??
                  buildTelegramDeepLink(generatedCode.botUsername, generatedCode.codigo)
                }
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1 rounded bg-teal-600 px-2.5 py-1.5 text-xs font-medium text-white hover:bg-teal-700"
              >
                <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
                Abrir en Telegram
              </a>
            </div>
          </div>
        )}
      </section>

      {/* RF18: Recordatorio Dinámico de Tomas */}
      <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Clock className="h-5 w-5 text-teal-600" aria-hidden="true" />
            <h2 className="text-base font-semibold text-slate-900">
              Recordatorio Dinámico de Tomas (RF18)
            </h2>
          </div>
          {reminder && (
            <span
              className={`rounded-full px-2.5 py-0.5 text-xs font-medium ${
                reminder.activo
                  ? "bg-emerald-50 text-emerald-700"
                  : "bg-slate-100 text-slate-600"
              }`}
            >
              {reminder.activo ? `Activo cada ${reminder.intervaloHoras} h` : "Pausado"}
            </span>
          )}
        </div>

        <p className="text-xs text-slate-600">
          El servicio en segundo plano evalúa el tiempo transcurrido desde la última toma y notifica por el panel y Telegram cuando se cumple el intervalo (1 a 8 horas).
        </p>

        <form onSubmit={handleSaveReminder} className="space-y-3 pt-2 border-t border-slate-100">
          <div className="flex items-center justify-between gap-4">
            <label htmlFor="reminder-interval" className="text-xs font-medium text-slate-700">
              Intervalo entre tomas (1–8 horas):
            </label>
            <select
              id="reminder-interval"
              value={intervalHours}
              onChange={(e) => setIntervalHours(Number(e.target.value))}
              className="rounded-lg border border-slate-300 px-3 py-1.5 text-xs"
            >
              {[1, 2, 3, 4, 5, 6, 7, 8].map((h) => (
                <option key={h} value={h}>
                  Cada {h} {h === 1 ? "hora" : "horas"}
                </option>
              ))}
            </select>
          </div>

          <label className="inline-flex items-center gap-2 text-xs text-slate-700">
            <input
              type="checkbox"
              checked={reminderActive}
              onChange={(e) => setReminderActive(e.target.checked)}
            />
            Activar notificaciones automáticas de recordatorio de toma
          </label>

          <button
            type="submit"
            disabled={isSavingReminder}
            className="w-full rounded-xl bg-teal-600 px-4 py-2 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors"
          >
            {isSavingReminder ? "Guardando configuración..." : "Guardar configuración de recordatorio"}
          </button>
        </form>
      </section>
    </div>
  );
}
