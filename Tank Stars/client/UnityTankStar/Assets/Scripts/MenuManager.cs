// MenuManager — Gestiona el menú principal: selecció de mapa, crear/unir-se a partida, VS IA
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class MenuManager : MonoBehaviour
{
    private string apiUrl = "http://localhost/api";
    private static readonly string[] MapTypes = { "desert", "snow", "grassland", "canyon", "volcanic" };

    private VisualElement root;
    private Label welcomeText;
    private Label selectedMapLabel;
    private Label mapDescriptionLabel;
    private TextField roomCodeInput;
    private Label messageText;
    private Button createBtn;
    private Button joinBtn;
    private Button vsAiBtn;
    private Button quitBtn;
    private Button mapPrevBtn;
    private Button mapNextBtn;

    private int selectedMapIndex;

    void OnEnable()
    {
        var document = GetComponent<UIDocument>();
        if (document == null)
        {
            Debug.LogError("MenuManager requires a UIDocument on the same GameObject.");
            enabled = false;
            return;
        }

        root = document.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("MenuManager could not access the UIDocument rootVisualElement.");
            enabled = false;
            return;
        }

        welcomeText   = root.Q<Label>("welcome-text");
        selectedMapLabel = root.Q<Label>("selected-map-label");
        mapDescriptionLabel = root.Q<Label>("map-description-label");
        roomCodeInput = root.Q<TextField>("room-code-input");
        messageText   = root.Q<Label>("message-text");
        createBtn     = root.Q<Button>("create-btn");
        joinBtn       = root.Q<Button>("join-btn");
        vsAiBtn       = root.Q<Button>("vs-ai-btn");
        quitBtn       = root.Q<Button>("quit-btn");
        mapPrevBtn    = root.Q<Button>("map-prev-btn");
        mapNextBtn    = root.Q<Button>("map-next-btn");

        if (welcomeText == null || selectedMapLabel == null || mapDescriptionLabel == null ||
            roomCodeInput == null || messageText == null || createBtn == null ||
            joinBtn == null || vsAiBtn == null || quitBtn == null || mapPrevBtn == null || mapNextBtn == null)
        {
            Debug.LogError("MenuManager is missing one or more UI elements. Check MenuScreen.uxml is assigned to the scene UIDocument.");
            enabled = false;
            return;
        }

        var gameManager = GameManager.EnsureInstance();
        welcomeText.text = string.IsNullOrEmpty(gameManager.username)
            ? "Benvingut!"
            : "Benvingut, " + gameManager.username + "!";
        selectedMapIndex = GetMapIndex(gameManager.mapType);
        UpdateMapSelection();

        createBtn.clicked += OnCreateClicked;
        joinBtn.clicked += OnJoinClicked;
        vsAiBtn.clicked += OnVsAiClicked;
        quitBtn.clicked += OnQuitClicked;
        mapPrevBtn.clicked += OnMapPrevClicked;
        mapNextBtn.clicked += OnMapNextClicked;
    }

    void OnDisable()
    {
        if (createBtn != null)
        {
            createBtn.clicked -= OnCreateClicked;
        }

        if (joinBtn != null)
        {
            joinBtn.clicked -= OnJoinClicked;
        }

        if (vsAiBtn != null)
        {
            vsAiBtn.clicked -= OnVsAiClicked;
        }

        if (quitBtn != null)
        {
            quitBtn.clicked -= OnQuitClicked;
        }

        if (mapPrevBtn != null)
        {
            mapPrevBtn.clicked -= OnMapPrevClicked;
        }

        if (mapNextBtn != null)
        {
            mapNextBtn.clicked -= OnMapNextClicked;
        }
    }

    void OnCreateClicked()
    {
        StartCoroutine(CreateGame());
    }

    void OnJoinClicked()
    {
        StartCoroutine(JoinByCode());
    }

    void OnVsAiClicked()
    {
        var gameManager = GameManager.EnsureInstance();
        gameManager.gameMode = GameManager.GameMode.VsAI;
        gameManager.mapType = MapTypes[selectedMapIndex];
        SceneManager.LoadScene("VsAIScene");
    }

    void OnQuitClicked()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void OnMapPrevClicked()
    {
        selectedMapIndex = (selectedMapIndex - 1 + MapTypes.Length) % MapTypes.Length;
        UpdateMapSelection();
    }

    void OnMapNextClicked()
    {
        selectedMapIndex = (selectedMapIndex + 1) % MapTypes.Length;
        UpdateMapSelection();
    }

    IEnumerator CreateGame()
    {
        messageText.RemoveFromClassList("error-text");
        messageText.text = "Creant partida...";

        var gameManager = GameManager.EnsureInstance();
        gameManager.mapType = MapTypes[selectedMapIndex];
        string json = "{\"playerId\":" + gameManager.playerId +
                      ",\"mapType\":\"" + gameManager.mapType + "\"}";
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

        UnityWebRequest req = new UnityWebRequest(apiUrl + "/games", "POST");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            CreateGameResponse response = JsonUtility.FromJson<CreateGameResponse>(req.downloadHandler.text);
            gameManager.gameId   = response.gameId;
            gameManager.roomCode = response.roomCode;
            gameManager.mapType  = string.IsNullOrEmpty(response.mapType) ? gameManager.mapType : response.mapType;
            SceneManager.LoadScene("WaitingScene");
        }
        else
        {
            messageText.AddToClassList("error-text");
            messageText.text = "No s'ha pogut crear la partida. Docker està actiu?";
        }
    }

    IEnumerator JoinByCode()
    {
        string code = roomCodeInput.value.Trim().ToUpper();
        if (code.Length == 0)
        {
            messageText.AddToClassList("error-text");
            messageText.text = "Introdueix un codi de sala.";
            yield break;
        }

        messageText.RemoveFromClassList("error-text");
        messageText.text = "Buscant sala...";

        UnityWebRequest findReq = UnityWebRequest.Get(apiUrl + "/games/room/" + code);
        yield return findReq.SendWebRequest();

        if (findReq.result != UnityWebRequest.Result.Success)
        {
            messageText.AddToClassList("error-text");
            messageText.text = "Sala no trobada.";
            yield break;
        }

        GameResponse game = JsonUtility.FromJson<GameResponse>(findReq.downloadHandler.text);
        Debug.Log($"[MenuManager] Sala trobada: id={game.id}, status={game.status}, map={game.map_type}");

        var gameManager = GameManager.EnsureInstance();
        Debug.Log($"[MenuManager] Unint-se amb playerId={gameManager.playerId}");
        string json = "{\"playerId\":" + gameManager.playerId + "}";
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

        UnityWebRequest joinReq = new UnityWebRequest(apiUrl + "/games/" + game.id + "/join", "POST");
        joinReq.uploadHandler   = new UploadHandlerRaw(body);
        joinReq.downloadHandler = new DownloadHandlerBuffer();
        joinReq.SetRequestHeader("Content-Type", "application/json");

        yield return joinReq.SendWebRequest();

        if (joinReq.result == UnityWebRequest.Result.Success)
        {
            gameManager.gameId   = game.id;
            gameManager.roomCode = code;
            gameManager.mapType  = string.IsNullOrEmpty(game.map_type) ? "desert" : game.map_type;
            SceneManager.LoadScene("WaitingScene");
        }
        else
        {
            messageText.AddToClassList("error-text");
            // Mostrar l'error real del servidor per depurar
            string serverBody = joinReq.downloadHandler?.text ?? "";
            Debug.LogError($"[MenuManager] Join failed: HTTP {joinReq.responseCode} — {serverBody}");

            if (joinReq.responseCode == 400 && serverBody.Contains("your own"))
                messageText.text = "No pots unir-te a la teva pròpia sala. Utilitza un altre compte.";
            else if (joinReq.responseCode == 400 && serverBody.Contains("full"))
                messageText.text = "La sala ja està plena.";
            else if (joinReq.responseCode == 400 && serverBody.Contains("no longer"))
                messageText.text = "La partida ja no accepta jugadors.";
            else if (joinReq.responseCode == 0)
                messageText.text = "No s'ha pogut connectar al servidor. Docker està actiu?";
            else
                messageText.text = $"No s'ha pogut unir (codi {joinReq.responseCode}).";
        }
    }

    private void UpdateMapSelection()
    {
        string selectedMap = MapTypes[selectedMapIndex];
        selectedMapLabel.text = FormatMapType(selectedMap);
        mapDescriptionLabel.text = GetMapDescription(selectedMap);

        // Actualitzar fons de pantalla
        var bgTex = Resources.Load<Texture2D>("Images/backgrounds/bg_" + selectedMap);
        if (bgTex != null && root != null) root.style.backgroundImage = new StyleBackground(bgTex);
    }

    private int GetMapIndex(string mapType)
    {
        for (int index = 0; index < MapTypes.Length; index++)
        {
            if (MapTypes[index] == mapType)
            {
                return index;
            }
        }

        return 0;
    }

    private string FormatMapType(string mapType)
    {
        if (string.IsNullOrEmpty(mapType))
        {
            return "Desert";
        }

        return char.ToUpper(mapType[0]) + mapType.Substring(1);
    }

    private string GetMapDescription(string mapType)
    {
        switch (mapType)
        {
            case "snow":
                return "Crestes nevades altes amb pics estrets i bona cobertura.";
            case "grassland":
                return "Turons verds arrodonits amb línies de visió mitjanes.";
            case "canyon":
                return "Vall profunda al centre amb vores escarpades i tirs llargs.";
            case "volcanic":
                return "Pendents volcànics afilats amb terreny alt exposat.";
            default:
                return "Dunes càlides amb cobertura suau i cràters arrodonits.";
        }
    }
}

[System.Serializable]
public class CreateGameResponse
{
    public int gameId;
    public string roomCode;
    public string mapType;
}

[System.Serializable]
public class GameResponse
{
    public int id;
    public string room_code;
    public string map_type;
    public string status;
}
