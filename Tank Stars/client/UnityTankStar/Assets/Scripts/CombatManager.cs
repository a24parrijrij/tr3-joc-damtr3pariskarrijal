// CombatManager — UXML-driven version.
// All visuals live in CombatScreen.uxml / Styles.uss.
// This script only handles game logic and moves the UI elements by changing their style.
using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using NativeWebSocket;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class CombatManager : MonoBehaviour
{
    private const string SocketUrl       = "ws://localhost/game/";
    private const float  DefaultP1X      = 15f;   // % across screen (0-100)
    private const float  DefaultP2X      = 85f;
    private const float  MoveStep        = 2f;    // % per button press
    private const float  TerrainBottomPc = 26f;   // must match .world-terrain height in CSS

    // ── UXML element references ─────────────────────────────────────────────

    // Game world
    private VisualElement background;
    private VisualElement localTank;
    private VisualElement localBarrelPivot;
    private VisualElement enemyTank;
    private VisualElement enemyBarrelPivot;
    private VisualElement projectileEl;
    private VisualElement explosionEl;

    // HUD
    private Label    roomCodeLabel;
    private Label    mapTypeLabel;
    private Label    connectionStatusLabel;
    private Label    turnLabel;
    private Label    combatLogLabel;
    private Label    localPlayerNameLabel;
    private Label    enemyPlayerNameLabel;
    private Label    angleValueLabel;
    private Label    powerValueLabel;
    private VisualElement localHpFill;
    private VisualElement enemyHpFill;
    private VisualElement localHpAnchor;
    private VisualElement enemyHpAnchor;
    private Slider   angleSlider;
    private Slider   powerSlider;
    private Button   fireButton;
    private Button   leaveButton;
    private Button   moveLeftButton;
    private Button   moveRightButton;
    private Label    damagePopup;
    private VisualElement turnBanner;
    private Label    turnBannerText;
    private VisualElement gameOverOverlay;
    private VisualElement gameOverCrown;
    private Label    gameOverTitle;
    private Label    gameOverSubtitle;
    private Label    goLocalHp;
    private Label    goEnemyHp;
    private Label    goDuration;
    private Button   goMenuBtn;

    // ── Game state ──────────────────────────────────────────────────────────

    private WebSocket    websocket;
    private GameManager  gameManager;

    private int   player1Id;
    private int   player2Id;
    private bool  isPlayer1;
    private float player1X = DefaultP1X;
    private float player2X = DefaultP2X;
    private float localX   = DefaultP1X;
    private string activeMapType = "desert";
    private bool  gameFinished;
    private bool  shotInFlight;
    private int   localHp  = 100;
    private int   enemyHp  = 100;

    private Coroutine projectileRoutine;
    private Coroutine explosionRoutine;
    private Coroutine damagePopupRoutine;
    private Coroutine turnBannerRoutine;
    private Coroutine gameOverRoutine;

    // Background image paths per map
    private static readonly System.Collections.Generic.Dictionary<string, string> BgPaths
        = new System.Collections.Generic.Dictionary<string, string>
    {
        { "desert",    "url('../Images/backgrounds/bg_desert.png')" },
        { "snow",      "url('../Images/backgrounds/bg_snow.png')" },
        { "grassland", "url('../Images/backgrounds/bg_grassland.png')" },
        { "canyon",    "url('../Images/backgrounds/bg_canyon.png')" },
        { "volcanic",  "url('../Images/backgrounds/bg_volcanic.png')" },
    };

    // Terrain colours per map
    private static readonly System.Collections.Generic.Dictionary<string, Color> TerrainColors
        = new System.Collections.Generic.Dictionary<string, Color>
    {
        { "desert",    new Color(0.66f, 0.52f, 0.23f) },
        { "snow",      new Color(0.88f, 0.92f, 0.97f) },
        { "grassland", new Color(0.39f, 0.63f, 0.27f) },
        { "canyon",    new Color(0.59f, 0.35f, 0.22f) },
        { "volcanic",  new Color(0.35f, 0.18f, 0.16f) },
    };

    // ── Lifecycle ────────────────────────────────────────────────────────────

    void OnEnable()
    {
        var doc = GetComponent<UIDocument>();
        if (doc == null) { Debug.LogError("CombatManager needs a UIDocument."); enabled = false; return; }

        var root = doc.rootVisualElement;

        // Game world elements
        background       = root.Q<VisualElement>("background");
        localTank        = root.Q<VisualElement>("local-tank");
        localBarrelPivot = root.Q<VisualElement>("local-barrel-pivot");
        enemyTank        = root.Q<VisualElement>("enemy-tank");
        enemyBarrelPivot = root.Q<VisualElement>("enemy-barrel-pivot");
        projectileEl     = root.Q<VisualElement>("projectile");
        explosionEl      = root.Q<VisualElement>("explosion");

        // HUD elements
        roomCodeLabel         = root.Q<Label>("room-code-label");
        mapTypeLabel          = root.Q<Label>("map-type-label");
        connectionStatusLabel = root.Q<Label>("connection-status-label");
        turnLabel             = root.Q<Label>("turn-label");
        combatLogLabel        = root.Q<Label>("combat-log-label");
        localPlayerNameLabel  = root.Q<Label>("local-player-name");
        enemyPlayerNameLabel  = root.Q<Label>("enemy-player-name");
        angleValueLabel       = root.Q<Label>("angle-value-label");
        powerValueLabel       = root.Q<Label>("power-value-label");
        localHpFill           = root.Q<VisualElement>("local-hp-fill");
        enemyHpFill           = root.Q<VisualElement>("enemy-hp-fill");
        localHpAnchor         = root.Q<VisualElement>("local-hp-anchor");
        enemyHpAnchor         = root.Q<VisualElement>("enemy-hp-anchor");
        angleSlider           = root.Q<Slider>("angle-slider");
        powerSlider           = root.Q<Slider>("power-slider");
        fireButton            = root.Q<Button>("fire-btn");
        leaveButton           = root.Q<Button>("leave-btn");
        moveLeftButton        = root.Q<Button>("move-left-btn");
        moveRightButton       = root.Q<Button>("move-right-btn");
        damagePopup           = root.Q<Label>("damage-popup");
        turnBanner            = root.Q<VisualElement>("turn-banner");
        turnBannerText        = root.Q<Label>("turn-banner-text");
        gameOverOverlay       = root.Q<VisualElement>("game-over-overlay");
        gameOverCrown         = root.Q<VisualElement>("game-over-crown");
        gameOverTitle         = root.Q<Label>("game-over-title");
        gameOverSubtitle      = root.Q<Label>("game-over-subtitle");
        goLocalHp             = root.Q<Label>("go-local-hp");
        goEnemyHp             = root.Q<Label>("go-enemy-hp");
        goDuration            = root.Q<Label>("go-duration");
        goMenuBtn             = root.Q<Button>("go-menu-btn");

        if (fireButton == null || angleSlider == null || localTank == null)
        {
            Debug.LogError("CombatManager: missing UI elements. Check CombatScreen.uxml.");
            enabled = false;
            return;
        }

        gameManager = GameManager.EnsureInstance();

        // Initial HUD text
        if (roomCodeLabel != null)
            roomCodeLabel.text = string.IsNullOrEmpty(gameManager.roomCode) ? "Room ------" : "Room " + gameManager.roomCode;
        if (localPlayerNameLabel != null)
            localPlayerNameLabel.text = string.IsNullOrEmpty(gameManager.username) ? "You" : gameManager.username;
        if (enemyPlayerNameLabel != null)
            enemyPlayerNameLabel.text = "Opponent";
        if (connectionStatusLabel != null) connectionStatusLabel.text = "Connecting...";
        if (turnLabel != null)             turnLabel.text = "Waiting...";
        if (combatLogLabel != null)        combatLogLabel.text = "";
        if (angleSlider != null)           angleSlider.value = 45f;
        if (powerSlider != null)           powerSlider.value = 75f;
        if (turnBanner != null)            turnBanner.AddToClassList("hidden");
        if (gameOverOverlay != null)       gameOverOverlay.AddToClassList("hidden");
        if (damagePopup != null)           damagePopup.AddToClassList("hidden");

        SetHpBar(localHpFill, 100);
        SetHpBar(enemyHpFill, 100);
        UpdateSliderLabels();
        SetFireEnabled(false);
        SetMoveEnabled(false);

        // Apply starting map
        activeMapType = NormalizeMap(gameManager.mapType);
        ApplyMap(activeMapType);
        PlaceTanks();

        // Register callbacks
        if (angleSlider != null) angleSlider.RegisterValueChangedCallback(_ => { UpdateSliderLabels(); ApplyBarrelAngle(angleSlider.value); });
        if (fireButton  != null) fireButton.clicked  += OnFireClicked;
        if (leaveButton != null) leaveButton.clicked += OnLeaveClicked;
        if (moveLeftButton  != null) moveLeftButton.clicked  += OnMoveLeft;
        if (moveRightButton != null) moveRightButton.clicked += OnMoveRight;
        if (goMenuBtn != null) goMenuBtn.clicked += OnLeaveClicked;

        if (gameManager.gameId > 0 && gameManager.playerId > 0)
            _ = ConnectWebSocket();
        else if (connectionStatusLabel != null)
            connectionStatusLabel.text = "No game context";
    }

    void OnDisable()
    {
        if (fireButton  != null) fireButton.clicked  -= OnFireClicked;
        if (leaveButton != null) leaveButton.clicked -= OnLeaveClicked;
        if (moveLeftButton  != null) moveLeftButton.clicked  -= OnMoveLeft;
        if (moveRightButton != null) moveRightButton.clicked -= OnMoveRight;
        if (goMenuBtn != null) goMenuBtn.clicked -= OnLeaveClicked;

        StopAllCoroutines();
        _ = CloseWebSocket();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif
    }

    // ── Map visuals ──────────────────────────────────────────────────────────

    private void ApplyMap(string map)
    {
        // Terrain colour
        Color terrainColor;
        if (TerrainColors.TryGetValue(map, out terrainColor))
        {
            var terrain = GetComponent<UIDocument>()?.rootVisualElement.Q<VisualElement>("terrain");
            if (terrain != null) terrain.style.backgroundColor = terrainColor;
        }

        // Background image — load via Resources and assign directly
        string path = "Images/backgrounds/bg_" + map;
        var tex = Resources.Load<Texture2D>(path);
        if (tex != null && background != null)
            background.style.backgroundImage = new StyleBackground(tex);

        if (mapTypeLabel != null)
            mapTypeLabel.text = char.ToUpper(map[0]) + map.Substring(1);
    }

    // ── Tank placement (% → CSS left/bottom) ────────────────────────────────

    private void PlaceTanks()
    {
        // local tank
        float lx = (player1Id == 0) ? DefaultP1X : (isPlayer1 ? player1X : player2X);
        float ex = (player1Id == 0) ? DefaultP2X : (isPlayer1 ? player2X : player1X);
        PlaceTank(localTank, lx, facingRight: lx <= ex);
        PlaceTank(enemyTank, ex, facingRight: ex < lx);
        ApplyBarrelAngle(angleSlider != null ? angleSlider.value : 45f);

        // Position HP anchors above each tank
        PositionHpAnchor(localHpAnchor, localTank);
        PositionHpAnchor(enemyHpAnchor, enemyTank);
    }

    // xPercent is 0-100 across the screen width.
    // We use CSS left % for horizontal position.
    private void PlaceTank(VisualElement tank, float xPercent, bool facingRight)
    {
        if (tank == null) return;
        // Offset by half the tank width (~6%) so the centre lands at xPercent
        tank.style.left  = Length.Percent(xPercent - 6f);
        tank.style.scale = new StyleScale(new Scale(new Vector3(facingRight ? 1f : -1f, 1f, 1f)));
    }

    private void ApplyBarrelAngle(float angle)
    {
        // Local barrel (always positive — CSS rotate is clockwise for positive)
        if (localBarrelPivot != null)
            localBarrelPivot.style.rotate = new StyleRotate(new Rotate(new Angle(-angle, AngleUnit.Degree)));

        // Enemy barrel — mirrored via scale:-1 in CSS, so same angle works
        if (enemyBarrelPivot != null)
            enemyBarrelPivot.style.rotate = new StyleRotate(new Rotate(new Angle(-angle, AngleUnit.Degree)));
    }

    private void PositionHpAnchor(VisualElement anchor, VisualElement tank)
    {
        if (anchor == null || tank == null) return;
        anchor.style.display = DisplayStyle.Flex;
        anchor.style.left    = tank.style.left;
        anchor.style.bottom  = Length.Percent(TerrainBottomPc + 12f);
    }

    // ── Movement ─────────────────────────────────────────────────────────────

    private void OnMoveLeft()
    {
        if (!CanMove()) return;
        float min = isPlayer1 ? 3f : 58f;
        float max = isPlayer1 ? 42f : 97f;
        float newX = Mathf.Clamp(localX - MoveStep, min, max);
        ApplyLocalMove(newX);
    }

    private void OnMoveRight()
    {
        if (!CanMove()) return;
        float min = isPlayer1 ? 3f : 58f;
        float max = isPlayer1 ? 42f : 97f;
        float newX = Mathf.Clamp(localX + MoveStep, min, max);
        ApplyLocalMove(newX);
    }

    private void ApplyLocalMove(float newX)
    {
        localX = newX;
        if (isPlayer1) player1X = newX; else player2X = newX;
        PlaceTanks();
        _ = SendJson(new MoveTankMessage { type = "move_tank", gameId = gameManager.gameId, playerId = gameManager.playerId, newX = newX });
    }

    // ── Fire ─────────────────────────────────────────────────────────────────

    private async void OnFireClicked()
    {
        if (!CanFire()) return;

        int angle = Mathf.RoundToInt(angleSlider != null ? angleSlider.value : 45f);
        int power = Mathf.RoundToInt(powerSlider != null ? powerSlider.value : 75f);

        if (combatLogLabel != null) combatLogLabel.text = "Shot fired!";

        shotInFlight = true;
        SetFireEnabled(false);
        SetMoveEnabled(false);

        float landingX = PredictLandingX(localX, angle, power, isPlayer1);
        StartProjectileArc(localTank, landingX);

        await SendJson(new FireShotMessage { type = "fire_shot", gameId = gameManager.gameId, playerId = gameManager.playerId, angle = angle, power = power });
    }

    // Simple arc: move projectile from tank position to landing position
    private void StartProjectileArc(VisualElement fromTank, float toLandingX)
    {
        StopTracked(ref projectileRoutine);
        projectileRoutine = StartCoroutine(AnimateProjectile(fromTank, toLandingX));
    }

    private IEnumerator AnimateProjectile(VisualElement fromTank, float toLandingX)
    {
        if (projectileEl == null) yield break;

        float startX = float.IsNaN(fromTank.style.left.value.value) ? 15f : fromTank.style.left.value.value + 6f;
        float startY = TerrainBottomPc + 12f;
        float endX   = toLandingX;
        float endY   = TerrainBottomPc + 2f;
        float arc    = 25f; // % of screen height for arc peak

        projectileEl.RemoveFromClassList("hidden");
        float duration = 0.9f;
        float elapsed  = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float x = Mathf.Lerp(startX, endX, t);
            float y = Mathf.Lerp(startY, endY, t) + arc * Mathf.Sin(Mathf.PI * t);
            projectileEl.style.left   = Length.Percent(x);
            projectileEl.style.bottom = Length.Percent(y);
            yield return null;
        }

        projectileEl.AddToClassList("hidden");
        shotInFlight = false;
        SetFireEnabled(IsMyTurn() && !gameFinished);
        SetMoveEnabled(IsMyTurn() && !gameFinished);
        projectileRoutine = null;
    }

    // ── Explosion ────────────────────────────────────────────────────────────

    private void PlayExplosion(float xPercent)
    {
        StopTracked(ref explosionRoutine);
        explosionRoutine = StartCoroutine(AnimateExplosion(xPercent));
    }

    private IEnumerator AnimateExplosion(float xPercent)
    {
        if (explosionEl == null) yield break;

        string[] frames = {
            "Images/vfx/explosion_01", "Images/vfx/explosion_02",
            "Images/vfx/explosion_03", "Images/vfx/explosion_04",
            "Images/vfx/explosion_05", "Images/vfx/explosion_06",
            "Images/vfx/explosion_07", "Images/vfx/explosion_08",
        };

        explosionEl.style.left   = Length.Percent(xPercent - 4f);
        explosionEl.style.bottom = Length.Percent(TerrainBottomPc - 2f);
        explosionEl.RemoveFromClassList("hidden");

        foreach (string path in frames)
        {
            var tex = Resources.Load<Texture2D>(path);
            if (tex != null) explosionEl.style.backgroundImage = new StyleBackground(tex);
            yield return new WaitForSeconds(0.07f);
        }

        explosionEl.AddToClassList("hidden");
        explosionRoutine = null;
    }

    // ── WebSocket ────────────────────────────────────────────────────────────

    private async Task ConnectWebSocket()
    {
        websocket = new WebSocket(SocketUrl);
        websocket.OnOpen    += () => { if (connectionStatusLabel != null) connectionStatusLabel.text = "Joining..."; _ = SendJoinGame(); };
        websocket.OnMessage += bytes => HandleMessage(Encoding.UTF8.GetString(bytes));
        websocket.OnError   += err  => { if (connectionStatusLabel != null) connectionStatusLabel.text = "Error"; shotInFlight = false; SetFireEnabled(false); };
        websocket.OnClose   += _    => { if (!gameFinished && connectionStatusLabel != null) connectionStatusLabel.text = "Disconnected"; };

        try { await websocket.Connect(); }
        catch (Exception ex) { if (connectionStatusLabel != null) connectionStatusLabel.text = "Cannot connect: " + ex.Message; }
    }

    private async Task CloseWebSocket()
    {
        if (websocket == null) return;
        if (websocket.State == WebSocketState.Open || websocket.State == WebSocketState.Connecting)
            await websocket.Close();
        websocket = null;
    }

    private async Task SendJoinGame()
    {
        await SendJson(new JoinGameMessage { type = "join_game", gameId = gameManager.gameId, playerId = gameManager.playerId });
    }

    private async Task SendJson(object payload)
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
        { if (connectionStatusLabel != null) connectionStatusLabel.text = "Not connected"; return; }
        await websocket.SendText(JsonUtility.ToJson(payload));
    }

    // ── Message handling ─────────────────────────────────────────────────────

    private void HandleMessage(string json)
    {
        SocketMessage msg = JsonUtility.FromJson<SocketMessage>(json);
        if (msg == null || string.IsNullOrEmpty(msg.type)) return;

        switch (msg.type)
        {
            case "connected":
                if (connectionStatusLabel != null) connectionStatusLabel.text = msg.message;
                break;

            case "joined_waiting":
                ApplyMapFromMsg(msg);
                if (connectionStatusLabel != null) connectionStatusLabel.text = "Waiting...";
                if (combatLogLabel != null) combatLogLabel.text = "Waiting for opponent";
                break;

            case "positions_update":
                if (msg.player1X > 0) player1X = msg.player1X;
                if (msg.player2X > 0) player2X = msg.player2X;
                PlaceTanks();
                break;

            case "game_start":
            case "game_update":
            case "game_end":
                ApplyGameState(msg);
                break;

            case "terrain_destroyed":
                PlayExplosion(msg.impactX);
                break;

            case "error":
                if (connectionStatusLabel != null) connectionStatusLabel.text = "Error";
                if (combatLogLabel != null) combatLogLabel.text = msg.message;
                shotInFlight = false;
                SetFireEnabled(IsMyTurn() && !gameFinished);
                SetMoveEnabled(IsMyTurn() && !gameFinished);
                break;
        }
    }

    private void ApplyGameState(SocketMessage msg)
    {
        ApplyMapFromMsg(msg);

        player1Id = msg.player1Id;
        player2Id = msg.player2Id;
        isPlayer1 = gameManager.playerId == player1Id;

        if (msg.player1X > 0) player1X = msg.player1X;
        if (msg.player2X > 0) player2X = msg.player2X;
        localX = isPlayer1 ? player1X : player2X;

        int newLocalHp = isPlayer1 ? msg.player1Hp : msg.player2Hp;
        int newEnemyHp = isPlayer1 ? msg.player2Hp : msg.player1Hp;
        SetHpBar(localHpFill, newLocalHp);
        SetHpBar(enemyHpFill, newEnemyHp);
        localHp = newLocalHp;
        enemyHp = newEnemyHp;

        if (localPlayerNameLabel != null)
            localPlayerNameLabel.text = string.IsNullOrEmpty(gameManager.username) ? "You" : gameManager.username;
        if (!string.IsNullOrEmpty(msg.roomCode) && roomCodeLabel != null)
            roomCodeLabel.text = "Room " + msg.roomCode;

        gameFinished = msg.type == "game_end" || msg.status == "finished";
        bool myTurn  = msg.currentTurnPlayerId == gameManager.playerId;

        if (gameFinished)
        {
            if (connectionStatusLabel != null) connectionStatusLabel.text = "Finished";
            if (turnLabel != null) turnLabel.text = msg.winnerPlayerId == gameManager.playerId ? "VICTORY!" : "DEFEAT";
            ShowGameOver(msg);
        }
        else
        {
            if (connectionStatusLabel != null) connectionStatusLabel.text = "Live";
            if (turnLabel != null) turnLabel.text = myTurn ? "Your turn" : "Enemy turn";
            if (msg.type == "game_start" || myTurn) ShowTurnBanner(myTurn ? "YOUR TURN" : "ENEMY TURN");
        }

        if (msg.type == "game_update" && msg.lastAttackerPlayerId != 0 && msg.lastDamage > 0)
            ShowDamagePopup(msg.lastDamage, msg.lastLandingX);

        PlaceTanks();
        SetFireEnabled(myTurn && !gameFinished && !shotInFlight);
        SetMoveEnabled(myTurn && !gameFinished && !shotInFlight);
        if (combatLogLabel != null) combatLogLabel.text = BuildLog(msg);
    }

    private void ApplyMapFromMsg(SocketMessage msg)
    {
        if (!string.IsNullOrEmpty(msg.mapType))
        {
            activeMapType = NormalizeMap(msg.mapType);
            gameManager.mapType = activeMapType;
            ApplyMap(activeMapType);
        }
    }

    // ── Game over ────────────────────────────────────────────────────────────

    private void ShowGameOver(SocketMessage msg)
    {
        if (gameOverOverlay == null) return;
        bool won = msg.winnerPlayerId == gameManager.playerId;

        if (gameOverTitle != null)
        {
            gameOverTitle.text = won ? "VICTORY!" : "DEFEAT";
            gameOverTitle.RemoveFromClassList("game-over-title-victory");
            gameOverTitle.RemoveFromClassList("game-over-title-defeat");
            gameOverTitle.AddToClassList(won ? "game-over-title-victory" : "game-over-title-defeat");
        }
        if (gameOverSubtitle != null)
            gameOverSubtitle.text = won ? "You destroyed the enemy tank!" : "Your tank was destroyed...";
        if (goLocalHp  != null) goLocalHp.text  = localHp.ToString();
        if (goEnemyHp  != null) goEnemyHp.text  = enemyHp.ToString();
        if (goDuration != null) goDuration.text  = msg.durationSeconds + "s";

        StopTracked(ref gameOverRoutine);
        gameOverRoutine = StartCoroutine(ShowGameOverDelay(1.5f));
    }

    private IEnumerator ShowGameOverDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (gameOverOverlay != null) gameOverOverlay.RemoveFromClassList("hidden");
        gameOverRoutine = null;
    }

    // ── Turn banner ──────────────────────────────────────────────────────────

    private void ShowTurnBanner(string text)
    {
        if (turnBanner == null || turnBannerText == null) return;
        StopTracked(ref turnBannerRoutine);
        turnBannerRoutine = StartCoroutine(AnimateTurnBanner(text));
    }

    private IEnumerator AnimateTurnBanner(string text)
    {
        turnBannerText.text = text;
        turnBanner.RemoveFromClassList("hidden");
        yield return new WaitForSeconds(1.2f);
        turnBanner.AddToClassList("hidden");
        turnBannerRoutine = null;
    }

    // ── Damage popup ─────────────────────────────────────────────────────────

    private void ShowDamagePopup(int damage, float xPercent)
    {
        if (damagePopup == null) return;
        StopTracked(ref damagePopupRoutine);
        damagePopupRoutine = StartCoroutine(AnimateDamagePopup(damage, xPercent));
    }

    private IEnumerator AnimateDamagePopup(int damage, float xPercent)
    {
        damagePopup.text = "-" + damage;
        damagePopup.style.left   = Length.Percent(xPercent);
        damagePopup.style.bottom = Length.Percent(TerrainBottomPc + 18f);
        damagePopup.style.opacity = 1f;
        damagePopup.RemoveFromClassList("hidden");

        float elapsed = 0f;
        while (elapsed < 0.8f)
        {
            elapsed += Time.deltaTime;
            damagePopup.style.opacity = 1f - (elapsed / 0.8f);
            yield return null;
        }

        damagePopup.AddToClassList("hidden");
        damagePopupRoutine = null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SetHpBar(VisualElement fill, int hp)
    {
        if (fill == null) return;
        fill.style.width = Length.Percent(Mathf.Clamp(hp, 0, 100));
    }

    private void UpdateSliderLabels()
    {
        if (angleValueLabel != null && angleSlider != null)
            angleValueLabel.text = Mathf.RoundToInt(angleSlider.value).ToString();
        if (powerValueLabel != null && powerSlider != null)
            powerValueLabel.text = Mathf.RoundToInt(powerSlider.value).ToString();
    }

    private void SetFireEnabled(bool on)
    {
        if (fireButton == null) return;
        fireButton.SetEnabled(on && websocket != null && websocket.State == WebSocketState.Open && !gameFinished && !shotInFlight);
    }

    private void SetMoveEnabled(bool on)
    {
        bool ok = on && !shotInFlight;
        if (moveLeftButton  != null) moveLeftButton.SetEnabled(ok);
        if (moveRightButton != null) moveRightButton.SetEnabled(ok);
    }

    private bool IsMyTurn() => turnLabel != null && turnLabel.text == "Your turn";

    private bool CanFire()  => !gameFinished && !shotInFlight &&
                                websocket != null && websocket.State == WebSocketState.Open &&
                                player1Id != 0 && IsMyTurn();

    private bool CanMove()  => !gameFinished && !shotInFlight &&
                                websocket != null && websocket.State == WebSocketState.Open &&
                                player1Id != 0 && IsMyTurn();

    private float PredictLandingX(float attackerX, float angle, float power, bool p1)
    {
        float distance  = (power / 100f) * 80f * Mathf.Sin(2f * angle * Mathf.Deg2Rad);
        float direction = p1 ? 1f : -1f;
        return Mathf.Clamp(attackerX + distance * direction, 0f, 100f);
    }

    private string NormalizeMap(string m)
    {
        string[] valid = { "desert", "snow", "grassland", "canyon", "volcanic" };
        if (!string.IsNullOrEmpty(m))
            foreach (string v in valid) if (v == m.ToLower().Trim()) return v;
        return "desert";
    }

    private string BuildLog(SocketMessage msg)
    {
        if (gameFinished)
            return (msg.winnerPlayerId == gameManager.playerId ? "Victory" : "Defeat") + " — " + msg.durationSeconds + "s";
        if (msg.type == "game_start")
            return msg.currentTurnPlayerId == gameManager.playerId ? "You go first!" : "Opponent starts";
        if (msg.lastAttackerPlayerId == 0) return "";
        string who = msg.lastAttackerPlayerId == gameManager.playerId ? "You" : "Enemy";
        string res = msg.lastShotResult == "direct_hit" ? "direct hit!" : msg.lastShotResult == "near_hit" ? "near hit" : "miss";
        return $"{who}: {msg.lastAngle}° / {msg.lastPower}%  {res}  ({msg.lastDamage} dmg)";
    }

    private void OnLeaveClicked()
    {
        gameManager.ResetMatchState();
        SceneManager.LoadScene("MenuScene");
    }

    private void StopTracked(ref Coroutine c) { if (c != null) { StopCoroutine(c); c = null; } }
}

// ── Network message types ────────────────────────────────────────────────────

[Serializable] public class JoinGameMessage  { public string type; public int gameId; public int playerId; }
[Serializable] public class FireShotMessage  { public string type; public int gameId; public int playerId; public int angle; public int power; }
[Serializable] public class MoveTankMessage  { public string type; public int gameId; public int playerId; public float newX; }

[Serializable]
public class SocketMessage
{
    public string type;
    public string message;
    public int    gameId;
    public string roomCode;
    public string mapType;
    public int    player1Id;
    public int    player2Id;
    public int    player1Hp;
    public int    player2Hp;
    public float  player1X;
    public float  player2X;
    public int    currentTurnPlayerId;
    public int    winnerPlayerId;
    public string status;
    public string lastShotResult;
    public int    lastDamage;
    public float  lastLandingX;
    public int    lastAttackerPlayerId;
    public int    lastAngle;
    public int    lastPower;
    public float  impactX;
    public float  impactY;
    public float  radius;
    public int    durationSeconds;
    public int[]  terrainHeights;
    public int    terrainEventId;
}
