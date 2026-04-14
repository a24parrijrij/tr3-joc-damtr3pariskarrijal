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

    private const string SocketUrl = "ws://localhost/game/";

    // Elements UXML
    private VisualElement root;
    private Label turnLabel, combatLogLabel, angleValueLabel, powerValueLabel;
    private Label connectionStatusLabel, roomCodeLabel, mapTypeLabel;
    private Label localPlayerNameLabel, enemyPlayerNameLabel, damagePopup;
    private VisualElement localHpFill, enemyHpFill;
    private Slider angleSlider, powerSlider;
    private Button fireButton, leaveButton, moveLeftButton, moveRightButton;
    private VisualElement turnBanner;
    private Label turnBannerText;
    private VisualElement gameOverOverlay;
    private Label gameOverTitle, gameOverSubtitle, goLocalHp, goEnemyHp, goDuration;

    private GameObject backgroundObj;

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

    // Propietats públiques (CombatInput les utilitza)
    public TankController LocalTank => isPlayer1 ? player1Tank : player2Tank;
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

        gameManager = GameManager.EnsureInstance();

        // Valors inicials
        if (angleSlider != null) angleSlider.value = 45f;
        if (powerSlider != null) powerSlider.value = 75f;
        if (turnBanner != null) turnBanner.AddToClassList("hidden");
        if (gameOverOverlay != null) gameOverOverlay.AddToClassList("hidden");
        if (damagePopup != null) damagePopup.AddToClassList("hidden");
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
        UpdateSliderLabels();
        SetFireEnabled(false);
        SetMoveEnabled(false);

        // Fons de pantalla inicial
        SetupWorldBackground(gameManager.mapType ?? "desert");

        // Amagar tancs fins que arribi game_start
        if (player1Tank != null) player1Tank.gameObject.SetActive(false);
        if (player2Tank != null) player2Tank.gameObject.SetActive(false);

        // Registrar callbacks
        if (angleSlider != null) angleSlider.RegisterValueChangedCallback(_ => UpdateSliderLabels());
        if (powerSlider != null) powerSlider.RegisterValueChangedCallback(_ => UpdateSliderLabels());
        if (fireButton != null) fireButton.clicked += OnFireClicked;
        if (leaveButton != null) leaveButton.clicked += OnLeaveClicked;
        if (moveLeftButton != null) moveLeftButton.clicked += OnMoveLeft;
        if (moveRightButton != null) moveRightButton.clicked += OnMoveRight;
        var goMenuBtn = root.Q<Button>("go-menu-btn");
        if (goMenuBtn != null) goMenuBtn.clicked += OnLeaveClicked;

        uiBound = true;

        // Connectar al WebSocket
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
            type = "join_game",
            gameId = gameManager.gameId,
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
                HandleTerrainDestroyed(msg);
                break;

            case "positions_update":
                if (msg.player1X > 0) player1X = msg.player1X;
                if (msg.player2X > 0) player2X = msg.player2X;
                PlaceTanksFromPercent();
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

        // Actualitzar fons amb el mapa del servidor
        SetupWorldBackground(activeMapType);

        // Generar terreny amb seed del servidor (fallback a gameManager.gameId)
        if (terrain != null)
        {
            int seed = msg.gameId > 0 ? msg.gameId : gameManager.gameId;
            Debug.Log($"[CombatManager] game_start: seed={seed}, map={activeMapType}, p1X={player1X}, p2X={player2X}");
            terrain.GenerateTerrain(seed, activeMapType);
        }

        // Mostrar i col·locar tancs
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
        if (turnLabel != null) turnLabel.text = myTurn ? "El teu torn" : "Torn de l'oponent";
        ShowTurnBanner(myTurn ? "EL TEU TORN" : "TORN OPONENT");

        LocalTank?.StartTurn();

        SetFireEnabled(myTurn);
        SetMoveEnabled(myTurn);
    }

    // ── game_update ────────────────────────────────────────────────────────

    private void HandleGameUpdate(SocketMessage msg)
    {
        player1Id = msg.player1Id;
        player2Id = msg.player2Id;
        isPlayer1 = gameManager.playerId == player1Id;

        if (msg.player1X > 0) player1X = msg.player1X;
        if (msg.player2X > 0) player2X = msg.player2X;
        PlaceTanksFromPercent();

        UpdateHpFromMsg(msg);

        // El servidor ha processat el tir anterior — resetejar shotInFlight
        shotInFlight = false;

        currentTurnPlayerId = msg.currentTurnPlayerId;
        bool myTurn = IsMyTurn();

        if (turnLabel != null) turnLabel.text = myTurn ? "El teu torn" : "Torn de l'oponent";
        ShowTurnBanner(myTurn ? "EL TEU TORN" : "TORN OPONENT");
        LocalTank?.StartTurn();

        // Animar l'últim tir si n'hi ha
        if (!string.IsNullOrEmpty(msg.lastShotResult) && msg.lastAttackerPlayerId > 0)
        {
            bool isLocalAttacker = msg.lastAttackerPlayerId == gameManager.playerId;
            var shooter = isLocalAttacker ? LocalTank : RemoteTank;
            var target = isLocalAttacker ? RemoteTank : LocalTank;

            if (shooter != null && target != null)
            {
                bool facingRight = shooter.transform.position.x < target.transform.position.x;
                shooter.SetBarrelAngle(msg.lastAngle, facingRight);
                AnimateProjectile(shooter.transform.position, msg.lastLandingX, msg.lastAngle, msg.lastPower, facingRight);
            }

            if (combatLogLabel != null && msg.lastDamage > 0)
            {
                string who = isLocalAttacker ? "Tu" : "Oponent";
                string resultat = msg.lastShotResult == "direct_hit" ? "impacte directe!"
                    : msg.lastShotResult == "near_hit" ? "gairebé!" : "aigua!";
                combatLogLabel.text = $"{who}: {msg.lastAngle}°/{msg.lastPower}% {resultat} (-{msg.lastDamage} HP)";
            }

            if (msg.lastDamage > 0)
                ShowDamagePopup(msg.lastDamage, msg.lastLandingX);

            if (msg.lastImpactX > 0 && msg.lastImpactY > 0)
                PlayExplosion(msg.lastImpactX, msg.lastImpactY);
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

        if (connectionStatusLabel != null) connectionStatusLabel.text = "FI DE PARTIDA";

        bool won = msg.winnerPlayerId == gameManager.playerId;
        if (turnLabel != null) turnLabel.text = won ? "VICTÒRIA!" : "DERROTA!";
        ShowGameOver(won, msg.durationSeconds);
    }

    // ── terrain_destroyed ──────────────────────────────────────────────────

    private void HandleTerrainDestroyed(SocketMessage msg)
    {
        if (terrain == null) return;
        float worldX = (msg.impactX / 100f) * terrain.width - terrain.width / 2f;
        terrain.DestroyTerrain(new Vector2(worldX, msg.impactY), msg.radius);
        player1Tank?.PlaceOnTerrain();
        player2Tank?.PlaceOnTerrain();
    }

    // ── HP ──────────────────────────────────────────────────────────────────

    private void UpdateHpFromMsg(SocketMessage msg)
    {
        localHp = isPlayer1 ? msg.player1Hp : msg.player2Hp;
        enemyHp = isPlayer1 ? msg.player2Hp : msg.player1Hp;
        SetHpBar(localHpFill, localHp);
        SetHpBar(enemyHpFill, enemyHp);
        if (LocalTank != null) LocalTank.currentHp = localHp;
        if (RemoteTank != null) RemoteTank.currentHp = enemyHp;
    }

    // ── Col·locació dels tancs ─────────────────────────────────────────────

    private void PlaceTanksFromPercent()
    {
        if (terrain == null) return;

        // Convertir 0-100% a espai mundial. player1X sempre va a player1Tank.
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

    // ── Accions del jugador ────────────────────────────────────────────────

    public void OnMoveLeft()
    {
        if (!CanMove()) return;
        LocalTank.Move(-1f, 0.2f);
        LocalTank.PlaceOnTerrain();
        SendTankPosition();
    }

    public void OnMoveRight()
    {
        if (!CanMove()) return;
        LocalTank.Move(1f, 0.2f);
        LocalTank.PlaceOnTerrain();
        SendTankPosition();
    }

    private void SendTankPosition()
    {
        if (terrain == null || LocalTank == null) return;
        float newXPerc = (LocalTank.transform.position.x + terrain.width / 2f) / terrain.width * 100f;
        _ = SendJson(new MoveTankMessage
        {
            type = "move_tank",
            gameId = gameManager.gameId,
            playerId = gameManager.playerId,
            newX = newXPerc
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

        if (combatLogLabel != null) combatLogLabel.text = "Tir llançat!";

        bool facingRight = LocalTank.transform.position.x < RemoteTank.transform.position.x;
        LocalTank.SetBarrelAngle(angle, facingRight);

        // Animació visual predictiva del projectil
        float landingX = PredictLandingX(LocalTank.transform.position.x, angle, power, isPlayer1);
        AnimateProjectile(LocalTank.transform.position, landingX, angle, power, facingRight);

        _ = SendJson(new FireShotMessage
        {
            type = "fire_shot",
            gameId = gameManager.gameId,
            playerId = gameManager.playerId,
            angle = Mathf.RoundToInt(angle),
            power = Mathf.RoundToInt(power)
        });
    }

    private void OnLeaveClicked()
    {
        gameManager.ResetMatchState();
        SceneManager.LoadScene("MenuScene");
    }

    private void UnbindButtons()
    {
        if (!uiBound || root == null) return;
        if (fireButton != null) fireButton.clicked -= OnFireClicked;
        if (leaveButton != null) leaveButton.clicked -= OnLeaveClicked;
        if (moveLeftButton != null) moveLeftButton.clicked -= OnMoveLeft;
        if (moveRightButton != null) moveRightButton.clicked -= OnMoveRight;
        var goMenuBtn = root.Q<Button>("go-menu-btn");
        if (goMenuBtn != null) goMenuBtn.clicked -= OnLeaveClicked;
    }

    // ── Animació del projectil (purament visual, sense físiques) ───────────

    private void AnimateProjectile(Vector3 startPos, float landingXPercent, float angle, float power, bool facingRight)
    {
        if (projectilePrefab == null || terrain == null) return;

        float landingWorldX = (landingXPercent / 100f) * terrain.width - terrain.width / 2f;
        Vector3 endPos = new Vector3(landingWorldX, terrain.GetHeightAtX(landingWorldX) + 0.5f, 0);

        StopTracked(ref projectileRoutine);
        projectileRoutine = StartCoroutine(AnimateProjectileArc(startPos, endPos));
    }

    private IEnumerator AnimateProjectileArc(Vector3 start, Vector3 end)
    {
        var proj = Instantiate(projectilePrefab, start, Quaternion.identity);

        // Desactivar físiques i col·lisions perquè és un projectil purament visual.
        // Sense això, el Rigidbody2D (gravetat) lluitaria amb el posicionament manual
        // i el collider podria xocar amb el terreny destruint l'objecte prematurament.
        var rb = proj.GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = false;

        var col2d = proj.GetComponent<Collider2D>();
        if (col2d != null) col2d.enabled = false;

        var pc = proj.GetComponent<ProjectileController>();
        if (pc != null) pc.enabled = false;

        float duration = 1.2f;
        float elapsed = 0f;
        float arcHeight = 3f;

        while (elapsed < duration && proj != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float x = Mathf.Lerp(start.x, end.x, t);
            float y = Mathf.Lerp(start.y, end.y, t) + arcHeight * Mathf.Sin(Mathf.PI * t);
            proj.transform.position = new Vector3(x, y, 0);
            yield return null;
        }

        if (proj != null)
        {
            if (explosionPrefab != null)
            {
                var exp = Instantiate(explosionPrefab, end, Quaternion.identity);
                Destroy(exp, 1.5f);
            }
            CameraShake();
            Destroy(proj);
        }
    }

    // ── Efectes ────────────────────────────────────────────────────────────

    private void PlayExplosion(float impactXPercent, float impactY)
    {
        if (explosionPrefab == null || terrain == null) return;
        float worldX = (impactXPercent / 100f) * terrain.width - terrain.width / 2f;
        var exp = Instantiate(explosionPrefab, new Vector3(worldX, impactY, 0), Quaternion.identity);
        Destroy(exp, 1.5f);
    }

    private void ShowDamagePopup(int damage, float xPercent)
    {
        if (damagePopup == null) return;
        damagePopup.text = "-" + damage;
        damagePopup.style.left = Length.Percent(Mathf.Clamp(xPercent, 10f, 90f));
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
        if (moveLeftButton != null) moveLeftButton.SetEnabled(on);
        if (moveRightButton != null) moveRightButton.SetEnabled(on);
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
        if (goLocalHp != null) goLocalHp.text = localHp.ToString();
        if (goEnemyHp != null) goEnemyHp.text = enemyHp.ToString();
        if (goDuration != null) goDuration.text = duration + "s";

        StartCoroutine(ShowGameOverDelay(1.5f));
    }

    private IEnumerator ShowGameOverDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (gameOverOverlay != null) gameOverOverlay.RemoveFromClassList("hidden");
    }

    // ── Fons de pantalla ─────────────────────────────────────────────────

    private void SetupWorldBackground(string mapType)
    {
        var tex = Resources.Load<Texture2D>("Images/backgrounds/bg_" + mapType);
        if (tex == null) return;

        if (backgroundObj == null)
        {
            backgroundObj = new GameObject("Background");
            backgroundObj.AddComponent<SpriteRenderer>();
        }

        var sr = backgroundObj.GetComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
        sr.sortingOrder = -100;

        var cam = mainCamera != null ? mainCamera : Camera.main;
        if (cam != null)
        {
            float camH = cam.orthographicSize * 2f;
            float camW = camH * cam.aspect;
            float sprW = tex.width / 100f;
            float sprH = tex.height / 100f;
            float scale = Mathf.Max(camW / sprW, camH / sprH);
            backgroundObj.transform.localScale = new Vector3(scale, scale, 1);
            backgroundObj.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 10);
        }
    }

    // ── Utilitats ──────────────────────────────────────────────────────────

    public bool IsMyTurn() => gameManager != null && gameStarted && !gameFinished
                              && currentTurnPlayerId == gameManager.playerId;
    public bool IsCurrentTurn() => IsMyTurn();
    public bool CanFire() => IsMyTurn() && !shotInFlight;
    public bool CanMove() => IsMyTurn() && !shotInFlight;

    private float PredictLandingX(float attackerWorldX, float angle, float power, bool p1)
    {
        float attackerPerc = terrain != null
            ? (attackerWorldX + terrain.width / 2f) / terrain.width * 100f
            : 50f;
        float distance = (power / 100f) * 40f * Mathf.Sin(2f * angle * Mathf.Deg2Rad);
        float direction = p1 ? 1f : -1f;
        return Mathf.Clamp(attackerPerc + distance * direction, 0f, 100f);
    }

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
}

// Missatges WebSocket
[Serializable] public class JoinGameMessage { public string type; public int gameId; public int playerId; }
[Serializable] public class FireShotMessage { public string type; public int gameId; public int playerId; public int angle; public int power; }
[Serializable] public class MoveTankMessage { public string type; public int gameId; public int playerId; public float newX; }

[Serializable]
public class SocketMessage
{
    public string type;
    public string message;
    public int gameId;
    public string roomCode;
    public string mapType;
    public int player1Id;
    public int player2Id;
    public int player1Hp;
    public int player2Hp;
    public float player1X;
    public float player2X;
    public int currentTurnPlayerId;
    public int winnerPlayerId;
    public string status;
    public string lastShotResult;
    public int lastDamage;
    public float lastLandingX;
    public int lastAttackerPlayerId;
    public int lastAngle;
    public int lastPower;
    public float lastImpactX;
    public float lastImpactY;
    public float impactX;
    public float impactY;
    public float radius;
    public int durationSeconds;
    public int[] terrainHeights;
    public int terrainEventId;
}
