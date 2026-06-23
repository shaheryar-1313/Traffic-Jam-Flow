using UnityEngine;

namespace Game
{
    /// <summary>
    /// An abstract class that provides base functionalities of a singleton for its derived classes
    /// </summary>
    /// <typeparam name="T">The type of singleton instance</typeparam>
    public abstract class Singleton<T> : MonoBehaviour where T : Component
    {
        private static T s_Instance;

        private static bool s_applicationIsQuitting = false;

        public static T Instance
        {
            get
            {
                if (s_applicationIsQuitting)
                {
                    return null;
                }

                if (s_Instance == null)
                {
                    s_Instance = FindFirstObjectByType<T>();
                    if (s_Instance == null)
                    {
                        GameObject gameObject = new GameObject() { name = typeof(T).Name };
                        s_Instance = gameObject.AddComponent<T>();
                    }
                }

                return s_Instance;
            }
        }

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                if (Application.isPlaying)
                {
                    Destroy(s_Instance.gameObject);
                }
                else
                {
                    DestroyImmediate(s_Instance.gameObject);
                }
            }

            s_Instance = this as T;
            OnPostAwake();
        }

        protected virtual void OnPostAwake() => _ = 0;

        protected virtual void OnDestroy()
        {
            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        protected virtual void OnApplicationQuit()
        {
            s_applicationIsQuitting = true;
        }
    }
}