using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LudumDare60.ReturnWindow
{
    public sealed class ReturnEndingView : MonoBehaviour
    {
        public CanvasGroup overlay;
        public TMP_Text heading;
        public TMP_Text body;
        public ReturnPilotController pilot;
        public Transform stayedLookTarget;
        public string missionScene = "ReturnWindow";
        private bool shown;

        public void Show(MissionState state, MissionSchedule schedule)
        {
            if (shown) return;
            shown = true;
            pilot.Ending = true;
            pilot.SetMenu(false);
            heading.text = state == MissionState.Departed ? "THE WAY HOME" : "ANOTHER TOMORROW";
            body.text = state == MissionState.Departed
                ? "You reach the waiting orbit.\nLater, inside the planned return window,\nthe Earth-return burn begins.\n\nEarth has not answered yet.\nYou do not know whether home remembers you.\nBut you are finally on your way."
                : "The departure deadline has passed.\nNext study period: " + schedule.NextOpportunityStart.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant()
                    + ".\nThe damaged craft must be reassessed.\n\nStored food cannot cover the wait.\nThe AI asks about the greenhouse.\n\n" + MissionSchedule.Label(schedule.SowingDate) + ": plant the first seed.";
            overlay.gameObject.SetActive(true);
            StartCoroutine(Reveal(state));
        }

        private IEnumerator Reveal(MissionState state)
        {
            Quaternion from = pilot.transform.rotation;
            Quaternion to = state == MissionState.Stayed && stayedLookTarget != null
                ? Quaternion.LookRotation(stayedLookTarget.position - pilot.transform.position, Vector3.up)
                : from * Quaternion.Euler(-12, 0, 0);
            float elapsed = 0;
            overlay.alpha = 0;
            while (elapsed < 2.5f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / 2.5f);
                pilot.transform.rotation = Quaternion.Slerp(from, to, t);
                overlay.alpha = Mathf.Clamp01((elapsed - .5f) / 2);
                yield return null;
            }
            overlay.alpha = 1;
        }

        public void RestartMission()
        {
            StopAllCoroutines();
            Time.timeScale = 1;
            var current = MissionController.Instance;
            if (current != null) Destroy(current.gameObject);
            StartCoroutine(Reload());
        }

        private IEnumerator Reload()
        {
            yield return null; // Destroy the persistent run before the next scene's Awake.
            SceneManager.LoadScene(missionScene);
        }
    }
}
