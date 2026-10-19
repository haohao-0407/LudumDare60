using UnityEngine;
namespace LudumDare60.ReturnWindow
{
    public sealed class ReturnResumeButton : MonoBehaviour
    {
        public ReturnPilotController pilot;
        public void Resume() => pilot.SetMenu(false);
    }
}
