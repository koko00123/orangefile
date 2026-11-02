# Módulo Mouse — Tareas para Codex

Una tarea por sesión, en orden. Antes de empezar leer `AGENTS.md`, `docs/mouse/SPEC.md` y `docs/mouse/XMBC-FORMAT.md`.
Al terminar: `dotnet build` y `dotnet test` en verde, marcar `[x]` y agregar una línea de notas debajo.

Prompt sugerido para cada sesión de Codex:
> Lee AGENTS.md y docs/mouse/*. Implementa la siguiente tarea sin marcar de docs/mouse/TASKS.md. Cumple todos sus criterios de aceptación, corre build y tests, y márcala como hecha con notas.

---

## [ ] T0 — Proyecto de pruebas
- Crear `Orange.Tests` (xUnit, `net8.0-windows10.0.19041.0`) que referencia `Orange`. Agregarlo a una solución `Orange.sln` en la raíz (crearla si no existe).
- Helper `Fixtures.RealXmbcPath()` que devuelve `ORANGE_XMBC_FIXTURE` o `null` → pruebas con el archivo real usan `Skip` si es `null`.
- Agregar al `.gitignore`: `*.xmbcp`, `XMBCSettings.xml`.

**Aceptación:** `dotnet test` corre (aunque solo haya una prueba trivial). `build.ps1` sigue funcionando y no empaqueta el proyecto de pruebas.

## [ ] T1 — Modelo + almacenamiento
- `Orange/Mouse/Model/*` según SPEC §5 y §6 (records inmutables; acciones como jerarquía polimórfica con `[JsonPolymorphic]` + `"type"`).
- `MouseStore`: cargar/guardar `%LOCALAPPDATA%\Orange\mouse.json`; `Export(path)`, `Import(path)`; guardado atómico (escribir `.tmp` y renombrar).
- `Settings.MouseModuleEnabled` (bool, default `false`).

**Aceptación:** pruebas de ida y vuelta JSON (modelo → JSON → modelo idéntico) incluyendo todos los tipos de acción; archivo corrupto → se registra y se carga config vacía sin excepción.

## [ ] T2 — Parser de teclas simuladas
- `KeySequenceParser.Parse(string) → IReadOnlyList<KeyStep>` según XMBC-FORMAT §5.
- Pasos: `KeyChord(modifiers, key)`, `MouseClick`, `MouseDown/Up`, `Wheel`, `Wait(ms)`, `SetCursor(abs|rel)`, `MoveCursor`, `Run(path)`, `Layer(n)`, `Unknown(token)`.

**Aceptación:** pruebas para al menos: `{CTRL}{ALT}p`, `{CTRL}{SHIFT}{ESC}`, `{LWIN}{CTRL}{SHIFT}C`, `{RUN:C:\a b\x - copia.exe}{LAYER:1}`, `{WAIT:0,5}`, `{WAITMS:0.3}`, `{WAITMS:100}{ENTER}`, `{ASET:221,150}{LMB}`, `{MADD:x,22}`, `{LMBD}{LMBU}`, token desconocido `{FOO}`. Con el archivo real (si existe): parsear **todas** las secuencias sin excepción y sin `Unknown` salvo los listados como pendientes.

## [ ] T3 — Importador XMBC
- `XmbcImporter.Import(string path) → (MouseConfig, ImportReport)` según XMBC-FORMAT §2–§6.
- `XmbcActionMap` con la tabla de §4.
- `ImportReport`: perfiles importados, asignaciones por tipo, lista de `Unsupported` (perfil, capa, botón, código), advertencias de tokens.
- Comando de diagnóstico: `Orange.exe --import-xmbc <ruta> --report <salida.md>` que escribe el informe en Markdown y sale sin abrir la app.

**Aceptación:** con el archivo real: 48 perfiles + Default; ~600 asignaciones no `NoChange`; capa N+1 ← `_LayerN`; `W2`/`W3` separados en sus campos; los dos perfiles `vivaldi.exe` (con y sin zona) se conservan. Pruebas con un XML sintético mínimo dentro del repo (sin datos personales).

## [ ] T4 — ProfileMatcher
- Entrada: `WindowSnapshot { Process, Class, RootClass, ParentClass, Title, Rect, CursorPoint }`. Salida: perfil elegido.
- Prioridad según SPEC §7.1; regex con timeout (`Regex` con `TimeSpan` 50 ms) y compilados una vez.
- `Win32WindowInfo : IWindowInfo` que arma el snapshot con caché por HWND (SPEC §7.1).

**Aceptación:** pruebas con snapshots sintéticos para: proceso simple, W2 reloj, W2 + área, W3 regex título, perfil deshabilitado, empate zona vs. sin zona, fallback Default.

## [ ] T5 — Motor (sin hooks)
- `MouseEngine` con dependencias inyectadas (`IWindowInfo`, `IInputSender`, `IProcessLauncher`, `IClock`).
- API: `HandleButton(button, isDown, point) → bool block`, `HandleKey(vk, mods, isDown, isRepeat) → bool block`.
- Capas, `autoRevertSeconds`, `SameAsDefault`/`SameAsLayer1`, `keyTriggers`, cola de acciones de un solo consumidor.
- Bloquear el "up" de un botón cuyo "down" fue bloqueado.

**Aceptación:** pruebas con reloj falso: cambio de capa + reversión tras 0,3 s; `NoChange` no bloquea; `Disable` bloquea down y up; `F13` → `SwitchLayer 2` → siguiente clic usa capa 2; secuencia `{RUN:…}{LAYER:1}` llama al launcher y vuelve a capa 1.

## [ ] T6 — Hooks reales y ejecución
- `MouseHook` (WH_MOUSE_LL) y `KeyTriggerHook` (WH_KEYBOARD_LL) en un **hilo dedicado con su propio message loop** (no el hilo de UI).
- `Win32InputSender` con `SendInput`, marcando `dwExtraInfo` con una firma de Orange; los hooks ignoran eventos con esa firma.
- `Media`/`Window`/`Edit`/`Browser` vía `SendInput` de teclas virtuales (`VK_MEDIA_*`, `VK_VOLUME_*`, etc.).
- Integración en `App.OnStartup`: si `MouseModuleEnabled`, iniciar motor; menú de bandeja "Mouse…" y "Pausar mouse"; atajo de emergencia `Ctrl+Alt+Shift+Pausa`.
- Fail-open: cualquier excepción en el hook → registrar y dejar pasar el evento.

**Aceptación (manual, documentar en notas):** con XMBC cerrado y el perfil real importado, en Explorer y Spotify los botones hacen lo mismo que hacían en XMBC; arrastrar con Shift/Ctrl/Alt (rueda de Orange) sigue funcionando; pausar devuelve el mouse a la normalidad.

## [ ] T7 — Interfaz de configuración
- `UI/Mouse/MouseWindow` según SPEC §9, estilo visual de las ventanas existentes de Orange.
- Editor de teclas simuladas: campo de texto con la sintaxis + botón "Capturar" que graba una combinación + vista previa legible ("Ctrl + Alt + P").
- Selector de perfil: lista de procesos en ejecución, y "capturar ventana bajo el cursor (3 s)" que rellena proceso/clase/título.
- Mostrar acciones `Unsupported` con ícono de aviso y su código.
- Traducciones es/pt en `Loc.cs`.

**Aceptación (manual):** crear un perfil nuevo, asignar una tecla simulada, aplicar y probar sin reiniciar; exportar e importar JSON reproduce la misma configuración.

## [ ] T8 — OSD de capa y pulido
- `LayerOsd`: ventana sin foco (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT`) junto al cursor con "Capa N · nombre", 600 ms.
- Rendimiento: medir el tiempo del callback del hook (log de debug opcional) y verificar < 1 ms con caché caliente.
- Actualizar `README.md` (es/en/pt) con una sección corta del módulo Mouse (desactivado por defecto).

**Aceptación:** OSD no roba foco; sin regresiones en la rueda de conversión.

## [ ] T9 — Migración del usuario (checklist, no código)
1. En `sade.exe`, asignar los botones extra a F13–F24.
2. En Orange → Mouse → Disparadores, asignar F13–F24 a la acción equivalente.
3. Cerrar XMBC y quitarlo del inicio de Windows.
4. Usar una semana; anotar diferencias en `docs/mouse/TASKS.md`.

---
## Notas / pendientes descubiertos
(Codex agrega aquí lo que encuentre, con referencia a P1–P6 de XMBC-FORMAT §8.)
