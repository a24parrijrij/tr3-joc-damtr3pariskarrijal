// CombatManager — Gestiona el combat multijugador vs player amb WebSocket
// Utilitza GameObjects per als tancs, terreny i projectils. UI amb UI Toolkit.
using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using NativeWebSocket;

public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance;

    [Header("References")]
    public TerrainGenerator terrain;
    public TankController player1Tank;
    public TankController player2Tank;
    public GameObject projectilePrefab;
    public GameObject explosionPrefab;
    public Camera mainCamera;

    // URL del WebSocket — apunta al servidor de joc
    private const string SocketUrl = "ws://65.108.158.166/game/";

    // Elements UXML
    private VisualElement root;
    private Label turnLabel, combatLogLabel, angleValueLabel, powerValueLabel;
    private Label connectionStatusLabel, roomCodeLabel, mapTypeLabel;
    private Label localPlayerNameLabel, enemyPlayerNameLabel, damagePopup;
    private VisualElement localHpFill, enemyHpFill;
    private Label localHpNum, enemyHpNum;
    private Slider angleSlider, powerSlider;
    private Button fireButton, leaveButton, moveLeftButton, moveRightButton;
    private VisualElement turnBanner;
    private Label turnBannerText;
    private VisualElement gameOverOverlay;
    private Label gameOverTitle, gameOverSubtitle, goLocalHp, goEnemyHp, goDuration;
    private Label turnTimerLabel;

    private GameObject backgroundObj;

    // Temporitzador de torn
    private float turnTimeLimit = 15f;
    private float _turnTimeRemaining = 0f;
    private bool _timerActive = false;

    // Crater pendent del servidor (terrain_destroyed arriba abans de game_update)
    private float pendingLandingWorldX;
    private int[] pendingTerrainHeights;

    // WebSocket i estat del joc
    private WebSocket websocket;
    private GameManager gameManager;
    private int player1Id, player2Id;
    private bool isPlayer1;
    private float player1X = 15f, player2X = 85f;
    private string activeMapType = "desert";
    private bool gameStarted;
    private bool gameFinished;
    private bool shotInFlight;
    private int localHp = 100, enemyHp = 100;
    private int currentTurnPlayerId;
    private bool uiBound;
    private Coroutine projectileRoutine, shakeRoutine;

    private const float MultiplayerShotSpeedScale = 0.12f;
    private const float MultiplayerPhysicsGravity = 9.81f;
    // Match the server's horizontal shot scale so replayed bullets do not visually
    // pass through tanks when the authoritative hit test says they missed.
    private const float MultiplayerWorldToPercentX =
        (80f * MultiplayerPhysicsGravity) / ((MultiplayerShotSpeedScale * 100f) * (MultiplayerShotSpeedScale * 100f));
    private const float MultiplayerShotStepSeconds = 1f / 120f;
    private const float MultiplayerMaxShotTime = 5f;

    // Propietats públiques (CombatInput les utilitza)
    public TankController LocalTank  => isPlayer1 ? player1Tank : player2Tank;
    public TankController RemoteTank => isPlayer1 ? player2Tank : player1Tank;

    void Awake() => Instance = this;
    void Start() => StartCoroutine(InitUI());

    void OnDisable()
    {
        UnbindButtons();
        StopAllCoroutines();
        _ = CloseWebSocket();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif
        if (!_timerActive || !uiBound) return;

        _turnTimeRemaining -= Time.deltaTime;

        int secs = Mathf.CeilToInt(Mathf.Max(0f, _turnTimeRemaining));
        if (turnTimerLabel != null)
        {
            turnTimerLabel.text = secs.ToString();
            if (secs <= 5) turnTimerLabel.AddToClassList("urgent");
            else           turnTimerLabel.RemoveFromClassList("urgent");
        }

        if (_turnTimeRemaining <= 0f)
        {
            _timerActive = false;
            if (turnTimerLabel != null) turnTimerLabel.AddToClassList("hidden");
            if (combatLogLabel != null) combatLogLabel.text = "Temps esgotat! Tir automàtic!";
            float angle = angleSlider != null ? angleSlider.value : 45f;
            float power = powerSlider != null ? powerSlider.value : 75f;
            FireShot(angle, power);
        }
    }

    // ── Inicialització UI ──────────────────────────────────────────────────

    private IEnumerator InitUI()
    {
        yield return null;

        var uiDoc = GetComponent<UIDocument>();
        if (uiDoc == null) { Debug.LogError("[CombatManager] Cal UIDocument!"); yield break; }
        root = uiDoc.rootVisualElement;
        if (root == null) { Debug.LogError("[CombatManager] rootVisualElement és null!"); yield break; }

        turnLabel             = root.Q<Label>("turn-label");
        combatLogLabel        = root.Q<Label>("combat-log-label");
        angleValueLabel       = root.Q<Label>("angle-value-label");
        powerValueLabel       = root.Q<Label>("power-value-label");
        localHpFill           = root.Q<VisualElement>("local-hp-fill");
        enemyHpFill           = root.Q<VisualElement>("enemy-hp-fill");
        localHpNum            = root.Q<Label>("local-hp-num");
        enemyHpNum            = root.Q<Label>("enemy-hp-num");
        angleSlider           = root.Q<Slider>("angle-slider");
        powerSlider           = root.Q<Slider>("power-slider");
        fireButton            = root.Q<Button>("fire-btn");
        leaveButton           = root.Q<Button>("leave-btn");
        moveLeftButton        = root.Q<Button>("move-left-btn");
        moveRightButton       = root.Q<Button>("move-right-btn");
        turnBanner            = root.Q<VisualElement>("turn-banner");
        turnBannerText        = root.Q<Label>("turn-banner-text");
        gameOverOverlay       = root.Q<VisualElement>("game-over-overlay");
        gameOverTitle         = root.Q<Label>("game-over-title");
        gameOverSubtitle      = root.Q<Label>("game-over-subtitle");
        goLocalHp             = root.Q<Label>("go-local-hp");
        goEnemyHp             = root.Q<Label>("go-enemy-hp");
        goDuration            = root.Q<Label>("go-duration");
        connectionStatusLabel = root.Q<Label>("connection-status-label");
        roomCodeLabel         = root.Q<Label>("room-code-label");
        mapTypeLabel          = root.Q<Label>("map-type-label");
        localPlayerNameLabel  = root.Q<Label>("local-player-name");
        enemyPlayerNameLabel  = root.Q<Label>("enemy-player-name");
        damagePopup           = root.Q<Label>("damage-popup");
        turnTimerLabel        = root.Q<Label>("turn-timer-label");

        gameManager = GameManager.EnsureInstance();

        if (angleSlider != null)  angleSlider.value  = 45f;
        if (powerSlider != null)  powerSlider.value  = 75f;
        if (turnBanner      != null) turnBanner.AddToClassList("hidden");
        if (gameOverOverlay != null) gameOverOverlay.AddToClassList("hidden");
        if (damagePopup     != null) damagePopup.AddToClassList("hidden");
        if (connectionStatusLabel != null) connectionStatusLabel.text = "Connectant...";
        if (roomCodeLabel != null)
            roomCodeLabel.text = "Sala " + (string.IsNullOrEmpty(gameManager.roomCode) ? "------" : gameManager.roomCode);
        if (localPlayerNameLabel != null)
            localPlayerNameLabel.text = string.IsNullOrEmpty(gameManager.username) ? "Tu" : gameManager.username;
        if (enemyPlayerNameLabel != null) enemyPlayerNameLabel.text = "Oponent";
        if (mapTypeLabel != null)
        {
            string mt = gameManager.mapType ?? "desert";
            mapTypeLabel.text = char.ToUpper(mt[0]) + mt.Substring(1);
        }

        SetHpBar(localHpFill, 100);
        SetHpBar(enemyHpFill, 100);
        if (localHpNum != null) localHpNum.text = "100";
        if (enemyHpNum != null) enemyHpNum.text = "100";
        UpdateSliderLabels();
        SetFireEnabled(false);
        SetMoveEnabled(false);

        SetupWorldBackground(gameManager.mapType ?? "desert");

        if (player1Tank != null) player1Tank.gameObject.SetActive(false);
        if (player2Tank != null) player2Tank.gameObject.SetActive(false);

        if (angleSlider    != null) angleSlider.RegisterValueChangedCallback(_ => UpdateSliderLabels());
        if (powerSlider    != null) powerSlider.RegisterValueChangedCallback(_ => UpdateSliderLabels());
        if (fireButton     != null) fireButton.clicked     += OnFireClicked;
        if (leaveButton    != null) leaveButton.clicked    += OnLeaveClicked;
        if (moveLeftButton  != null) moveLeftButton.clicked  += OnMoveLeft;
        if (moveRightButton != null) moveRightButton.clicked += OnMoveRight;
        var goMenuBtn = root.Q<Button>("go-menu-btn");
        if (goMenuBtn != null) goMenuBtn.clicked += OnLeaveClicked;

        uiBound = true;

        if (gameManager.gameId > 0 && gameManager.playerId > 0)
            _ = ConnectWebSocket();
        else if (connectionStatusLabel != null)
            connectionStatusLabel.text = "Sense context de joc";
    }

    // ── WebSocket ──────────────────────────────────────────────────────────

    private async Task ConnectWebSocket()
    {
        try
        {
            websocket = new WebSocket(SocketUrl);

            websocket.OnOpen += () =>
            {
                if (connectionStatusLabel != null) connectionStatusLabel.text = "Unint-se...";
                _ = SendJoinGame();
            };

            websocket.OnMessage += bytes => HandleMessage(Encoding.UTF8.GetString(bytes));

            websocket.OnError += err =>
            {
                Debug.LogError("[CombatManager] WS error: " + err);
                if (connectionStatusLabel != null) connectionStatusLabel.text = "Error de connexió";
            };

            websocket.OnClose += _ =>
            {
                if (!gameFinished && connectionStatusLabel != null)
                    connectionStatusLabel.text = "Desconnectat";
            };

            await websocket.Connect();
        }
        catch (Exception ex)
        {
            Debug.LogError("[CombatManager] Error de connexió: " + ex.Message);
            if (connectionStatusLabel != null) connectionStatusLabel.text = "No connectat";
        }
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
        await SendJson(new JoinGameMessage
        {
            type     = "join_game",
            gameId   = gameManager.gameId,
            playerId = gameManager.playerId
        });
    }

    private async Task SendJson(object payload)
    {
        if (websocket == null || websocket.State != WebSocketState.Open) return;
        string json = JsonUtility.ToJson(payload);
        Debug.Log("[CombatManager] WS enviat: " + json);
        await websocket.SendText(json);
    }

    // ── Gestió de missatges del servidor ────────────────────────────────────

    private void HandleMessage(string json)
    {
        Debug.Log("[CombatManager] WS rebut: " + json);

        var msg = JsonUtility.FromJson<SocketMessage>(json);
        if (msg == null || string.IsNullOrEmpty(msg.type)) return;

        switch (msg.type)
        {
            case "connected":
                if (connectionStatusLabel != null) connectionStatusLabel.text = msg.message;
                break;

            case "joined_waiting":
                activeMapType = NormalizeMap(msg.mapType);
                if (connectionStatusLabel != null) connectionStatusLabel.text = "Esperant oponent...";
                if (combatLogLabel != null) combatLogLabel.text = "Esperant jugador 2...";
                break;

            case "game_start":
                HandleGameStart(msg);
                break;

            case "game_update":
                HandleGameUpdate(msg);
                break;

            case "game_end":
                HandleGameEnd(msg);
                break;

            case "terrain_destroyed":
                // Store crater info — applied at the END of the bullet animation
                // so the mesh isn't modified before the projectile reaches the ground.
                // impactX from server is 0-100 percent, convert to world space.
                if (terrain != null && terrain.width > 0)
                {
                    pendingLandingWorldX = (msg.impactX / 100f) * terrain.width - terrain.width / 2f;
                    pendingTerrainHeights = msg.terrainHeights;
                    Debug.Log($"[TERRAIN] terrain_destroyed queued: impactX%={msg.impactX} worldX={pendingLandingWorldX}");
                }
                break;

            case "positions_update":
                if (msg.player1X > 0) player1X = msg.player1X;
                if (msg.player2X > 0) player2X = msg.player2X;
                SyncRemoteTankPosition();
                break;

            case "error":
                if (combatLogLabel != null) combatLogLabel.text = msg.message;
                shotInFlight = false;
                SetFireEnabled(IsMyTurn());
                SetMoveEnabled(IsMyTurn());
                break;
        }
    }

    // ── game_start ─────────────────────────────────────────────────────────

    private void HandleGameStart(SocketMessage msg)
    {
        player1Id = msg.player1Id;
        player2Id = msg.player2Id;
        isPlayer1 = gameManager.playerId == player1Id;

        if (msg.player1X > 0) player1X = msg.player1X;
        if (msg.player2X > 0) player2X = msg.player2X;

        if (!string.IsNullOrEmpty(msg.mapType))
        {
            activeMapType = NormalizeMap(msg.mapType);
            gameManager.mapType = activeMapType;
            if (mapTypeLabel != null)
                mapTypeLabel.text = char.ToUpper(activeMapType[0]) + activeMapType.Substring(1);
        }

        SetupWorldBackground(activeMapType);

        if (terrain != null)
        {
            var cam = mainCamera != null ? mainCamera : Camera.main;
            if (cam != null)
            {
                terrain.width = cam.orthographicSize * 2f * cam.aspect;
                float halfW = terrain.width / 2f - 0.5f;
                if (player1Tank != null) player1Tank.worldBoundsX = halfW;
                if (player2Tank != null) player2Tank.worldBoundsX = halfW;
            }

            if (msg.terrainHeights != null && msg.terrainHeights.Length > 0)
            {
                Debug.Log($"[CombatManager] game_start: terreny servidor ({msg.terrainHeights.Length} cols), map={activeMapType}");
                terrain.LoadServerHeights(msg.terrainHeights, activeMapType);
            }
            else
            {
                int seed = msg.gameId > 0 ? msg.gameId : gameManager.gameId;
                Debug.Log($"[CombatManager] game_start: seed fallback seed={seed}, map={activeMapType}");
                terrain.GenerateTerrain(seed, activeMapType);
            }
        }

        // Reset any leftover crater from a previous game
        pendingLandingWorldX = 0f;
        pendingTerrainHeights = null;

        if (player1Tank != null) player1Tank.gameObject.SetActive(true);
        if (player2Tank != null) player2Tank.gameObject.SetActive(true);
        PlaceTanksFromPercent();

        UpdateHpFromMsg(msg);

        gameStarted = true;
        gameFinished = false;
        shotInFlight = false;
        currentTurnPlayerId = msg.currentTurnPlayerId;
        bool myTurn = IsMyTurn();

        if (connectionStatusLabel != null) connectionStatusLabel.text = "En directe";

        string myName = string.IsNullOrEmpty(gameManager.username) ? "Tu" : gameManager.username;
        if (localPlayerNameLabel != null)
            localPlayerNameLabel.text = isPlayer1 ? myName : "Oponent";
        if (enemyPlayerNameLabel != null)
            enemyPlayerNameLabel.text = isPlayer1 ? "Oponent" : myName;

        if (turnLabel != null) turnLabel.text = myTurn ? "El teu torn" : "Torn de l'oponent";
        ShowTurnBanner(myTurn ? "EL TEU TORN" : "TORN OPONENT");

        LocalTank?.StartTurn();
        SetFireEnabled(myTurn);
        SetMoveEnabled(myTurn);

        if (myTurn) StartTurnTimer();
        else        StopTurnTimer();
    }

    // ── game_update ────────────────────────────────────────────────────────

    private void HandleGameUpdate(SocketMessage msg)
    {
        player1Id = msg.player1Id;
        player2Id = msg.player2Id;
        isPlayer1 = gameManager.playerId == player1Id;

        Vector3 p1PosBefore = player1Tank != null ? player1Tank.transform.position : Vector3.zero;
        Vector3 p2PosBefore = player2Tank != null ? player2Tank.transform.position : Vector3.zero;

        if (msg.player1X > 0) player1X = msg.player1X;
        if (msg.player2X > 0) player2X = msg.player2X;
        SyncRemoteTankPosition();

        UpdateHpFromMsg(msg);
        ApplyPendingTerrain();

        shotInFlight = false;

        currentTurnPlayerId = msg.currentTurnPlayerId;
        bool myTurn = IsMyTurn();

        if (turnLabel != null) turnLabel.text = myTurn ? "El teu torn" : "Torn de l'oponent";
        ShowTurnBanner(myTurn ? "EL TEU TORN" : "TORN OPONENT");

        if (myTurn) StartTurnTimer();
        else        StopTurnTimer();

        LocalTank?.StartTurn();

        if (!string.IsNullOrEmpty(msg.lastShotResult) && msg.lastAttackerPlayerId > 0)
        {
            bool isLocalAttacker = msg.lastAttackerPlayerId == gameManager.playerId;

            Vector3 shooterFirePos   = (msg.lastAttackerPlayerId == player1Id) ? p1PosBefore : p2PosBefore;
            Vector3 targetPreShotPos = (msg.lastAttackerPlayerId == player1Id) ? p2PosBefore : p1PosBefore;

            var shooter = isLocalAttacker ? LocalTank : RemoteTank;
            var target  = isLocalAttacker ? RemoteTank : LocalTank;

            if (shooter != null && target != null)
            {
                bool facingRight = shooterFirePos.x < target.transform.position.x;
                shooter.SetBarrelAngle(msg.lastAngle, facingRight);

                AnimateProjectile(
                    shooterFirePos,
                    msg.lastLandingX,
                    msg.lastAngle,
                    msg.lastPower,
                    facingRight,
                    msg.lastShotResult == "direct_hit",
                    targetPreShotPos,
                    target
                );
            }
            else
            {
                // No animation possible — apply crater immediately
                ApplyPendingTerrain();
            }

            if (combatLogLabel != null && msg.lastDamage > 0)
            {
                string who      = isLocalAttacker ? "Tu" : "Oponent";
                string resultat = msg.lastShotResult == "direct_hit" ? "impacte directe!"
                    : msg.lastShotResult == "near_hit" ? "gairebé!" : "aigua!";
                combatLogLabel.text = $"{who}: {msg.lastAngle}°/{msg.lastPower}% {resultat} (-{msg.lastDamage} HP)";
            }

            if (msg.lastDamage > 0)
                ShowDamagePopup(msg.lastDamage, msg.lastLandingX);
        }
        else if (combatLogLabel != null)
        {
            combatLogLabel.text = "";
        }

        SetFireEnabled(myTurn);
        SetMoveEnabled(myTurn);
    }

    // ── game_end ───────────────────────────────────────────────────────────

    private void HandleGameEnd(SocketMessage msg)
    {
        player1Id = msg.player1Id;
        player2Id = msg.player2Id;
        isPlayer1 = gameManager.playerId == player1Id;

        UpdateHpFromMsg(msg);

        gameFinished = true;
        shotInFlight = false;
        StopTurnTimer();

        if (connectionStatusLabel != null) connectionStatusLabel.text = "FI DE PARTIDA";

        bool won = msg.winnerPlayerId == gameManager.playerId;
        if (turnLabel != null) turnLabel.text = won ? "VICTÒRIA!" : "DERROTA!";
        ShowGameOver(won, msg.durationSeconds);
    }

    private void ApplyPendingTerrain()
    {
        if (terrain == null) return;

        if (pendingTerrainHeights != null && pendingTerrainHeights.Length > 0)
            terrain.LoadServerHeights(pendingTerrainHeights, activeMapType);

        pendingLandingWorldX = 0f;
        pendingTerrainHeights = null;

        player1Tank?.PlaceOnTerrain();
        player2Tank?.PlaceOnTerrain();
    }

    // ── HP ──────────────────────────────────────────────────────────────────

    private void UpdateHpFromMsg(SocketMessage msg)
    {
        SetHpBar(localHpFill, msg.player1Hp);
        SetHpBar(enemyHpFill, msg.player2Hp);
        if (localHpNum != null) localHpNum.text = msg.player1Hp.ToString();
        if (enemyHpNum != null) enemyHpNum.text = msg.player2Hp.ToString();

        localHp = isPlayer1 ? msg.player1Hp : msg.player2Hp;
        enemyHp = isPlayer1 ? msg.player2Hp : msg.player1Hp;
        if (LocalTank  != null) LocalTank.currentHp  = localHp;
        if (RemoteTank != null) RemoteTank.currentHp = enemyHp;
    }

    // ── Col·locació dels tancs ─────────────────────────────────────────────

    private void PlaceTanksFromPercent()
    {
        if (terrain == null) return;

        float p1World = (player1X / 100f) * terrain.width - terrain.width / 2f;
        float p2World = (player2X / 100f) * terrain.width - terrain.width / 2f;

        if (player1Tank != null)
        {
            player1Tank.transform.position = new Vector3(p1World, player1Tank.transform.position.y, 0);
            player1Tank.PlaceOnTerrain();
        }
        if (player2Tank != null)
        {
            player2Tank.transform.position = new Vector3(p2World, player2Tank.transform.position.y, 0);
            player2Tank.PlaceOnTerrain();
        }
    }

    private void SyncRemoteTankPosition()
    {
        if (terrain == null || terrain.width <= 0) return;

        float remoteXPercent = isPlayer1 ? player2X : player1X;
        float worldX = (remoteXPercent / 100f) * terrain.width - terrain.width / 2f;
        float worldY = terrain.GetHeightAtX(worldX) + 0.35f;

        TankController remote = isPlayer1 ? player2Tank : player1Tank;
        if (remote == null) return;

        remote.transform.position = new Vector3(worldX, worldY, 0f);

        var rb = remote.GetComponent<Rigidbody2D>();
        if (rb != null) { rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; }
    }

    // ── Accions del jugador ────────────────────────────────────────────────

    public void OnMoveLeft()
    {
        if (!CanMove()) return;
        LocalTank.Move(-1f, 0.05f);
        LocalTank.PlaceOnTerrain();
        SendTankPosition();
    }

    public void OnMoveRight()
    {
        if (!CanMove()) return;
        LocalTank.Move(1f, 0.05f);
        LocalTank.PlaceOnTerrain();
        SendTankPosition();
    }

    public void SendTankPosition()
    {
        if (terrain == null || LocalTank == null) return;
        float xPercent = (LocalTank.transform.position.x + terrain.width / 2f) / terrain.width * 100f;
        if (isPlayer1) player1X = xPercent;
        else           player2X = xPercent;
        _ = SendJson(new MoveTankMessage
        {
            type     = "move_tank",
            gameId   = gameManager.gameId,
            playerId = gameManager.playerId,
            newX     = xPercent
        });
    }

    public void OnFireClicked()
    {
        if (!CanFire()) return;
        float angle = angleSlider != null ? angleSlider.value : 45f;
        float power = powerSlider != null ? powerSlider.value : 75f;
        FireShot(angle, power);
    }

    public void FireShot(float angle, float power)
    {
        if (!CanFire()) return;

        shotInFlight = true;
        SetFireEnabled(false);
        SetMoveEnabled(false);
        StopTurnTimer();

        if (combatLogLabel != null) combatLogLabel.text = "Tir llançat!";

        bool facingRight = LocalTank.transform.position.x < RemoteTank.transform.position.x;
        LocalTank.SetBarrelAngle(angle, facingRight);

        _ = SendJson(new FireShotMessage
        {
            type     = "fire_shot",
            gameId   = gameManager.gameId,
            playerId = gameManager.playerId,
            angle    = Mathf.RoundToInt(angle),
            power    = Mathf.RoundToInt(power)
        });

        if (angleSlider != null)
            angleSlider.value = Mathf.Clamp(angle + UnityEngine.Random.Range(-10f, 10f), 0f, 90f);
    }

    private void OnLeaveClicked()
    {
        gameManager.ResetMatchState();
        SceneManager.LoadScene("MenuScene");
    }

    private void UnbindButtons()
    {
        if (!uiBound || root == null) return;
        if (fireButton      != null) fireButton.clicked      -= OnFireClicked;
        if (leaveButton     != null) leaveButton.clicked     -= OnLeaveClicked;
        if (moveLeftButton  != null) moveLeftButton.clicked  -= OnMoveLeft;
        if (moveRightButton != null) moveRightButton.clicked -= OnMoveRight;
        var goMenuBtn = root.Q<Button>("go-menu-btn");
        if (goMenuBtn != null) goMenuBtn.clicked -= OnLeaveClicked;
    }

    // ── Animació del projectil ─────────────────────────────────────────────

    private void AnimateProjectile(
        Vector3 startPos,
        float landingXPercent,
        float angle,
        float power,
        bool facingRight,
        bool isDirect = false,
        Vector3 directHitTargetPos = default,
        TankController target = null)
    {
        if (projectilePrefab == null || terrain == null) return;

        float landingWorldX = (landingXPercent / 100f) * terrain.width - terrain.width / 2f;
        pendingLandingWorldX = landingWorldX;

        Vector3 endPos = isDirect
            ? new Vector3(directHitTargetPos.x, directHitTargetPos.y, 0f)
            : new Vector3(landingWorldX, terrain.GetHeightAtX(landingWorldX), 0f);

        var heights = pendingTerrainHeights;
        pendingTerrainHeights = null;

        StopTracked(ref projectileRoutine);
        projectileRoutine = StartCoroutine(AnimateProjectileArc(startPos, endPos, heights, isDirect ? target.transform : null));
    }

    private IEnumerator AnimateProjectileArc(Vector3 start, Vector3 end, int[] newTerrainHeights, Transform target = null)
    {
        var proj = Instantiate(projectilePrefab, start, Quaternion.identity);

        var rb = proj.GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = false;

        var col2d = proj.GetComponent<Collider2D>();
        if (col2d != null) col2d.enabled = false;

        var pc = proj.GetComponent<ProjectileController>();
        if (pc != null) pc.enabled = false;

        float arcHeight = 3f;
        if (terrain != null)
        {
            float midBaseY = (start.y + end.y) / 2f;
            for (int s = 1; s <= 8; s++)
            {
                float tx = s / 9f;
                float sx = Mathf.Lerp(start.x, end.x, tx);
                float terrainPeakY = terrain.GetHeightAtX(sx);
                float needed = terrainPeakY - midBaseY + 2f;
                if (needed > arcHeight) arcHeight = needed;
            }
            arcHeight = Mathf.Clamp(arcHeight, 3f, 12f);
        }

        float duration = 1.2f;
        float elapsed = 0f;

        // Check if this is a direct hit - bullet should stop at tank position
        bool isDirectHit = target != null;
        Vector3 targetPosition = isDirectHit ? target.transform.position : end;

        while (elapsed < duration && proj != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float x = Mathf.Lerp(start.x, targetPosition.x, t);
            float y = Mathf.Lerp(start.y, targetPosition.y, t) + arcHeight * Mathf.Sin(Mathf.PI * t);
            proj.transform.position = new Vector3(x, y, 0);
            yield return null;
        }

        if (proj != null)
        {
            if (explosionPrefab != null)
            {
                var expPos = proj.transform.position;
                var exp = Instantiate(explosionPrefab, expPos, Quaternion.identity);
                Destroy(exp, 1.5f);
            }
            CameraShake();
            Destroy(proj);
        }

        // Apply terrain destruction matching VS AI mode (radius 0.5 world units, cosine falloff).
        if (terrain != null)
        {
            float finalX = isDirectHit ? target.transform.position.x : pendingLandingWorldX;
            float craterY = terrain.GetHeightAtX(finalX);
            terrain.DestroyTerrain(new Vector2(finalX, craterY), 0.5f);
            player1Tank?.PlaceOnTerrain();
            player2Tank?.PlaceOnTerrain();
        }
    }

    // ── Efectes ────────────────────────────────────────────────────────────

    private void ShowDamagePopup(int damage, float xPercent)
    {
        if (damagePopup == null) return;
        damagePopup.text = "-" + damage;
        damagePopup.style.left   = Length.Percent(Mathf.Clamp(xPercent, 10f, 90f));
        damagePopup.style.bottom = Length.Percent(35f);
        damagePopup.style.opacity = 1f;
        damagePopup.RemoveFromClassList("hidden");
        StartCoroutine(FadeDamagePopup());
    }

    private IEnumerator FadeDamagePopup()
    {
        yield return new WaitForSeconds(0.8f);
        if (damagePopup != null)
        {
            damagePopup.style.opacity = 0f;
            damagePopup.AddToClassList("hidden");
        }
    }

    private void CameraShake()
    {
        if (mainCamera == null) return;
        StopTracked(ref shakeRoutine);
        shakeRoutine = StartCoroutine(DoCameraShake());
    }

    private IEnumerator DoCameraShake()
    {
        Vector3 original = mainCamera.transform.position;
        for (int i = 0; i < 6; i++)
        {
            mainCamera.transform.position = original + (Vector3)UnityEngine.Random.insideUnitCircle * 0.15f;
            yield return new WaitForSeconds(0.05f);
        }
        mainCamera.transform.position = original;
    }

    // ── UI helpers ─────────────────────────────────────────────────────────

    private void UpdateSliderLabels()
    {
        if (angleValueLabel != null && angleSlider != null)
            angleValueLabel.text = Mathf.RoundToInt(angleSlider.value).ToString();
        if (powerValueLabel != null && powerSlider != null)
            powerValueLabel.text = Mathf.RoundToInt(powerSlider.value).ToString();
    }

    private void SetHpBar(VisualElement fill, int hp)
    {
        if (fill == null) return;
        fill.style.width = Length.Percent(Mathf.Clamp(hp, 0, 100));
    }

    private void SetFireEnabled(bool on)
    {
        if (fireButton != null) fireButton.SetEnabled(on);
    }

    private void SetMoveEnabled(bool on)
    {
        if (moveLeftButton  != null) moveLeftButton.SetEnabled(on);
        if (moveRightButton != null) moveRightButton.SetEnabled(on);
    }

    private void StartTurnTimer()
    {
        _turnTimeRemaining = turnTimeLimit;
        _timerActive       = true;
        if (turnTimerLabel != null)
        {
            turnTimerLabel.text = Mathf.CeilToInt(turnTimeLimit).ToString();
            turnTimerLabel.RemoveFromClassList("urgent");
            turnTimerLabel.RemoveFromClassList("hidden");
        }
    }

    private void StopTurnTimer()
    {
        _timerActive = false;
        if (turnTimerLabel != null) turnTimerLabel.AddToClassList("hidden");
    }

    private void ShowTurnBanner(string text)
    {
        if (turnBanner == null || turnBannerText == null) return;
        turnBannerText.text = text;
        turnBanner.RemoveFromClassList("hidden");
        CancelInvoke(nameof(HideTurnBanner));
        Invoke(nameof(HideTurnBanner), 1.2f);
    }

    private void HideTurnBanner()
    {
        if (turnBanner != null) turnBanner.AddToClassList("hidden");
    }

    private void ShowGameOver(bool won, int duration)
    {
        SetFireEnabled(false);
        SetMoveEnabled(false);

        if (gameOverTitle != null)
        {
            gameOverTitle.text = won ? "VICTÒRIA!" : "DERROTA!";
            gameOverTitle.RemoveFromClassList("game-over-title-victory");
            gameOverTitle.RemoveFromClassList("game-over-title-defeat");
            gameOverTitle.AddToClassList(won ? "game-over-title-victory" : "game-over-title-defeat");
        }
        if (gameOverSubtitle != null)
            gameOverSubtitle.text = won ? "Has destruït el tanc enemic!" : "El teu tanc ha estat destruït...";
        if (goLocalHp  != null) goLocalHp.text  = localHp.ToString();
        if (goEnemyHp  != null) goEnemyHp.text  = enemyHp.ToString();
        if (goDuration != null) goDuration.text  = duration + "s";

        StartCoroutine(ShowGameOverDelay(1.5f));
    }

    private IEnumerator ShowGameOverDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (gameOverOverlay != null) gameOverOverlay.RemoveFromClassList("hidden");
    }

    // ── Fons de pantalla ──────────────────────────────────────────────────

    private void SetupWorldBackground(string mapType)
    {
        var tex = Resources.Load<Texture2D>("Images/backgrounds/bg_" + mapType);
        if (tex == null) return;

        if (backgroundObj == null)
        {
            backgroundObj = new GameObject("Background");
            backgroundObj.AddComponent<SpriteRenderer>();
        }

        var sr    = backgroundObj.GetComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
        sr.sortingOrder = -100;

        var cam = mainCamera != null ? mainCamera : Camera.main;
        if (cam != null)
        {
            float camH = cam.orthographicSize * 2f;
            float camW = camH * cam.aspect;
            float sprW = tex.width  / 100f;
            float sprH = tex.height / 100f;
            float scale = Mathf.Max(camW / sprW, camH / sprH);
            backgroundObj.transform.localScale = new Vector3(scale, scale, 1);
            backgroundObj.transform.position   = new Vector3(
                cam.transform.position.x, cam.transform.position.y, 10);
        }
    }

    // ── Utilitats ──────────────────────────────────────────────────────────

    public bool IsMyTurn()      => gameManager != null && gameStarted && !gameFinished
                                   && currentTurnPlayerId == gameManager.playerId;
    public bool IsCurrentTurn() => IsMyTurn();
    public bool CanFire()       => IsMyTurn() && !shotInFlight;
    public bool CanMove()       => IsMyTurn() && !shotInFlight;

    private string NormalizeMap(string m)
    {
        string[] valid = { "desert", "snow", "grassland", "canyon", "volcanic" };
        if (!string.IsNullOrEmpty(m))
            foreach (string v in valid)
                if (v == m.ToLower().Trim()) return v;
        return "desert";
    }

    private void StopTracked(ref Coroutine c)
    {
        if (c != null) { StopCoroutine(c); c = null; }
    }

    private float WorldXToPercent(float worldX)
    {
        if (terrain == null || terrain.width <= 0f) return 0f;
        return (worldX + terrain.width / 2f) / terrain.width * 100f;
    }

    private float PercentToWorldX(float xPercent)
    {
        if (terrain == null) return 0f;
        return (xPercent / 100f) * terrain.width - terrain.width / 2f;
    }

    private float WorldYToHeightUnits(float worldY)
    {
        if (terrain == null || Mathf.Abs(terrain.maxHeight) < 0.0001f) return 0f;
        float localY = worldY - terrain.transform.position.y;
        return ((localY - terrain.baseHeight) / terrain.maxHeight) * 100f;
    }

    private float HeightUnitsToWorldY(float heightUnits)
    {
        if (terrain == null) return 0f;
        float localY = terrain.baseHeight + (heightUnits / 100f) * terrain.maxHeight;
        return terrain.transform.position.y + localY;
    }

    private bool HasReachedImpact(float currentXPercent, float impactXPercent, bool facingRight)
    {
        return facingRight ? currentXPercent >= impactXPercent : currentXPercent <= impactXPercent;
    }
}

// ── Missatges WebSocket ────────────────────────────────────────────────────
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
    public float  lastImpactX;
    public float  lastImpactY;
    public float  impactX;
    public float  impactY;
    public float  radius;
    public int    durationSeconds;
    public int[]  terrainHeights;
    public int    terrainEventId;
}
