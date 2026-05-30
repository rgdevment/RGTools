# RGTools — Optimizer & GameGuard
- Sin proyecto de tests (BAJO-9).
- `IStartupService.IsEnabledAsync` no se usa para sincronizar el checkbox con la tarea real (MEDIO-5).

---

## §8. Auditoría de riesgos (crash / corte de luz / cambios de perfil) — 2026-05-30

Segunda auditoría, enfocada en **dejar Windows en estado roto** ante reinicios, cortes y transiciones.
Hardware: Intel Core Ultra 7 265K · RTX 5070 Ti 16GB · 64GB DDR5 6400 · 2 monitores · UPS Forza 1500VA · SSD PCIe 5.0.

### ✅ Corregido (resiliencia ante fallos)
- **🔴 Estado huérfano tras crash**: el arranque ya NO confía solo en `ActiveProfile`. `ModeManager.IsDirty`
  detecta CUALQUIER snapshot en `states\` y `SanitizeToWorkAsync()` restaura todo (GPU registry, toasts,
  WSearch, plan de energía) aunque el crash ocurriera a mitad de una activación.
- **🔴 Orden de persistencia**: el "intent" (`ActiveProfile=target`) se persiste ANTES de mutar el sistema
  (test `SwitchTo_PersistsIntent_BeforeActivating`) → tras crash, el arranque sabe que debe limpiar.
- **🔴 Planes de energía duplicados**: eliminado `powercfg /duplicatescheme` (creaba un plan basura cada vez).
  Ahora se guarda el GUID del plan activo, se pasa a High Performance, y se restaura el original.
- **🔴 Escritura no atómica**: `SystemStateStore` escribe a `.tmp` + `File.Move` atómico → un corte de luz
  no deja JSON de rollback corrupto (mitigado además por tu UPS).
- **🔴 hosts corrupto**: Zen ya no reescribe el archivo completo. Bloquea/restaura por **marcador de línea**
  (`# RGTools-Zen`): solo añade/quita sus propias líneas. Imposible perder entradas del sistema.
- **🟡 Kill brusco**: ahora `CloseMainWindow()` + espera 3s + `-Force` solo si siguen vivos → menos riesgo
  de corromper datos de qBittorrent/Docker.
- **🟢 Refactor**: GPU Priority extraído a `GpuPriorityService`; claves de estado en `StateKeys`; rollback
  intra-modo idempotente (Gaming/Work restauran GPU+toasts+power sin acumular).

### 🖥️ Optimización para el hardware real
- **Monitor secundario**: NO se apaga (tu LG vertical de consulta — útil para ver Teams en Gaming).
- **Plan de energía**: High Performance estándar (no "Ultimate" duplicado). Con tu 265K + UPS sin riesgo.
- **GPU Priority 8/6/High**: acotado a `Tasks\Games`, no afecta audio MMCSS global. Válido para RTX 5070 Ti.
- **UPS Forza 1500VA**: mitiga cortes de luz, pero NO los cierres forzados (crash/BSOD/reset) → el sanitize
  en arranque sigue siendo la red de seguridad real.

### 📋 Aceptado / falsas alarmas
- **Falsa alarma**: el auditor reportó un `RGTools.App/Core/App.xaml.cs` "fantasma" — verificado, NO existe.
- **Pomodoro no sobrevive reinicios** (UX menor, documentado).
- **Consentimiento recordado** para GPU/hosts (por diseño; el sanitize cubre la mutación repetida).
