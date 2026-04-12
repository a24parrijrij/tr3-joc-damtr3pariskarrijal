using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using System.Collections;

public class AuthManager : MonoBehaviour
{
    private string apiUrl = "http://localhost/api";

    private TextField usernameField;
    private TextField passwordField;
    private Label messageText;
    private Button loginBtn;
    private Button registerBtn;

    void OnEnable()
    {
        var uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
        {
            Debug.LogError("AuthManager requires a UIDocument on the same GameObject.");
            enabled = false;
            return;
        }

        var root = uiDocument.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("AuthManager could not access the UIDocument rootVisualElement.");
            enabled = false;
            return;
        }

        usernameField = root.Q<TextField>("username-field");
        passwordField = root.Q<TextField>("password-field");
        messageText   = root.Q<Label>("message-text");
        loginBtn      = root.Q<Button>("login-btn");
        registerBtn   = root.Q<Button>("register-btn");

        if (usernameField == null || passwordField == null || messageText == null ||
            loginBtn == null || registerBtn == null)
        {
            Debug.LogError("AuthManager is missing one or more UI elements. Check LoginScreen.uxml is assigned to the scene UIDocument.");
            enabled = false;
            return;
        }

        loginBtn.clicked += OnLoginClicked;
        registerBtn.clicked += OnRegisterClicked;
    }

    void OnDisable()
    {
        if (loginBtn != null)
        {
            loginBtn.clicked -= OnLoginClicked;
        }

        if (registerBtn != null)
        {
            registerBtn.clicked -= OnRegisterClicked;
        }
    }

    void OnLoginClicked()
    {
        StartCoroutine(Login());
    }

    void OnRegisterClicked()
    {
        StartCoroutine(Register());
    }

    IEnumerator Register()
    {
        messageText.RemoveFromClassList("error-text");
        messageText.RemoveFromClassList("success-text");
        messageText.text = "Registering...";

        string json = "{\"username\":\"" + usernameField.value +
                      "\",\"password\":\"" + passwordField.value + "\"}";
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

        UnityWebRequest req = new UnityWebRequest(apiUrl + "/auth/register", "POST");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            messageText.AddToClassList("success-text");
            messageText.text = "Account created! You can now log in.";
        }
        else
        {
            messageText.AddToClassList("error-text");
            messageText.text = "Username already exists.";
        }
    }

    IEnumerator Login()
    {
        messageText.RemoveFromClassList("error-text");
        messageText.RemoveFromClassList("success-text");
        messageText.text = "Logging in...";

        string json = "{\"username\":\"" + usernameField.value +
                      "\",\"password\":\"" + passwordField.value + "\"}";
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);

        UnityWebRequest req = new UnityWebRequest(apiUrl + "/auth/login", "POST");
        req.uploadHandler   = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            var gameManager = GameManager.EnsureInstance();
            LoginResponse response = JsonUtility.FromJson<LoginResponse>(req.downloadHandler.text);
            gameManager.authToken = response.token;
            gameManager.playerId  = response.id;
            gameManager.username  = response.username;
            SceneManager.LoadScene("MenuScene");
        }
        else
        {
            messageText.AddToClassList("error-text");
            messageText.text = "Wrong username or password.";
        }
    }
}

[System.Serializable]
public class LoginResponse
{
    public string token;
    public int id;
    public string username;
}
