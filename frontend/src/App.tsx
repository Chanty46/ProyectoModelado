import { Routes, Route } from "react-router-dom";
import { ShieldCheck } from "lucide-react";

function HomeView() {
  return (
    <main className="min-h-screen flex flex-col items-center justify-center p-6 bg-slate-50 text-slate-800">
      <section className="max-w-md w-full rounded-2xl border border-slate-200 bg-white p-8 shadow-sm text-center space-y-4">
        <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-teal-50 text-teal-600">
          <ShieldCheck className="h-6 w-6" aria-hidden="true" />
        </div>
        <h1 className="text-2xl font-semibold tracking-tight text-slate-900">
          Cuna Inteligente
        </h1>
        <p className="text-sm text-slate-600">
          Ecosistema Ciberfísico para Supervisión Perimetral, Microclima y Nutrición Infantil.
        </p>
        <div className="pt-2 text-xs text-slate-500">
          Hito M0 — Esqueleto base inicializado.
        </div>
      </section>
    </main>
  );
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<HomeView />} />
    </Routes>
  );
}
