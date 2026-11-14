using UnityEngine;
using TMPro;

namespace FireAndBrimstone
{
    /// <summary>
    /// Drives the back-wall World-Space HUD inside each RoomEscape_Arena.
    /// Displays Stage Name, Attempt #, Time Remaining, and Objective Status in view of the fixed wide camera.
    /// </summary>
    public class ArenaHUD : MonoBehaviour
    {
        [Header("TextMeshPro UI References (World Space Canvas on Back Wall)")]
        public TextMeshProUGUI stageTitleText;
        public TextMeshProUGUI attemptCounterText;
        public TextMeshProUGUI timerText;
        public TextMeshProUGUI statusBannerText;

        [Header("Timer Color Thresholds")]
        public Color normalTimerColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        public Color lowTimerWarningColor = new Color(1.0f, 0.3f, 0.1f, 1f);

        public void UpdateStageAndAttempt(string stageTitle, int attemptNumber, string initialStatus)
        {
            if (stageTitleText != null)
            {
                stageTitleText.text = stageTitle;
            }
            if (attemptCounterText != null)
            {
                attemptCounterText.text = $"ATTEMPT #{attemptNumber:D4}";
            }
            if (statusBannerText != null)
            {
                statusBannerText.text = initialStatus;
            }
        }

        public void UpdateTimerDisplay(float remainingSeconds)
        {
            if (timerText == null) return;

            timerText.text = $"TIME: {remainingSeconds:00.0}s";
            timerText.color = (remainingSeconds <= 8.0f) ? lowTimerWarningColor : normalTimerColor;
        }

        public void SetStatusBanner(string bannerMessage)
        {
            if (statusBannerText != null)
            {
                statusBannerText.text = bannerMessage;
            }
        }
    }
}