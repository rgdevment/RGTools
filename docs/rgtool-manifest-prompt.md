# Prompt: estandarizar un repo con un manifiesto `.rgtool.json`

Prompt **autónomo y autocontenido** para correr en **cualquier** repositorio, individualmente. El agente
solo conoce el repo donde se ejecuta — no sabe nada de RGTools, del hub ni de otros repos, y no lo
necesita. Sirve igual sea cual sea el lenguaje o propósito del proyecto, ahora o en el futuro.

Uso: copia el bloque de abajo **tal cual** a un agente dentro del repo objetivo. No hay nada que editar.
El esquema que produce es el contrato que el hub consumirá más tarde; ver
[`hub-utilities-plan.md`](./hub-utilities-plan.md) para el lado del hub.

---

````text
# Estandarizar este proyecto con un manifiesto .rgtool.json

Estás trabajando dentro de un repositorio de software. Tu tarea es crear (o actualizar) un manifiesto
estándar llamado `.rgtool.json` en la raíz de ESTE repositorio, que describa de forma declarativa cómo
se prepara, valida, ejecuta y versiona este proyecto. Una herramienta externa leerá ese manifiesto para
automatizar esas acciones; no necesitas saber cuál. Trabaja ÚNICAMENTE con el código de este repositorio:
no asumas nada que no puedas verificar aquí. El manifiesto debe quedar correcto sea cual sea el lenguaje
o propósito del proyecto.

REGLA DE ORO: no inventes comandos. Cada comando que declares debe estar verificado — ejecutándolo o
confirmándolo en la configuración del repo (manifest de paquetes, scripts, entrypoint).

## Esquema de `.rgtool.json` (créalo/actualízalo en la raíz)
{
  "schema": 1,
  "id": "<identificador corto, normalmente el nombre del repo en kebab-case>",
  "name": "<nombre legible>",
  "description": "<una línea: qué hace>",
  "category": "<una de: Network | Privacy | Productivity | System | Database>",
  "requirements": {
    "runtime": "<runtime y versión mínima REALES; ej. python>=3.10, node>=20, dotnet>=8. Léelo de la config>",
    "system": ["<dependencias de SISTEMA externas al runtime; ej. ffmpeg>=4. [] si no hay>"]
  },
  "provision": {
    "strategy": "<ManagedEnv | ScriptInstaller | PrebuiltBinary | SystemPackage | None>",
    "command": "<comando idempotente que deja el proyecto LISTO; \"\" si strategy = None>"
  },
  "preflight": "<comando que devuelve exit 0 SOLO si el entorno está listo para ejecutar; \"\" si no aplica>",
  "launch": { "kind": "<Exe | Interpreter>", "command": "<comando que arranca el proyecto>" },
  "version": "<comando que imprime la versión; \"\" si no hay>",
  "elevated": false,
  "artifacts": [
    { "label": "<nombre legible, ej. Reportes>", "path": "<carpeta de salidas: relativa al repo o absoluta con %VARS%>", "pattern": "<glob, ej. report-*.txt o **/*_MINUTA.md>", "limit": 0 }
  ]
}

### Significado de los valores
- `provision.strategy`:
  - `ManagedEnv`      → crea/sincroniza un entorno de dependencias aislado (ej. `uv sync`, `npm ci`, `dotnet restore`).
  - `ScriptInstaller` → hay dependencias de SISTEMA que instalar; el comando es un script idempotente del repo.
  - `PrebuiltBinary`  → se distribuye un binario ya compilado; el comando lo ubica o descarga.
  - `SystemPackage`   → es un paquete del sistema operativo (gestor de paquetes del SO).
  - `None`            → no requiere preparación.
- `launch.kind`: `Interpreter` si corre a través de un runtime (`python -m`, `node`, `uv run`, `dotnet`);
  `Exe` si es un binario ejecutable directo.
- `elevated`: `true` solo si el lanzamiento EXIGE privilegios de administrador. Por defecto `false`.
- `artifacts` (opcional): archivos de salida que el proyecto genera y que conviene abrir desde fuera
  (reportes, minutas, exports). `path` admite ruta relativa al repo o absoluta con variables de entorno
  (`%VAR%`); `pattern` es un glob (`**/` = recursivo); `limit` 0 = todos, N = solo los N más recientes por
  fecha. Omite el campo o usa `[]` si el proyecto no genera salidas relevantes.

## Pasos
1. Identifica lenguaje, runtime y versión mínima reales (lee `pyproject.toml`/`requirements.txt`,
   `package.json`, `*.csproj`, `go.mod`, etc.) y la forma de ejecución (CLI, TUI, GUI, servicio). Detecta
   también si el proyecto GENERA archivos de salida (reportes, minutas, exports) y en qué carpeta, para
   declararlos en `artifacts`.
2. Detecta dependencias de SISTEMA externas al runtime (binarios en PATH, librerías nativas, servicios).
3. Elige `provision.strategy` según la tabla de arriba.
4. COMPLETA LO QUE FALTE, de forma idempotente y sin romper el uso actual del proyecto:
   - Si no hay forma fiable de preparar el entorno → crea el comando/script de provisión (preferir un
     entorno aislado por proyecto; un script del repo cuando haya dependencias de sistema).
   - Si no hay forma de comprobar que el entorno está listo → añade un `preflight`: un subcomando tipo
     `--doctor`/`doctor` o un pequeño script que valide runtime + dependencias + binarios de sistema en
     su versión mínima, devolviendo exit 0 solo si todo está OK.
   - Asegura un comando de `version` fiable si el proyecto puede exponerlo.
5. VERIFICA EJECUTANDO: corre `provision.command` en limpio, luego `preflight` (debe dar exit 0), luego
   `version` y un `launch` que arranque correctamente. No declares un comando que no hayas confirmado.
6. Escribe `.rgtool.json` en la raíz con los comandos verificados.

## Restricciones
- No cambies la interfaz pública del proyecto (CLI/entrypoint); solo añade lo necesario.
- Idempotencia obligatoria en la provisión: re-ejecutar debe ser seguro.
- No incluyas secretos ni credenciales en el manifiesto.
- El lanzamiento no debe requerir administrador salvo que sea imprescindible (`elevated: true` + justifícalo en el reporte).

## Entregable (reporte final)
- Archivos creados/modificados (`.rgtool.json` + scripts si los hubo).
- Para cada comando del manifiesto: cómo lo verificaste y el resultado (exit code / salida resumida).
- Gaps que dejaste pendientes y por qué.
- Confirmación de que el proyecto sigue funcionando como antes.
````

---

## Notas (lado RGTools, NO parte del prompt)

Guía interna para dar de alta cada repo en el hub una vez estandarizado. El agente que corre el prompt
no ve esto.

- **videomerge** — `ScriptInstaller` (ya tiene `scripts\install-windows.ps1`: winget + ffmpeg). Falta
  formalizar `preflight` (verificar ffmpeg/SVT-AV1) y el manifiesto. Categoría `Productivity`.
- **meet-copilot** — `ManagedEnv` (uv, **sin binario**); no tiene install ni preflight, GUI + secretos +
  LM Studio externo. El que más completar. Categoría `Productivity`.
- **Netmon** — `ManagedEnv` (uv, recomendado: evita compilar `aioquic`) o `PrebuiltBinary` (su `.exe`
  actual, ruta offline). Categoría `Network`.
