import { useState, type FormEvent } from "react";
import { accountApi, ApiError, type AccountContextResponse } from "../../lib/api";

interface BootstrapFamilyFormProps {
  accessToken: string;
  onCreated: (created: AccountContextResponse) => void;
  onError: (msg: string | null) => void;
}

/**
 * Formulario de alta inicial de familia y primer perfil de cuidador cuando el
 * usuario autenticado aún no pertenece a ninguna familia activa (RF01 / RF23).
 */
export function BootstrapFamilyForm({
  accessToken,
  onCreated,
  onError,
}: BootstrapFamilyFormProps) {
  const [familyNameInput, setFamilyNameInput] = useState("Mi Familia");
  const [initialProfileInput, setInitialProfileInput] = useState("Mamá / Papá");

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    onError(null);
    try {
      const created = await accountApi.bootstrap(accessToken, {
        nombreFamilia: familyNameInput,
        nombrePerfilInicial: initialProfileInput,
        avatarInicial: "P1",
      });
      onCreated(created);
    } catch (err) {
      if (err instanceof ApiError) {
        onError(err.message);
      }
    }
  }

  return (
    <section className="mx-auto max-w-lg rounded-2xl border border-slate-200 bg-white p-8 shadow-sm space-y-5">
      <h2 className="text-lg font-semibold text-slate-900">
        Crear Cuenta Familiar (RF01)
      </h2>
      <p className="text-xs text-slate-600">
        Aún no perteneces a ninguna familia activa. Crea una familia nueva como Usuario Principal o abre el enlace de invitación que te hayan compartido (RF02).
      </p>
      <form onSubmit={(e) => void handleSubmit(e)} className="space-y-4">
        <div>
          <label htmlFor="family-name" className="block text-xs font-medium text-slate-700 mb-1">
            Nombre de la familia
          </label>
          <input
            id="family-name"
            type="text"
            required
            maxLength={80}
            value={familyNameInput}
            onChange={(e) => setFamilyNameInput(e.target.value)}
            className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
          />
        </div>
        <div>
          <label htmlFor="initial-profile" className="block text-xs font-medium text-slate-700 mb-1">
            Tu perfil de cuidador inicial (1–30 caracteres, RF23)
          </label>
          <input
            id="initial-profile"
            type="text"
            required
            maxLength={30}
            value={initialProfileInput}
            onChange={(e) => setInitialProfileInput(e.target.value)}
            className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm"
          />
        </div>
        <button
          type="submit"
          className="w-full rounded-xl bg-teal-600 px-4 py-2.5 text-sm font-medium text-white hover:bg-teal-700"
        >
          Crear familia como Usuario Principal
        </button>
      </form>
    </section>
  );
}
