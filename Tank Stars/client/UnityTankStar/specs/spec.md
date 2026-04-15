# SDD Especificació — Tank Stars

---

## 1. TerrainGenerator

### 1.1 GenerateTerrain(int seed, string mapType)

#### Entrada
- `seed`: enter per inicialitzar el RNG (reproducibilitat determinista).
- `mapType`: cadena, un de: `"desert"`, `"snow"`, `"grassland"`, `"canyon"`, `"volcanic"`.

#### Sortida
- Mesh visible amb turons des de `x = -width/2` fins a `x = +width/2` unitats mundials.
- `PolygonCollider2D` coincidint exactament amb la superfície del mesh.
- Material del `MeshRenderer` actualitzat amb la textura del bioma.

#### Comportament
- Utilitza els **MAP_PRESETS** del servidor (24 punts, escala 0–100) com a forma base del bioma.
- Afegeix una petita pertorbació Perlin (FBM de 3 octaves, amplitud ±0.4 unitats) per a varietat visual entre partides.
- Si el `mapType` no és reconegut, fa servir el preset de `"desert"` com a fallback.
- L'alçada del terreny es clamp a `[baseHeight + 0.3, baseHeight + maxHeight]`.
- Crida `ApplyMapTypeTexture(mapType)` al final per aplicar la textura correcta.

#### Presets per bioma (idèntics als del servidor)

| Bioma     | Forma característica                  |
|-----------|---------------------------------------|
| desert    | Turons suaus i ondulats               |
| snow      | Turons alts i arrodonits              |
| grassland | Pendents suaus i baixos               |
| canyon    | Vall profunda al centre (min h=20)    |
| volcanic  | Pics aguts i pronunciats (max h=82)   |

---

### 1.2 LoadServerHeights(int[] serverHeights, string mapType = null)

#### Entrada
- `serverHeights`: array d'enters 0–100 (qualsevol nombre de columnes).
- `mapType` *(opcional)*: si s'especifica, actualitza la textura del bioma.

#### Comportament
- Interpola (lerp lineal) les alçades del servidor a les `columns` columnes locals.
- Fórmula de conversió: `worldY = baseHeight + (h / 100f) * maxHeight`.
- Si `mapType != null`, actualitza `_currentMapType` i crida `ApplyMapTypeTexture`.
- Usat en mode multijugador per assegurar que tots dos clients tinguin el **mateix terreny** autoritzat pel servidor.

---

### 1.3 DestroyTerrain(Vector2 impactWorld, float radius)

#### Entrada
- `impactWorld`: posició mundial de l'impacte.
- `radius`: radi del cràter en unitats mundials (valor estàndard: **0.5** en client Unity, **5** en el servidor).

#### Comportament
- Perfil de cràter **còsinus** (bol arrodonit, no V punxeguda):
  - `depthFactor = cos((dx / radius) * π / 2)`
  - `carved = localImpactY − radius × 0.7 × depthFactor`
- S'aplica `heights[i] = min(heights[i], max(baseHeight + 0.2, carved))`.
- Reconstrueix mesh i collider.
- **No** crida `ApplyMapTypeTexture` (la textura no canvia entre impactes).

---

### 1.4 ApplyMapTypeTexture(string mapType)

#### Comportament
- Carrega `Resources/Images/terrain/terrain_{mapType}.png`.
- `wrapModeU = Repeat` (la textura tila horitzontalment).
- `wrapModeV = Clamp` (la textura no tila verticalment; V=1 sempre és la superfície).
- Usa `meshRenderer.material` (instància automàtica) per evitar problemes de keywords de shader.
- Aplica la textura via `mat.mainTexture` (universal) i `mat.SetTexture("_BaseMap", ...)` (URP).
- Reseteja `_BaseColor` i `_Color` a blanc perquè la textura es vegi a plena brillantor.
- El tilat horitzontal és **`uvTileCount = 5`** repeticions — ja encaixat als UV del mesh (no via `mainTextureScale`).

#### Mapeig UV del mesh (BuildMesh)
```
U = (x − startX) / texTileWidth       // tila cada (width / uvTileCount) unitats mundials
V_superfície = clamp((heights[i] − baseHeight) / maxHeight, 0, 1)
V_base       = 0
```
- **Terreny alt → V proper a 1** → mostra la part superior de la textura (herba, neu).
- **Terreny baix → V proper a 0** → mostra la part inferior de la textura (terra, roca).
- Elimina l'efecte de "panells rectangulars" que apareixia amb V=1 fix a tota superfície.

---

### 1.5 GetHeightAtX(float worldX)

#### Sortida
- Coordenada Y mundial de la superfície a aquella X (columna més propera, sense interpolació).
- Retorna `transform.position.y + heights[idx]`.

---

## 2. TankController

### 2.1 Move(float direction, float deltaTime)

- Pas de moviment per frame: `direction × moveSpeed × deltaTime` (botons de pantalla: `deltaTime = 0.05f`).
- **Límit de moviment per torn**: `maxMovePerTurn = 2.5` unitats mundials acumulades.
- **Límit de pendent**: si `|atan2(Δy, |Δx|)| > 65°`, el moviment es bloqueja (clift quasi vertical).
  - Llindar augmentat de 55° → **65°** per permetre que el tanc (llarg) pugi pendents moderats.
- X i Y del destí es calculen juntes en un sol pas per evitar clipping amb terrenys pendents.
- La velocitat del `Rigidbody2D` es reseteja a zero en cada pas.
- Límit de món: `±worldBoundsX` (calculat dinàmicament des de l'amplada de càmera).

### 2.2 PlaceOnTerrain()
- Situa el tanc a `terrain.GetHeightAtX(x) + 0.35f`.
- Reseteja `linearVelocity` i `angularVelocity` del `Rigidbody2D`.

### 2.3 TakeDamage(int amount)
- `currentHp = max(0, currentHp − amount)`.
- Si `currentHp ≤ 0`, crida `PlaceOnTerrain()` per estabilitzar la posició.

---

## 3. ProjectileController

- Segueix una paràbola calculada per `Launch(angle, power, facingRight)`.
- Ignora col·lisions amb el propi tanc que el va disparar.
- Crida el callback `OnImpact(Vector2 pos, bool hitTank)` en col·lisió.

---

## 4. Mode Multijugador (CombatManager + servidor WebSocket)

### 4.1 Flux de torn
1. El servidor autoritza el torn: envia `game_start` → `game_update` → `game_end`.
2. **`positions_update`**: arriba primer, confirma la nova X del jugador local.
3. **`game_update`**: arriba després, porta les alçades del terreny actualitzades i la HP.
4. `HandleGameUpdate` **només sincronitza la X del jugador remot** (no sobreescriu la local) per evitar que la posició es resetegi.

### 4.2 Animació del projectil
- `AnimateProjectileArc`: mostra l'arc visual. Mostra el cràter al **final** de l'animació (no durant), sincronitzant-se amb les dades del servidor.
- Arc dinàmic: mostra 8 punts al llarg del trajecte per garantir que el projectil voli per sobre dels pics del terreny.
- Les alçades del terreny del servidor s'apliquen (`LoadServerHeights`) just en acabar l'animació.

### 4.3 Servidor (Node.js WebSocket — `server/game/index.js`)
- **Cràter del servidor**: `TERRAIN_RADIUS = 5`, perfil còsinus, multiplicador `0.8`.
- `calculateShot`: calcula l'impacte, el dany i actualitza `terrainHeights[]` al servidor.
- `handleMoveTank`: valida que el jugador sigui el del torn actual, actualitza `playerNX`.
- `handleFireShot`: valida angle (0–90), potència (0–100), jugador actiu i torn.
- Els estats `player1Hp`, `player2Hp`, `player1X`, `player2X` es sincronitzen via `buildStatePayload`.

---

## 5. Mode VS IA (VsAIManager + TankAgent ML-Agents)

### 5.1 Flux de torn
1. `UpdateTurnUI()`: inicia el torn del jugador, arranca el temporitzador.
2. Torn del jugador: controls actius, temporitzador decrementant.
3. Si el temps s'esgota (`turnTimeLimit = 15s`): **tir automàtic** amb els valors actuals dels sliders.
4. Torn de la IA: `StartAITurn()` activa `canCaptureActions = true` a `TankAgent`.
5. `PulseAIDecision()` envia `RequestDecision()` cada 0.2s fins que l'agent dispara.
6. Safety panic: si la IA no dispara en 6s, `ForceAIShot()` dispara un tir heurístic.

### 5.2 Temporitzador de torn
- `turnTimeLimit = 15f` (configurable a l'Inspector).
- Visible com a cercle a dalt al centre de la pantalla (`turn-timer-label`).
- Torna vermell (`urgent`) quan queden ≤ 5 segons.
- Es para en `PlayerFires()`, `ShowGameOver()` i en el torn de la IA.

### 5.3 TankAgent (ML-Agents)
- Observacions (5 floats): posX local, posX enemic, distància, HP local, HP enemic.
- Accions contínues: `[0]` → angle (0–90°), `[1]` → potència (0–100%).
- Recompenses entrenament: +1.0 impacte directe, -0.01×distància en fallida.
- Mode VS IA: danys aplicats directament (`TakeDamage`) sense recompenses d'entrenament.

---

## 6. HUD (CombatScreen.uxml / CombatStyles.uss)

### 6.1 Estructura
```
hud-header (fila horitzontal)
  hud-side-left   → nom jugador local + barra HP blava (esquerra→dreta)
  hud-center      → pastilla mapa · etiqueta de torn · codi sala
  hud-side-right  → barra HP enemiga (dreta→esquerra, flex-direction:row-reverse) + nom enemic
game-area         → bàner de torn · log de combat · popup de dany
bottom-bar        → slider angle · botons (◀ FOCA ▶) · slider potència
hud-overlay       → botó X · cercle temporitzador · game-over overlay
```

### 6.2 Barra HP enemiga invertida
- El `hud-hp-track-right` té `flex-direction: row-reverse`.
- Quan C# fa `fill.style.width = Length.Percent(hp)`, la barra creix des de la dreta cap a l'esquerra.

### 6.3 Temporitzador visual
- `.turn-timer-label`: cercle absolut, `top: 72px`, `left: 50%`, `translate: -50% 0`.
- `.turn-timer-label.urgent`: canvia de groc a vermell.
- Ocult per defecte (classe `hidden`); visible només durant el torn del jugador en mode VS IA.

---

## 7. Casos límit

| Situació | Solució |
|---|---|
| Cràters superposats | `min(existing, carved)` — el terreny mai puja per impacte |
| Tanc llisca post-destrucció | `PlaceOnTerrain()` reseteja velocitat del Rigidbody2D |
| Moviment en pendent >65° | `Move()` retorna 0 (clift gairebé vertical bloqueja el tanc) |
| Posició resetejada en multijugador | `HandleGameUpdate` no sobreescriu la X del jugador local |
| IA no respon | `ForceAIShot()` recupera el bucle de joc al cap de 6s |
| Temps de torn esgotat (VS IA) | Tir automàtic amb valors actuals dels sliders |
| `mapType` no reconegut | Fallback a preset `"desert"` |
| `seed = 0` | Vàlid; `Random.InitState(0)` funciona normalment |
| Terreny fora de límits | Columnes clampejades amb `Mathf.Clamp` |
| Textura no trobada | Advertència al log; conserva el material anterior |
