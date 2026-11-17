using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace FirefighterVR.Stats
{
    // One finished mission. Field names are the JSON keys read by the stats website.
    [Serializable]
    public sealed class MissionRunRecord
    {
        public string id = "";
        public long ts;                 // Unix milliseconds (UTC)
        public string utc = "";         // ISO-8601 UTC
        public string mode = "coop";    // coop | solo
        public string driver = "";
        public string assistant = "";
        public float missionSeconds;    // dispatch accepted -> hose stowed
        public float penaltySeconds;    // red lights crossed without siren x penalty
        public float totalSeconds;      // missionSeconds + penaltySeconds
        public int redLights;           // red lights crossed WITHOUT the siren
        public float drivingSeconds;
        public float sirenSeconds;
        public string incident = "";
        public string location = "";

        public static string NewId() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + UnityEngine.Random.Range(0x1000, 0xFFFF).ToString("x4");
        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
        }
    }

    // Local history (every headset keeps its own) + optional shared Firebase Realtime Database.
    // The database URL lives in Assets/Resources/MissionStatsConfig.json so it can be changed without code edits.
    public static class MissionStatsStore
    {
        [Serializable] sealed class Config { public string databaseUrl = ""; public string collection = "runs"; }
        [Serializable] sealed class LocalHistory { public List<MissionRunRecord> runs = new List<MissionRunRecord>(); }

        const string LocalFile = "mission_runs.json";
        const int LocalLimit = 300;
        static Config config;

        static Config Settings
        {
            get
            {
                if (config != null) return config;
                config = new Config();
                var asset = Resources.Load<TextAsset>("MissionStatsConfig");
                if (asset) { try { JsonUtility.FromJsonOverwrite(asset.text, config); } catch (Exception e) { Debug.LogWarning("[MissionStats] Bad MissionStatsConfig.json: " + e.Message); } }
                config.databaseUrl = (config.databaseUrl ?? "").Trim().TrimEnd('/');
                if (string.IsNullOrEmpty(config.collection)) config.collection = "runs";
                return config;
            }
        }
        public static bool RemoteConfigured => Settings.databaseUrl.StartsWith("https://");
        static string LocalPath => Path.Combine(Application.persistentDataPath, LocalFile);

        public static string CleanName(string name, string fallback)
        {
            name = (name ?? "").Replace("<", "").Replace(">", "").Replace("\n", " ").Trim();
            if (name.Length > 24) name = name.Substring(0, 24);
            return name.Length == 0 ? fallback : name;
        }

        public static List<MissionRunRecord> LoadLocal()
        {
            try
            {
                if (!File.Exists(LocalPath)) return new List<MissionRunRecord>();
                var history = JsonUtility.FromJson<LocalHistory>(File.ReadAllText(LocalPath));
                return history?.runs ?? new List<MissionRunRecord>();
            }
            catch (Exception e) { Debug.LogWarning("[MissionStats] Could not read local history: " + e.Message); return new List<MissionRunRecord>(); }
        }

        public static void SaveLocal(MissionRunRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.id)) return;
            try
            {
                var runs = LoadLocal();
                runs.RemoveAll(r => r.id == record.id);
                runs.Add(record);
                runs = runs.OrderBy(r => r.ts).ToList();
                if (runs.Count > LocalLimit) runs = runs.Skip(runs.Count - LocalLimit).ToList();
                File.WriteAllText(LocalPath, JsonUtility.ToJson(new LocalHistory { runs = runs }));
            }
            catch (Exception e) { Debug.LogWarning("[MissionStats] Could not save local history: " + e.Message); }
        }

        // PUT keeps the upload idempotent: retrying the same record never creates duplicates.
        public static IEnumerator Upload(MissionRunRecord record, Action<bool, string> done = null)
        {
            if (!RemoteConfigured) { done?.Invoke(false, "Stats database not configured"); yield break; }
            string url = $"{Settings.databaseUrl}/{Settings.collection}/{UnityWebRequest.EscapeURL(record.id)}.json";
            string body = JsonConvert.SerializeObject(record);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                using (var request = new UnityWebRequest(url, "PUT"))
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.timeout = 15;
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success) { done?.Invoke(true, "Saved online"); yield break; }
                    Debug.LogWarning($"[MissionStats] Upload attempt {attempt + 1} failed: {request.error}");
                }
                yield return new WaitForSecondsRealtime(2 + attempt * 3);
            }
            done?.Invoke(false, "Saved on this headset (offline)");
        }

        // Most recent finished missions, newest first. Online when configured, otherwise this headset's history.
        public static IEnumerator FetchRecent(int count, string excludeId, Action<List<MissionRunRecord>, bool> done)
        {
            if (RemoteConfigured)
            {
                string url = $"{Settings.databaseUrl}/{Settings.collection}.json?orderBy=%22%24key%22&limitToLast={count + 1}";
                using (var request = UnityWebRequest.Get(url))
                {
                    request.timeout = 12;
                    yield return request.SendWebRequest();
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        List<MissionRunRecord> runs = null;
                        try { runs = Parse(request.downloadHandler.text); } catch (Exception e) { Debug.LogWarning("[MissionStats] Could not parse online stats: " + e.Message); }
                        if (runs != null) { done(runs.Where(r => r.id != excludeId).OrderByDescending(r => r.ts).Take(count).ToList(), true); yield break; }
                    }
                    else Debug.LogWarning("[MissionStats] Fetch failed: " + request.error);
                }
            }
            done(LoadLocal().Where(r => r.id != excludeId).OrderByDescending(r => r.ts).Take(count).ToList(), false);
        }

        static List<MissionRunRecord> Parse(string json)
        {
            var list = new List<MissionRunRecord>();
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null") return list;
            var root = JObject.Parse(json);
            foreach (var property in root.Properties())
            {
                var record = property.Value.ToObject<MissionRunRecord>();
                if (record == null) continue;
                if (string.IsNullOrEmpty(record.id)) record.id = property.Name;
                list.Add(record);
            }
            return list;
        }
    }
}
