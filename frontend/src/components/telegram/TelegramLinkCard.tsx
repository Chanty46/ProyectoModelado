import { Check, Copy, ExternalLink, MessageSquare, Send, Unlink } from "lucide-react";
import type {
  GenerateTelegramCodeResponse,
  TelegramBotStatusResponse,
  TelegramLinkStatusResponse,
  TelegramTestMessageResponse,
} from "../../lib/api";
import { buildTelegramDeepLink } from "../../lib/formatters";

interface TelegramLinkCardProps {
  linkStatus: TelegramLinkStatusResponse | null;
  botStatus: TelegramBotStatusResponse | null;
  generatedCode: GenerateTelegramCodeResponse | null;
  copiedCommand: boolean;
  testResult: TelegramTestMessageResponse | null;
  isSendingTest: boolean;
  isGeneratingCode: boolean;
  isUnlinking: boolean;
  onGenerateCode: () => void;
  onSendTestMessage: () => void;
  onCopyCommand: () => void;
  onUnlinkTelegram: () => void;
}

/**
 * Tarjeta de vinculación del usuario con el bot de Telegram (@Bot) por código temporal
 * de un uso, envío de mensaje de prueba y desvinculación inmediata (RF19 / RF38 / RF39).
 */
export function TelegramLinkCard({
  linkStatus,
  botStatus,
  generatedCode,
  copiedCommand,
  testResult,
  isSendingTest,
  isGeneratingCode,
  isUnlinking,
  onGenerateCode,
  onSendTestMessage,
  onCopyCommand,
  onUnlinkTelegram,
}: TelegramLinkCardProps) {
  const botHealthy = Boolean(botStatus?.tokenConfigured && botStatus?.botReachable);
  const displayBotHandle =
    botStatus?.resolvedUsername ??
    botStatus?.botUsername ??
    "@CunaIntBot";
  const normalizedBotHandle = displayBotHandle.startsWith("@")
    ? displayBotHandle
    : `@${displayBotHandle}`;

  return (
    <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm space-y-4">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2.5">
          <span className="flex h-8 w-8 items-center justify-center rounded-xl bg-teal-50 text-teal-600">
            <MessageSquare className="h-4 w-4" aria-hidden="true" />
          </span>
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

      <div className="space-y-2.5">
        <p className="text-xs leading-relaxed text-slate-600">
          Vincula tu cuenta de Telegram personal con{" "}
          <span className="font-mono font-semibold text-slate-800">
            {botStatus?.resolvedUsername ??
              botStatus?.botUsername ??
              "@CunasInteligentes_Modelado_Bot"}
          </span>{" "}
          para recibir alertas críticas redundantes (RF19) y consultar{" "}
          <code className="font-mono">/estado</code> en &lt; 2 s (RF39).
        </p>

        <div className="grid grid-cols-3 gap-2 text-center">
          {[
            { step: "1", title: "Genera código", hint: "Vigencia 60 min" },
            { step: "2", title: "Envía /start", hint: `En ${normalizedBotHandle}` },
            { step: "3", title: "Usa /estado", hint: "Alertas en vivo < 2 s" },
          ].map((item) => (
            <div key={item.step} className="rounded-xl border border-slate-200 bg-slate-50/80 p-2.5">
              <span className="inline-flex h-5 w-5 items-center justify-center rounded-full bg-teal-600 text-[10px] font-bold text-white">
                {item.step}
              </span>
              <p className="mt-1 text-[11px] font-semibold text-slate-800">{item.title}</p>
              <p className="text-[10px] text-slate-500">{item.hint}</p>
            </div>
          ))}
        </div>

        {botStatus && (
          <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-[11px]">
            <span
              className={`inline-flex items-center gap-1.5 font-medium ${
                botHealthy ? "text-emerald-700" : "text-amber-700"
              }`}
            >
              <span
                className={`h-2 w-2 rounded-full ${
                  botHealthy ? "bg-emerald-500" : "bg-amber-500"
                }`}
              />
              {botHealthy
                ? "Bot Telegram HTTP activo"
                : botStatus.tokenConfigured
                ? "Token configurado (sin conexión HTTP)"
                : "Modo simulación local (sin TELEGRAM_BOT_TOKEN)"}
            </span>
            <span className="text-slate-500">
              Requiere <code className="font-mono">/start</code> previo (usa Chat ID, no teléfono)
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
                onClick={onSendTestMessage}
                className="inline-flex items-center gap-1 rounded-lg bg-teal-600 px-2.5 py-1.5 text-xs font-medium text-white hover:bg-teal-700 disabled:opacity-50 transition-colors"
              >
                <Send className="h-3.5 w-3.5" aria-hidden="true" />
                {isSendingTest ? "Enviando..." : "Enviar mensaje de prueba"}
              </button>
              <button
                type="button"
                disabled={isUnlinking}
                onClick={onUnlinkTelegram}
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
          onClick={onGenerateCode}
          className="w-full rounded-xl border border-teal-600 bg-teal-50 px-4 py-2 text-xs font-medium text-teal-800 hover:bg-teal-100 disabled:opacity-50 transition-colors"
        >
          {isGeneratingCode
            ? "Generando código..."
            : "Generar código de vinculación de 1 uso (60 min)"}
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
              onClick={onCopyCommand}
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
  );
}
