using UnityEngine;
using TMPro;

public class ScoreSystem : MonoBehaviour
{
    [Header("TMP UI References")]
    [SerializeField] private TMP_Text carsDamagedText;
    [SerializeField] private TMP_Text timeSurvivedText;
    [SerializeField] private TMP_Text totalScoreText;

    [Header("Scoring")]
    [SerializeField] private bool roundTimeToInt = true; 
    [SerializeField] private string carsPrefix = "Cars Damaged: ";
    [SerializeField] private string timePrefix = "Time Survived: ";
    [SerializeField] private string totalPrefix = "Total Score: ";

    private int carsDamaged = 0;
    private float timeSurvived = 0f;

    private bool isRunning = true;

    void Start()
    {
        timeSurvived = 0f;
        carsDamaged = 0;
        UpdateUI();
    }

    void Update()
    {
        if (!isRunning) return;

        timeSurvived += Time.deltaTime;
        UpdateUI();
    }

    private void UpdateUI()
    {
        float shownTime = roundTimeToInt ? Mathf.Floor(timeSurvived) : timeSurvived;
        int totalScore = Mathf.RoundToInt(carsDamaged * shownTime); // Example scoring formula

        if (carsDamagedText != null)
            carsDamagedText.text = carsPrefix + carsDamaged;

        if (timeSurvivedText != null)
            timeSurvivedText.text = timePrefix + shownTime.ToString(roundTimeToInt ? "0" : "0.0");

        if (totalScoreText != null)
            totalScoreText.text = totalPrefix + totalScore;
    }

    
    public void AddCarDamaged(int amount = 1)
    {
        carsDamaged += amount;
        if (carsDamaged < 0) carsDamaged = 0;
        UpdateUI();
    }

    public int GetCarsDamaged() => carsDamaged;
    public float GetTimeSurvived() => timeSurvived;

    // (game over)
    public void StopScore()
    {
        isRunning = false;
        UpdateUI();
    }

    // reset for new round
    public void ResetScore()
    {
        carsDamaged = 0;
        timeSurvived = 0f;
        isRunning = true;
        UpdateUI();
    }
}
