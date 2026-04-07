using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public string authToken;
    public int playerId;
    public string username;
    public int gameId;
    public string roomCode;

    public void ResetMatchState()
    {
        gameId = 0;
        roomCode = string.Empty;
    }

    public static GameManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        var existing = FindFirstObjectByType<GameManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        var go = new GameObject("GameManager");
        return go.AddComponent<GameManager>();
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
