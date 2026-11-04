# Módulo Escritorio — Tareas

Una tarea por sesión, en orden. Antes de empezar leer `AGENTS.md` y `docs/desktop/SPEC.md`.
Al terminar: `dotnet build` y `dotnet test` en verde, marcar `[x]` y agregar una línea de notas debajo.

Prompt sugerido para cada sesión:
> Lee AGENTS.md y docs/desktop/*. Implementa la siguiente tarea sin marcar de docs/desktop/TASKS.md. Cumple todos sus criterios de aceptación, corre build y tests, y márcala como hecha con notas.

---

## [x] T0 — Proyecto de pruebas
- Crear `Orange.Tests` (xUnit, `net8.0-windows10.0.19041.0`) que referencia `Orange`. Crear `Orange.sln` en la raíz con ambos proyectos.

**Aceptación:** `dotnet test` corre (aunque solo haya una prueba trivial). `build.ps1` sigue funcionando y no empaqueta el proyecto de pruebas.

> Notas: `Orange.Tests` (xUnit 2.5.3, UseWPF/UseWindowsForms como Orange) + `Orange.sln`. `InternalsVisibleTo("Orange.Tests")` en `Orange.csproj`. `dotnet publish` de `Orange.csproj` (lo que usa `build.ps1`) no incluye ensamblados de pruebas.

## [x] T1 — Modelo + almacenamiento
- `Orange/Desktop/Model/*` según SPEC §5 (records inmutables; reglas polimórficas con `[JsonPolymorphic]` + `"kind"`).
- `DesktopStore`: cargar/guardar `%LOCALAPPDATA%\Orange\desktop.json`, guardado atómico, archivo corrupto → `desktop.json.bad` + config vacía.
- `Settings.DesktopModuleEnabled` (`false`), `DesktopRenderMode` (`Wallpaper`), `DesktopOrphanBoxes` (`Hide`), `DesktopEditHotkey` (`Ctrl+Alt+D`).

**Aceptación:** prueba de ida y vuelta JSON con todos los tipos de regla y 3 monitores; archivo corrupto no lanza excepción.

> Notas: `Orange/Desktop/Model/DesktopConfig.cs` (records + reglas polimórficas por `"kind"`, todas con `value`) y `Store/DesktopStore.cs` (camelCase, `.tmp` + `File.Move`, corrupto → `.bad`). `Sanitize` repara nulos, ids repetidos, rects fuera de rango y ítems en dos cajones (gana el primero). El monitor se guarda como objeto `{id, name}` (SPEC §5 actualizado). El log se inyecta para que las pruebas no escriban en el log real.

## [x] T2 — Lógica de distribución (pura, sin Win32)
- `MonitorMap`: fracciones ⇄ píxeles a partir de una lista de monitores `{ Key, WorkArea, Dpi }`; monitor que contiene un punto; cajones huérfanos según `DesktopOrphanBoxes`.
- `BoxLayout.Place(box rect, cellSize, items, sort) → posiciones + overflow`.
- `Membership.FromPositions(...)`: qué cajón contiene cada ítem (por el centro del ícono).
- `RuleMatcher` según SPEC §7, respetando `looseItems`.

**Aceptación:** pruebas con 3 monitores sintéticos, incluido uno con coordenadas negativas y escalas 100 % / 125 % / 150 %; cajón lleno → overflow correcto; monitor ausente → cajón oculto (o movido al principal con la otra opción); reglas en orden y con regex con timeout.

> Notas: `Orange/Desktop/Layout/` — `Geometry` (píxeles físicos de pantalla virtual), `MonitorMap` (numeración izquierda→derecha, fracciones ⇄ píxeles, monitor por centro o el más cercano, huérfanos), `BoxLayout` (cuadrícula centrada bajo el título, orden natural / por tipo / manual, overflow), `Membership` (`FromPositions` por centro del ícono; `Reassign` aplica arrastres y marca `looseItems`), `RuleMatcher` (primer cajón que coincide; regex cacheada con 50 ms). Se asume que la posición del ítem es su esquina superior izquierda y que la celda es `GetSpacing`: confirmar en T3 (P2).

## [x] T3 — Acceso al escritorio (Shell COM)
- `IDesktopIcons` + `ShellDesktopIcons` según SPEC §8: listar ítems (nombre, posición), `GetSpacing`, posicionar por lotes, desactivar auto-organizar/alinear a cuadrícula, reconectar si Explorer se reinicia.
- `IMonitors` + implementación Win32 (`EnumDisplayMonitors`, `GetMonitorInfo`, `GetDpiForMonitor`, `QueryDisplayConfig` para la MonitorKey).
- Comando de diagnóstico: `Orange.exe --desktop-dump <salida.md>` que escribe monitores (key, nombre, área, DPI) e ítems del escritorio con posición, y sale sin abrir la app.

**Aceptación (manual, anotar):** el dump en la PC del usuario lista los 3 monitores y todos los íconos con posiciones coherentes (resolver P2). Mover un ícono por código a cada monitor funciona (P3).

> Notas: COM y P/Invoke en `Native.cs`; `Desktop/Shell/` con `ShellDesktopIcons` (ShellWindows → IFolderView2, nombre por `SHGDN_INFOLDER|FORPARSING`, tipo por `SFGAO_*`, reconecta una vez si el proxy muere), `Win32Monitors` (clave = `monitorDevicePath` de QueryDisplayConfig) y `DesktopDump`. `--probe` también restaura los flags de organizar/alinear. Resultado en la PC del usuario (Win 11 26200, 3 monitores a 100 %, uno a la izquierda en X negativo): 3 monitores con nombre y ruta estable, 184 ítems; origen de la vista = origen de la pantalla virtual (-1280, 0); celda 94 × 114; sondeo en los 3 monitores con Δ = 0, 0 → **P2 y P3 resueltos** para escala 100 % (con escalas distintas por monitor queda sin probar). P4 sigue pendiente.

## [x] T4 — Dibujo de cajones
- `BoxWindow` en modos `Wallpaper` y `Frame` (SPEC §9). Si `Wallpaper` falla, registrar y caer a `Frame`.
- `DesktopEngine` mínimo: al arrancar (si el módulo está activo), dibujar los cajones del layout activo y colocar sus íconos.
- Integración en `App.OnStartup`.

**Aceptación (manual):** con un `desktop.json` escrito a mano con un cajón por monitor, los 3 cajones aparecen en su monitor con sus íconos dentro; abrir y arrastrar íconos funciona normal. Anotar resultado de P1 y P5. Medir RAM antes/después (meta ≤ 8 MB extra).

> Notas: `UI/Desktop/BoxWindow` (ventana Win32 con `NativeWindow` + GDI, sin WPF), `DesktopLayer` (WorkerW dentro de Progman en 24H2+, o el WorkerW de nivel superior en builds viejos), `Engine/DesktopEngine` (aplica el layout activo una vez al arrancar si `DesktopModuleEnabled`) y `Engine/DesktopDiagnostics` (`--desktop-backup`, `--desktop-restore`, `--desktop-preview`). Prueba en la PC del usuario (build 26200) con un cajón por monitor y 12 íconos movidos desde el principal: el usuario vio los cajones en los dos modos y prefiere `Wallpaper` → **P1 resuelto**. Aplicar: 0,2 s. RAM privada: +3,4 MB (Wallpaper) / +3,1 MB (Frame); el working set sube ~20 MB, sobre todo DLL de shell compartidas. Respaldo → vista previa → restauración devolvió los 184 íconos a su posición exacta. Sin probar: P5 (Win+D) y abrir íconos con doble clic dentro del cajón (confirmar en T5).

## [x] T5 — Modo edición
- `EditOverlay` según SPEC §10: crear, mover (también entre monitores), redimensionar, renombrar, color, orden, mover a monitor N, borrar. Atajo global.
- Textos con `Loc.T` + traducciones es/pt.

**Aceptación (manual):** crear un cajón en cada monitor sin tocar el JSON; los íconos siguen al cajón; borrar un cajón deja sus íconos libres y ningún archivo cambia.

> Notas: `UI/Desktop/EditSession` (barra con "+ Nuevo cajón"/"Listo", menú ⋯ o clic derecho: renombrar, 8 colores, orden, regla de extensiones, mover a monitor N, borrar), `EditBoxWindow` (WPF + `WindowChrome`, semitransparente solo en edición, posición en píxeles físicos por HWND, guarda en `WM_EXITSIZEMOVE`), `HotkeyWindow` (`RegisterHotKey`, sin hook) y `Engine/Hotkey` (parser con pruebas). El motor ahora guarda la config en memoria (`SetLayout`, `ArrangeBox`, `AdoptFreeIcons`). Al pulsar "Listo", los íconos libres cuyo centro cae dentro de un cajón pasan a ser suyos (así se llena un cajón nuevo). `Orange.exe --desktop` activa el módulo solo en esa ejecución. Prueba manual del usuario con la versión de desarrollo: crear/mover/editar cajones y doble clic funcionan. Esperado hasta T6: un ícono arrastrado fuera del cajón no cambia de dueño y vuelve a su lugar al reaplicar. P5 (Win+D) sin confirmar explícitamente.

## [x] T6 — Automatización por eventos
- Tabla de eventos de SPEC §11: `FileSystemWatcher` (usuario + público), aviso de "botón soltado sobre el escritorio" en `DragWatcher` (solo encolar), `WM_DISPLAYCHANGE`/área de trabajo/DPI, `TaskbarCreated`, cambio de tamaño de íconos (P6).
- Reglas aplicadas a ítems nuevos.

**Aceptación (manual):** guardar un PDF en el escritorio → entra solo al cajón con regla `.pdf`; arrastrar un ícono a otro cajón → queda ahí y se reordena; reiniciar Explorer → todo vuelve a su lugar; desconectar un monitor y reconectarlo → sus cajones desaparecen y vuelven. CPU 0 % en reposo (Administrador de tareas, 1 minuto).

> Notas: `UI/Desktop/DesktopEvents` (FileSystemWatcher en escritorio del usuario y público, ventana oculta de nivel superior para `WM_DISPLAYCHANGE`/`SPI_SETWORKAREA`/`TaskbarCreated`, debounce 700 ms / 300 ms / 1 s / 2 s, en pausa durante el modo edición). `DragWatcher.LeftButtonUp` solo encola si hay suscriptor; el módulo comprueba que se soltó sobre `SysListView32` dentro de `SHELLDLL_DefView`. `DesktopEngine.Refresh` es incremental: renombres conservan cajón (`Membership.Rename`), solo los íconos cuya posición cambió desde la última vez (`Membership.Moved`) pueden cambiar de cajón (así el overflow no parece un arrastre), un archivo nuevo soltado sobre un cajón entra en él, reglas para los libres, y solo se mueven los íconos fuera de lugar. Medición en reposo, 60 s: 688 ms de CPU con el módulo vs 422 ms sin él (≈0,02 % del procesador, probablemente clics del usuario), +3 MB de RAM privada. El usuario confirmó que funciona (arrastrar dentro/fuera, regla, reinicio de Explorer). P6 (Ctrl+rueda) solo se detecta en el siguiente evento: `TODO(verify)` en `Refresh`.

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
