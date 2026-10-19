#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace My4KGame.EditorTools
{
    /// <summary>
    /// 打开工程时自动进游戏场景 —— 专治"打开 Unity 看到一片蓝天灰地、按 ▶ 什么也没发生"。
    ///
    /// 两个保险：
    ///   1. 如果编辑器停在一个还没保存的 Untitled 空场景上，直接打开 Assets/Scenes/My4KGame.unity；
    ///   2. 顺便把"播放模式启动场景"设成它 —— 这样哪怕你手上开着别的场景，按 ▶ 也会加载游戏场景。
    /// 每个编辑器会话只自动跳一次（SessionState 记着），不会跟你抢着开场景。
    /// </summary>
    [InitializeOnLoad]
    public static class AutoOpenGameScene
    {
        private const string ScenePath = "Assets/Scenes/My4KGame.unity";
        private const string SessionKey = "My4KGame.AutoOpenedScene";

        static AutoOpenGameScene()
        {
            EditorApplication.delayCall += OnEditorReady;
        }

        private static void OnEditorReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            // 正在编译 / 正在导入资源：等下一帧再看
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += OnEditorReady;
                return;
            }

            EnsurePlayModeStartScene();
            PruneMissingBuildScenes();

            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);

            if (!File.Exists(ScenePath)) return;

            Scene active = SceneManager.GetActiveScene();
            bool untitled = string.IsNullOrEmpty(active.path);
            if (!untitled) return;   // 已经开了某个场景，不动它

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[My4KGame] 已自动打开游戏场景：" + ScenePath +
                      "。直接按顶部的 ▶ 播放键，选难度开始玩。");
        }

        [MenuItem("My4KGame/0. 打开游戏场景（按 ▶ 就能玩）", false, 0)]
        public static void OpenGameSceneMenu()
        {
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("My4KGame",
                    "找不到场景文件：" + ScenePath + "\n\n先执行菜单 My4KGame → 1. 一键生成全部。", "好");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log("[My4KGame] 已打开游戏场景：" + ScenePath);
        }

        /// <summary>清掉 Build Settings 里指向不存在场景的死条目（打包时会报"场景找不到"）。</summary>
        private static void PruneMissingBuildScenes()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes == null || scenes.Length == 0) return;

            var kept = new List<EditorBuildSettingsScene>(scenes.Length);
            bool removed = false;
            foreach (EditorBuildSettingsScene scene in scenes)
            {
                if (File.Exists(scene.path)) kept.Add(scene);
                else removed = true;
            }

            if (!removed || kept.Count == 0) return;

            EditorBuildSettings.scenes = kept.ToArray();
            Debug.Log("[My4KGame] Build Settings 里清理了 " + (scenes.Length - kept.Count) + " 条无效场景记录。");
        }

        /// <summary>把"播放模式启动场景"设为游戏场景（只在它是空的时候设，尊重用户后来的选择）。</summary>
        private static void EnsurePlayModeStartScene()
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null) return;
            if (EditorSceneManager.playModeStartScene == sceneAsset) return;
            if (EditorSceneManager.playModeStartScene != null) return;

            EditorSceneManager.playModeStartScene = sceneAsset;
            Debug.Log("[My4KGame] 已把「播放模式启动场景」设为 " + ScenePath +
                      "（所以无论当前开着哪个场景，按 ▶ 都会进游戏）");
        }
    }
}
#endif
