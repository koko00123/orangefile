# Módulo Escritorio de Orange — Especificación

## 1. Objetivo
Ordenar los íconos del escritorio de Windows en **cajones**: rectángulos con título donde se agrupan los íconos (Trabajo, Programas, Documentos…). Repartidos en **varios monitores** (el usuario tiene 3).

Prioridades, en este orden:
1. **Ligero.** Vive dentro de Orange (que ya tiene .NET + WPF cargados). Meta: **≤ 8 MB de RAM extra** con 10 cajones y **0 % de CPU en reposo** (nada de temporizadores ni sondeo; solo eventos).
2. **Fiable.** Nunca borra, mueve ni renombra archivos. Solo cambia **posiciones** de íconos. Si algo falla, el escritorio queda como estaba.
3. **Simple.** Colores sólidos, sin transparencia ni animaciones. Sin widgets de clima, correo, música, etc.

## 2. Idea central: usar los íconos reales del escritorio
Orange **no dibuja íconos**. El escritorio de Windows ya es una vista de carpeta (la de Explorer). Orange:
- dibuja solo el **fondo del cajón** (rectángulo de color + barra de título), y
- **coloca los íconos reales** dentro de ese rectángulo usando la API de Shell (`IFolderView`).

Así abrir, arrastrar, renombrar, el menú contextual y las miniaturas siguen siendo los de Windows, sin código extra ni memoria extra.

Alternativa descartada: dibujar los íconos en ventanas propias (como hacen los widgets de carpeta). Exige reimplementar menú contextual, arrastrar/soltar, miniaturas y renombrar, y consume más RAM.

## 3. Alcance
### Dentro (v1)
- Cajones: crear, mover, redimensionar, renombrar, cambiar color, borrar (borrar el cajón **no** borra archivos; sus íconos quedan libres).
- **Multi-monitor**: cada cajón pertenece a un monitor y guarda su posición relativa a ese monitor (§6).
- Asignar íconos a un cajón: arrastrando el ícono dentro del cajón (con el mouse, como siempre) o por **reglas** (§7).
- Ordenar dentro del cajón: por nombre, tipo o manual.
- Reacomodar automáticamente cuando aparece un archivo nuevo en el escritorio, cuando Explorer se reinicia o cuando cambia la resolución/monitores.
- **Layouts**: guardar varias distribuciones con nombre y cambiar desde la bandeja o con un atajo.
- Ocultar/mostrar todos los íconos y cajones (atajo y menú de bandeja).
- Activación con `Settings.DesktopModuleEnabled` (por defecto `false`, Orange es público).

### Fuera (por ahora)
- Transparencia, desenfoque, animaciones.
- Plegar cajones, scroll dentro del cajón, pestañas.
- Widgets que no sean de íconos.
- Mostrar carpetas que no estén en el escritorio.

## 4. Arquitectura

```
Orange/Desktop/
  Model/      DesktopConfig, Box, BoxRule, Layout, MonitorKey
  Store/      DesktopStore (carga/guarda %LOCALAPPDATA%\Orange\desktop.json)
  Layout/     BoxLayout (posiciones de íconos dentro de un cajón), Membership (ícono → cajón), RuleMatcher, MonitorMap
  Shell/      IDesktopIcons + ShellDesktopIcons (COM: IShellWindows → IFolderView/IFolderView2)
  Engine/     DesktopEngine (orquesta: eventos → recalcular → aplicar posiciones)
Orange/UI/Desktop/
  BoxWindow.cs        fondo de un cajón (sin XAML, dibujo mínimo)
  EditOverlay.cs      modo edición: mover/redimensionar/menú del cajón
Orange.Tests/         proyecto xUnit (nuevo)
```

Reglas:
- `Model`, `Store`, `Layout` **no dependen de Win32** → se prueban con xUnit. El acceso a Shell/ventanas se inyecta por interfaces (`IDesktopIcons`, `IMonitors`, `IClock`).
- Todo P/Invoke y COM nuevo va en `Orange/Input/Native.cs` (interfaces COM incluidas).
- El módulo no instala hooks propios. Para saber cuándo el usuario soltó un ícono, se suscribe a un evento nuevo de `DragWatcher` ("botón izquierdo soltado sobre el escritorio"), que solo **encola** el aviso (regla de hooks de `AGENTS.md`).
- Trabajo con COM de Shell en el hilo de UI (STA) y con un debounce: nunca dentro de un hook.

## 5. Modelo de datos (`desktop.json`)
```jsonc
{
  "version": 1,
  "activeLayout": "Casa",
  "layouts": [
    {
      "name": "Casa",
      "boxes": [
        {
          "id": "b1",                       // estable, generado
          "title": "Programas",
          "color": "#2B2B2B",               // fondo sólido
          "titleColor": "#FFFFFF",
          "monitor": { "id": "\\\\?\\DISPLAY#DEL41A8#...", "name": "DELL U2419H" }, // MonitorKey (§6)
          "rect": { "x": 0.02, "y": 0.05, "w": 0.20, "h": 0.40 }, // fracciones del área de trabajo del monitor
          "sort": "Name",                   // Name | Type | Manual
          "items": ["Visual Studio Code.lnk", "Orange.lnk"],     // nombres de ítem (§8)
          "rules": [ { "kind": "Extension", "value": ".lnk" } ] // Extension | NameContains | Kind | Regex
        }
      ],
      "looseItems": ["Nota.txt"]          // sacados a mano de un cajón (§7)
    }
  ]
}
```
- Guardado atómico (`.tmp` + renombrar). Archivo corrupto → `JobRunner.Log`, se respalda como `desktop.json.bad` y se arranca vacío.
- `items` es el orden manual y la pertenencia explícita. Un ítem pertenece a **un solo** cajón.

## 6. Multi-monitor
- **MonitorKey**: identificador estable del monitor (ruta de dispositivo de `QueryDisplayConfig`/`DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorDevicePath`), con respaldo `DeviceName` (`\\.\DISPLAY2`) si no se obtiene. Guardar también un nombre legible ("DELL U2419H") para la interfaz.
- La posición del cajón se guarda en **fracciones del área de trabajo** (sin barra de tareas) de su monitor, para sobrevivir a cambios de resolución y escala.
- Conversión a coordenadas del escritorio: área de trabajo del monitor (`GetMonitorInfo`, píxeles físicos, Orange es PerMonitorV2) → coordenadas cliente de la vista del escritorio (origen = esquina superior izquierda de la pantalla virtual; `TODO(verify)` P2).
- Monitor ausente (desconectado o portátil sin pantallas externas): sus cajones **no se dibujan** y sus íconos **no se mueven** (Windows los habrá reacomodado; al volver el monitor, se reacomodan de nuevo). Opción `Settings.DesktopOrphanBoxes`: `Hide` (por defecto) | `MoveToPrimary`.
- Evento de cambio: `WM_DISPLAYCHANGE` y `WM_SETTINGCHANGE (SPI_SETWORKAREA)` → debounce 1 s → recalcular todo.
- En modo edición se puede arrastrar un cajón de un monitor a otro: su `monitor` pasa a ser el monitor que contiene el centro del cajón.
- Menú del cajón: "Mover al monitor ▸ 1 / 2 / 3" (numerados de izquierda a derecha).

## 7. Reglas
Para ítems **sin cajón** (nuevos o libres). Se evalúan en orden de cajones y de reglas; gana la primera.
- `Extension` (`.pdf;.docx`), `NameContains` (texto, sin distinguir mayúsculas), `Kind` (`Shortcut` | `Folder` | `File`), `Regex` (con timeout de 50 ms).
- Un ítem que el usuario sacó a mano de un cajón queda marcado como "libre a propósito" (`looseItems` en el layout) y las reglas no lo vuelven a meter.

## 8. Acceso al escritorio (Shell)
- Obtener la vista: `IShellWindows.FindWindowSW(CSIDL_DESKTOP, …, SWC_DESKTOP, …, SWFO_NEEDDISPATCH)` → `IServiceProvider.QueryService(SID_STopLevelBrowser)` → `IShellBrowser.QueryActiveShellView` → `IFolderView2`.
- Leer: `Items(SVGIO_ALLVIEW)`, `GetItemPosition`, `GetSpacing` (tamaño de celda), `GetFolder` → nombre de cada ítem con `SIGDN_PARENTRELATIVEPARSING` (nombre de archivo con extensión; ítems virtuales como la Papelera tienen nombre GUID y también valen).
- Escribir: `SelectAndPositionItems(…, SVSI_POSITIONITEM)` por lotes.
- Requisito: desactivar "Organizar íconos automáticamente" y "Alinear a la cuadrícula" (`SetCurrentFolderFlags(FWF_AUTOARRANGE | FWF_SNAPTOGRID, 0)`). Avisar al usuario la primera vez. `TODO(verify)` P4.
- El escritorio combina el del usuario y el público (`C:\Users\Public\Desktop`); ambos se vigilan.
- Si Explorer se reinicia (mensaje registrado `TaskbarCreated`), la vista COM queda inválida: soltarla, esperar 2 s y reaplicar.

## 9. Dibujo de los cajones
Dos modos, `Settings.DesktopRenderMode` (porque P1 no está verificado):
- **`Wallpaper` (preferido):** el fondo del cajón se dibuja **detrás** de los íconos, en la capa del fondo de pantalla (ventana hija del `WorkerW` que aparece al enviar `0x052C` a `Progman`, la técnica de los fondos animados). Los íconos quedan encima y siguen siendo clicables. No desaparece con Win+D.
- **`Frame` (respaldo):** ventana normal sin activar (`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`), siempre al fondo del orden Z, con una **región con hueco** (`SetWindowRgn`): solo se ven la barra de título y el borde; el interior es el escritorio real. Se usa si no se encuentra el `WorkerW`.

Detalles comunes:
- Color sólido, barra de título con el nombre y el contador de ítems; si no caben, "+N" en la barra.
- Una ventana por cajón; se redibuja solo al cambiar tamaño, color, título o DPI.

## 10. Modo edición
En modo `Wallpaper` los clics van a los íconos, no al cajón. Por eso mover/redimensionar se hace en un **modo edición**:
- Entrar: menú de bandeja "Editar escritorio" o atajo (por defecto `Ctrl+Alt+D`, configurable, `RegisterHotKey`).
- Los cajones pasan a ventanas normales encima del escritorio, con asas de tamaño, "+ Nuevo cajón", y menú (renombrar, color, orden, reglas, mover a monitor, borrar).
- Mientras se mueve un cajón, sus íconos lo siguen al soltar.
- Salir: `Esc`, botón "Listo" o el mismo atajo. Se guarda y los cajones vuelven al modo normal.

## 11. Flujo de eventos (sin sondeo)
| Evento | Acción |
|---|---|
| Archivo creado/renombrado/borrado en los dos escritorios (`FileSystemWatcher`) | debounce 700 ms → leer ítems → reglas → reacomodar |
| Usuario suelta botón izquierdo sobre el escritorio (aviso de `DragWatcher`) | 300 ms → leer posiciones → actualizar pertenencia por posición (ítem dentro de un cajón = de ese cajón) → reacomodar ese cajón |
| `WM_DISPLAYCHANGE` / cambio de área de trabajo / DPI | debounce 1 s → recalcular todo |
| `TaskbarCreated` (Explorer reiniciado) | reconectar COM → reaplicar |
| Cambio de layout | aplicar el layout entero |

Costo esperado: leer ~100 ítems por COM tarda pocos milisegundos; se hace solo cuando ocurre un evento.

## 12. Bandeja e interfaz
Menú de bandeja (si el módulo está activo): "Editar escritorio", "Layout ▸ (lista)", "Ocultar íconos del escritorio".
En `SettingsWindow`, una sección "Escritorio": activar, modo de dibujo, atajos, qué hacer con cajones de monitores ausentes.
Todos los textos con `Loc.T` + traducciones `es` y `pt`.

## 13. Pendiente de verificar
- **P1** — En Windows 11 24H2/25H2 cambió la jerarquía `Progman`/`WorkerW`. Verificar que el modo `Wallpaper` funciona en la PC del usuario; si no, usar `Frame`.
- **P2** — Sistema de coordenadas de `GetItemPosition`/`SelectAndPositionItems` con 3 monitores de distinta escala (DPI) y monitores con coordenadas negativas (a la izquierda o arriba del principal).
- **P3** — Que `SelectAndPositionItems` desde otro proceso (proxy COM a Explorer) funcione con muchos ítems a la vez sin parpadeo.
- **P4** — Si apagar `FWF_AUTOARRANGE`/`FWF_SNAPTOGRID` persiste tras reiniciar Explorer, o hay que reaplicarlo.
- **P5** — Comportamiento de los cajones con Win+D y "Mostrar escritorio".
- **P6** — Cambio del tamaño de íconos (Ctrl+rueda en el escritorio) → `GetSpacing` cambia: detectar y reacomodar.
