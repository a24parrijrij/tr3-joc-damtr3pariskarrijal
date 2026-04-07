using System;
using System.Text;
using System.Threading.Tasks;
using NativeWebSocket;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class CombatManager : MonoBehaviour
{
    private const string SocketUrl = "ws://localhost/game/";

    private WebSocket websocket;
    private GameManager gameManager;

    private Label roomCodeLabel;
    private Label connectionStatusLabel;
    private Label turnLabel;
    private Label combatLogLabel;
    private Label localPlayerNameLabel;
    private Label enemyPlayerNameLabel;
    private Label localHpLabel;
    private Label enemyHpLabel;
    private Label angleValueLabel;
    private Label powerValueLabel;
    private VisualElement localHpFill;
    private VisualElement enemyHpFill;
    private Slider angleSlider;
    private Slider powerSlider;
    private Button fireButton;
    private Button leaveButton;

    private int player1Id;
    private int player2Id;
    private string lastMessageType;
    private bool gameFinished;

    void OnEnable()
    {
        var document = GetComponent<UIDocument>();
        if (document == null)
        {
            Debug.LogError("CombatManager requires a UIDocument on the same GameObject.");
            enabled = false;
            return;
        }

        var root = document.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("CombatManager could not access the UIDocument rootVisualElement.");
            enabled = false;
            return;
        }

        roomCodeLabel = root.Q<Label>("room-code-label");
        connectionStatusLabel = root.Q<Label>("connection-status-label");
        turnLabel = root.Q<Label>("turn-label");
        combatLogLabel = root.Q<Label>("combat-log-label");
        localPlayerNameLabel = root.Q<Label>("local-player-name");
        enemyPlayerNameLabel = root.Q<Label>("enemy-player-name");
        localHpLabel = root.Q<Label>("local-hp-label");
        enemyHpLabel = root.Q<Label>("enemy-hp-label");
        angleValueLabel = root.Q<Label>("angle-value-label");
        powerValueLabel = root.Q<Label>("power-value-label");
        localHpFill = root.Q<VisualElement>("local-hp-fill");
        enemyHpFill = root.Q<VisualElement>("enemy-hp-fill");
        angleSlider = root.Q<Slider>("angle-slider");
        powerSlider = root.Q<Slider>("power-slider");
        fireButton = root.Q<Button>("fire-btn");
        leaveButton = root.Q<Button>("leave-btn");

        if (roomCodeLabel == null || connectionStatusLabel == null || turnLabel == null ||
            combatLogLabel == null || localPlayerNameLabel == null || enemyPlayerNameLabel == null ||
            localHpLabel == null || enemyHpLabel == null || angleValueLabel == null ||
            powerValueLabel == null || localHpFill == null || enemyHpFill == null ||
            angleSlider == null || powerSlider == null || fireButton == null || leaveButton == null)
        {
            Debug.LogError("CombatManager is missing one or more UI elements. Check CombatScreen.uxml is assigned to the scene UIDocument.");
            enabled = false;
            return;
        }

        gameManager = GameManager.EnsureInstance();
        roomCodeLabel.text = "Room " + (string.IsNullOrEmpty(gameManager.roomCode) ? "------" : gameManager.roomCode);
        localPlayerNameLabel.text = string.IsNullOrEmpty(gameManager.username)
            ? "You"
            : gameManager.username + " (You)";
        enemyPlayerNameLabel.text = "Opponent";
        angleSlider.value = 45f;
        powerSlider.value = 75f;
        connectionStatusLabel.text = "Connecting to game service...";
        turnLabel.text = "Waiting for both players...";
        combatLogLabel.text = "Opening the combat socket and joining the match.";
        SetHpBar(localHpFill, 100);
        SetHpBar(enemyHpFill, 100);
        localHpLabel.text = "100 HP";
        enemyHpLabel.text = "100 HP";
        UpdateSliderLabels();
        UpdateFireButton();

        angleSlider.RegisterValueChangedCallback(_ => UpdateSliderLabels());
        powerSlider.RegisterValueChangedCallback(_ => UpdateSliderLabels());
        fireButton.clicked += OnFireClicked;
        leaveButton.clicked += OnLeaveClicked;

        if (gameManager.gameId <= 0 || gameManager.playerId <= 0)
        {
            connectionStatusLabel.text = "Missing game context.";
            combatLogLabel.text = "Return to the menu and create or join a room again.";
            return;
        }

        _ = ConnectWebSocket();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif
    }

    void OnDisable()
    {
        if (fireButton != null)
        {
            fireButton.clicked -= OnFireClicked;
        }

        if (leaveButton != null)
        {
            leaveButton.clicked -= OnLeaveClicked;
        }

        _ = CloseWebSocket();
    }

    private async Task ConnectWebSocket()
    {
        websocket = new WebSocket(SocketUrl);

        websocket.OnOpen += () =>
        {
            connectionStatusLabel.text = "Connected. Joining room...";
            _ = SendJoinGame();
        };

        websocket.OnMessage += bytes =>
        {
            HandleSocketMessage(Encoding.UTF8.GetString(bytes));
        };

        websocket.OnError += error =>
        {
            connectionStatusLabel.text = "Socket error.";
            combatLogLabel.text = error;
            UpdateFireButton();
        };

        websocket.OnClose += closeCode =>
        {
            if (!gameFinished)
            {
                connectionStatusLabel.text = "Connection closed.";
            }

            UpdateFireButton();
        };

        try
        {
            await websocket.Connect();
        }
        catch (Exception exception)
        {
            connectionStatusLabel.text = "Could not connect to game service.";
            combatLogLabel.text = exception.Message;
        }
    }

    private async Task CloseWebSocket()
    {
        if (websocket == null)
        {
            return;
        }

        if (websocket.State == WebSocketState.Open || websocket.State == WebSocketState.Connecting)
        {
            await websocket.Close();
        }

        websocket = null;
    }

    private async Task SendJoinGame()
    {
        var payload = new JoinGameMessage
        {
            type = "join_game",
            gameId = gameManager.gameId,
            playerId = gameManager.playerId,
        };

        await SendJson(payload);
    }

    private async void OnFireClicked()
    {
        if (!CanFire())
        {
            return;
        }

        combatLogLabel.text = "Shot submitted to the server...";

        var payload = new FireShotMessage
        {
            type = "fire_shot",
            gameId = gameManager.gameId,
            playerId = gameManager.playerId,
            angle = Mathf.RoundToInt(angleSlider.value),
            power = Mathf.RoundToInt(powerSlider.value),
        };

        await SendJson(payload);
    }

    private async Task SendJson(object payload)
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
        {
            connectionStatusLabel.text = "Socket is not ready.";
            UpdateFireButton();
            return;
        }

        await websocket.SendText(JsonUtility.ToJson(payload));
    }

    private void HandleSocketMessage(string json)
    {
        SocketMessage message = JsonUtility.FromJson<SocketMessage>(json);
        if (message == null || string.IsNullOrEmpty(message.type))
        {
            return;
        }

        lastMessageType = message.type;

        if (message.type == "connected")
        {
            connectionStatusLabel.text = message.message;
            return;
        }

        if (message.type == "joined_waiting")
        {
            connectionStatusLabel.text = message.message;
            combatLogLabel.text = "Your opponent has not finished loading CombatScene yet.";
            return;
        }

        if (message.type == "error")
        {
            connectionStatusLabel.text = "Server rejected the action.";
            combatLogLabel.text = message.message;
            UpdateFireButton(turnLabel.text == "Your turn to fire");
            return;
        }

        if (message.type == "game_start" || message.type == "game_update" || message.type == "game_end")
        {
            ApplyGameState(message);
        }
    }

    private void ApplyGameState(SocketMessage message)
    {
        player1Id = message.player1Id;
        player2Id = message.player2Id;
        bool isPlayerOne = gameManager.playerId == player1Id;

        int localHp = isPlayerOne ? message.player1Hp : message.player2Hp;
        int enemyHp = isPlayerOne ? message.player2Hp : message.player1Hp;

        localPlayerNameLabel.text = BuildPlayerLabel(isPlayerOne ? 1 : 2, true);
        enemyPlayerNameLabel.text = BuildPlayerLabel(isPlayerOne ? 2 : 1, false);
        localHpLabel.text = localHp + " HP";
        enemyHpLabel.text = enemyHp + " HP";
        SetHpBar(localHpFill, localHp);
        SetHpBar(enemyHpFill, enemyHp);

        if (!string.IsNullOrEmpty(message.roomCode))
        {
            roomCodeLabel.text = "Room " + message.roomCode;
        }

        gameFinished = message.type == "game_end" || message.status == "finished";

        if (gameFinished)
        {
            connectionStatusLabel.text = "Match finished.";
            turnLabel.text = message.winnerPlayerId == gameManager.playerId
                ? "Victory"
                : "Defeat";
        }
        else
        {
            connectionStatusLabel.text = "Both players connected.";
            turnLabel.text = message.currentTurnPlayerId == gameManager.playerId
                ? "Your turn to fire"
                : "Opponent turn";
        }

        combatLogLabel.text = BuildCombatLog(message);
        UpdateFireButton(message.currentTurnPlayerId == gameManager.playerId);
    }

    private string BuildPlayerLabel(int slot, bool isLocal)
    {
        if (isLocal)
        {
            return string.IsNullOrEmpty(gameManager.username)
                ? "Tank P" + slot + " (You)"
                : gameManager.username + " (P" + slot + ")";
        }

        return "Opponent (P" + slot + ")";
    }

    private string BuildCombatLog(SocketMessage message)
    {
        if (gameFinished)
        {
            string result = message.winnerPlayerId == gameManager.playerId ? "You win" : "You lose";
            return result + " in " + message.durationSeconds + "s. Last shot: " + DescribeShot(message);
        }

        if (message.type == "game_start")
        {
            return message.currentTurnPlayerId == gameManager.playerId
                ? "The duel started. You fire first."
                : "The duel started. Opponent fires first.";
        }

        return DescribeShot(message);
    }

    private string DescribeShot(SocketMessage message)
    {
        if (message.lastAttackerPlayerId == 0)
        {
            return "Adjust the angle and power, then wait for the first valid shot.";
        }

        string shooter = message.lastAttackerPlayerId == gameManager.playerId ? "You" : "Opponent";
        string resultText = "missed";

        if (message.lastShotResult == "direct_hit")
        {
            resultText = "landed a direct hit";
        }
        else if (message.lastShotResult == "near_hit")
        {
            resultText = "landed a near hit";
        }

        return shooter + " " + resultText + " with angle " + message.lastAngle +
               " and power " + message.lastPower + ", dealing " + message.lastDamage +
               " damage.";
    }

    private void UpdateSliderLabels()
    {
        angleValueLabel.text = Mathf.RoundToInt(angleSlider.value) + "°";
        powerValueLabel.text = Mathf.RoundToInt(powerSlider.value) + "%";
    }

    private void SetHpBar(VisualElement fill, int hp)
    {
        fill.style.width = Length.Percent(Mathf.Clamp(hp, 0, 100));
    }

    private bool CanFire()
    {
        return !gameFinished &&
               websocket != null &&
               websocket.State == WebSocketState.Open &&
               lastMessageType != "joined_waiting" &&
               player1Id != 0 &&
               player2Id != 0 &&
               turnLabel.text == "Your turn to fire";
    }

    private void UpdateFireButton()
    {
        UpdateFireButton(false);
    }

    private void UpdateFireButton(bool isPlayerTurn)
    {
        if (fireButton == null)
        {
            return;
        }

        bool enabled = isPlayerTurn &&
                       websocket != null &&
                       websocket.State == WebSocketState.Open &&
                       !gameFinished;
        fireButton.SetEnabled(enabled);
    }

    private void OnLeaveClicked()
    {
        gameManager.ResetMatchState();
        SceneManager.LoadScene("MenuScene");
    }
}

[Serializable]
public class JoinGameMessage
{
    public string type;
    public int gameId;
    public int playerId;
}

[Serializable]
public class FireShotMessage
{
    public string type;
    public int gameId;
    public int playerId;
    public int angle;
    public int power;
}

[Serializable]
public class SocketMessage
{
    public string type;
    public string message;
    public int gameId;
    public string roomCode;
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
    public int durationSeconds;
}
