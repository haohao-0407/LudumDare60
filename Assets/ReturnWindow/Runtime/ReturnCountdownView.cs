using System;
using TMPro;
using Pioneer60.Localization;
using UnityEngine;

namespace LudumDare60.ReturnWindow
{
    /// <summary>Six physical seven-segment digits: DD HH MM, without rapidly changing seconds.</summary>
    public sealed class ReturnCountdownView : MonoBehaviour
    {
        [SerializeField] private Renderer[] segments = Array.Empty<Renderer>();
        [SerializeField] private Material litMaterial;
        [SerializeField] private Material unlitMaterial;
        [SerializeField] private Renderer warningLamp;
        [SerializeField] private TMP_Text stateLabel;
        [SerializeField] private AudioSource warningAudio;
        private static readonly int[] Masks = { 0x3f, 0x06, 0x5b, 0x4f, 0x66, 0x6d, 0x7d, 0x07, 0x7f, 0x6f };
        private long displayedMinute = -1;
        private int displayedState = -1;
        private bool warningPlayed;
        private bool lampOn;

        public void Configure(Renderer[] digitSegments, Material lit, Material unlit, Renderer lamp,
            TMP_Text status, AudioSource audio)
        {
            segments = digitSegments; litMaterial = lit; unlitMaterial = unlit;
            warningLamp = lamp; stateLabel = status; warningAudio = audio;
            displayedMinute = -1; displayedState = -1; warningPlayed = false;
        }

        public void Render(double remainingMissionSeconds, bool closed, bool departureLocked, bool warning)
        {
            if (double.IsNaN(remainingMissionSeconds) || double.IsInfinity(remainingMissionSeconds)) remainingMissionSeconds = 0;
            // Ceiling prevents a misleading all-zero readout during the final positive minute.
            long minutes = (long)Math.Ceiling(Math.Max(0, Math.Min(remainingMissionSeconds, 100d * 86400d - 60d)) / 60d);
            int state = closed ? 2 : departureLocked ? 1 : 0;
            if (minutes != displayedMinute || state != displayedState)
            {
                displayedMinute = minutes; displayedState = state;
                int days = (int)(minutes / 1440), hours = (int)(minutes / 60 % 24), mins = (int)(minutes % 60);
                int[] digits = { days / 10, days % 10, hours / 10, hours % 10, mins / 10, mins % 10 };
                for (int i = 0; i < segments.Length; i++)
                {
                    int digit = i / 7;
                    bool on = !closed && digit < digits.Length && (Masks[digits[digit]] & (1 << (i % 7))) != 0;
                    if (segments[i])
                    {
                        segments[i].gameObject.SetActive(!closed);
                        segments[i].sharedMaterial = on ? litMaterial : unlitMaterial;
                    }
                }
                if (stateLabel)
                {
                    stateLabel.text = Loc.Say(closed ? "WINDOW CLOSED" : departureLocked ? "DEPARTURE LOCKED" : "TIME TO LAST DEPARTURE");
                    stateLabel.transform.localPosition = new Vector3(0, closed ? .088f : .058f, .0655f);
                    stateLabel.fontSize = closed ? .19f : .084f;
                }
            }
            bool light = warning || closed;
            if (light != lampOn)
            {
                lampOn = light;
                if (warningLamp) warningLamp.sharedMaterial = light ? litMaterial : unlitMaterial;
            }
            if (warning && !warningPlayed && !closed && !departureLocked)
            {
                warningPlayed = true;
                if (warningAudio && warningAudio.clip) warningAudio.Play();
            }
        }
    }
}
