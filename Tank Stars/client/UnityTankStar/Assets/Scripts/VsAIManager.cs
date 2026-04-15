// VsAIManager — Lògica de la partida VS IA.
// Utilitza el mateix CombatScreen.uxml que CombatManager.
// L'UI es vincula a Start() amb un yield d'1 frame perquè el UIDocument
// estigui completament construït abans de consultar els elements.
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.MLAgents;
using System.Collections;
using UnityEngine.UIElements;

public class VsAIManager : MonoBehaviour
{
    public static VsAIManager Instance;

    [Header("References")]
    public TerrainGenerator terrain;
    public TankController   playerTank;
    public TankController   aiTank;
    public TankAgent        aiAgent;

    // ── UXML cache ─────────────────────────────────────────────────────────
    private VisualElement root;
    private Label         turnLabel;
    private Label         combatLogLabel;
    private Label         angleValueLabel;
    private Label         powerValueLabel;
    private VisualElement localHpFill;
    private VisualElement enemyHpFill;
    private Slider        angleSlider;
    private Slider        powerSlider;
    private Button        fireButton;
    private Button        leaveButton;
    private Button        moveLeftButton;
    private Button        moveRightButton;
    private VisualElement turnBanner;
    private Label         turnBannerText;
    private VisualElement gameOverOverlay;
    private Label         gameOverTitle;
    private Label         gameOverSubtitle;
    private Label         goLocalHp;
    private Label         goEnemyHp;
    private Label         connectionStatusLabel;
    private Label         roomCodeLabel;
    private Label         mapTypeLabel;
    private Label         localPlayerNameLabel;
    private Label         enemyPlayerNameLabel;

    private GameObject backgroundObj;

    // ── State ──────────────────────────────────────────────────────────────
    public bool isPlayerTurn           = true;
    public bool isGameOver             = false;
    public bool isResolutionInProgress = false;
    private bool uiBound               = false;

    void Awake() { Instance = this; }

    void Start() { StartCoroutine(BindUIThenSetup()); }

    void OnDisable()
    {
        UnbindButtons();
        StopAllCoroutines();
    }

    // ── UI binding ─────────────────────────────────────────────────────────

    private IEnumerator BindUIThenSetup()
    {
        yield return null; // wait one frame so UIDocument finishes building

        var uiDoc = GetComponent<UIDocument>();
        if (uiDoc == null) { Debug.LogError("VsAIManager: no UIDocument!"); yield break; }

        root = uiDoc.rootVisualElement;
        if (root == null) { Debug.LogError("VsAIManager: rootVisualElement is null!"); yield break; }

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
        connectionStatusLabel = root.Q<Label>("connection-status-label");
        roomCodeLabel         = root.Q<Label>("room-code-label");
        mapTypeLabel          = root.Q<Label>("map-type-label");
        localPlayerNameLabel  = root.Q<Label>("local-player-name");
        enemyPlayerNameLabel  = root.Q<Label>("enemy-player-name");

        if (angleSlider == null) Debug.LogError("VsAIManager: 'angle-slider' not found in UXML!");
        if (powerSlider == null) Debug.LogError("VsAIManager: 'power-slider' not found in UXML!");
        if (fireButton  == null) Debug.LogError("VsAIManager: 'fire-btn' not found in UXML!");

        if (angleSlider != null) angleSlider.value = 45f;
        if (powerSlider != null) powerSlider.value = 75f;
        if (turnBanner      != null) turnBanner.AddToClassList("hidden");
        if (gameOverOverlay != null) gameOverOverlay.AddToClassList("hidden");
        if (connectionStatusLabel != null) connectionStatusLabel.text = "VS IA";
        if (roomCodeLabel         != null) roomCodeLabel.text         = "Mode VS IA";
        if (enemyPlayerNameLabel  != null) enemyPlayerNameLabel.text  = "IA";

        var gm = GameManager.EnsureInstance();
        if (localPlayerNameLabel != null)
            localPlayerNameLabel.text = string.IsNullOrEmpty(gm?.username) ? "Tu" : gm.username;
        if (mapTypeLabel != null)
        {
            string mt = gm?.mapType ?? "desert";
            mapTypeLabel.text = char.ToUpper(mt[0]) + mt.Substring(1);
        }

        SetHpBar(localHpFill, 100);
        SetHpBar(enemyHpFill, 100);
        UpdateSliderLabels();

        if (angleSlider     != null) angleSlider.RegisterValueChangedCallback(_ => UpdateSliderLabels());
        if (powerSlider     != null) powerSlider.RegisterValueChangedCallback(_ => UpdateSliderLabels());
        if (fireButton      != null) fireButton.clicked      += OnFireClicked;
        if (leaveButton     != null) leaveButton.clicked     += OnLeaveClicked;
        if (moveLeftButton  != null) moveLeftButton.clicked  += OnMoveLeft;
        if (moveRightButton != null) moveRightButton.clicked += OnMoveRight;
        var goMenuBtn = root.Q<Button>("go-menu-btn");
        if (goMenuBtn != null) goMenuBtn.clicked += OnLeaveClicked;

        uiBound = true;

        // Sliders always visible; only fire/move buttons disabled until game starts
        SetControlsEnabled(true);

        StartCoroutine(SetupGameCoroutine());
    }

    private void UnbindButtons()
    {
        if (!uiBound || root == null) return;
        if (fireButton      != null) fireButton.clicked      -= OnFireClicked;
        if (leaveButton     != null) leaveButton.clicked     -= OnLeaveClicked;
        if (moveLeftButton  != null) moveLeftButton.clicked  -= OnMoveLeft;
        if (moveRightButton != null) moveRightButton.clicked -= OnMoveRight;
        var goMenuBtn = root?.Q<Button>("go-menu-btn");
        if (goMenuBtn != null) goMenuBtn.clicked -= OnLeaveClicked;
    }

    // ── Game setup ─────────────────────────────────────────────────────────

    private IEnumerator SetupGameCoroutine()
    {
        isGameOver             = false;
        isPlayerTurn           = true;
        isResolutionInProgress = false;

        var gm   = GameManager.EnsureInstance();
        int seed = Random.Range(1, 99999);
        string mapType = gm?.mapType ?? "desert";

        SetupWorldBackground(mapType);

        if (terrain != null)
            terrain.GenerateTerrain(seed, mapType);

        yield return new WaitForSeconds(0.4f);

        if (playerTank != null && aiTank != null)
        {
            playerTank.terrain = terrain;
            aiTank.terrain     = terrain;
            playerTank.transform.position = new Vector3(-7f, 0, 0);
            aiTank.transform.position     = new Vector3(7f,  0, 0);
            playerTank.PlaceOnTerrain();
            aiTank.PlaceOnTerrain();
            playerTank.currentHp = playerTank.maxHp;
            aiTank.currentHp     = aiTank.maxHp;
        }

        if (aiAgent != null)
        {
            aiAgent.isVsAIMode        = true;
            aiAgent.canCaptureActions = false;
        }

        RefreshHpBars();
        UpdateTurnUI();
    }

    // ── Button handlers ────────────────────────────────────────────────────

    private void OnMoveLeft()
    {
        if (!IsPlayerTurn() || playerTank == null) return;
        playerTank.Move(-1f, 0.05f);
        playerTank.PlaceOnTerrain();
    }

    private void OnMoveRight()
    {
        if (!IsPlayerTurn() || playerTank == null) return;
        playerTank.Move(1f, 0.05f);
        playerTank.PlaceOnTerrain();
    }

    private void OnFireClicked()
    {
        if (!IsPlayerTurn()) return;
        float angle = angleSlider != null ? angleSlider.value : 45f;
        float power = powerSlider != null ? powerSlider.value : 75f;
        PlayerFires(angle, power);
    }

    private void OnLeaveClicked()
    {
        GameManager.EnsureInstance()?.ResetMatchState();
        SceneManager.LoadScene("MenuScene");
    }

    // ── Player fires ───────────────────────────────────────────────────────

    public void PlayerFires(float angle, float power)
    {
        if (!IsPlayerTurn()) return;
        isResolutionInProgress = true;
        SetControlsEnabled(false);
        if (combatLogLabel != null) combatLogLabel.text = "Tir llançat!";

        if (aiAgent == null || aiAgent.projectilePrefab == null)
        {
            Debug.LogError("VsAIManager: cannot fire — aiAgent or projectilePrefab is null.");
            isResolutionInProgress = false;
            SetControlsEnabled(true);
            return;
        }

        Vector3 spawnPos = playerTank.barrel != null
            ? playerTank.barrel.position
            : playerTank.transform.position + Vector3.up * 0.5f;

        GameObject proj = Instantiate(aiAgent.projectilePrefab, spawnPos, Quaternion.identity);
        Collider2D pCol = proj.GetComponent<Collider2D>();
        Collider2D tCol = playerTank.GetComponent<Collider2D>();
        if (pCol != null && tCol != null) Physics2D.IgnoreCollision(pCol, tCol);

        playerTank.SetBarrelAngle(angle, playerTank.transform.position.x < aiTank.transform.position.x);

        var pc = proj.GetComponent<ProjectileController>();
        if (pc == null) { Destroy(proj); return; }
        pc.SetImpactCallback(OnPlayerProjectileImpact);
        pc.Launch(angle, power, playerTank.transform.position.x < aiTank.transform.position.x);

        // same barrel recoil as multiplayer — shifts angle a bit after firing
        if (angleSlider != null)
            angleSlider.value = Mathf.Clamp(angle + UnityEngine.Random.Range(-4f, 4f), 0f, 90f);

        CancelInvoke(nameof(OnProjectileResolved));
        Invoke(nameof(OnProjectileResolved), 5.0f);
    }

    private void OnPlayerProjectileImpact(Vector2 impactWorld, bool hitTank)
    {
        CancelInvoke(nameof(OnProjectileResolved));

        if (terrain != null)
        {
            terrain.DestroyTerrain(impactWorld, 0.8f);
            playerTank?.PlaceOnTerrain();
            aiTank?.PlaceOnTerrain();
        }

        if (aiAgent != null && aiAgent.explosionPrefab != null)
        {
            var exp = Instantiate(aiAgent.explosionPrefab,
                new Vector3(impactWorld.x, impactWorld.y, 0f), Quaternion.identity);
            Destroy(exp, 2f);
        }

        if (hitTank)
        {
            aiTank.TakeDamage(35);
            if (combatLogLabel != null) combatLogLabel.text = "Impacte directe! -35 HP";
        }
        else
        {
            float dist = Vector2.Distance(impactWorld, aiTank.transform.position);
            if (dist < 1.5f) { aiTank.TakeDamage(15); if (combatLogLabel != null) combatLogLabel.text = "Gairebé! -15 HP"; }
            else { if (combatLogLabel != null) combatLogLabel.text = "Aigua!"; }
        }

        OnProjectileResolved();
    }

    public void OnProjectileResolved()
    {
        CancelInvoke(nameof(OnProjectileResolved));
        isResolutionInProgress = false;
        if (aiAgent != null) aiAgent.canCaptureActions = false;

        RefreshHpBars();

        if (aiTank != null && aiTank.currentHp <= 0)    { isGameOver = true; ShowGameOver("VICTÒRIA!", "Has destruït el tanc de la IA!"); return; }
        if (playerTank != null && playerTank.currentHp <= 0) { isGameOver = true; ShowGameOver("DERROTA!", "La IA ha destruït el teu tanc..."); return; }

        isPlayerTurn = !isPlayerTurn;
        UpdateTurnUI();

        if (!isPlayerTurn && !isGameOver)
            Invoke(nameof(StartAITurn), 1.0f);
    }

    // ── AI turn ────────────────────────────────────────────────────────────

    private void StartAITurn()
    {
        if (isGameOver || aiAgent == null) return;
        
        Debug.Log("VsAIManager: AI Turn started. Enabling brain pulse...");
        
        aiAgent.canCaptureActions = true;
        isResolutionInProgress = true; 

        // 💓 THE PULSE: Request a decision every 0.2s until the AI actually fires.
        // This fixes the "Silent Brain" issue where RequestDecision() is sometimes ignored.
        CancelInvoke(nameof(PulseAIDecision));
        InvokeRepeating(nameof(PulseAIDecision), 0.3f, 0.2f);

        // 🚨 PANIC SAFETY: If the AI hasn't fired in 6 seconds, force a heuristic shot to recover the loop.
        CancelInvoke(nameof(ForceAIShot));
        Invoke(nameof(ForceAIShot), 6.0f);
    }

    private void PulseAIDecision()
    {
        if (isGameOver || aiAgent == null || isPlayerTurn) 
        {
            CancelInvoke(nameof(PulseAIDecision));
            return;
        }

        // If the AI has finally fired, stop Pulsing.
        if (aiAgent.isWaitingForShot)
        {
            CancelInvoke(nameof(PulseAIDecision));
            return;
        }

        Debug.Log("[VsAIManager] Nudging AI Brain for a decision...");
        aiAgent.RequestDecision();
    }

    private void ForceAIShot()
    {
        if (isGameOver || isPlayerTurn || (aiAgent != null && aiAgent.isWaitingForShot)) return;

        Debug.LogWarning("VsAIManager: AI Brain failed to respond. Forcing a Panic Shot to recover game loop.");
        aiAgent?.FireActualShot(UnityEngine.Random.Range(30f, 60f), 65f);
        CancelInvoke(nameof(PulseAIDecision));
    }

    // ── Turn UI ────────────────────────────────────────────────────────────

    private void UpdateTurnUI()
    {
        if (!uiBound) return;
        if (turnLabel != null) turnLabel.text = isPlayerTurn ? "El teu torn" : "Torn de la IA";
        ShowTurnBanner(isPlayerTurn ? "EL TEU TORN" : "TORN DE LA IA");
        SetControlsEnabled(isPlayerTurn);
        playerTank?.StartTurn();
        if (!isPlayerTurn) aiTank?.StartTurn();
    }

    private void ShowTurnBanner(string text)
    {
        if (turnBanner == null || turnBannerText == null) return;
        turnBannerText.text = text;
        turnBanner.RemoveFromClassList("hidden");
        CancelInvoke(nameof(HideTurnBanner));
        Invoke(nameof(HideTurnBanner), 1.2f);
    }
    private void HideTurnBanner() => turnBanner?.AddToClassList("hidden");

    // ── HP bars ────────────────────────────────────────────────────────────

    private void RefreshHpBars()
    {
        if (!uiBound) return;
        if (playerTank != null) SetHpBar(localHpFill, playerTank.currentHp);
        if (aiTank     != null) SetHpBar(enemyHpFill, aiTank.currentHp);
    }

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

    private void SetControlsEnabled(bool on)
    {
        bool ok = on && !isGameOver;
        if (fireButton      != null) fireButton.SetEnabled(ok);
        if (moveLeftButton  != null) moveLeftButton.SetEnabled(ok);
        if (moveRightButton != null) moveRightButton.SetEnabled(ok);
        // Sliders stay interactive always so player can pre-aim
        if (angleSlider != null) angleSlider.SetEnabled(true);
        if (powerSlider != null) powerSlider.SetEnabled(true);
    }

    // ── Game over ──────────────────────────────────────────────────────────

    private void ShowGameOver(string title, string subtitle)
    {
        SetControlsEnabled(false);
        if (gameOverTitle != null)
        {
            gameOverTitle.text = title;
            gameOverTitle.RemoveFromClassList("game-over-title-victory");
            gameOverTitle.RemoveFromClassList("game-over-title-defeat");
            gameOverTitle.AddToClassList(title == "VICTÒRIA!" ? "game-over-title-victory" : "game-over-title-defeat");
        }
        if (gameOverSubtitle != null) gameOverSubtitle.text = subtitle;
        if (goLocalHp != null) goLocalHp.text = playerTank != null ? playerTank.currentHp.ToString() : "0";
        if (goEnemyHp != null) goEnemyHp.text = aiTank     != null ? aiTank.currentHp.ToString()     : "0";
        StartCoroutine(ShowGameOverDelay(1.5f));
    }

    private IEnumerator ShowGameOverDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (gameOverOverlay != null) gameOverOverlay.RemoveFromClassList("hidden");
    }

    public bool IsPlayerTurn() => isPlayerTurn && !isGameOver && !isResolutionInProgress;

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

        var sr = backgroundObj.GetComponent<SpriteRenderer>();
        sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100);
        sr.sortingOrder = -100;

        var cam = Camera.main;
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
}