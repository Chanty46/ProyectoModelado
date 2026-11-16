/**
 * Error HTTP normalizado devuelto por el backend de Cuna Inteligente.
 */
export class ApiError extends Error {
  public readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

/** URL base del backend local configurada vía Vite o puerto 5080 por defecto. */
export const apiBaseUrl = (
  import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080"
).replace(/\/$/, "");

/**
 * Ejecuta una petición JSON autenticada con JWT Bearer contra la API del backend.
 * Extrae el mensaje `error` del cuerpo JSON cuando el servidor responde con código no exitoso.
 */
export async function requestJson<T>(
  path: string,
  accessToken: string,
  init?: RequestInit
): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${accessToken}`,
      ...(init?.headers ?? {}),
    },
  });

  if (!response.ok) {
    let errorMessage = `Error HTTP ${response.status}`;
    try {
      const payload = (await response.json()) as { error?: string };
      if (payload.error) {
        errorMessage = payload.error;
      }
    } catch {
      // Ignorar si el cuerpo de error no contiene JSON válido
    }
    throw new ApiError(response.status, errorMessage);
  }

  return (await response.json()) as T;
}
