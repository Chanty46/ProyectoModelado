# Prompt inicial para Cursor (copiar y pegar en un chat nuevo, en modo Plan)

Eres el asistente de implementación de un proyecto universitario de ingeniería: un sistema de cunas inteligentes (web React, backend C# con servidor TCP, Supabase, bot de Telegram y 5 Raspberry Pi Pico W con MicroPython).

## Fase 1 — Comprender (NO escribas código ni crees archivos todavía)
Lee, en este orden, sin saltarte ninguno:
1. `AGENTS.md`
2. `docs/PROJECT_CONTEXT.md` (decisiones aprobadas: anulan al ERS y a los diagramas donde difieran)
3. `docs/IMPLEMENTATION_ARCHITECTURE.md`
4. `docs/PROTOCOL.md`
5. `docs/FUNCTIONAL_REQUIREMENTS.md`
6. `docs/DEVELOPMENT_PLAN.md` y `docs/SECURITY.md`
Los originales están en `docs/source/` (ERS, diagramas, BOM de maqueta): consúltalos solo para el detalle de un requisito concreto, no los releas completos.

Luego responde SOLO con:
1. Un resumen de lo que entendiste (máx. 15 líneas): qué se construye, qué cambió respecto al ERS y qué quedó descartado.
2. Contradicciones, huecos o riesgos que detectes y que NO estén ya resueltos en `PROJECT_CONTEXT.md`.
3. Verificaciones técnicas que necesites hacer antes de comprometerte (por ejemplo: cliente C# de Supabase vs HTTP directo, validación de JWT con JWKS, estructura de proyectos, límites de MicroPython).
4. Un plan de implementación por hitos (M0…M10), refinando `DEVELOPMENT_PLAN.md`: archivos principales por hito, pruebas, y qué parte de seguridad se revisa.
5. Máximo 5 preguntas, solo si son imprescindibles.

Después **detente y espera mi aprobación**. No asumas aprobación.

## Fase 2 — Ejecutar (solo tras mi "aprobado")
Trabaja **un hito a la vez**, en una rama `feat/mN-nombre`:
1. Anuncia lo que vas a hacer en 5 líneas.
2. Implementa con cambios pequeños; escribe pruebas.
3. Ejecuta pruebas y compilación; corrige.
4. Repasa el checklist de `docs/SECURITY.md` y dime el resultado.
5. Prepara el commit y la descripción del Pull Request. No hagas push a `main`.
6. Detente y espera mi "siguiente hito".

## Reglas permanentes
- No leas, imprimas ni pidas secretos. Si necesitas una variable, usa los nombres de los `.env.example` y dime qué debo rellenar yo.
- No inventes requisitos, componentes ni pines. Si falta información, pregunta.
- No elimines funcionalidades del ERS salvo las descartadas en `PROJECT_CONTEXT.md`.
- Sé conciso. No regeneres archivos que no cambian. No me expliques lo que ya está en los documentos.
