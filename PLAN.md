# RGTools — Optimizer & GameGuard

> **Índice y navegación → [index.yaml](index.yaml).** Léelo primero: estado escaneable (servicios,
> fases, decisiones, mapa de archivos) + mapa `navegacion` que apunta a la sección exacta de este plan.
> Este archivo (§1-§9) es **el detalle**. Fuente única (fusiona el antiguo UPDATE.md con el plan).

---

## §1. Auditoría del estado actual

**Stack:** .NET 10 (`net10.0-windows`), WPF tray (`H.NotifyIcon.Wpf` 2.4), MVVM (`CommunityToolkit.Mvvm` 8.4),
publicación win-x64 single-file self-contained. Manifiesto **requireAdministrator**. Config/logs/states en
`%APPDATA%\RGTools\`. JSON con source generators.

**Solución (3 proyectos):** `RGTools.Core` (lib, toda la lógica) · `RGTools.App` (WPF tray, UI + DI) ·
`RGTools.Tests` (xUnit + NSubstitute).

**Servicios:** ver `servicios` en [index.yaml](index.yaml). CORE: DNS Guardian, VPN, Jumpbox.

### Deuda técnica / bugs (todos resueltos)
Sin DI → Generic Host · doble suscripción de eventos · código muerto · `GC.Collect()` · persistencia en
`BaseDirectory` → `%APPDATA%` · `IDisposable` liberado por el Host. (detalle histórico en §7).

---

## §2. Arquitectura objetivo

```
Generic Host (Microsoft.Extensions.Hosting) + DI
├── Infra: IConfigService, INotificationService, IUserConsentService, ISystemStateStore, IProcessRunner
├── Core: IDnsGuardianService, IVpnService, IJumpboxService, IStartupService
├── Soporte de modos: IPowerPlanService, IGpuPriorityService, IWorkloadGuard, INotificationSilencer, IHostsBlocker
├── Modos (IMode): WorkModeService, GamingModeService, ZenModeService + ModeManager + KillAllService
└── HealthCheckService (BackgroundService)
```

### Patrón de rollback transaccional (regla de oro)
1. **Snapshot** del estado previo → `ISystemStateStore` (escritura atómica .tmp+Move).
2. **Consentimiento** si es invasiva → `IUserConsentService`.
3. **Persistir intent** (`ActiveProfile`) ANTES de mutar.
4. **Aplicar** el cambio.
5. **Restaurar** en `DeactivateAsync` **y** en el arranque vía `SanitizeToWorkAsync` si hay snapshots huérfanos.
   `WorkModeService.ActivateAsync` restaura el conjunto COMPLETO (Workload, GPU, Toasts, Hosts, Power),
   por lo que el sanitize tras crash deja Windows limpio aunque no pase por el `Deactivate` del modo previo.

---

## §3. Spec de ejecución por fases

### FASE 0 — Fundación ✅ · FASE 1 — Optimización ✅
DI/Host, `%APPDATA%`, interfaces, infra (notif/consent/state), fugas corregidas, StartupService async,
config con lock, arranque ~389ms.

### FASE 2 — Modos / Perfiles (`IMode`) ✅
`ModeManager` (un modo activo, transiciones serializadas, persistencia, sanitize). UI: PERFILES + Restaurar.

#### 💼 Trabajo (`WorkModeService`) — estado base
Restaura lo que Gaming/Zen tocaron: WSearch, GPU registry, toasts, hosts, plan de energía → original.

#### 🎮 Gaming (`GamingModeService`)
Cierra (cierre ordenado + force): **Docker, WSL2, LM Studio, Slack, Discord, Spark, WhatsApp, qBittorrent**
+ detiene WSearch. **NO** toca Teams/VS Code/navegador. Plan **High Performance**. **Silencia toasts de
Windows** (Teams sigue abierto pero sin interrumpir). **[opt-in]** GPU Priority 8/6/High con rollback.
NO auto-abre launchers (Steam/Battle.net). Monitor 2º: se mantiene (panel vertical de consulta).

#### 🧘 Zen (`ZenModeService`)
Silencia notificaciones no críticas (RGTools) · Pomodoro 25/5 · **[opt-in]** bloqueo de sitios en `hosts`
(vía `IHostsBlocker`, por marcador de línea, solo si `ZenBlockedHosts` no vacío).

#### 🧹 "Restaurar Estado Limpio" (`KillAllService`) — reset, NO apagado
1. Vuelve a **Trabajo** (revierte todo). 2. Apaga **VPN**. 3. **DNS sigue ON**. 4. **App NO se cierra**.

### FASE 3 — `HealthCheckService` ✅
BackgroundService 60s → tooltip del tray: modo · DNS · disco C: · ping 1.1.1.1.

---

## §4. Desviaciones y limitaciones conocidas
- **Notificaciones**: `H.NotifyIcon` en vez de `Microsoft.Windows.AppNotifications` (evita MSIX en single-file).
- **Temperatura CPU/GPU**: no incluida (no fiable sin SDK extra).
- **Staged (no implementado)**: Nagle off, apagar monitor 2º, monitor activo de Gaming 45s.
- **Docker/LM Studio/apps cerradas**: no se reabren automáticamente al volver a Trabajo (se reabren a mano).
- Sin comentarios/XML doc (decisión del usuario).

---

## §5. Estado de progreso
- [x] Fase 0 · [x] Fase 1 · [x] Fase 2 · [x] Fase 3
- [x] **DnsGuardian endurecido** (lock/try-catch/_disposed/deadlock stderr) — lógica DNS intacta.
- [x] **Separación Core/UI** (`RGTools.Core`).
- [x] **Tests** (xUnit + NSubstitute) — 21/21 verdes.
- [x] **Pulido de deuda** (JumpboxService desacoplado, checkbox startup sincronizado, paths inyectables).
- [x] **Ajustes de perfiles** (cierre de apps reales, silenciador de toasts, Restaurar Estado Limpio).
- [x] **Auditoría de riesgos** crash/corte de luz (§8) + **3ª pasada Opus** (§9) — 2 bugs corregidos.

### Estructura
```
RGTools.Core/
├── Abstractions/   I{Config,DnsGuardian,Vpn,Jumpbox,Startup,Notification,UserConsent,SystemStateStore,
│                   ProcessRunner,Mode,ModeManager,PowerPlan,GpuPriority,WorkloadGuard,
│                   NotificationSilencer,HostsBlocker,KillAll}.cs
├── AppPaths · AppSettings · ConfigService · StateKeys
├── NotificationService · UserConsentService · SystemStateStore · ProcessRunner
├── DnsGuardianService · VpnService · JumpboxService · StartupService · LogService
├── PowerPlanService · GpuPriorityService · WorkloadGuardService · NotificationSilencerService
├── HostsBlockerService · HealthCheckService
└── Modes/ ModeManager · WorkModeService · GamingModeService · ZenModeService · KillAllService
RGTools.App/  App.xaml.cs (Host+DI) · ViewModels/TrayViewModel · Views/DashboardView
RGTools.Tests/  ModeManager · UserConsentService · ConfigService · SystemStateStore · HostsRollback Tests
```

---

## §6. Cómo probar
**A) Build** — `dotnet build RGTools.slnx -c Debug` y `-c Release`: 0/0.
**B) Tests** — `dotnet test RGTools.slnx`: 21/21.
**C) Smoke** — lanzar `.exe`, leer `%APPDATA%\RGTools\logs\rgtools.log`: secuencia limpia, sin `[CRITICAL]`.
**D) Funcional de perfiles** (usuario):
1. **Gaming** → pide consentimiento GPU; cierra apps; High Performance; silencia toasts.
2. **Trabajo** → restaura WSearch + GPU registry + hosts + plan.
3. **Zen** → silencia + Pomodoro.
4. **Restaurar Estado Limpio** → vuelve a Work, apaga VPN, DNS sigue ON, app queda abierta.
5. **Reiniciar tras crash en Gaming/Zen** → el arranque debe sanear a Work (sanitize, incluye hosts).

---

## §7. Auditoría general (agente independiente · 30 hallazgos)

### ✅ Corregidos
- Rollback no durable / `GetAwaiter().GetResult()` en UI (GPU/hosts) → todo `await`, snapshot antes de mutar.
- `ModeManager` inconsistente ante fallo → recuperación/force a Work.
- Duplicación de procesos → `IProcessRunner`.
- `ConfigService`: `Current` solo tras escribir OK.
- Fuga de handles `Process` y de eventos en `DashboardView`.

### ✅ `DnsGuardianService` endurecido (Opción A — lógica intacta)
`_cts`/`_networkWatcher` bajo `Lock`; try/catch en watcher WMI; flag `_disposed`; stdout/stderr en paralelo.

---

## §8. Auditoría de riesgos (crash / corte de luz / cambios de perfil)

Hardware: Intel Core Ultra 7 265K · RTX 5070 Ti 16GB · 64GB DDR5 6400 · 2 monitores · UPS Forza 1500VA · SSD PCIe 5.0.

### ✅ Corregido (resiliencia ante fallos)
- **🔴 Estado huérfano tras crash**: el arranque ya NO confía solo en `ActiveProfile`. `ModeManager.IsDirty`
  detecta CUALQUIER snapshot en `states\` y `SanitizeToWorkAsync()` restaura todo aunque el crash ocurriera
  a mitad de una activación.
- **🔴 Orden de persistencia**: el intent (`ActiveProfile=target`) se persiste ANTES de mutar el sistema.
- **🔴 Planes de energía duplicados**: eliminado `powercfg /duplicatescheme`. Se guarda el GUID activo, se
  pasa a High Performance, y se restaura el original.
- **🔴 Escritura no atómica**: `SystemStateStore` y `HostsBlocker` escriben a `.tmp` + `File.Move` atómico.
- **🔴 hosts corrupto**: Zen bloquea/restaura por **marcador de línea** (`# RGTools-Zen`); imposible perder
  entradas del sistema.
- **🟡 Kill brusco**: `CloseMainWindow()` + espera 3s + `-Force` solo si siguen vivos.

### 🖥️ Optimización para el hardware real
- **Monitor secundario**: NO se apaga (tu LG vertical de consulta — útil para ver Teams en Gaming).
- **Plan de energía**: High Performance estándar (no "Ultimate" duplicado). Con tu 265K + UPS sin riesgo.
- **GPU Priority 8/6/High**: acotado a `Tasks\Games`, no afecta audio MMCSS global. Válido para RTX 5070 Ti.
- **UPS Forza 1500VA**: mitiga cortes de luz, pero NO los cierres forzados (crash/BSOD/reset) → el sanitize
  en arranque sigue siendo la red de seguridad real.

---

## §9. Tercera auditoría (Opus, pasada final)

Confirmó solidez (DI sin doble registro, interfaces sincronizadas, store atómico, ModeManager bien
serializado, disposables OK) y detectó **2 bugs reales corregidos**:

- **🔴 Work no restauraba los hosts de Zen** → tras crash en Zen, el `sanitize` arrancaba Work pero Work
  ignoraba `ZenHosts` → sitios bloqueados en `hosts` para siempre + `IsDirty` permanentemente true.
  **Fix**: extraído `IHostsBlocker`/`HostsBlockerService` (mismo patrón que GPU); **Work, Zen y el sanitize
  restauran el MISMO conjunto** (Workload, GPU, Toasts, Hosts, Power). Tests `HostsRollbackTests` lo cubren.
- **🟡 Silenciador de toasts sobrescribía el snapshot** en doble-activación → perdía el valor original.
  **Fix**: `SilenceAsync` guarda solo si `!Exists` (idempotente, igual que GPU/Power).
- **🟢** `ZenModeService` ahora `IDisposable` (cancela Pomodoro al cerrar); Work resetea `MinimumLevel=Info`.

**Tests: 21/21.** Build Debug+Release 0/0.

### Confirmado sólido por la 3ª auditoría
DI sin doble registro · interfaces ↔ implementaciones sincronizadas · `SystemStateStore` atómico ·
`ModeManager` serializado con `SemaphoreSlim`, persiste intent antes de aplicar, `ForceWorkAsync` ante fallo ·
idempotencia de GPU/Power/Toasts/Hosts (guarda `Exists`) · disposables correctos (DNS/VPN/Zen) ·
`DashboardView` desuscribe eventos en `OnClosed`. **Nada destructivo que rompa Windows.**
