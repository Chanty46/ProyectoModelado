import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act, render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MobileControlCard } from "./MobileControlCard";

type ChangeCallback = (change: { new: Record<string, unknown> }) => void;

interface FakeChannel {
  name: string;
  filter?: { table: string; filter: string };
  callback?: ChangeCallback;
  on: (event: string, filter: { table: string; filter: string }, cb: ChangeCallback) => FakeChannel;
  subscribe: (cb?: (status: string) => void) => FakeChannel;
}

// Cliente de Supabase falso: guarda inserciones y canales para simular Realtime.
const fake = vi.hoisted(() => {
  const state = {
    inserts: [] as Array<{ table: string; row: Record<string, unknown> }>,
    channels: [] as FakeChannel[],
    insertError: null as null | { code?: string; message?: string },
    heartbeat: null as string | null,
    commandEstado: "pendiente" as string,
  };

  const client = {
    channel(name: string) {
      const ch: FakeChannel = {
        name,
        on(_event, filter, cb) {
          ch.filter = filter;
          ch.callback = cb;
          return ch;
        },
        subscribe(cb) {
          cb?.("SUBSCRIBED");
          return ch;
        },
      };
      state.channels.push(ch);
      return ch;
    },
    removeChannel: async () => "ok",
    from(table: string) {
      return {
        insert: (row: Record<string, unknown>) => {
          state.inserts.push({ table, row });
          return Promise.resolve({ error: state.insertError });
        },
        select: () => ({
          eq: () => ({
            maybeSingle: () =>
              Promise.resolve({
                data: table === "cunas" ? { ultimo_heartbeat: state.heartbeat } : { estado: state.commandEstado },
                error: null,
              }),
          }),
        }),
      };
    },
  };

  return { state, client };
});

vi.mock("../../lib/supabase", () => ({ supabase: fake.client }));

function emitCommandUpdate(estado: string) {
  const ch = fake.state.channels.find((c) => c.filter?.table === "comandos");
  expect(ch).toBeDefined();
  const id = ch!.filter!.filter.replace("id=eq.", "");
  act(() => {
    ch!.callback!({ new: { id, estado } });
  });
}

describe("MobileControlCard", () => {
  beforeEach(() => {
    fake.state.inserts = [];
    fake.state.channels = [];
    fake.state.insertError = null;
    fake.state.heartbeat = new Date().toISOString();
    fake.state.commandEstado = "pendiente";
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("inserts a pending movil_on command with patron, velocidad and duracion_min", async () => {
    render(<MobileControlCard cunaId="cuna-1" />);

    fireEvent.change(screen.getByLabelText(/Patrón del móvil/i), { target: { value: "espiral" } });
    fireEvent.change(screen.getByLabelText(/Velocidad del móvil/i), { target: { value: "rapida" } });
    fireEvent.change(screen.getByLabelText(/Duración del móvil/i), { target: { value: "12" } });
    fireEvent.click(screen.getByRole("button", { name: /Encender móvil/i }));

    await waitFor(() => expect(fake.state.inserts).toHaveLength(1));
    const { table, row } = fake.state.inserts[0];
    expect(table).toBe("comandos");
    expect(row).toMatchObject({
      cuna_id: "cuna-1",
      tipo: "movil_on",
      estado: "pendiente",
      payload: { patron: "espiral", velocidad: "rapida", duracion_min: 12 },
    });
    // Se escucha exactamente la fila insertada
    const ch = fake.state.channels.find((c) => c.filter?.table === "comandos");
    expect(ch?.filter?.filter).toBe(`id=eq.${row.id as string}`);
    expect(screen.getByText(/Enviando…/)).toBeInTheDocument();
  });

  it("shows Confirmado when the crib marks the command as confirmado", async () => {
    render(<MobileControlCard cunaId="cuna-1" />);
    fireEvent.click(screen.getByRole("button", { name: /Encender móvil/i }));
    await waitFor(() => expect(fake.state.inserts).toHaveLength(1));

    emitCommandUpdate("confirmado");

    expect(await screen.findByText(/Confirmado/)).toBeInTheDocument();
  });

  it("shows an error when the crib marks the command as error", async () => {
    render(<MobileControlCard cunaId="cuna-1" />);
    fireEvent.click(screen.getByRole("button", { name: /Encender móvil/i }));
    await waitFor(() => expect(fake.state.inserts).toHaveLength(1));

    emitCommandUpdate("error");

    expect(await screen.findByText(/no pudo ejecutarlo/i)).toBeInTheDocument();
  });

  it("sends movil_off with an empty payload when pressing Detener móvil", async () => {
    render(<MobileControlCard cunaId="cuna-1" />);
    fireEvent.click(screen.getByRole("button", { name: /Detener móvil/i }));

    await waitFor(() => expect(fake.state.inserts).toHaveLength(1));
    expect(fake.state.inserts[0].row).toMatchObject({ tipo: "movil_off", payload: {}, estado: "pendiente" });
  });

  it("shows 'La cuna no respondió' after 10 seconds without an answer", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    render(<MobileControlCard cunaId="cuna-1" />);
    fireEvent.click(screen.getByRole("button", { name: /Encender móvil/i }));
    await waitFor(() => expect(fake.state.inserts).toHaveLength(1));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(10_000);
    });

    expect(await screen.findByText(/La cuna no respondió/)).toBeInTheDocument();
  });

  it("shows the insert error when RLS rejects the command", async () => {
    fake.state.insertError = { code: "42501", message: "new row violates row-level security policy" };
    render(<MobileControlCard cunaId="cuna-1" />);
    fireEvent.click(screen.getByRole("button", { name: /Encender móvil/i }));

    expect(await screen.findByText(/No tienes permiso para controlar esta cuna/)).toBeInTheDocument();
  });

  it("validates the duration (whole number from 1 to 30) before sending", () => {
    render(<MobileControlCard cunaId="cuna-1" />);
    const input = screen.getByLabelText(/Duración del móvil/i);
    const submit = screen.getByRole("button", { name: /Encender móvil/i });

    for (const bad of ["0", "31", "2.5", ""]) {
      fireEvent.change(input, { target: { value: bad } });
      expect(submit).toBeDisabled();
      expect(input).toHaveAttribute("aria-invalid", "true");
    }

    fireEvent.change(input, { target: { value: "30" } });
    expect(submit).toBeEnabled();
    expect(fake.state.inserts).toHaveLength(0);
  });

  it("shows En línea or Desconectada from cunas.ultimo_heartbeat", async () => {
    fake.state.heartbeat = new Date(Date.now() - 5_000).toISOString();
    const { unmount } = render(<MobileControlCard cunaId="cuna-1" />);
    expect(await screen.findByText("En línea")).toBeInTheDocument();
    unmount();

    fake.state.channels = [];
    fake.state.heartbeat = new Date(Date.now() - 90_000).toISOString();
    render(<MobileControlCard cunaId="cuna-1" />);
    expect(await screen.findByText("Desconectada")).toBeInTheDocument();

    // Un heartbeat nuevo por Realtime la vuelve a poner En línea
    const ch = fake.state.channels.find((c) => c.filter?.table === "cunas");
    act(() => {
      ch!.callback!({ new: { id: "cuna-1", ultimo_heartbeat: new Date().toISOString() } });
    });
    expect(await screen.findByText("En línea")).toBeInTheDocument();
  });
});
