import { Component, type ReactNode, type ErrorInfo } from "react";
import { AlertOctagon, RotateCcw } from "lucide-react";

interface Props {
  children: ReactNode;
}

interface State {
  hasError: boolean;
  error: Error | null;
}

export class ErrorBoundary extends Component<Props, State> {
  public override state: State = {
    hasError: false,
    error: null,
  };

  public static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  public override componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error("Error no controlado capturado por ErrorBoundary:", error, errorInfo);
  }

  private handleReset = () => {
    this.setState({ hasError: false, error: null });
    window.location.reload();
  };

  public override render() {
    if (this.state.hasError) {
      return (
        <div className="min-h-screen flex items-center justify-center bg-slate-50 p-4">
          <div className="max-w-md w-full rounded-2xl border border-red-200 bg-white p-6 shadow-sm text-center space-y-4">
            <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-red-100 text-red-600">
              <AlertOctagon className="h-6 w-6" aria-hidden="true" />
            </div>
            <div>
              <h1 className="text-base font-semibold text-slate-900">
                Algo salió mal en la aplicación
              </h1>
              <p className="mt-1 text-xs text-slate-600">
                Ocurrió un error inesperado al renderizar la interfaz. Puedes recargar la aplicación para continuar.
              </p>
            </div>
            {this.state.error?.message && (
              <pre className="overflow-x-auto rounded-lg border border-slate-200 bg-slate-50 p-2 text-left text-[11px] font-mono text-slate-700">
                {this.state.error.message}
              </pre>
            )}
            <button
              type="button"
              onClick={this.handleReset}
              className="inline-flex items-center gap-1.5 rounded-xl bg-teal-600 px-4 py-2 text-xs font-medium text-white hover:bg-teal-700 transition-colors"
            >
              <RotateCcw className="h-4 w-4" aria-hidden="true" />
              Recargar aplicación
            </button>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
