# Estandarizar un repo como herramienta del hub RGTools

Prompt reutilizable para correr **un agente por repo** (aparte de RGTools) y dejar cada utilidad satélite
conforme al estándar `.rgtool.json` que consume el hub. Ver el contrato completo en
[`hub-utilities-plan.md`](./hub-utilities-plan.md).

Uso: copia el bloque de abajo a un agente, editando solo `REPO OBJETIVO`. Mismo prompt para los tres
repos (Netmon, meet-copilot, videomerge). Un repo entra al registro del hub solo cuando expone un
`.rgtool.json` válido; hasta entonces queda fuera ("No estandarizada").

---

````text
# Estandarizar este repo como herramienta del hub RGTools

## REPO OBJETIVO  (edita estas 3 líneas por repo)
- Ruta:      D:\Code\github_personal\videomerge
- id:        videomerge
- Categoría: Productivity        # uno de: Network | Privacy | Productivity | System | Database

## Rol y objetivo
Este repo se integrará como "herramienta satélite" de RGTools, una tray app .NET que descubre, prepara
y lanza utilidades externas SIN empaquetarlas. Para que RGTools pueda hacerlo, este repo debe exponer un
contrato estándar: un manifiesto `.rgtool.json` en su raíz, más los comandos de provisión, validación y
lanzamiento que el manifiesto declara. Tu trabajo es dejar el repo conforme a ese estándar, verificando
que cada comando declarado realmente funciona. NO inventes comandos: confírmalos inspeccionando y
ejecutando en el repo.

## Modelo (dos ejes)
- Provisión (cómo dejar el repo LISTO):
  - `ManagedPythonEnv`  → entorno Python aislado con `uv` (preferido para repos Python). Ensure = `uv sync`.
  - `ScriptInstaller`   → hay deps de SISTEMA (ej. ffmpeg) que instalar; Ensure = un script idempotente
                          (PowerShell + winget) que ya exista o que crees.
  - `PrebuiltExe`       → ya se distribuye un .exe; Ensure = ubicarlo/descargarlo.
  - `SystemPackage`     → es un binario de sistema puro (winget).
  - `None`              → ya disponible sin preparación.
- Lanzamiento (qué proceso arranca una vez listo):
  - `Interpreter` (`uv run <cmd>`), `Exe`, `Wsl`.

## Esquema de `.rgtool.json` (créalo en la raíz del repo)
```jsonc
{
  "schema": 1,
  "id": "<id>",
  "name": "<Nombre legible>",
  "description": "<una línea: qué hace>",
  "category": "<Network|Privacy|Productivity|System|Database>",
  "requirements": {
    "runtime": "python>=3.10",            // versión REAL exigida (lee pyproject/requirements)
    "system": ["ffmpeg>=4"]               // deps de sistema; [] si no hay. Informativo.
  },
  "provision": {
    "strategy": "<ManagedPythonEnv|ScriptInstaller|PrebuiltExe|SystemPackage|None>",
    "command": "<comando idempotente de preparación>"   // ej. "uv sync" o "pwsh -File scripts/install-windows.ps1"
  },
  "preflight": "<comando que valida entorno LISTO; exit 0 = ok, !=0 = falta preparar>",
  "launch": { "kind": "<Interpreter|Exe|Wsl>", "command": "<comando de arranque>" },
  "version": "<comando que imprime la versión>",   // ej. "uv run <cli> --version"
  "runAs": "deelevated"                  // el launch NO debe requerir admin salvo que sea imprescindible
}
```

## Pasos
1. Inspecciona el repo: `pyproject.toml`/`requirements.txt`/`setup.py`/`*.csproj`, entrypoint real,
   modo (CLI/TUI/GUI), scripts existentes (`scripts/`, `*.ps1`, `*.spec`), y deps de sistema (ffmpeg,
   binarios nativos, GPU, claves/secretos en config).
2. Decide `provision.strategy` con la tabla de arriba. Regla: repos Python sin dep de sistema →
   `ManagedPythonEnv` (uv). Con dep de sistema versión-crítica (ffmpeg, etc.) → `ScriptInstaller`.
3. COMPLETA LO QUE FALTE (idempotente, Windows-first con `pwsh`/`winget`, re-ejecutable sin romper):
   - Si no hay comando de provisión funcional → créalo. Para Python, asegura que `uv sync` funciona
     (añade/ajusta `pyproject.toml` con sus deps si hace falta). Para deps de sistema, crea/ajusta
     `scripts/install-windows.ps1` que detecte e instale lo necesario y verifique versiones mínimas.
   - Si no hay `preflight` → añádelo: un comando que devuelva exit 0 sólo si el entorno está listo
     (intérprete + deps + binarios de sistema presentes y en versión mínima). Si el CLI no tiene un
     subcomando de chequeo, agrega uno (`--doctor`/`doctor`) o un pequeño script `scripts/preflight.ps1`.
   - Asegura un comando `version` fiable.
4. Verifica EJECUTANDO: corre `provision.command` en limpio, luego `preflight` (debe dar 0), luego
   `version` y un `launch` que arranque sin requerir admin. No marques un comando en el manifiesto que
   no hayas confirmado.
5. Escribe `.rgtool.json` en la raíz con los comandos verificados.

## Restricciones
- NO rompas el uso actual del repo (no cambies su CLI/entrypoint público; solo añade lo necesario).
- Idempotencia obligatoria en todo lo de provisión: re-ejecutar debe ser seguro.
- El lanzamiento corre de-elevado (sin heredar admin); si algo exige privilegios, decláralo y justifícalo.
- No subas secretos al manifiesto; las claves/API se quedan en la config propia del repo.

## Entregable (reporte final)
- Lista de archivos creados/modificados (`.rgtool.json` + scripts).
- Para cada comando del manifiesto: cómo lo verificaste y el resultado (exit code / salida resumida).
- GAPS que dejaste pendientes y por qué (ej. "preflight no valida la versión de ffmpeg porque…").
- Confirmación de que el repo sigue funcionando como antes.
````

---

## Notas por repo

- **videomerge** — el más rápido: ya tiene `scripts\install-windows.ps1` idempotente (winget + ffmpeg).
  Estrategia `ScriptInstaller`. Falta formalizar `preflight` (verificar ffmpeg/SVT-AV1) y el manifiesto.
- **meet-copilot** — el que más completar: no tiene install ni preflight, GUI CustomTkinter, maneja
  secretos y LM Studio externo. Estrategia `ManagedPythonEnv` (uv), **sin `.exe`**.
- **Netmon** — intermedio: ya tiene PyInstaller (`netmon.spec`). Decidir `ManagedPythonEnv` (uv,
  recomendado: evita compilar `aioquic`) vs `PrebuiltExe` (su `.exe` actual, ruta offline).
