using System;
using TMPro;
using UnityEngine;

namespace LudumDare60.ReturnWindow
{
    /// <summary>Dated paper calendar. Completed dates receive crosses; only the deadline receives a ring.</summary>
    public sealed class ReturnCalendarView : MonoBehaviour
    {
        public const int CellsPerPage = 20;
        [SerializeField, Min(1)] private int dayCount = MissionSchedule.DefaultDays;
        [SerializeField] private TMP_Text currentDayLabel;
        [SerializeField] private TMP_Text[] dayLabels = Array.Empty<TMP_Text>();
        [SerializeField] private LineRenderer[] firstStrokes = Array.Empty<LineRenderer>();
        [SerializeField] private LineRenderer[] secondStrokes = Array.Empty<LineRenderer>();
        [SerializeField] private Transform currentDayMarker;
        [SerializeField] private Transform lastDayCircle;
        [SerializeField] private GameObject normalContent;
        [SerializeField] private GameObject sowingContent;
        [SerializeField] private GameObject[] checklistTicks = Array.Empty<GameObject>();
        [SerializeField] private AudioSource pencilAudio;
        [SerializeField, Min(.05f)] private float strokeDuration = .24f;

        private Vector3[] firstEnds;
        private Vector3[] secondEnds;
        private float[] strokeProgress;
        private bool[] completed;
        private bool initialized;
        private int page = -1;
        private bool sowing;
        public int DayCount { get => dayCount; set => dayCount = Mathf.Max(1, value); }
        public DateTime StartDate { get; set; } = new DateTime(1981, 8, 25);
        public bool DepartureConfirmed { get; set; }
        // Negative keeps the prototype's salvage/repair/launch checklist.
        public int RepairChecklistMask { get; set; } = -1;
        public bool StrokeAnimationPaused { get; set; }

        public bool IsCrossAnimationComplete(int oneBasedDay)
        {
            int cell = oneBasedDay - 1 - page * CellsPerPage;
            return initialized && cell >= 0 && cell < completed.Length &&
                   completed[cell] && strokeProgress[cell] >= 2f;
        }

        public void Configure(TMP_Text caption, TMP_Text[] labels, LineRenderer[] first, LineRenderer[] second,
            Transform marker, Transform finalCircle, GameObject plan, GameObject sowingPage,
            GameObject[] ticks, AudioSource audio, int days = MissionSchedule.DefaultDays)
        {
            currentDayLabel = caption; dayLabels = labels; firstStrokes = first; secondStrokes = second;
            currentDayMarker = marker; lastDayCircle = finalCircle; normalContent = plan;
            sowingContent = sowingPage; checklistTicks = ticks; pencilAudio = audio;
            dayCount = Mathf.Max(1, days); initialized = false; page = -1;
        }

        private void Initialize()
        {
            if (initialized) return;
            int count = Mathf.Min(firstStrokes.Length, secondStrokes.Length);
            firstEnds = new Vector3[count]; secondEnds = new Vector3[count];
            strokeProgress = new float[count]; completed = new bool[count];
            for (int i = 0; i < count; i++)
            {
                firstEnds[i] = firstStrokes[i].GetPosition(1);
                secondEnds[i] = secondStrokes[i].GetPosition(1);
                firstStrokes[i].enabled = false; secondStrokes[i].enabled = false;
            }
            initialized = true;
        }

        public void Render(int completedDays, int currentDay, bool isSowing, float normalizedRepairProgress = 0f)
        {
            Initialize();
            completedDays = Mathf.Clamp(completedDays, 0, dayCount);
            currentDay = Mathf.Clamp(currentDay, 1, dayCount);
            sowing = isSowing;
            if (normalContent) normalContent.SetActive(true);
            if (sowingContent) sowingContent.SetActive(sowing);
            if (currentDayLabel) currentDayLabel.text = sowing
                ? MissionSchedule.Label(StartDate.AddDays(dayCount)) + " / SOW"
                : MissionSchedule.Label(StartDate.AddDays(currentDay - 1));

            int nextPage = (currentDay - 1) / CellsPerPage;
            bool changedPage = nextPage != page;
            page = nextPage;
            for (int i = 0; i < dayLabels.Length; i++)
            {
                int day = page * CellsPerPage + i + 1;
                if (dayLabels[i]) dayLabels[i].text = day <= dayCount ? MissionSchedule.CellLabel(StartDate.AddDays(day - 1)) : "--";
            }
            for (int i = 0; i < completed.Length; i++)
            {
                bool crossed = page * CellsPerPage + i < completedDays;
                if (changedPage || sowing || DepartureConfirmed) { completed[i] = crossed; strokeProgress[i] = crossed ? 2f : 0f; ApplyStroke(i); }
                else if (crossed && !completed[i])
                {
                    completed[i] = true; strokeProgress[i] = 0f;
                    if (pencilAudio && pencilAudio.clip && !pencilAudio.isPlaying) pencilAudio.Play();
                }
            }
            if (currentDayMarker)
            {
                currentDayMarker.gameObject.SetActive(completedDays < dayCount);
                currentDayMarker.localPosition = CellPosition((currentDay - 1) % CellsPerPage) + new Vector3(0, -.0115f, .013f);
            }
            if (lastDayCircle)
            {
                lastDayCircle.gameObject.SetActive((dayCount - 1) / CellsPerPage == page);
                lastDayCircle.localPosition = CellPosition((dayCount - 1) % CellsPerPage) + new Vector3(0, 0, .013f);
            }
            float progress = Mathf.Clamp01(normalizedRepairProgress);
            for (int i = 0; i < checklistTicks.Length; i++)
                if (checklistTicks[i]) checklistTicks[i].SetActive(RepairChecklistMask >= 0
                    ? (RepairChecklistMask & (1 << i)) != 0
                    : i == 0 ? progress > 0f : i == 3 ? DepartureConfirmed : progress >= 1f);
        }

        // Front faces +Z. Viewed from the front, visual right is local -X.
        public static Vector3 CellPosition(int cell) => new Vector3(.093f - (cell % 4) * .062f, .236f - (cell / 4) * .033f, 0);

        private void Update()
        {
            if (!initialized || sowing || StrokeAnimationPaused) return;
            for (int i = 0; i < completed.Length; i++)
            {
                if (!completed[i] || strokeProgress[i] >= 2f) continue;
                strokeProgress[i] = Mathf.Min(2f, strokeProgress[i] + Time.deltaTime / strokeDuration);
                ApplyStroke(i);
            }
        }

        private void ApplyStroke(int i)
        {
            float t = strokeProgress[i];
            firstStrokes[i].enabled = t > 0f;
            secondStrokes[i].enabled = t > 1f;
            firstStrokes[i].SetPosition(1, Vector3.Lerp(firstStrokes[i].GetPosition(0), firstEnds[i], Mathf.Clamp01(t)));
            secondStrokes[i].SetPosition(1, Vector3.Lerp(secondStrokes[i].GetPosition(0), secondEnds[i], Mathf.Clamp01(t - 1f)));
        }
    }
}
