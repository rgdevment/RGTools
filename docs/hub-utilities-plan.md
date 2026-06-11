# RGTools como HUB de utilidades — Plan

## Contexto

Hoy RGTools es una tray app .NET 10 (WPF, admin) bien resuelta: perfiles Trabajo/Gaming,
DNS Guardian, VPN FortiClient y túnel DB (Jumpbox), todo bajo el patrón
snapshot→aplicar→restaurar. Hay más utilidades dispersas que conviene centralizar, **cada una
con requisitos y lenguaje propios**:

- **Netmon** (`D:\Code\github_personal\Netmon`): monitor de red Python 3.11, TUI Rich, maduro (v9).
  Deps con compilación nativa (`aioquic` QUIC, `psutil`). Ya tiene PyInstaller (`netmon.spec`).
- **meet-copilot** (`D:\Code\github_personal\meet-copilot`): agente de reuniones Teams, GUI Python
  (CustomTkinter), beta v2. Usa LLMs (OpenAI/Anthropic/Gemini/LM Studio) + secretos en config propio
  + `uiautomation` (frágil a cambios de Teams). No empaqueta.
- **videomerge** (`D:\Code\github_personal\videomerge`): CLI Python 3.10 (Rich), deps livianas pero
  **depende de ffmpeg/ffprobe externos versión-crítico** (SVT-AV1 ≥ 2.1.0). Ya tiene un instalador
  idempotente (`scripts\install-windows.ps1`: winget + verificación de versiones). Empaquetar a `.exe`
  sería > 500 MB e inviable.
- **Scripts de debloat** (`E:\Software\Scripts`): `Debloat-Windows.ps1`, `Debloat-Office.ps1`,
  `Remove-Edge.ps1`. PowerShell, requieren admin, **ya usan snapshot→restore con state JSON**.

**Objetivo**: convertir RGTools en el **centro de utilidades** que descubre, prepara y lanza estas
piezas sin fusionarlas en un monolito y **sin asumir un empaquetado uniforme**. Debe crecer agregando
herramientas con coste de mantenimiento mínimo en el host.

## Decisión central (redefinición)

> **El hub no empaqueta utilidades. Las descubre, valida y lanza.** Cada herramienta declara, en su
> propio repo, cómo prepararse y cómo ejecutarse. RGTools solo orquesta.

Esto deroga la premisa anterior *"Python siempre como `.exe`"*, que el propio `JumpboxService` ya
contradecía: Jumpbox lanza Python por intérprete (`uv run` dentro de WSL), sin empaquetar nada. Forzar
un `.exe` PyInstaller por utilidad rompe en los tres casos por motivos distintos (Netmon frágil de
compilar, meet-copilot 200 MB + alto mantenimiento, videomerge inviable por ffmpeg externo).

Se separan **dos ejes ortogonales** que el plan original había colapsado en uno:

- **Provisión** — cómo dejar la herramienta *lista*.
- **Lanzamiento** — qué proceso arrancar una vez lista.

## Descubrimiento: la herramienta es un repo Git

El hub no guarda rutas absolutas por herramienta. **Descubre por convención**: busca el repo clonado en
una lista de raíces conocidas, en orden:

```
D:\Code\github_personal\<repo>   ← default (donde clona)
C:\Code\github_personal\<repo>
E:\Code\github_personal\<repo>
```

Las raíces son configurables (`AppSettings.ToolRoots`, default la lista de arriba). El descubrimiento:

```
Discover(<repo>)
  ├─ encontrado en una raíz → continúa a Detect
  └─ no clonado en ninguna  → tile "Clonar" → git clone <repoUrl> en D:\Code\github_personal → Ensure
```

Por eso el índice central (`tools.json`) solo necesita lo mínimo para **encontrar o clonar** el repo —
`id`, `repoUrl`, carpeta y categoría — antes de que exista en disco. El detalle de provisión vive en el
`.rgtool.json` *dentro* del repo, que solo puede leerse una vez clonado. Dos capas, sin solapamiento.

## El estándar: manifiesto de herramienta por repo

Una vez clonado, cada repo expone un manifiesto estándar en su raíz, **`.rgtool.json`** (para las Python
se admite también una sección `[tool.rgtool]` en `pyproject.toml`). Es el **contrato** que el hub lee; el
conocimiento de requisitos y versiones vive con la herramienta, no hardcodeado en RGTools.

```jsonc
{
  "id": "videomerge",
  "name": "VideoMerge",
  "category": "Productivity",
  "requirements": {
    "runtime": "python>=3.10",
    "system": ["ffmpeg>=4", "ffmpeg.svtav1>=2.1.0"]   // informativo; lo valida el preflight del tool
  },
  "provision": {                                        // idempotente, re-ejecutable seguro
    "strategy": "ScriptInstaller",
    "command": "pwsh -File scripts/install-windows.ps1"
  },
  "preflight": "uv run vm --doctor",                    // exit 0 = listo; !=0 = preparar/recomendar
  "launch": { "kind": "Interpreter", "command": "uv run vm" },
  "version": "uv run vm --version"
}
```

Flujo completo del hub (**discover → clone → ensure → launch**):

```
Discover (buscar repo en ToolRoots)
  └─ no clonado       → tile "Clonar" → git clone → Ensure
Detect (lee .rgtool.json + preflight barato)
  ├─ Listo            → Launch
  ├─ No listo/Outdated→ tile "Preparar" → Ensure (provision.command) → Preflight → Launch
  └─ Roto             → tile "No disponible" + diagnóstico
```

El usuario nunca ejecuta una herramienta a medio preparar. El tile guía la cadena: **Clonar** (no está en
disco) → **Preparar** (clonado pero entorno no listo, dispara `provision.command` con progreso) →
**Lanzar** (listo).

## Los dos ejes

`ProvisionStrategy` — cómo se aprovisiona:

| Estrategia | Para qué | Ensure (idempotente) |
|---|---|---|
| `PrebuiltBinary` | binario ya compilado (Netmon opcional) | descargar/ubicar el `.exe` |
| `ManagedEnv` | repos Python (meet-copilot, videomerge, Netmon) | `uv sync` en entorno aislado por-tool |
| `ScriptInstaller` | requiere instalar deps de sistema (videomerge→ffmpeg) | correr el `.ps1` del repo (winget + verificación) |
| `SystemPackage` | binario de sistema puro (winget) | `winget install …` |
| `None` | ya disponible en el entorno (Jumpbox/WSL) | — |

`LaunchKind` — cómo se lanza:

| Kind | Proceso |
|---|---|
| `Exe` | ejecutable nativo |
| `Interpreter` | `uv run <módulo>` / `python -m …` |
| `Wsl` | `wsl … -c` (Jumpbox, reusa `IsSafeWslPath`/`AddWslArgs` intactos) |
| `InProcess` | acción C# en RGTools |
| `ScriptAction` | `.ps1` revertible (debloat) |

Cómo cae cada utilidad real:

| Herramienta | Provisión | Lanzamiento |
|---|---|---|
| **videomerge** | `ScriptInstaller` (reusa `install-windows.ps1`) | `Interpreter` → `uv run vm` |
| **meet-copilot** | `ManagedEnv` (uv, **sin `.exe`**) | `Interpreter` → `uv run` (GUI + secretos propios) |
| **Netmon** | `ManagedEnv` (recomendado) **o** `PrebuiltBinary` (fallback) | `Interpreter` `uv run netmon` **o** `Exe` |
| **Jumpbox** | `None` | `Wsl` (sin cambios) |
| **Debloat ×3** | `None` (script embebido en RGTools) | `ScriptAction` (revertible) |

> Para Netmon, `ManagedEnv` evita compilar `aioquic` en el equipo del usuario (uv resuelve wheels
> precompilados) y elimina el mantenimiento del `.spec`. El `.exe` actual queda como fallback opcional;
> la arquitectura soporta ambas estrategias para el mismo tool sin coste extra.

## Mecanismo del hub (generaliza `JumpboxService`)

`JumpboxService` ya es el embrión correcto: separa *preflight* (validar entorno) de *launch* (correr
proceso). Se generaliza a un registro + provisionador + launcher dirigido por datos.

**Servicios** (en `RGTools.Core/Tools/`, namespace `RGTools.App.Core`):

- `IToolRegistry` / `ToolRegistryService`: índice de herramientas conocidas (`tools.json`: `id`,
  `repoUrl`, carpeta). **Descubre** la ruta resolviendo la carpeta contra `ToolRoots` en orden (override
  por id si existe). Si el repo está clonado, **lee y valida su `.rgtool.json`** (existe + `schema`
  soportado + campos requeridos) para obtener provisión/preflight/launch. `All`, `ByCategory`, `Find(id)`,
  `ReloadAsync()`.
- **Gate de estandarización**: un repo clonado **sin `.rgtool.json` válido NO se registra como
  lanzable** — aparece como "No estandarizada" y se omite del flujo de provisión/launch hasta que cumpla
  el esquema. El hub asume el contrato; no infiere comandos de un repo sin manifiesto.
- `IToolProvisioner` / `ToolProvisioner`: `DetectAsync(tool) → ProvisionState {NotCloned |
  NotProvisioned | Provisioned(version) | Outdated | Broken}`; `AcquireAsync` (`git clone <repoUrl>` en la
  primera raíz de `ToolRoots` si `NotCloned`); `EnsureAsync(tool, progress, ct) → EnsureResult`
  (idempotente, encadena Acquire si falta). Despacha por `ProvisionStrategy`.
- `IToolLauncher` / `ToolLauncherService`: `PreflightAsync` (generaliza `ValidateWslEnvironmentAsync`),
  `DetectVersionAsync`, `LaunchAsync`. Despacha por `LaunchKind`. `JumpboxResult` → `ToolLaunchResult
  (Success, Version?, Error?)`.
- `IInternalToolHandler` → `WslJumpboxHandler`: Jumpbox pasa a `Provision=None` + `Launch=Wsl`,
  **reusando `IsSafeWslPath`/`AddWslArgs`**. `IJumpboxService` queda como fachada de compatibilidad en
  fase 1, se retira en fase 2.

**Tipos**: `ToolDescriptor` (record) con `Id`, `Name`, `Category`, `Provision` (estrategia + comando
ensure + ruta repo/lockfile), `Launch` (`LaunchSpec`: kind, command/args, `RunAs`), `PreflightChecks[]`,
`Version` (cómo obtenerla), `ConsentId?`. Enums: `ProvisionStrategy`, `LaunchKind`, `ToolCategory
{Network, Privacy, Productivity, System, Database}`, `ProvisionState`.

## Detección de "listo" (preflight, barato → caro)

1. **Existence check**: rutas/binarios presentes (como `[ -f jumbox.py ]`).
2. **Provision marker**: lockfile/`.venv` al día — hash del lockfile vs marcador en
   `%APPDATA%\RGTools\apps\<id>\.provisioned`.
3. **Version probe** (bajo demanda): `preflight`/`version` del manifiesto (`uv run vm --doctor`,
   `ffprobe -version`). Caro; solo si el barato falla o se necesita el número exacto.

La verificación de versión-crítico (ej. SVT-AV1 ≥ 2.1.0) **es responsabilidad del preflight del tool**,
no del hub: RGTools pregunta "¿estás listo?" y la herramienta responde. El conocimiento de versiones
vive en el repo de la herramienta.

## Runtimes y dependencias externas

- **Runtimes Python por-herramienta → `uv`** (no pipx, no venv manual): entorno aislado por proyecto,
  gestiona la versión de Python (Netmon 3.11 vs meet/videomerge 3.10 conviven), resuelve wheels
  precompilados (mitiga `aioquic` sin MSVC). Ya se usa en Jumpbox y en `install-windows.ps1`.
- **Binarios de sistema (ffmpeg) → `winget` vía el script del repo**: no se reinventa; `ScriptInstaller`
  invoca el `.ps1` existente y lee su exit code.
- **`uv` como dependencia de bootstrap del hub**: se instala vía winget la primera vez (lo hace el
  propio `install-windows.ps1`). Implica red en el primer "Preparar" de cada herramienta gestionada.

## Invariantes de seguridad

- **Los hijos NO heredan el token admin del host.** WPF como admin propaga el token a procesos hijos por
  defecto; `ToolLauncher` debe lanzar de-elevado (`LaunchSpec.RunAs = Deelevated`) salvo que la
  herramienta lo requiera explícitamente. Jumpbox ya lo evita lanzando en WSL.
- **`ScriptInstaller`/`SystemPackage` modifican el sistema global** → pasan por `UserConsentService`
  (igual que debloat) y registran qué instalaron.
- **Hashing**: scripts embebidos (debloat) por SHA-256; lockfile como marcador de provisión. Firma de
  `.exe` queda para la fase de auto-update.

## Scripts debloat como acciones revertibles (sin cambios)

- `IScriptDeployer` / `ScriptDeployer`: los `.ps1` se embeben como `EmbeddedResource` y se extraen a
  `%APPDATA%\RGTools\scripts\` solo si faltan o el **hash SHA-256 difiere** (evita ejecutar un `.ps1`
  manipulado). Resuelve que el publish single-file no arrastra archivos sueltos.
- `IRevertibleAction` (análogo a `IMode`): `Id`, `StateKey`, `ApplyAsync`, `RestoreAsync`, `IsApplied`.
  `DebloatAction` envuelve cada `.ps1`: consentimiento → `-Mode apply|restore -StatePath
  %APPDATA%\RGTools\states\<id>.json` → el state aterriza en `StatesDir`, de modo que
  `ISystemStateStore.Exists(StateKey)` refleja aplicado/no.
- **Único cambio en los `.ps1`**: aceptar `-Mode` y `-StatePath`.
- Claves nuevas en `StateKeys` (`DebloatWindows`, `DebloatOffice`, `RemoveEdge`) **separadas de
  `StateKeys.All`** (nuevo `StateKeys.DebloatAll`) para no mezclarlas con la sanitización de Gaming.

## Manifiesto y versionado

- **`tools.json`** (no dentro de `AppSettings`): índice editable de herramientas conocidas con lo mínimo
  para **encontrarlas o clonarlas** — `id`, `repoUrl`, carpeta, categoría. Plantilla bundled
  `tools.default.json` (`EmbeddedResource`) + copia en `%APPDATA%\RGTools\tools.json` (merge). En
  `AppSettings`: `ToolRoots` (lista de raíces de búsqueda, default `D:\Code\github_personal` + C/E) y
  `Dictionary<string,string> ToolPaths` (override de ruta por id para casos no convencionales, generaliza
  `JumboxFolderPath`). **Source-gen dedicado `ToolsJsonContext`** — no contaminar `AppJsonContext`.
- **El detalle de provisión/preflight/launch NO está en `tools.json`**: vive en el `.rgtool.json` de
  cada repo (legible solo tras clonar). `tools.json` solo dice "conozco esta herramienta y este es su repo".
- **Versionado**: la versión la da el `version` del manifiesto (probe del propio tool). En
  `PrebuiltBinary`, respaldo por nombre de archivo (`Netmon-9.0.0.exe`) o `Sidecar` (`version.txt`).

## Dashboard y categorías

- `ToolTileViewModel` (`Name`, `VersionLabel`, `State`, `PrimaryCommand`) con **cuatro estados**:
  **Clonar** (repo no encontrado en `ToolRoots` → `git clone` con progreso, encadena Preparar),
  **Preparar** (clonado pero entorno no listo/outdated → `EnsureAsync` con progreso), **Lanzar** (listo),
  **No disponible** (roto/sin `repoUrl`). Revertibles (debloat) con toggle aplicar/restaurar.
- `ToolsHubViewModel`: `ObservableCollection` agrupada por `ToolCategory`.
- `DashboardView.xaml`: `ItemsControl` por categoría (Red, Privacidad/Debloat, Productividad, Sistema,
  Base de datos), reutilizando estilos (`ActionButton`, `StatusDot`). VPN/DNS/Perfiles se mantienen;
  Jumpbox migra a tile "Base de datos".
- `App.xaml.cs` `BuildHost`: registrar `IScriptDeployer`, `IToolRegistry`, `IToolProvisioner`,
  `IToolLauncher`, `IInternalToolHandler` (WslJumpbox), `IRevertibleAction`×3, `ToolsHubViewModel`. En
  `OnStartup` tras `LoadAsync()`: `EnsureExtractedAsync()` (scripts) + `ReloadAsync()` (registry).

## Estado de implementación

**Piloto (videomerge) — hecho.** Pipeline end-to-end en `RGTools.Core/Tools/`: `ToolModels` (descriptor +
manifiesto + enums), `ToolsJsonContext` (source-gen del `.rgtool.json`), `ToolRunner` (ejecuta vía
`cmd /c` con `WorkingDirectory = repo`; launch en consola visible), `ToolRegistryService` (índice
hardcoded de 1 entrada + descubrimiento por `ToolRoots` + lectura/validación del manifiesto),
`ToolProvisionerService` (Detect/Ensure), `ToolLauncherService`. Tile "videomerge" en el dashboard con
estados Preparar/Lanzar. 45 tests verdes. `AppSettings.ToolRoots` opcional (default D/C/E).

**Pendiente antes de generalizar:**
- **Quoting del runner**: `ToolRunner` pasa `cmd /c {commandLine}` crudo. Funciona para videomerge
  (`uv sync`, `uv run vm`); Netmon usa comillas internas (`python -c "..."`) y meet-copilot usa `&&` +
  rutas `.venv\Scripts\...` → validar/escapar antes de darlas de alta.
- **De-elevación**: el launch hereda el token admin del host (TODO marcado en `ToolRunner.Launch`).
- **git clone** (Discover→Clone) no implementado: los repos deben estar ya clonados.
- **Índice**: hardcoded a videomerge; pasar a `tools.default.json` embebido al sumar herramientas.

## Etapas (roadmap)

**Fase 1 — MVP del hub** (sin red, sin romper nada, usa lo que ya funciona):
`ToolDescriptor` con los dos ejes · `ToolRegistry` con `tools.default.json` (Jumpbox + Netmon `.exe`
actual + 3 debloat) · `ToolLauncher` cubriendo `Wsl` (Jumpbox tal cual) + `Exe` (Netmon `.exe`) +
`ScriptAction` (debloat) · `ScriptDeployer` con hash · `AppPaths.ScriptsDir` · `IRevertibleAction` +
`DebloatAction`×3 con state unificado y consentimiento · `ToolsHubViewModel` + tiles · fachadas de
compatibilidad para Jumpbox. **Entregable**: tiles por categoría, lanzar Netmon, debloat aplicar/restaurar.

**Fase 2 — Descubrimiento + provisión real (`git` + `uv` + manifiestos por repo)**:
descubrimiento por `ToolRoots` (D/C/E) + `AcquireAsync` (`git clone`) con tile **"Clonar"** ·
`IToolProvisioner` + estrategia `ManagedEnv` (uv) para meet-copilot y videomerge · estrategia
`ScriptInstaller` invocando `videomerge\install-windows.ps1` · lectura de `.rgtool.json` por repo ·
`LaunchKind.Interpreter` (`uv run`) · tile gana estados **"Clonar"/"Preparar"** con progreso ·
**de-elevación de procesos hijos** (invariante de seguridad) · retirar `IJumpboxService`/`JumboxFolderPath`
(→ `ToolPaths["jumpbox"]`) · submenú "Herramientas" en el tray. **Se elimina** el empaquetado de
meet-copilot a `.exe` del roadmap original.

**Fase 3 — Auto-update**: GitHub Releases para `PrebuiltBinary`/satélites
(`IReleaseUpdater`/`GitHubReleaseUpdater`, `Repo`/`AssetPattern`, hash/firma, UI "v9.1.0 disponible").
Los `ManagedEnv` se actualizan con `uv sync` re-ejecutado cuando `Detect=Outdated`.

**Fase 4 — Plugins .NET in-process** (`PluginAssembly`): contrato `IToolPlugin`, carga `plugins/*.dll`
con `AssemblyLoadContext`. Solo si surge una utilidad nativa que justifique compartir el proceso.

## Catálogo de nuevas utilidades sugeridas

Candidatas, no comprometidas:

- **Red**: flush DNS / reset Winsock / release-renew IP; overlay de latencia; export del estado de Netmon.
- **Privacidad/Debloat** (ya): + limpiar tareas de telemetría residuales; toggles de cámara/mic.
- **Mantenimiento/Sistema**: limpieza de temp/WinSxS; vaciar caché de Windows Update; RAM standby
  cleaner; timer resolution; puntos de restauración.
- **Productividad**: meet-copilot, videomerge (satélites); captura+OCR; gestor de portapapeles.
- **Toggles rápidos**: modo oscuro, hibernación on/off, nivel de UAC, mostrar extensiones/ocultos.

Todas las que tocan el sistema pasan por snapshot→aplicar→restaurar + consentimiento.

## Qué se conserva y qué se redefine

**Se conserva**: `IToolRegistry`/`tools.json`/`ToolsRoot`/`ToolPaths`; Jumpbox (ahora `Provision=None` +
`Launch=Wsl`, reusando lógica WSL); debloat con `IRevertibleAction`/`ScriptDeployer`/hash/state
unificado; `ToolsHubViewModel` + tiles + MVVM; source-gen JSON dedicado; fases 3 y 4.

**Se redefine**: premisa "Python siempre `.exe`" → **eliminada**; `ToolKind` único → **dos ejes**
`ProvisionStrategy` × `LaunchKind`; el detalle de provisión migra de `tools.json` central al
**`.rgtool.json` por repo**; se añade `IToolProvisioner` y el flujo ensure-then-launch; el tile gana
estado "Preparar"; de-elevación de hijos como invariante; empaquetar meet-copilot a `.exe` → sustituido
por `ManagedEnv`.

## Riesgos

1. **Single-file + scripts**: mitigado embebiendo `.ps1` + extracción con hash.
2. **Ejecutar scripts/binarios de terceros como admin**: de-elevación de hijos + consentimiento +
   hash (scripts) + hash/firma y repo fijado (satélites, fase 3) + validación estricta de ruta tipo
   `JumpboxService`.
3. **Primer arranque requiere red** (git clone + uv/winget): mitigado con estados "Clonar"/"Preparar"
   explícitos y progreso; `PrebuiltBinary` queda como ruta offline para Netmon si se necesita.
3b. **Clonar como admin / origen del repo**: el `repoUrl` debe estar fijado en `tools.default.json`
   (bundled, no editable sin querer); requiere `git` en PATH (preflight); el clon hereda los mismos
   invariantes de de-elevación que el launch. Origen no confiable = superficie de ataque.
4. **State de los `.ps1`**: exige parametrizar `-StatePath -Mode` (toca repos externos).
5. **Source-gen JSON**: cada tipo nuevo debe registrarse en un `JsonSerializerContext` o falla en runtime.
6. **Manifiesto por repo desactualizado**: el `.rgtool.json` debe versionarse con su herramienta;
   `Sidecar`/preflight como respaldo si falta.
7. **Acoplamiento UI**: centralizar en `ToolsHubViewModel` (MVVM) desde el inicio.

## Archivos críticos

- `RGTools.Core/JumpboxService.cs` (modelo de referencia preflight/launch a generalizar)
- `RGTools.Core/Tools/*` (nuevos: descriptores, registry, provisioner, launcher)
- `RGTools.Core/AppSettings.cs` (+ `ToolRoots` lista, `ToolPaths` override)
- `RGTools.Core/ConfigService.cs` (source-gen; nuevo `ToolsJsonContext`)
- `RGTools.Core/StateKeys.cs` (+ claves debloat, `DebloatAll`)
- `RGTools.Core/AppPaths.cs` (+ `ScriptsDir`, `ToolsRoot`)
- `RGTools.App/App.xaml.cs` (DI + bootstrap)
- `RGTools.App/Views/DashboardView.xaml` (+ .cs) y `ViewModels/` (nuevos VMs)
- `RGTools.App/RGTools.App.csproj` + `installer/RGTools.iss` (EmbeddedResource de scripts/manifiesto)
- Scripts en `E:\Software\Scripts\*.ps1` → mover a `RGTools.App/scripts/` y parametrizar `-Mode`/`-StatePath`
- **En cada repo de herramienta**: añadir `.rgtool.json` (o `[tool.rgtool]` en `pyproject.toml`)

## Verificación

- `dotnet build RGTools.slnx -c Debug` y `dotnet test RGTools.slnx` (33/33) verdes tras cada fase.
- Fase 1 manual: dashboard → tiles por categoría; lanzar Netmon (TUI con versión correcta); aplicar un
  debloat → `state json` en `%APPDATA%\RGTools\states\` y `IsApplied=true`; restaurar → `Clear` + sistema revertido.
- Fase 2 manual: tool sin preparar → tile "Preparar" → `EnsureAsync` (uv/script) con progreso →
  preflight OK → "Lanzar"; confirmar que `uv run` arranca cada tool y que el hijo **no corre como admin**.
- Verificar que el publish single-file (`build-release.ps1`) extrae los `.ps1` a `%APPDATA%` con hash correcto.

---

> Nota: este documento es la versión local del plan. Si Ultraplan devuelve una versión refinada,
> reemplazar/combinar su contenido aquí mismo.
