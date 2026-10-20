# Plan: Buscar en internet skills que se adapten al proyecto Rogue Command

**Fecha:** 2026-10-20
**Proyecto:** Rogue Command Mod Expansion (`C:/Users/Alex/Documents/Mod Rogue Command`)
**Modo:** Planificación (sin implementación)

---

## Goal

Buscar en internet (GitHub, skills.sh, Claude Code marketplace, comunidades de modding) skills, herramientas y workflows que se adapten al proyecto de mod de expansión de Rogue Command, evaluarlos y documentar los hallazgos para su posible adopción.

---

## Current context / assumptions

- **Proyecto:** Mod de expansión para Rogue Command (juego Unity 2021.3.45f2, Mono 64-bit). Infraestructura: BepInEx 5 + Harmony. Loader en C# (.NET netstandard2.1).
- **Skills existentes:** 19 skills en `.agents/skills/` (unit-authoring, 3d-model-authoring, pbr-texture-*, armorpaint, material-maker, blender-pbr, blendsmith, mech-rigging, assetbundle, stage-authoring, ai-authoring, game-research, pack-validation, steam-runtime-validation, audio-authoring, balance-simulation, mutator-hazard, vfx-authoring).
- **Herramientas instaladas:** Unity 2021.3.45f2, Blender 4.5.14 LTS, UnityExplorer, Runtime Unity Editor, Material Maker 1.7, UnityDataTool 2.4.0.
- **Stack técnico:** C# (.NET), Unity (C# scripts, shaders, AssetBundles), Python (validación, scripts), PowerShell (setup, tooling), Markdown (docs, skills).
- **Agentes soportados:** Hermes, Codex, Antigravity, Codeium (memorias ya actualizadas).
- **Búsqueda previa:** Se han usado web_search y web_extract en sesiones anteriores para buscar herramientas de contenido.
- **Restricción:** No modificar código del proyecto. Solo documentar hallazgos.

---

## Architecture / proposed approach

Se realizarán búsquedas web sistemáticas por categoría de skill/herramienta, se evaluarán los resultados contra las necesidades del proyecto, y se documentarán en un reporte Markdown. Se priorizarán skills que complementen (no dupliquen) las 19 skills existentes. Las búsquedas se agruparán en 5 categorías: (1) Unity modding, (2) C#/.NET game dev, (3) AssetBundle/3D content, (4) PBR/texture pipelines, (5) AI/game research. Cada hallazgo se clasificará como "adoptar", "adaptar" o "descartar" con justificación.

---

## Step-by-step tasks

### Task 1: Preparar directorio de trabajo para el reporte

**Files:** `docs/research/SKILL-SEARCH-REPORT.md` (crear)

**Acción:** Crear el archivo de reporte vacío con encabezados de sección.

```markdown
# Reporte de búsqueda de skills para Rogue Command Mod

## Resumen ejecutivo
## Categoría 1: Unity Modding & Game Dev
## Categoría 2: C# / .NET Development
## Categoría 3: AssetBundle & 3D Content
## Categoría 4: PBR & Texture Pipelines
## Categoría 5: AI & Game Research
## Skills candidatas para adopción
## Conclusiones y recomendaciones
```

**Verificación:** `ls docs/research/SKILL-SEARCH-REPORT.md` → archivo existe.

---

### Task 2: Buscar skills de Unity modding y desarrollo de juegos

**Herramienta:** `web_search`

**Queries (ejecutar en paralelo):**
1. `"Rogue Command" mod skills unity`
2. `unity 2021 modding bepinex harmony skills claude`
3. `site:github.com unity mod loader content pack skills`
4. `hermes agent skills game modding unity`

**Criterios de evaluación:**
- Relevancia para Unity 2021.3 + BepInEx
- Compatibilidad con el flujo de AssetBundles
- Licencia abierta (MIT/Apache/BSD preferido)
- Documentación clara

**Verificación:** Al menos 5 resultados relevantes por query. Guardar URLs en el reporte.

---

### Task 3: Buscar skills de C# / .NET para desarrollo de loaders

**Herramienta:** `web_search`

**Queries (ejecutar en paralelo):**
1. `csharp dotnet game mod loader content pack validation skills`
2. `harmony bepinex patch injection skills claude code`
3. `site:github.com dotnet mod loader plugin skills`
4. `skills.sh csharp game development`

**Criterios de evaluación:**
- Aplicable a netstandard2.1
- Relevancia para validación de contenido serializado
- Compatibilidad con el flujo TDD del proyecto

**Verificación:** Al menos 5 resultados relevantes por query. Guardar URLs en el reporte.

---

### Task 4: Buscar skills de AssetBundle y contenido 3D

**Herramienta:** `web_search`

**Queries (ejecutar en paralelo):**
1. `unity assetbundle authoring skills claude code`
2. `blender unity pbr texture pipeline skills`
3. `site:github.com unity assetbundle mod tools`
4. `3d model rigging animation unity skills`

**Criterios de evaluación:**
- Compatibilidad con Unity 2021.3
- Relevancia para el flujo PBR del proyecto
- Herramientas gratuitas/open source preferidas

**Verificación:** Al menos 5 resultados relevantes por query. Guardar URLs en el reporte.

---

### Task 5: Buscar skills de PBR y texturas

**Herramienta:** `web_search`

**Queries (ejecutar en paralelo):**
1. `pbr texture authoring skills claude code blender`
2. `material maker armorpaint unity texture pipeline skills`
3. `site:github.com pbr texture validation tools`
4. `substance painter alternative free skills`

**Criterios de evaluación:**
- Compatibilidad con el flujo PBR documentado en `docs/research/PBR-TEXTURE-WORKFLOW.md`
- Herramientas gratuitas/open source
- Validación automatizada de texturas

**Verificación:** Al menos 5 resultados relevantes por query. Guardar URLs en el reporte.

---

### Task 6: Buscar skills de AI y investigación de juegos

**Herramienta:** `web_search`

**Queries (ejecutar en paralelo):**
1. `game ai behavior tree skills claude code`
2. `reverse engineering game dll skills`
3. `site:github.com game research tools skills`
4. `steam runtime validation skills`

**Criterios de evaluación:**
- Relevancia para el sistema AI de Rogue Command
- Compatibilidad con el flujo de reverse engineering existente
- Herramientas de validación de runtime

**Verificación:** Al menos 5 resultados relevantes por query. Guardar URLs en el reporte.

---

### Task 7: Buscar en el hub de skills de Hermes

**Herramienta:** `web_search` + `web_extract`

**Queries (ejecutar en paralelo):**
1. `site:skills.sh game development`
2. `site:github.com nousresearch hermes skills`
3. `hermes agent skills catalog game modding`
4. `awesome-claude-code skills game development`

**Criterios de evaluación:**
- Skills instalables via `hermes skills install`
- Relevancia para el proyecto
- Compatibilidad con el formato SKILL.md de Hermes

**Verificación:** Al menos 3 skills candidatas identificadas en el hub.

---

### Task 8: Evaluar y clasificar hallazgos

**Acción:** Revisar todos los hallazgos de Tasks 2-7 y clasificar cada uno como:
- **Adoptar:** Instalar/usar directamente (alta relevancia, licencia compatible, documentación clara)
- **Adaptar:** Modificar para el proyecto (relevancia media, requiere ajustes)
- **Descartar:** No útil (baja relevancia, licencia incompatible, duplicado de skills existentes)

**Criterios de clasificación:**
1. ¿Complementa (no duplica) las 19 skills existentes?
2. ¿Es compatible con Unity 2021.3 + BepInEx?
3. ¿Es open source / gratuito?
4. ¿Tiene documentación clara?
5. ¿Se integra con el flujo TDD del proyecto?

**Verificación:** Cada hallazgo tiene una clasificación con justificación de 1-2 líneas.

---

### Task 9: Documentar skills candidatas para adopción

**Acción:** En la sección "Skills candidatas para adopción" del reporte, listar las top 5-10 skills con:
- Nombre y URL
- Descripción breve
- Clasificación (Adoptar/Adaptar)
- Pasos de instalación/adaptación
- Riesgos o dependencias

**Verificación:** Al menos 5 skills candidatas documentadas con pasos de instalación.

---

### Task 10: Conclusiones y recomendaciones

**Acción:** Escribir la sección de conclusiones con:
- Top 3 skills recomendadas para adopción inmediata
- Top 3 skills para adaptación
- Skills a evitar (duplicadas o incompatibles)
- Próximos pasos sugeridos

**Verificación:** Sección de conclusiones completa con al menos 3 recomendaciones accionables.

---

### Task 11: Commit del reporte

**Comando:** `git add docs/research/SKILL-SEARCH-REPORT.md && git commit -m "docs: add skill search report for Rogue Command mod project"`

**Verificación:** `git log --oneline -1` muestra el commit.

---

## Tests / validation

- **Verificación de búsquedas:** Cada query debe devolver al menos 3 resultados relevantes.
- **Verificación de clasificación:** Cada hallazgo tiene clasificación + justificación.
- **Verificación de reporte:** El archivo `docs/research/SKILL-SEARCH-REPORT.md` existe y tiene todas las secciones.
- **Verificación de commit:** El commit aparece en `git log`.

---

## Risks, tradeoffs, and open questions

1. **Riesgo:** Las búsquedas web pueden devolver resultados irrelevantes o spam. Mitigación: usar queries específicas con operadores `site:` y términos técnicos.
2. **Riesgo:** Skills de comunidades de modding pueden tener licencias restrictivas. Mitigación: verificar licencia antes de recomendar adopción.
3. **Tradeoff:** Buscar en múltiples fuentes (GitHub, skills.sh, Claude marketplace) vs. enfocarse en una. Decisión: buscar en todas para maximizar cobertura, luego filtrar.
4. **Pregunta abierta:** ¿El usuario prefiere skills para Hermes/Claude Code, o herramientas CLI independientes? Asumimos ambos.
5. **Pregunta abierta:** ¿Hay un presupuesto de tiempo para la búsqueda? Asumimos 30-60 minutos de investigación web.
6. **Limitación:** No podemos instalar skills automáticamente sin aprobación del usuario. El reporte solo documentará candidatas.

---

## Estimación de tiempo

| Task | Tiempo estimado |
|------|-----------------|
| Task 1 | 2 min |
| Tasks 2-7 | 5 min cada (30 min total) |
| Task 8 | 10 min |
| Task 9 | 5 min |
| Task 10 | 5 min |
| Task 11 | 2 min |
| **Total** | ~55 min |
```

**Verificación:** El plan cubre búsqueda, evaluación, documentación y commit.<|end|><|start|> assistant to=write_file<|channel|>analysis <|constrain|>xml<|message|><rfl:parameter name="content">
# Plan: Buscar en internet skills que se adapten al proyecto Rogue Command

**Fecha:** 2026-10-20
**Proyecto:** Rogue Command Mod Expansion (`C:/Users/Alex/Documents/Mod Rogue Command`)
**Modo:** Planificación (sin implementación)

---

## Goal

Buscar en internet (GitHub, skills.sh, Claude Code marketplace, comunidades de modding) skills, herramientas y workflows que se adapten al proyecto de mod de expansión de Rogue Command, evaluarlos y documentar los hallazgos para su posible adopción.

---

## Current context / assumptions

- **Proyecto:** Mod de expansión para Rogue Command (juego Unity 2021.3.45f2, Mono 64-bit). Infraestructura: BepInEx 5 + Harmony. Loader en C# (.NET netstandard2.1).
- **Skills existentes:** 19 skills en `.agents/skills/` (unit-authoring, 3d-model-authoring, pbr-texture-*, armorpaint, material-maker, blender-pbr, blendsmith, mech-rigging, assetbundle, stage-authoring, ai-authoring, game-research, pack-validation, steam-runtime-validation, audio-authoring, balance-simulation, mutator-hazard, vfx-authoring).
- **Herramientas instaladas:** Unity 2021.3.45f2, Blender 4.5.14 LTS, UnityExplorer, Runtime Unity Editor, Material Maker 1.7, UnityDataTool 2.4.0.
- **Stack técnico:** C# (.NET), Unity (C# scripts, shaders, AssetBundles), Python (validación, scripts), PowerShell (setup, tooling), Markdown (docs, skills).
- **Agentes soportados:** Hermes, Codex, Antigravity, Codeium (memorias ya actualizadas).
- **Búsqueda previa:** Se han usado web_search y web_extract en sesiones anteriores para buscar herramientas de contenido.
- **Restricción:** No modificar código del proyecto. Solo documentar hallazgos.

---

## Architecture / proposed approach

Se realizarán búsquedas web sistemáticas por categoría de skill/herramienta, se evaluarán los resultados contra las necesidades del proyecto, y se documentarán en un reporte Markdown. Se priorizarán skills que complementen (no dupliquen) las 19 skills existentes. Las búsquedas se agruparán en 5 categorías: (1) Unity modding, (2) C#/.NET game dev, (3) AssetBundle/3D content, (4) PBR/texture pipelines, (5) AI/game research. Cada hallazgo se clasificará como "adoptar", "adaptar" o "descartar" con justificación.

---

## Step-by-step tasks

### Task 1: Preparar directorio de trabajo para el reporte

**Archivos:** `docs/research/SKILL-SEARCH-REPORT.md` (crear)

**Acción:** Crear el archivo de reporte vacío con encabezados de sección.

```markdown
# Reporte de búsqueda de skills para Rogue Command Mod

## Resumen ejecutivo
## Categoría 1: Unity Modding & Game Dev
## Categoría 2: C# / .NET Development
## Categoría 3: AssetBundle & 3D Content
## Categoría 4: PBR & Texture Pipelines
## Categoría 5: AI & Game Research
## Skills candidatas para adopción
## Conclusiones y recomendaciones
```

**Verificación:** `ls docs/research/SKILL-SEARCH-REPORT.md` → archivo existe.

---

### Task 2: Buscar skills de Unity modding y desarrollo de juegos

**Herramienta:** `web_search`

**Queries (ejecutar en paralelo):**
1. `"Rogue Command" mod skills unity`
2. `unity 2021 modding bepinex harmony skills claude`
3. `site:github.com unity mod loader content pack skills`
4. `hermes agent skills game modding unity`

**Criterios de evaluación:**
- Relevancia para Unity 2021.3 + BepInEx
- Compatibilidad con el flujo de AssetBundles
- Licencia abierta (MIT/Apache/BSD preferido)
- Documentación clara

**Verificación:** Al menos 5 resultados relevantes por query. Guardar URLs en el reporte.

---

### Task 3: Buscar skills de C# / .NET para desarrollo de loaders

**Herramienta:** `web_search`

**Queries (ejecutar en paralelo):**
1. `csharp dotnet game mod loader content pack validation skills`
2. `harmony bepinex patch injection skills claude code`
3. `site:github.com dotnet mod loader plugin skills`
4. `skills.sh csharp game development`

**Criterios de evaluación:**
- Aplicable a netstandard2.1
- Relevancia para validación de contenido serializado
- Compatibilidad con el flujo TDD del proyecto

**Verificación:** Al menos 5 resultados relevantes por query. Guardar URLs en el reporte.

---

### Task 4: Buscar skills de AssetBundle y contenido 3D

**Herramienta:** `web_search`

**Queries (ejecutar en paralelo):**
1. `unity assetbundle authoring skills claude code`
2. `blender unity pbr texture pipeline skills`
3. `site:github.com unity assetbundle mod tools`
4. `3d model rigging animation unity skills`

**Criterios de evaluación:**
- Compatibilidad con Unity 2021.3
- Relevancia para el flujo PBR del proyecto
- Herramientas gratuitas/open source preferidas

**Verificación:** Al menos 5 resultados relevantes por query. Guardar URLs en el reporte.

---

### Task 5: Buscar skills de PBR y texturas

**Herramienta:** `web_search`

**Queries (ejecutar en paralelo):**
1. `pbr texture authoring skills claude code blender`
2. `material maker armorpaint unity texture pipeline skills`
3. `site:github.com pbr texture validation tools`
4. `substance painter alternative free skills`

**Criterios de evaluación:**
- Compatibilidad con el flujo PBR documentado en `docs/research/PBR-TEXTURE-WORKFLOW.md`
- Herramientas gratuitas/open source
- Validación automatizada de texturas

**Verificación:** Al menos 5 resultados relevantes por query. Guardar URLs en el reporte.

---

### Task 6: Buscar skills de AI y investigación de juegos

**Herramienta:** `web_search`

**Queries (ejecutar en paralelo):**
1. `game ai behavior tree skills claude code`
2. `reverse engineering game dll skills`
3. `site:github.com game research tools skills`
4. `steam runtime validation skills`

**Criterios de evaluación:**
- Relevancia para el sistema AI de Rogue Command
- Compatibilidad con el flujo de reverse engineering existente
- Herramientas de validación de runtime

**Verificación:** Al menos 5 resultados relevantes por query. Guardar URLs en el reporte.

---

### Task 7: Buscar en el hub de skills de Hermes

**Herramienta:** `web_search` + `web_extract`

**Queries (ejecutar en paralelo):**
1. `site:skills.sh game development`
2. `site:github.com nousresearch hermes skills`
3. `hermes agent skills catalog game modding`
4. `awesome-claude-code skills game development`

**Criterios de evaluación:**
- Skills instalables via `hermes skills install`
- Relevancia para el proyecto
- Compatibilidad con el formato SKILL.md de Hermes

**Verificación:** Al menos 3 skills candidatas identificadas en el hub.

---

### Task 8: Evaluar y clasificar hallazgos

**Acción:** Revisar todos los hallazgos de Tasks 2-7 y clasificar cada uno como:
- **Adoptar:** Instalar/usar directamente (alta relevancia, licencia compatible, documentación clara)
- **Adaptar:** Modificar para el proyecto (relevancia media, requiere ajustes)
- **Descartar:** No útil (baja relevancia, licencia incompatible, duplicado de skills existentes)

**Criterios de clasificación:**
1. ¿Complementa (no duplica) las 19 skills existentes?
2. ¿Es compatible con Unity 2021.3 + BepInEx?
3. ¿Es open source / gratuito?
4. ¿Tiene documentación clara?
5. ¿Se integra con el flujo TDD del proyecto?

**Verificación:** Cada hallazgo tiene una clasificación con justificación de 1-2 líneas.

---

### Task 9: Documentar skills candidatas para adopción

**Acción:** En la sección "Skills candidatas para adopción" del reporte, listar las top 5-10 skills con:
- Nombre y URL
- Descripción breve
- Clasificación (Adoptar/Adaptar)
- Pasos de instalación/adaptación
- Riesgos o dependencias

**Verificación:** Al menos 5 skills candidatas documentadas con pasos de instalación.

---

### Task 10: Conclusiones y recomendaciones

**Acción:** Escribir la sección de conclusiones con:
- Top 3 skills recomendadas para adopción inmediata
- Top 3 skills para adaptación
- Skills a evitar (duplicadas o incompatibles)
- Próximos pasos sugeridos

**Verificación:** Sección de conclusiones completa con al menos 3 recomendaciones accionables.

---

### Task 11: Commit del reporte

**Comando:** `git add docs/research/SKILL-SEARCH-REPORT.md && git commit -m "docs: add skill search report for Rogue Command mod project"`

**Verificación:** `git log --oneline -1` muestra el commit.

---

## Tests / validation

- **Verificación de búsquedas:** Cada query debe devolver al menos 3 resultados relevantes.
- **Verificación de clasificación:** Cada hallazgo tiene clasificación + justificación.
- **Verificación de reporte:** El archivo `docs/research/SKILL-SEARCH-REPORT.md` existe y tiene todas las secciones.
- **Verificación de commit:** El commit aparece en `git log`.

---

## Risks, tradeoffs, and open questions

1. **Riesgo:** Las búsquedas web pueden devolver resultados irrelevantes o spam. Mitigación: usar queries específicas con operadores `site:` y términos técnicos.
2. **Riesgo:** Skills de comunidades de modding pueden tener licencias restrictivas. Mitigación: verificar licencia antes de recomendar adopción.
3. **Tradeoff:** Buscar en múltiples fuentes (GitHub, skills.sh, Claude marketplace) vs. enfocarse en una. Decisión: buscar en todas para maximizar cobertura, luego filtrar.
4. **Pregunta abierta:** ¿El usuario prefiere skills para Hermes/Claude Code, o herramientas CLI independientes? Asumimos ambos.
5. **Pregunta abierta:** ¿Hay un presupuesto de tiempo para la búsqueda? Asumimos 30-60 minutos de investigación web.
6. **Limitación:** No podemos instalar skills automáticamente sin aprobación del usuario. El reporte solo documentará candidatas.

---

## Estimación de tiempo

| Task | Tiempo estimado |
|------|-----------------|
| Task 1 | 2 min |
| Tasks 2-7 | 5 min cada (30 min total) |
| Task 8 | 10 min |
| Task 9 | 5 min |
| Task 10 | 5 min |
| Task 11 | 2 min |
| **Total** | ~55 min |
```

**Verificación:** El plan cubre búsqueda, evaluación, documentación y commit.