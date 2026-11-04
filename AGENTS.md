# AGENTS.md — instrucciones para agentes de código (Codex, Claude, etc.)

## Proyecto
Orange: app de bandeja para Windows en C# / .NET 8 / WPF (WinForms solo para el ícono de bandeja).
Ver `README.md` para qué hace y cómo compilar.

- Ejecutar: `dotnet run --project Orange`
- Instalador: `.\build.ps1` → `dist\Orange-Setup-<versión>.exe`
- Estructura: `Orange/Core` (lógica), `Orange/Input` (hooks globales, P/Invoke en `Native.cs`), `Orange/UI` (ventanas WPF), `installer/` (Inno Setup).

## Convenciones del código
- Comentarios y nombres en inglés. Comentarios cortos que explican el *por qué*.
- Textos visibles al usuario siempre con `Loc.T("English text")` / `Loc.F(...)`, y su traducción en `Orange/Core/Loc.cs` para `es` y `pt`.
- Preferencias en `Settings` (`%LOCALAPPDATA%\Orange\settings.json`). Datos grandes van en archivos propios dentro de `Paths.AppData`.
- Errores no fatales se registran con `JobRunner.Log(...)`; nunca deben tumbar la app.
- Callbacks de hooks (`WH_*_LL`) deben volver en microsegundos: nada de E/S, ni UI, ni locks largos dentro del hook. El trabajo se encola (dispatcher o hilo propio).
- Todo P/Invoke nuevo va en `Orange/Input/Native.cs`.
- Orange se reinicia sin privilegios si arranca como administrador (ver `App.OnStartup`, UIPI y arrastrar desde Explorer). No cambiar ese comportamiento sin que la especificación lo pida.

## Trabajo en curso: módulo "Escritorio" (cajones de íconos)
Especificación completa en `docs/desktop/`:
1. `docs/desktop/SPEC.md` — qué construir y arquitectura.
2. `docs/desktop/TASKS.md` — tareas en orden, con criterios de aceptación.

Reglas para este trabajo:
- Hacer **una tarea de `TASKS.md` por vez**, en orden. No adelantar tareas.
- Al terminar una tarea: compilar (`dotnet build`), correr pruebas (`dotnet test`), y marcarla como hecha en `TASKS.md` con una línea de notas.
- Si algo de la especificación es ambiguo o está en "Pendiente de verificar", **no inventar**: implementar detrás de una opción configurable o dejar `// TODO(verify):` y anotarlo en `TASKS.md`.
- Prioridad del módulo: poca RAM y 0 % de CPU en reposo. Nada de temporizadores ni sondeo; solo eventos.
- Nunca borrar, mover ni renombrar archivos del usuario: el módulo solo cambia posiciones de íconos.
- El repositorio es público: nunca copiar al repo nombres de archivos del escritorio del usuario ni rutas personales.
