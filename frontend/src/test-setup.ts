import "@testing-library/jest-dom";
import { vi } from "vitest";

// Mock ResizeObserver for Recharts ResponsiveContainer and DOM measurements
class MockResizeObserver {
  observe = vi.fn();
  unobserve = vi.fn();
  disconnect = vi.fn();
}
globalThis.ResizeObserver = MockResizeObserver as unknown as typeof ResizeObserver;

// Mock Web Audio API AudioContext for IntercomPushToTalk
class MockAudioContext {
  sampleRate = 44100;
  destination = {};
  createMediaStreamSource = vi.fn().mockReturnValue({
    connect: vi.fn(),
    disconnect: vi.fn(),
  });
  createScriptProcessor = vi.fn().mockReturnValue({
    connect: vi.fn(),
    disconnect: vi.fn(),
    onaudioprocess: null,
  });
  close = vi.fn().mockResolvedValue(undefined);
}
globalThis.AudioContext = MockAudioContext as unknown as typeof AudioContext;

// Mock navigator.clipboard safely
if (!navigator.clipboard) {
  Object.defineProperty(navigator, "clipboard", {
    value: {
      writeText: vi.fn().mockResolvedValue(undefined),
      readText: vi.fn().mockResolvedValue(""),
    },
    writable: true,
    configurable: true,
  });
} else {
  vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue(undefined);
}

// Mock window.URL createObjectURL / revokeObjectURL for file exports
if (!window.URL.createObjectURL) {
  window.URL.createObjectURL = vi.fn().mockReturnValue("blob:mock-url");
}
if (!window.URL.revokeObjectURL) {
  window.URL.revokeObjectURL = vi.fn();
}

// Mock getBoundingClientRect on Element prototype to provide standard dimensions for responsive containers
Element.prototype.getBoundingClientRect = vi.fn(() => ({
  width: 500,
  height: 300,
  top: 0,
  left: 0,
  bottom: 300,
  right: 500,
  x: 0,
  y: 0,
  toJSON: () => {},
}));
