import { UserCheck } from "lucide-react";
import type { CaregiverProfileRecord } from "../lib/api";

interface ProfileSelectorBarProps {
  profiles: CaregiverProfileRecord[];
  activeProfileId: string | null;
  onSelectProfile: (profileId: string) => void;
}

/**
 * IU-02: Cambio de perfil activo en <= 2 toques (aquí en 1 solo toque directo).
 */
export function ProfileSelectorBar({
  profiles,
  activeProfileId,
  onSelectProfile,
}: ProfileSelectorBarProps) {
  if (profiles.length === 0) {
    return null;
  }

  return (
    <nav
      aria-label="Selector rápido de perfil de cuidador (IU-02)"
      className="flex flex-wrap items-center gap-2 rounded-xl border border-slate-200 bg-white px-4 py-2.5 shadow-sm"
    >
      <span className="flex items-center gap-1.5 text-xs font-medium text-slate-600 mr-1">
        <UserCheck className="h-4 w-4 text-teal-600" aria-hidden="true" />
        Perfil activo:
      </span>
      <div className="flex flex-wrap items-center gap-1.5">
        {profiles.map((profile) => {
          const isSelected = profile.id === activeProfileId;
          return (
            <button
              key={profile.id}
              type="button"
              onClick={() => onSelectProfile(profile.id)}
              aria-pressed={isSelected}
              className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-medium transition-colors focus:outline-none focus:ring-2 focus:ring-teal-500 ${
                isSelected
                  ? "bg-teal-600 text-white shadow-sm"
                  : "bg-slate-100 text-slate-700 hover:bg-slate-200"
              }`}
            >
              <span
                aria-hidden="true"
                className={`inline-flex h-5 w-5 items-center justify-center rounded-full text-[10px] font-bold ${
                  isSelected ? "bg-teal-800 text-white" : "bg-slate-300 text-slate-800"
                }`}
              >
                {profile.avatar.slice(0, 2).toUpperCase()}
              </span>
              <span>{profile.nombre}</span>
            </button>
          );
        })}
      </div>
    </nav>
  );
}
