using UnityEngine;

namespace My4KGame
{
    /// <summary>
    /// 游戏入口。挂在场景里一个空物体上，播放时把整个游戏"长"出来。
    ///
    /// 这么做的好处是：场景文件可以小到只有一个物体，不怕丢失引用；
    /// 而且任何一个同学随手新建一个空场景、挂上这个脚本，就能跑起完整游戏。
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    [AddComponentMenu("My4KGame/Game Bootstrap")]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("进入播放时自动补齐 / 修复场景里的物体。关掉它 = 完全依赖场景里手摆的东西。")]
        public bool autoBuild = true;

        [Tooltip("限制帧率（0 = 不限制，跟随垂直同步）。音游建议不低于 60。")]
        public int targetFrameRate = 0;

        private void Awake()
        {
            Application.runInBackground = true;

            if (targetFrameRate > 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = targetFrameRate;
            }

            if (autoBuild) WorldBuilder.Build(transform);
        }
    }
}
