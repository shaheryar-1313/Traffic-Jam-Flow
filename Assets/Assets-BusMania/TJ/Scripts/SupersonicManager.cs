using System;
using SupersonicWisdomSDK;
using TJ.Scripts;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PluginScripts
{
    public class SupersonicManager : MonoBehaviour
    {
        public static SupersonicManager Instance { get; private set; }

        private void Awake()
        {
            if (!Instance)
                Instance = this;

            DontDestroyOnLoad(gameObject);

            SupersonicWisdom.Api.Initialize();

            // Directly continue after initialization
            OnInitialized();
        }

        public void LevelStart(int levelNum)
        {}

        // SupersonicWisdom.Api.NotifyLevelStarted(ESwLevelType.Regular, levelNum, null);


        public void LevelCompleted(int levelNum) 
        {}
            // SupersonicWisdom.Api.NotifyLevelCompleted(ESwLevelType.Regular, levelNum, null);

        public void LevelFail(int levelNum) 
        {}
            // SupersonicWisdom.Api.NotifyLevelFailed(ESwLevelType.Regular, levelNum, null);

        public void LevelRevived(int levelNum) 
        {}
            // SupersonicWisdom.Api.NotifyLevelRevived(ESwLevelType.Regular, levelNum, null);

        private void OnInitialized()
        {
            Debug.Log("Initialized");

            LoadLevel();
        }

        private void LoadLevel()
        {
            LevelManager.LoadScene();
        }

        private void OnDestroy()
        {
            // Prevent event leaks
            // SupersonicWisdom.Api.OnReady -= OnInitialized;
        }
    }
}