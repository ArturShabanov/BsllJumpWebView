using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;
    public TMPro.TextMeshProUGUI scoreText;
    public GameObject gameOverPanel;
    public Button restartButton;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        gameOverPanel.SetActive(false);
        UpdateScore(0);
        restartButton.onClick.AddListener(RestartGame);
    }

    public void UpdateScore(int score)
    {
        Debug.Log($"UpdateScore called: {score}");
        scoreText.text = score.ToString();
    }

    public void ShowGameOver()
    {
        gameOverPanel.SetActive(true);
    }

    void RestartGame()
    {
        GameManager.Instance.RestartGame();
    }
} 