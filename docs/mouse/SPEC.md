# Módulo Mouse de Orange — Especificación

## 1. Objetivo
Reemplazar **X-Mouse Button Control (XMBC)** dentro de Orange, para que el usuario tenga un solo programa en segundo plano.

- Mismas capacidades que el usuario usa hoy en XMBC: perfiles por aplicación/ventana/zona, 10 capas, remapeo de todos los botones del mouse, teclas simuladas, ejecutar programas, multimedia.
- **Importar** el perfil exportado de XMBC (`.xmbcp`) sin reconfigurar nada a mano.
- **Exportar/importar** perfiles propios de Orange (JSON).
- Interfaz con la **misma organización** que XMBC (lista de perfiles + pestañas de capas + un desplegable por botón), dibujada con WPF propio. No copiar íconos ni textos de XMBC.
- Nuevo disparador: teclas **F13–F24** (las envía el software del mouse Sadex, `sade.exe`) como entradas de primera clase.

XMBC es software cerrado: no hay código que reutilizar. Solo se reutiliza su **formato de archivo** (ver `XMBC-FORMAT.md`).

## 2. Contexto del usuario (datos reales, resumidos)
- 48 perfiles: programas (Word, Excel, AutoCAD, Photoshop, navegadores, Spotify…), zonas de la barra de tareas por monitor, y pestañas del navegador detectadas por título de ventana.
- 8 capas en uso; ~600 asignaciones reales. Las más usadas: "Teclas simuladas" (~170), "Deshabilitar" (~390), "Ejecutar aplicación" (~36), multimedia, cambiar capa.
- `SwitchOnMouseOver = true`: el perfil se elige por la **ventana bajo el cursor**, no por la ventana en primer plano.
- Flujo actual de los botones extra del Sadex: `sade.exe` envía `Ctrl+Alt+Shift+F5…F12` → XMBC cambia de capa (hotkey global) → el botón actúa en esa capa → XMBC vuelve sola a la capa 1 tras ~0,3 s (AutoSwitch).

## 3. Alcance
### Dentro (v1)
- Hooks globales de mouse y teclado; bloquear el evento original cuando hay acción asignada.
- Botones: `Left, Right, Middle, XLeft (X1), XRight (X2), WheelUp, WheelDown, TiltLeft, TiltRight`.
- Disparadores de teclado: F13–F24 y combinaciones configurables (para seguir aceptando `Ctrl+Alt+Shift+F5…F12` durante la transición).
- Capas 1–10 por perfil, con nombre, y "volver a capa 1 tras N segundos" (AutoSwitch) por capa.
- Coincidencia de perfiles: por proceso, por proceso+clase de ventana (W2), por título+proceso+clase (W3, con regex), por zona rectangular dentro de la ventana, y perfil "Default".
- Acciones: ver §6.
- Importador XMBC, exportar/importar JSON propio.
- Interfaz de configuración.
- Indicador en pantalla (OSD) breve al cambiar de capa (opcional, desactivable).
- Pausar/reanudar todo el módulo desde el menú de la bandeja.

### Fuera (por ahora)
- Velocidad de puntero, aceleración, cursores personalizados, "movimiento a scroll", botones persistentes (sticky), chords, scripts. El importador debe **listarlos como no soportados**, no fallar.
- Apps con privilegios de administrador (ver §8).

## 4. Arquitectura

```
Orange/Mouse/
  Model/        MouseConfig, Profile, Layer, Binding, MouseAction (+ tipos de acción), Trigger
  Import/       XmbcImporter (XML → MouseConfig), XmbcActionMap, ImportReport
  Keys/         KeySequenceParser ("{CTRL}{ALT}p" → pasos), KeySender (SendInput)
  Engine/       MouseEngine (estado, capas, timers), ProfileMatcher, ActionRunner
  Input/        MouseHook (WH_MOUSE_LL), KeyTriggerHook (WH_KEYBOARD_LL)
  Store/        MouseStore (carga/guarda %LOCALAPPDATA%\Orange\mouse.json, export/import)
Orange/UI/Mouse/
  MouseWindow.xaml(.cs)   ventana de configuración
  LayerOsd.xaml(.cs)      indicador de capa
Orange.Tests/             proyecto xUnit nuevo
```

Reglas:
- `Model`, `Import`, `Keys/KeySequenceParser` y `Engine/ProfileMatcher` **no dependen de Win32** → se prueban con xUnit sin hooks reales. El acceso a Win32 se inyecta por interfaces (`IWindowInfo`, `IInputSender`, `IProcessLauncher`, `IClock`).
- El módulo es independiente de `DragWatcher`. Si ambos instalan hooks, no deben interferir: el hook de Mouse **no** debe bloquear el clic izquierdo cuando no hay acción asignada (ni cuando la acción es "No change").
- Se activa con `Settings.MouseModuleEnabled` (por defecto `false`, porque Orange es un producto público).

## 5. Modelo de datos (JSON, `mouse.json`)
```jsonc
{
  "version": 1,
  "enabled": true,
  "profileSelection": "UnderCursor",        // o "Foreground"
  "simulatedKeyDelayMs": 1,
  "showLayerOsd": true,
  "keyTriggers": [                          // disparadores de teclado globales
    { "keys": "F13", "action": { "type": "SwitchLayer", "layer": 2 } },
    { "keys": "Ctrl+Alt+Shift+F5", "action": { "type": "SwitchLayer", "layer": 2 } }
  ],
  "profiles": [
    {
      "id": "default", "description": "Default", "enabled": true,
      "match": { "kind": "Default" },
      "layers": [ /* 10 capas */ ]
    },
    {
      "id": "…", "description": "RELOJ MON1", "enabled": true,
      "match": {
        "kind": "Window",                  // Process | Window | Title | Default
        "process": "explorer.exe",
        "windowClass": "TrayNotifyWnd",
        "parentClass": "Shell_TrayWnd",
        "title": null, "regex": false,
        "area": { "left": 0, "top": 0, "right": 295, "bottom": 60 }   // null = toda la ventana
      },
      "layers": [
        {
          "index": 1, "name": "", "autoRevertSeconds": null,
          "bindings": {
            "Left":   { "type": "NoChange" },
            "Middle": { "type": "RunApp", "path": "C:\\…\\app.exe", "args": "" },
            "XRight": { "type": "SimulatedKeys", "keys": "{LWIN}d", "mode": "OnPress" }
          }
        }
      ]
    }
  ]
}
```
- Un botón ausente en `bindings` = `NoChange`.
- `SameAsDefault` y `SameAsLayer1` son tipos de acción válidos (resolución en tiempo de ejecución).

## 6. Acciones (v1)
| Tipo | Descripción |
|---|---|
| `NoChange` | No interceptar; el evento pasa tal cual. |
| `Disable` | Bloquear el evento. |
| `MouseButton` | Emitir otro botón (`Left/Right/Middle/X1/X2/DoubleClick/WheelUp/WheelDown/TiltLeft/TiltRight`). |
| `SimulatedKeys` | Secuencia en sintaxis XMBC (ver `XMBC-FORMAT.md` §5). |
| `RunApp` | Ejecutar programa (ruta + argumentos). |
| `Media` | `PlayPause, Next, Previous, Stop, Mute, VolumeUp, VolumeDown`. |
| `Window` | `Close (Alt+F4), Minimize, Maximize, Restore, ShowDesktop, NextWindow, PrevWindow`. |
| `Edit` | `Copy, Cut, Paste, Undo, Redo, Enter, Escape`. |
| `Browser` | `Back, Forward, Refresh, Stop, Search, Home`. |
| `SwitchLayer` | Ir a capa N (`layer`), `Next`, `Previous`. |
| `SameAsDefault`, `SameAsLayer1` | Delegar. |
| `Unsupported` | Acción importada de XMBC que aún no existe; guarda el código original, no hace nada y se muestra en la UI con aviso. |

## 7. Comportamiento del motor
1. **Evento de botón** (hook de mouse): determinar perfil (§7.1) → capa activa → binding.
   - `NoChange` → `CallNextHookEx` sin bloquear.
   - Cualquier otra → bloquear (`return 1`) y encolar la acción en el hilo de acciones. Para botones, la acción se dispara al **presionar** y se bloquea también el **soltar** correspondiente (para no dejar un "up" huérfano).
   - Ignorar eventos inyectados por el propio Orange (`LLMHF_INJECTED` + marca en `dwExtraInfo`) para no entrar en bucle.
2. **Disparador de teclado** (hook de teclado): si la tecla/combinación está en `keyTriggers` o en el perfil, bloquear y ejecutar. Ignorar auto-repeat. Ignorar eventos inyectados por Orange.
3. **Capas**: estado por perfil (o global; ver Pendiente P4). Al cambiar a capa N con `autoRevertSeconds = s`, iniciar timer; al vencer, volver a capa 1. Mostrar OSD si está activo.
4. **Hilo de acciones**: una cola (`Channel<T>`) con un único consumidor, para que las secuencias se ejecuten en orden y fuera del hook.

### 7.1 Selección de perfil
- Ventana: la que está bajo el cursor (`WindowFromPoint` → ventana raíz con `GetAncestor(GA_ROOT)` para el título; la ventana hija para la clase) si `profileSelection = UnderCursor`; si no, `GetForegroundWindow`.
- Orden de prioridad (más específico gana): `Title` (W3) > `Window` (W2) con `area` > `Window` sin `area` > `Process` con `area` > `Process` > `Default`.
- Perfiles con `enabled = false` se ignoran.
- La consulta de proceso/clase/título se **cachea por HWND** (invalidar con `EVENT_OBJECT_NAMECHANGE` / `EVENT_OBJECT_DESTROY` o TTL de 500 ms). Nunca hacer `OpenProcess` dentro del hook sin caché.

## 8. Privilegios
Orange se reinicia sin privilegios a propósito (UIPI rompe arrastrar desde Explorer). Por eso v1 **corre sin admin**: las ventanas elevadas no se remapean (mismo comportamiento que XMBC sin admin). Mostrar esto en la UI. Un ayudante elevado separado queda para el futuro.

## 9. Interfaz (ver detalle en TASKS T7)
Ventana "Mouse" abierta desde la bandeja (`Mouse…`) y desde Configuración.
- Izquierda: lista de perfiles (descripción, coincidencia, activado). Botones Agregar, Editar, Copiar, Quitar. "Agregar" ofrece elegir un proceso en ejecución o "capturar ventana bajo el cursor" (cuenta regresiva de 3 s).
- Derecha: pestañas `Capa 1 … Capa 10` con nombre editable y "volver a capa 1 tras ___ s". Una fila por botón con desplegable de acción y botón `…` para parámetros (editor de teclas simuladas, ruta de programa).
- Pestaña "Disparadores": tabla de `keyTriggers`.
- Abajo: Importar XMBC…, Importar JSON…, Exportar JSON…, Aplicar, Cerrar.
- Todo texto vía `Loc.T` con traducción es/pt.

## 10. Requisitos no funcionales
- Callback de hook < 1 ms en el caso común (perfil en caché).
- Si el motor lanza excepción, se registra y el evento pasa sin bloquear (fail-open). El mouse nunca debe quedar inutilizable.
- Atajo de emergencia `Ctrl+Alt+Shift+Pausa` = pausar módulo.
- Pruebas xUnit para importador, parser de teclas, matcher y motor (con reloj falso).
