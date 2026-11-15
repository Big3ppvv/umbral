using System;
using Umbra.Core;
using UnityEngine;

namespace Umbra.Unity
{
    // PlayerPrefs uses native platform storage, including IndexedDB in WebGL.
    // This save belongs to the Unity edition; it does not require a Python API.
    public sealed class ProgressStore
    {
        private const string StorageKey = "umbra.unity.profile.v1";
        private bool hadSavedValue;
        private bool canWrite;
        private string lastSavedJson;
        public Progression Profile { get; private set; }
        public string Status { get; private set; }
        public bool Persistent { get; private set; }

        public ProgressStore()
        {
            Profile = new Progression();
            try
            {
                hadSavedValue = PlayerPrefs.HasKey(StorageKey);
                if (hadSavedValue)
                {
                    lastSavedJson = PlayerPrefs.GetString(StorageKey, "");
                    ProgressData data = JsonUtility.FromJson<ProgressData>(lastSavedJson);
                    if (!Progression.IsValid(data))
                    {
                        Status = "El guardado no es válido y se conserva sin modificar. El progreso nuevo solo durará esta sesión.";
                        Persistent = false;
                        return;
                    }
                    Profile = new Progression(data);
                }
                Persistent = true;
                canWrite = true;
                Status = "Progreso guardado en este dispositivo.";
            }
            catch (Exception)
            {
                // In particular, never delete or replace malformed saved JSON.
                Persistent = false;
                Status = hadSavedValue
                    ? "No se pudo leer el guardado; se conserva sin modificar. Progreso solo en esta sesión."
                    : "El almacenamiento no está disponible. Progreso solo en esta sesión.";
            }
        }

        public bool Save(out string message)
        {
            if (!canWrite)
            {
                message = Status;
                return false;
            }
            if (!Progression.IsValid(Profile.Data))
            {
                Persistent = false;
                canWrite = false;
                Status = "El perfil actual no es válido. El guardado anterior se conserva sin modificar.";
                message = Status;
                return false;
            }
            try
            {
                string json = JsonUtility.ToJson(Profile.Data);
                PlayerPrefs.SetString(StorageKey, json);
                PlayerPrefs.Save();
                lastSavedJson = json;
                hadSavedValue = true;
                Persistent = true;
                Status = "Progreso guardado en este dispositivo.";
                message = Status;
                return true;
            }
            catch (Exception)
            {
                // Keep the last acknowledged value in PlayerPrefs' memory cache
                // if a storage-limit or platform error interrupted the flush.
                try
                {
                    if (hadSavedValue) PlayerPrefs.SetString(StorageKey, lastSavedJson);
                    else PlayerPrefs.DeleteKey(StorageKey);
                }
                catch (Exception) { }
                Persistent = false;
                Status = "No se pudo guardar en el dispositivo. Tu progreso actual sigue disponible durante esta sesión.";
                message = Status;
                return false;
            }
        }
    }
}
