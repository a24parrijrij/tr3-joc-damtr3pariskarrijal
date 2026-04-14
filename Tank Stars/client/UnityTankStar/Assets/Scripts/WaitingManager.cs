// WaitingManager — Mostra la sala d'espera i fa polling per detectar el jugador 2
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using System.Collections;

public class WaitingManager : MonoBehaviour
{
    private string apiUrl = "http://localhost/api";

    private VisualElement root;
    private Label roomCodeText;
    private Label mapTypeText;
    private Label player2Status;
    private Button backBtn;

    void OnEnable()
    {
        var document = GetComponent<UIDocument>();
        if (document == null)
        {
            Debug.LogError("WaitingManager requires a UIDocument on the same GameObject.");
            enabled = false;
            return;
        }

        var gameManager = GameManager.EnsureInstance();

        root = document.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("WaitingManager could not access the UIDocument rootVisualElement.");
            enabled = false;
            return;
        }

        // Fons de pantalla
        string mapName = gameManager.mapType ?? "desert";
        var bgTex = Resources.Load<Texture2D>("Images/backgrounds/bg_" + mapName);
        if (bgTex != null) root.style.backgroundImage = new StyleBackground(bgTex);

        roomCodeText  = root.Q<Label>("room-code-text");
        mapTypeText   = root.Q<Label>("map-type-text");
        player2Status = root.Q<Label>("player2-status");
        backBtn       = root.Q<Button>("back-btn");

        if (roomCodeText == null || player2Status == null || backBtn == null)
        {
            Debug.LogError("WaitingManager is missing one or more UI elements. Check WaitingScreen.uxml is assigned to the scene UIDocument.");
            enabled = false;
            return;
        }

        roomCodeText.text = gameManager.roomCode;
        if (mapTypeText != null)
        {
            mapTypeText.text = "Mapa: " + FormatMapType(gameManager.mapType);
        }

        backBtn.clicked += OnBackClick;

        var gameId = gameManager.gameId;
        if (gameId <= 0)
        {
            roomCodeText.text = string.IsNullOrEmpty(gameManager.roomCode) ? "------" : gameManager.roomCode;
            player2Status.text = "No es pot consultar perquè no s'ha creat cap partida.";
            return;
        }

        StartCoroutine(PollForPlayer(gameId));
    }

    void OnDisable()
    {
        StopAllCoroutines();
        if (backBtn != null)
        {
            backBtn.clicked -= OnBackClick;
        }
    }

    IEnumerator PollForPlayer(int initialGameId)
    {
        while (true)
        {
            yield return new WaitForSeconds(2f);

            var currentGameId = GameManager.Instance?.gameId ?? initialGameId;
            if (currentGameId <= 0)
            {
                Debug.LogError("Game id was lost while polling; stopping.");
                yield break;
            }

            UnityWebRequest req = UnityWebRequest.Get(
                apiUrl + "/games/" + currentGameId
            );
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                GameStatusResponse game = JsonUtility.FromJson<GameStatusResponse>(
                    req.downloadHandler.text
                );

                if (game.status == "in_progress")
                {
                    player2Status.RemoveFromClassList("status-text");
                    player2Status.AddToClassList("success-text");
                    player2Status.text = "✅ Jugador 2 connectat! Començant...";
                    yield return new WaitForSeconds(1.5f);
                    SceneManager.LoadScene("CombatScene");
                    yield break;
                }
            }
        }
    }

    void OnBackClick()
    {
        StopAllCoroutines();
        GameManager.EnsureInstance().ResetMatchState();
        SceneManager.LoadScene("MenuScene");
    }

    private string FormatMapType(string mapType)
    {
        if (string.IsNullOrEmpty(mapType))
        {
            return "Desert";
        }

        return char.ToUpper(mapType[0]) + mapType.Substring(1);
    }
}

[System.Serializable]
public class GameStatusResponse
{
    public int id;
    public string status;
    public string room_code;
}
