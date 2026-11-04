# Módulo Escritorio — Tareas

Una tarea por sesión, en orden. Antes de empezar leer `AGENTS.md` y `docs/desktop/SPEC.md`.
Al terminar: `dotnet build` y `dotnet test` en verde, marcar `[x]` y agregar una línea de notas debajo.

Prompt sugerido para cada sesión:
> Lee AGENTS.md y docs/desktop/*. Implementa la siguiente tarea sin marcar de docs/desktop/TASKS.md. Cumple todos sus criterios de aceptación, corre build y tests, y márcala como hecha con notas.

---

## [ ] T0 — Proyecto de pruebas
- Crear `Orange.Tests` (xUnit, `net8.0-windows10.0.19041.0`) que referencia `Orange`. Crear `Orange.sln` en la raíz con ambos proyectos.

**Aceptación:** `dotnet test` corre (aunque solo haya una prueba trivial). `build.ps1` sigue funcionando y no empaqueta el proyecto de pruebas.

## [ ] T1 — Modelo + almacenamiento
- `Orange/Desktop/Model/*` según SPEC §5 (records inmutables; reglas polimórficas con `[JsonPolymorphic]` + `"kind"`).
- `DesktopStore`: cargar/guardar `%LOCALAPPDATA%\Orange\desktop.json`, guardado atómico, archivo corrupto → `desktop.json.bad` + config vacía.
- `Settings.DesktopModuleEnabled` (`false`), `DesktopRenderMode` (`Wallpaper`), `DesktopOrphanBoxes` (`Hide`), `DesktopEditHotkey` (`Ctrl+Alt+D`).

**Aceptación:** prueba de ida y vuelta JSON con todos los tipos de regla y 3 monitores; archivo corrupto no lanza excepción.

## [ ] T2 — Lógica de distribución (pura, sin Win32)
- `MonitorMap`: fracciones ⇄ píxeles a partir de una lista de monitores `{ Key, WorkArea, Dpi }`; monitor que contiene un punto; cajones huérfanos según `DesktopOrphanBoxes`.
- `BoxLayout.Place(box rect, cellSize, items, sort) → posiciones + overflow`.
- `Membership.FromPositions(...)`: qué cajón contiene cada ítem (por el centro del ícono).
- `RuleMatcher` según SPEC §7, respetando `looseItems`.

**Aceptación:** pruebas con 3 monitores sintéticos, incluido uno con coordenadas negativas y escalas 100 % / 125 % / 150 %; cajón lleno → overflow correcto; monitor ausente → cajón oculto (o movido al principal con la otra opción); reglas en orden y con regex con timeout.

## [ ] T3 — Acceso al escritorio (Shell COM)
- `IDesktopIcons` + `ShellDesktopIcons` según SPEC §8: listar ítems (nombre, posición), `GetSpacing`, posicionar por lotes, desactivar auto-organizar/alinear a cuadrícula, reconectar si Explorer se reinicia.
- `IMonitors` + implementación Win32 (`EnumDisplayMonitors`, `GetMonitorInfo`, `GetDpiForMonitor`, `QueryDisplayConfig` para la MonitorKey).
- Comando de diagnóstico: `Orange.exe --desktop-dump <salida.md>` que escribe monitores (key, nombre, área, DPI) e ítems del escritorio con posición, y sale sin abrir la app.

**Aceptación (manual, anotar):** el dump en la PC del usuario lista los 3 monitores y todos los íconos con posiciones coherentes (resolver P2). Mover un ícono por código a cada monitor funciona (P3).

## [ ] T4 — Dibujo de cajones
- `BoxWindow` en modos `Wallpaper` y `Frame` (SPEC §9). Si `Wallpaper` falla, registrar y caer a `Frame`.
- `DesktopEngine` mínimo: al arrancar (si el módulo está activo), dibujar los cajones del layout activo y colocar sus íconos.
- Integración en `App.OnStartup`.

**Aceptación (manual):** con un `desktop.json` escrito a mano con un cajón por monitor, los 3 cajones aparecen en su monitor con sus íconos dentro; abrir y arrastrar íconos funciona normal. Anotar resultado de P1 y P5. Medir RAM antes/después (meta ≤ 8 MB extra).

## [ ] T5 — Modo edición
- `EditOverlay` según SPEC §10: crear, mover (también entre monitores), redimensionar, renombrar, color, orden, mover a monitor N, borrar. Atajo global.
- Textos con `Loc.T` + traducciones es/pt.

**Aceptación (manual):** crear un cajón en cada monitor sin tocar el JSON; los íconos siguen al cajón; borrar un cajón deja sus íconos libres y ningún archivo cambia.

## [ ] T6 — Automatización por eventos
- Tabla de eventos de SPEC §11: `FileSystemWatcher` (usuario + público), aviso de "botón soltado sobre el escritorio" en `DragWatcher` (solo encolar), `WM_DISPLAYCHANGE`/área de trabajo/DPI, `TaskbarCreated`, cambio de tamaño de íconos (P6).
- Reglas aplicadas a ítems nuevos.

**Aceptación (manual):** guardar un PDF en el escritorio → entra solo al cajón con regla `.pdf`; arrastrar un ícono a otro cajón → queda ahí y se reordena; reiniciar Explorer → todo vuelve a su lugar; desconectar un monitor y reconectarlo → sus cajones desaparecen y vuelven. CPU 0 % en reposo (Administrador de tareas, 1 minuto).

## [ ] T7 — Layouts y ocultar
- Guardar layout actual con nombre, cambiar de layout, borrar layout. Menú de bandeja "Layout ▸" y atajo opcional por layout.
- "Ocultar íconos del escritorio": oculta íconos y cajones; volver a mostrar restaura todo.

**Aceptación (manual):** dos layouts distintos (ej. Casa / Trabajo) se alternan sin perder íconos; ocultar/mostrar funciona en los 3 monitores.

## [ ] T8 — Ajustes, README y pulido
- Sección "Escritorio" en `SettingsWindow` (SPEC §12).
- `README.md` / `README.en.md` / `README.pt.md`: sección corta del módulo (desactivado por defecto).
- Revisar memoria y CPU finales y anotarlas aquí.

**Aceptación:** sin regresiones en las ruedas de conversión ni en compartir con el teléfono.

---
## Notas / pendientes descubiertos
(Agregar aquí lo encontrado, con referencia a P1–P6 de SPEC §13.)
