# Formato de X-Mouse Button Control (XMBC 2.20.5)

Ingeniería inversa a partir del perfil real del usuario y de los archivos de idioma de XMBC.
Lo marcado **✅ verificado** se confirmó con datos reales. Lo marcado **❓ pendiente** debe confirmarse (ver §8) antes de darlo por correcto.

## 1. Archivos
- Exportación de perfil: `*.xmbcp` (XML). Configuración viva: `%APPDATA%\Highresolution Enterprises\XMouseButtonControl\XMBCSettings.xml` (mismo formato).
- Codificación: XML 1.0; leer con `XmlDocument`/`XDocument` respetando la declaración. Las descripciones pueden tener emojis y caracteres de ancho cero (limpiarlos solo para mostrar, no al guardar).

## 2. Estructura raíz
```xml
<root>
  <version major="2" minor="20" revision="5" build="0" />
  <Layers Number="8" />                       <!-- capas visibles en la UI de XMBC -->
  <GlobalHotkeys Enable="true"> <Hotkey0 Key="123" Modifier="7" /> … <Hotkey24 …/> </GlobalHotkeys>
  <LayerModifierKeys Enable="false"> <LayerKey0 …/> … </LayerModifierKeys>
  <SimulatedKeys Delay="1" />                 <!-- ms entre teclas simuladas -->
  <SwitchOnMouseOver Enable="true" />         <!-- perfil = ventana bajo el cursor -->
  <Persist Layer="false" />
  … (muchos ajustes globales que v1 ignora)
  <Default …> (mismo contenido que un Application) </Default>
  <Applications> <Application …/> × N </Applications>
</root>
```

## 3. `<Application>` (y `<Default>`)
Atributos relevantes:
| Atributo | Uso |
|---|---|
| `Name` | Clave de coincidencia (ver §3.1). |
| `Description` | Nombre visible. |
| `Enabled` | `true/false`. |
| `RegExMatching` | `true` → los campos de W2/W3 son regex. |
| `MatchType` | Siempre `1` en los datos reales. ❓ semántica. Guardar el valor. |
| `WindowArea` | `true` → aplicar rectángulo `Left/Top/Right/Bottom` (px relativos a la ventana ❓ cliente o ventana completa). |
| `Left`, `Top`, `Right`, `Bottom` | Rectángulo de zona. **Ojo:** también existen elementos hijos `<Left>`, `<Right>`; leer atributos con `GetAttribute`, no por propiedad dinámica. |
| `WindowMask` | `15` normal, `16` cuando hay `WindowArea`. ❓ Guardar valor. |

### 3.1 Formato de `Name` ✅
- `proceso.exe` → coincide por nombre de proceso (sin distinguir mayúsculas).
- `W2:<proceso> \;| <clase de ventana> \;| <clase de ventana padre/raíz>`
  - Ej.: `W2:explorer.exe \;| TrayNotifyWnd \;| Shell_TrayWnd` (reloj de la barra de tareas).
- `W3:<título> \;| <proceso> \;| <clase> \;| <clase padre>` con `*` = comodín.
  - Ej.: `W3:YouTube \;| vivaldi.exe \;| * \;| *` con `RegExMatching="true"` → título contiene/coincide "YouTube".
- Separador literal: ` \;| ` (espacio, barra invertida, punto y coma, barra vertical, espacio). Hacer `Split` con ese texto exacto y `Trim()`.
- Puede haber **dos perfiles con el mismo `Name`** que se distinguen por `WindowArea` (ej. `vivaldi.exe` completo y `vivaldi.exe` zona inferior). El de zona tiene prioridad.

### 3.2 Hijos de `<Application>`
- `<LayerNames layer1="" layer2="…" … layer10="" />`
- `<AutoSwitch layerN_switch="0|1" layerN_time="0.3" />` → si `switch=1`, volver a capa 1 tras `time` segundos. ❓ si el tiempo cuenta desde que se activa la capa o desde la última acción.
- `<RevertLayer1 …/>`, `<DisableNextPrev …/>`, `<Disable …/>` → booleanos por capa (guardar; v1 solo usa `Disable` = capa desactivada).
- Botones ✅:
  - Capa 1: `Left, Right, Middle, XLeft, XRight, TiltLeft, TiltRight, WheelUp, WheelDown`
  - Capa N+1: `<Boton>_LayerN` (o sea `Left_Layer1` = **capa 2**, …, `Left_Layer9` = capa 10). ✅ confirmado: `LayerNames layer2="salida-de-som-audifonos"` corresponde a `Left_Layer1` que ejecuta `audifonos.exe`.
  - `XLeft` = botón X1 ("atrás"), `XRight` = X2 ("adelante").

### 3.3 Elemento de botón
```xml
<Left action="28" keys="{CTRL}{ALT}p" keyaction="0" keyrepeat="0" app="" desc="" …>
  <mts …/>   <!-- movimiento-a-scroll, ignorar en v1 -->
</Left>
```
- `action`: código de acción (§4).
- `keys`: secuencia para teclas simuladas (§5).
- `keyaction`: modo de envío. Todos los datos reales con acción 28 tienen `keyaction="0"`. ❓ Asumir `0 = al presionar el botón`.
- `app`: ruta para "Ejecutar aplicación" (acción 41).

## 4. Códigos de acción
Los códigos 0–41 siguen el orden de la lista de acciones del archivo de idioma, y se validaron con tres anclas reales (28 = teclas simuladas con `keys`, 40 = sin cambios en 3722 asignaciones vacías, 41 = ejecutar app con `app`) y con contexto (Spotify X2 = 23 próxima pista, X1 = 24 pista anterior; barra de tareas rueda = 21/22 volumen).

| Código | Acción | Estado | Mapeo Orange |
|---|---|---|---|
| 0 | Clic izquierdo | ✅ deducido | `MouseButton Left` |
| 1 | Clic derecho | ✅ deducido | `MouseButton Right` |
| 2 | Siguiente ventana | deducido | `Window NextWindow` |
| 3 | Atrás | deducido | `Browser Back` |
| 4 | Adelante | deducido | `Browser Forward` |
| 5 | Clic central | ✅ deducido | `MouseButton Middle` |
| 6 | Deshabilitar | ✅ deducido (388 usos) | `Disable` |
| 7 | Cerrar (Alt+F4) | ✅ deducido | `Window Close` |
| 8 | Copiar | deducido | `Edit Copy` |
| 9 | Cortar | deducido | `Edit Cut` |
| 10 | Doble clic | deducido | `MouseButton DoubleClick` |
| 11 | Enter | deducido | `Edit Enter` |
| 12 | Maximizar | deducido | `Window Maximize` |
| 13 | Minimizar | deducido | `Window Minimize` |
| 14 | Nuevo (Ctrl+N) | deducido | `SimulatedKeys {CTRL}n` |
| 15 | Abrir (Ctrl+O) | deducido | `SimulatedKeys {CTRL}o` |
| 16 | Pegar | deducido | `Edit Paste` |
| 17 | Rehacer | deducido | `Edit Redo` |
| 18 | Mostrar/ocultar escritorio | deducido | `Window ShowDesktop` |
| 19 | Deshacer | deducido | `Edit Undo` |
| 20 | Silenciar | deducido | `Media Mute` |
| 21 | Subir volumen | ✅ | `Media VolumeUp` |
| 22 | Bajar volumen | ✅ | `Media VolumeDown` |
| 23 | Próxima pista | ✅ | `Media Next` |
| 24 | Pista anterior | ✅ | `Media Previous` |
| 25 | Detener | deducido | `Media Stop` |
| 26 | Reproducir/Pausar | ✅ deducido | `Media PlayPause` |
| 27 | Abrir correo | deducido | `Unsupported` |
| 28 | **Teclas simuladas** | ✅ | `SimulatedKeys` |
| 29 | Bloquear equipo | deducido | `SimulatedKeys {LWIN}l` |
| 30–39 | Explorador, Ejecutar, Restaurar, Escape, Ayuda, Navegador, Stop, Refresh, Buscar, Favoritos | deducido | mapear según tabla de §6 de SPEC |
| 40 | **Sin cambios (no interceptar)** | ✅ | `NoChange` |
| 41 | **Ejecutar aplicación** | ✅ | `RunApp` (`app`) |
| 46, 61, 74, 95, 100, 101, 127, 128 | ❓ desconocidos (1–5 usos cada uno) | pendiente | `Unsupported(code)` hasta verificar |

Implementar la tabla en `XmbcActionMap` como diccionario; cualquier código no listado → `Unsupported(code)` y una línea en el `ImportReport`.

## 5. Sintaxis de teclas simuladas (`keys`) ✅
- Texto libre = teclas literales (`p`, `C`, `t`). Mayúscula literal → enviar con Shift ❓ (en datos: `{LWIN}{CTRL}{SHIFT}C`; tratar el carácter como tecla `C`, Shift ya viene explícito).
- **Modificadores** `{CTRL} {ALT} {SHIFT} {LWIN} {RWIN}`: se mantienen presionados y se aplican a **la siguiente tecla no modificadora**; luego se sueltan todos. Ej.: `{CTRL}{SHIFT}{ESC}` = Ctrl+Shift+Esc.
- Teclas especiales usadas: `{ENTER} {TAB} {ESC} {ESCAPE} {SPACE} {DEL} {HOME} {PGDN} {UP} {DOWN} {LEFT} {RIGHT} {F2} {F4} {F5} {F6} {F8} {F10}`. Soportar también el resto de F1–F24, `{BACKSPACE}`, `{INS}`, `{END}`, `{PGUP}`, `{PRTSCN}`.
- Mouse: `{LMB} {RMB} {MMB}` (clic), `{LMBD}`/`{LMBU}` (bajar/subir izquierdo), `{MWUP} {MWDN}` (rueda).
- Esperas: `{WAITMS:n}` (ms; puede venir con decimales, ej. `0.3` → redondear), `{WAIT:n}` (segundos; acepta coma decimal, ej. `{WAIT:0,5}` = 0,5 s).
- Cursor: `{ASET:x,y}` (posición absoluta de pantalla ❓), `{PSET:x,y}` (relativa a la ventana ❓), `{MADD:x,22}` (desplazamiento relativo; `x` sin número ❓ = 0).
- `{RUN:ruta}` → ejecutar programa (rutas pueden tener espacios; termina en `}`).
- `{LAYER:n}` → cambiar a capa n.
- Token desconocido → ignorar y registrar advertencia (no abortar la secuencia).
- Delay entre teclas: `<SimulatedKeys Delay="1" />` (ms).

## 6. `GlobalHotkeys`
- `HotkeyN Key="<VK>" Modifier="<bits>"`. `Key=0` = sin asignar.
- `Modifier` es una máscara: el valor real `7` = Ctrl+Alt+Shift. ❓ bits exactos (probable 1=Alt, 2=Ctrl, 4=Shift, 8=Win, como `HOTKEYF_*`/`MOD_*`).
- Datos reales: Hotkey0=F12, Hotkey1=F5, Hotkey2=F6, Hotkey3=F7, Hotkey4=F8, Hotkey13=F9, Hotkey14=F10, Hotkey15=F11, todos con Modifier=7.
- ❓ **Qué hace cada índice** (activar/desactivar, capa 1…10, capa siguiente/anterior…). Ver §8.
- El importador los convierte en `keyTriggers` con la acción correspondiente.

## 7. `LayerModifierKeys`
`Enable="false"` en los datos del usuario → v1 solo los guarda; no implementarlos.

## 8. Pendiente de verificar (lo hace el usuario en XMBC)
| # | Pregunta | Cómo verificar |
|---|---|---|
| P1 | Códigos 46, 61, 74, 95, 100, 101, 127, 128 | En XMBC abrir el perfil indicado y anotar la acción: 46 = *BAR NOT MON1* capa 4 Izquierdo · 61 = *BAR NOT MON2* Medio · 74 = *MICROWIND* Tilt derecha · 95 = *VIVALDI* capa 3 Izquierdo · 100/101 = *Default* X2 / X1 · 127 = *EDGE* X2 · 128 = *SUMANTRA* Medio |
| P2 | Índice → función de `GlobalHotkeys` | XMBC → Configuración → pestaña de hotkeys: anotar qué fila tiene `Ctrl+Alt+Shift+F5`, `F6`, `F7`, `F8`, `F9`, `F10`, `F11`, `F12`. |
| P3 | AutoSwitch: ¿cuenta desde que se activa la capa o desde la última acción? | Probar con un perfil de prueba. |
| P4 | ¿La capa activa es global o por perfil? | Cambiar de capa en un programa y pasar el mouse a otro. |
| P5 | `{ASET}` / `{PSET}`: ¿coordenadas de pantalla o de ventana? | Revisar en la ayuda de XMBC (editor de teclas simuladas). |
| P6 | ¿Qué hace exactamente cada botón del Sadex hoy en `sade.exe`? (solo hotkey, o hotkey + clic) | Revisar en `sade.exe` y anotar por botón. |

Mientras no se verifiquen, el código usa el comportamiento "deducido" o `Unsupported`, y deja `// TODO(verify:Pn)`.
