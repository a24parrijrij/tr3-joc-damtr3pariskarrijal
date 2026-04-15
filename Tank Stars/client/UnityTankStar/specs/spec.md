# SDD Especificació — Terreny de Tank Stars

## 1. GenerateTerrain(int seed, string mapType)

### Entrada
- `seed`: enter per inicialitzar el generador de nombres aleatoris (reproducibilitat determinista).
- `mapType`: cadena de text, un de: "desert", "snow", "grassland", "canyon", "volcanic".

### Sortida
- Mesh visible amb turons des de x=-11 fins a x=+11 unitats mundials.
- PolygonCollider2D coincidint exactament amb la superfície del mesh.

### Comportament
- El bioma afecta el color dels vèrtexs, l'alçada màxima (maxHeight) i l'escala de soroll (noiseScale).
- El mateix seed + mapType sempre produeix exactament el mateix terreny.
- L'alçada del terreny mai baixa de `baseHeight + 0.3` ni supera `baseHeight + maxHeight`.
- S'utilitza FBM (Fractional Brownian Motion) amb múltiples octaves (varia per bioma).
- El mesh té 120 columnes amb 2 vèrtexs per columna (superfície i base).
- Els colors dels vèrtexs inferiors són el 40% del color del bioma per donar profunditat visual.
- El bioma "canyon" té un pas addicional que excava una vall al terç central del terreny.

### Valors reals per bioma (extrets del codi)

| Bioma      | Color (RGB aprox.)         | maxHeight | noiseScale | octaves | persistence | lacunarity |
|------------|----------------------------|-----------|------------|---------|-------------|------------|
| desert     | (0.88, 0.68, 0.28)         | 4.5       | 0.28       | 4       | 0.45        | 2.0        |
| snow       | (0.88, 0.93, 1.0)          | 7.5       | 0.55       | 6       | 0.60        | 2.4        |
| grassland  | (0.18, 0.72, 0.25)         | 2.8       | 0.18       | 3       | 0.40        | 1.8        |
| canyon     | (0.76, 0.35, 0.15)         | 7.0       | 0.50       | 4       | 0.50        | 2.0        |
| volcanic   | (0.22, 0.08, 0.08)         | 6.5       | 0.65       | 7       | 0.65        | 2.6        |

## 2. DestroyTerrain(Vector2 impactWorldPos, float radius)

### Entrada
- `impactWorldPos`: posició mundial de l'impacte (coordenades x, y).
- `radius`: radi del cràter en unitats mundials.

### Sortida
- Cràter circular esculpit al mesh.
- Collider actualitzat sense buits ni forats.

### Comportament
- Les columnes dins del radi tenen la seva alçada reduïda per una funció de profunditat: `depth = 1 - (dx / radius)`.
- L'alçada esculpida es calcula com: `carved = localY - radius * 1.4 * depth`.
- S'aplica `Mathf.Min(heights[i], carved)`: només es pot eliminar terreny, mai afegir-ne.
- L'alçada mínima és sempre `baseHeight + 0.2` (el terreny mai desapareix completament).
- Ambdós tancs es re-col·loquen a la superfície després de la destrucció (`PlaceOnTerrain`).
- `PlaceOnTerrain` també reseteja la velocitat del Rigidbody2D per evitar que el tanc llisqui cap als costats després d'un impacte.

## 3. GetHeightAtX(float worldX)

### Entrada
- `worldX`: coordenada X mundial.

### Sortida
- Coordenada Y mundial de la superfície del terreny a aquella X.

### Comportament
- Converteix la coordenada mundial a índex de columna local.
- Retorna l'alçada de la columna més propera (nearest-column, sense interpolació).
- Retorna `transform.position.y + heights[idx]` (posició Y del transform més l'alçada local).

## 4. Move (TankController)

### Comportament rellevant per al terreny
- La posició X i la posició Y del destí es calculen juntes en un sol pas abans de moure el tanc.
- Això evita que el collider del tanc xoqui temporalment amb terrenys pendents i causi moviment erràtic.
- La velocitat del Rigidbody2D es reseteja a zero després de cada moviment.

## 5. Casos límit

- **Destrucció a la vora del terreny**: les columnes fora del rang es limiten amb `Mathf.Clamp`.
- **Cràters superposats**: s'aplica el mínim entre l'alçada existent i la nova alçada esculpida.
- **Tanc llisca després de destrucció**: `PlaceOnTerrain()` reseteja la velocitat del Rigidbody2D.
- **Moviment en terreny pendent**: X i Y es calculen en un sol pas per evitar clipping.
- **Terreny amb seed = 0**: funciona normalment, `Random.InitState(0)` és un seed vàlid.
- **MapType no reconegut**: utilitza els valors per defecte del desert.
