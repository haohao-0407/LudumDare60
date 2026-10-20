using UnityEngine;

namespace LudumDare60.Audio
{
    /// <summary>
    /// 全局音乐音量。设置页的 BGM 滑条写这里，所有音乐播放器在算最终音量时乘上它。
    ///
    /// 默认 0：玩家不主动调高就没有音乐（按需求）。和 AudioListener.volume（总音量）分开，
    /// 总音量只影响全部声音，BGM 只影响音乐层。
    /// </summary>
    public static class MusicBus
    {
        private static float volume = 0f;

        /// <summary>0~1。改动会立刻影响正在播的音乐（播放器每帧读取）。</summary>
        public static float Volume
        {
            get { return volume; }
            set { volume = Mathf.Clamp01(value); }
        }
    }
}
