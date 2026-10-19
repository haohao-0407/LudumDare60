using UnityEngine;

namespace LudumDare60.Landing
{
    [CreateAssetMenu(menuName = "Pioneer 60/Orbit Credits", fileName = "OrbitCredits")]
    public sealed class OrbitCreditsDefinition : ScriptableObject
    {
        public string GameTitle = "PIONEER 60";
        public string ThanksMessage = "Thanks for playing";
        [TextArea(6, 30)] public string CreditsText = "SPECIAL THANKS\n\nTo everyone who played.";
        public string EndMessage = "TRANSMISSION COMPLETE";

        [Header("Scaled-time presentation")]
        [Min(0f)] public float OrbitDelay = 1.5f;
        [Min(0f)] public float FadeDuration = 1.2f;
        [Min(0f)] public float ThanksHoldDuration = 3f;
        [Tooltip("Pixels per second in the Canvas Scaler reference resolution.")]
        [Min(1f)] public float ScrollSpeed = 36f;
        [Min(0f)] public float CreditHoldDuration = 2f;
    }
}
