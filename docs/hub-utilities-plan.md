# RGTools como HUB de utilidades — Plan

## Contexto

Hoy RGTools es una tray app .NET 10 (WPF, admin) bien resuelta: perfiles Trabajo/Gaming,
DNS Guardian, VPN FortiClient y túnel DB (Jumpbox), todo bajo el patrón
snapshot→aplicar→restaurar. Pero hay más utilidades dispersas que conviene centralizar:

- **Netmon** (`D:\Code\github_personal\Netmon`): monitor de red Python 3.11, TUI Rich, maduro (v9),
  ya compila a `.exe` con PyInstaller. Complementa a DNS/VPN (mide QoS post-túnel), sin solapamiento.
- **meet-copilot** (`D:\Code\github_personal\meet-copilot`): agente de reuniones Teams, GUI
  Python (CustomTkinter), beta v2, usa LLMs (OpenAI/Anthropic/Gemini/LM Studio) y secretos en `meets_config.json`.
- **Scripts de debloat** (`E:\Software\Scripts`): `Debloat-Windows.ps1`, `Debloat-Office.ps1`,
  `Remove-Edge.ps1`. PowerShell excelentes, requieren admin y **ya usan snapshot→restore con state JSON**.

**Objetivo**: convertir RGTools en el **centro de utilidades** que registra y lanza estas piezas,
sin fusionarlas en un monolito. El resultado debe poder crecer con nuevas herramientas.

## Decisiones de arquitectura

1. **Repos separados; RGTools = hub.** Netmon y meet-copilot siguen en sus repos (lenguaje/CI propios).
   No hay monorepo ni fusión. Evita la fricción de un build multi-lenguaje.
2. **Satélites = releases ejecutables versionados.** Cada satélite publica su `.exe` (Netmon ya lo hace;
   meet-copilot añadiría build PyInstaller). RGTools los registra y lanza por versión
   (tile "Netmon v9.0.0 ▸ Lanzar"). Out-of-process: aislamiento + no heredan el admin del host.
3. **Scripts debloat = acciones nativas absorbidas.** No son satélites. Se mueven a `RGTools.App/scripts/`,
   se ejecutan vía el `ProcessRunner` existente y reusan `ISystemStateStore` para el snapshot/restore.
4. **Modelo de extensión de 4 tipos** (`ToolKind`):
   - `SatelliteExe` — proceso externo, cualquier lenguaje (Python, etc.). Aislado.
   - `NativeScript` — `.ps1` envuelto, revertible.
   - `InternalAction` — código C# ya compilado en RGTools (ej. Jumpbox).
   - `PluginAssembly` — **`.dll` .NET in-process** (fase futura). Para utilidades nativas nuevas:
     cargadas desde `plugins/*.dll` con `AssemblyLoadContext`, comparten DI y `ISystemStateStore`.
     Trade-off: más integradas pero corren en el proceso admin y un crash las arrastra a todas.
   > .exe vs .dll no es excluyente. Python → siempre `.exe`. DLL solo para utilidades .NET propias.

## Mecanismo del hub (generaliza `JumpboxService`)

`JumpboxService` ya es el embrión: valida un entorno (preflight), lanza un proceso con ruta configurable,
devuelve un `JumpboxResult`. Se generaliza a un registro + launcher dirigido por datos.

**Tipos nuevos** (en `RGTools.Core/Tools/`, namespace `RGTools.App.Core`):

- `ToolDescriptor` (record): `Id`, `Name`, `Description`, `Category`, `Kind`, `Source`, `Icon`,
  `ExecutablePath`/`ScriptFileName`, `Arguments[]`, `RequiredFiles[]`, `RequiresAdmin`, `ConsentId?`,
  `Versioning`, `Revertible`, `StateKey?`.
- Enums: `ToolKind`, `ToolSource {LocalPath, Bundled, GitHubRelease}`,
  `ToolCategory {Network, Privacy, Productivity, System, Database}`, `VersionStrategy {None, FileName, Sidecar, ExeFlag, GitHubTag}`.

**Servicios nuevos**:
- `IToolRegistry` / `ToolRegistryService`: carga el manifiesto + tools bundled; `All`, `ByCategory`,
  `Find(id)`, `ReloadAsync()`. Resuelve rutas combinando un `ToolsRoot` (`%APPDATA%\RGTools\apps\<id>\`)
  con override por id.
- `IToolLauncher` / `ToolLauncherService`: `PreflightAsync`, `DetectVersionAsync`, `LaunchAsync`.
  Despacha por `Kind`. Generaliza `JumpboxResult` → `ToolLaunchResult(Success, Version?, Error?)`.
- `IInternalToolHandler` → `WslJumpboxHandler`: Jumpbox pasa a `Kind=InternalAction`,
  **reusando `IsSafeWslPath`/`AddWslArgs` intactos** (no se reinventa la lógica WSL).
  `IJumpboxService` queda como fachada de compatibilidad en fase 1, se retira en fase 2.

## Scripts debloat como acciones revertibles

- `IScriptDeployer` / `ScriptDeployer`: los `.ps1` se embeben como `EmbeddedResource` y se extraen a
  `%APPDATA%\RGTools\scripts\` solo si faltan o el **hash SHA-256 difiere** del embebido
  (evita ejecutar un `.ps1` manipulado en disco). Resuelve que el publish single-file no arrastra archivos sueltos.
- `IRevertibleAction` (análogo a `IMode`): `Id`, `StateKey`, `ApplyAsync`, `RestoreAsync`, `IsApplied`.
  `DebloatAction` envuelve cada `.ps1`: consentimiento vía `UserConsentService(ConsentId)` →
  ejecuta con `-Mode apply|restore -StatePath %APPDATA%\RGTools\states\<id>.json` → el snapshot del script
  aterriza en el mismo `StatesDir`, de modo que `ISystemStateStore.Exists(StateKey)` refleja aplicado/no.
- **Único cambio en los `.ps1`**: aceptar `-Mode` y `-StatePath` (parametrizar ubicación del state).
- Claves nuevas en `StateKeys` (`DebloatWindows`, `DebloatOffice`, `RemoveEdge`) **separadas de
  `StateKeys.All`** para no mezclarlas con la sanitización de Gaming (nuevo `StateKeys.DebloatAll`).

## Dashboard y categorías

- `ToolTileViewModel` (`Name`, `VersionLabel`, `IsAvailable`, `IsApplied`, `LaunchCommand`, `RestoreCommand`)
  y `ToolsHubViewModel` (`ObservableCollection` agrupada por `ToolCategory`).
- `DashboardView.xaml`: `ItemsControl` por categoría (Red, Privacidad/Debloat, Productividad, Sistema,
  Base de datos), reutilizando estilos existentes (`ActionButton`, `StatusDot`). Tile = nombre + versión + "Lanzar";
  revertibles con toggle aplicar/restaurar. VPN/DNS/Perfiles se mantienen; Jumpbox migra a tile "Base de datos".
- `App.xaml.cs` `BuildHost`: registrar `IScriptDeployer`, `IToolRegistry`, `IToolLauncher`,
  `IInternalToolHandler` (WslJumpbox), `IRevertibleAction`×3, `ToolsHubViewModel`.
  En `OnStartup` tras `LoadAsync()`: `EnsureExtractedAsync()` + `ReloadAsync()`.

## Manifiesto y versionado

- **`tools.json` separado** (no dentro de `AppSettings`): catálogo semi-estático que crece.
  Plantilla bundled `tools.default.json` (`EmbeddedResource`) + copia editable en `%APPDATA%\RGTools\tools.json` (merge).
  En `AppSettings` solo entran `ToolsRoot` y `Dictionary<string,string> ToolPaths` (override de ruta por id,
  generaliza `JumboxFolderPath`). **Source-gen dedicado `ToolsJsonContext`** — no contaminar `AppJsonContext`
  (todo tipo JSON nuevo debe registrarse o falla en runtime).
- **Versionado** (`VersionStrategy`): default `FileName` (`Netmon-9.0.0.exe`, no ejecuta el binario) con
  `Sidecar` (`version.txt`) como respaldo. `ExeFlag` (`exe --version`) solo bajo demanda. `GitHubTag` en fase 3.

## Etapas (roadmap)

**Fase 1 — MVP del hub** (sin red, sin romper nada):
ToolDescriptor+enums · ToolRegistry con `tools.default.json` (Jumpbox + Netmon local + 3 debloat) ·
ToolLauncher (SatelliteExe + InternalAction reusando WSL) · ScriptDeployer con hash · `AppPaths.ScriptsDir` ·
IRevertibleAction + DebloatAction×3 con state unificado y consentimiento · ToolsHubViewModel + tiles ·
fachadas de compatibilidad para Jumpbox. **Entregable**: tile "Netmon v9.0.0 ▸ Lanzar", debloat aplicar/restaurar.

**Fase 2 — Consolidación**: migrar `DashboardView` a `IToolLauncher`; retirar `IJumpboxService`/`JumboxFolderPath`
(→ `ToolPaths["jumpbox"]`); `tools.json` editable en `%APPDATA%`; submenú "Herramientas" en el tray.
Empaquetar meet-copilot a `.exe` e integrarlo como satélite (con su config/secretos propios).

**Fase 3 — Auto-update GitHub Releases**: `IReleaseUpdater`/`GitHubReleaseUpdater`, `Source=GitHubRelease`,
`Repo`/`AssetPattern`, verificación hash/firma, UI "actualización disponible v9.1.0".

**Fase 4 — Plugins .NET in-process** (`PluginAssembly`): contrato `IToolPlugin`, carga `plugins/*.dll`
con `AssemblyLoadContext`. Solo si surge una utilidad nativa que justifique compartir el proceso.

## Catálogo de nuevas utilidades sugeridas

Agrupadas por la categoría del hub. Candidatas, no comprometidas:

- **Red**: flush DNS / reset Winsock / release-renew IP; overlay de latencia; export del estado de Netmon al tile.
- **Privacidad/Debloat** (ya): + limpiar tareas programadas de telemetría residuales; toggles de permisos de cámara/mic.
- **Mantenimiento/Sistema**: limpieza de temp/`%TEMP%`/WinSxS; vaciar caché de Windows Update;
  RAM standby cleaner; timer resolution para juegos; gestión de puntos de restauración.
- **Productividad**: meet-copilot (satélite); captura+OCR a portapapeles; gestor de portapapeles.
- **Toggles rápidos**: modo oscuro, hibernación on/off, nivel de UAC, mostrar extensiones/ocultos.

Todas las que tocan el sistema deben pasar por el patrón snapshot→aplicar→restaurar + consentimiento.

## Archivos críticos

- `RGTools.Core/JumpboxService.cs` (se generaliza/refactoriza a InternalAction)
- `RGTools.Core/AppSettings.cs` (+ `ToolsRoot`, `ToolPaths`)
- `RGTools.Core/ConfigService.cs` (source-gen; nuevo `ToolsJsonContext`)
- `RGTools.Core/StateKeys.cs` (+ claves debloat, `DebloatAll`)
- `RGTools.Core/AppPaths.cs` (+ `ScriptsDir`)
- `RGTools.App/App.xaml.cs` (DI + bootstrap)
- `RGTools.App/Views/DashboardView.xaml` (+ .cs) y `ViewModels/` (nuevos VMs)
- `RGTools.App/RGTools.App.csproj` + `installer/RGTools.iss` (EmbeddedResource de scripts/manifiesto)
- Scripts en `E:\Software\Scripts\*.ps1` → mover a `RGTools.App/scripts/` y parametrizar `-Mode`/`-StatePath`

## Riesgos

1. **Single-file + scripts**: mitigado embebiendo `.ps1` + extracción con hash (independiente del instalador).
2. **Ejecutar binarios/scripts de terceros como admin**: superficie de ataque → hash (scripts),
   hash/firma + repo fijado (satélites, fase 3), validación estricta de ruta tipo `JumpboxService`.
3. **State de los `.ps1`**: exige parametrizar `-StatePath -Mode` (toca repos externos).
4. **Source-gen JSON**: cada tipo nuevo debe registrarse en un `JsonSerializerContext` o falla en runtime.
5. **Versionado por nombre de exe**: frágil si el satélite no respeta la convención; `Sidecar` como respaldo.
6. **Acoplamiento UI**: centralizar en `ToolsHubViewModel` (MVVM) desde el inicio, no repetir code-behind.

## Verificación

- `dotnet build RGTools.slnx -c Debug` y `dotnet test RGTools.slnx` (33/33) verdes tras cada fase.
- Fase 1 manual: abrir dashboard → ver tiles por categoría; lanzar Netmon (abre su TUI con versión correcta);
  aplicar un debloat → verificar `state json` en `%APPDATA%\RGTools\states\` y `IsApplied=true` en el tile;
  restaurar → `Clear` del state y sistema revertido.
- Verificar que el publish single-file (`build-release.ps1`) extrae los `.ps1` a `%APPDATA%` con hash correcto.

---

> Nota: este documento es la versión local del plan. Si Ultraplan devuelve una versión refinada,
> reemplazar/combinar su contenido aquí mismo.
