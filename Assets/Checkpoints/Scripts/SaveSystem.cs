using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// The one save file (Application.persistentDataPath/save.json). Loaded once, on first use. Every change marks it dirty
/// and SaveSystemHost writes it at the end of that frame (so several changes in one frame are one write), and on
/// OnApplicationPause(true) / OnApplicationQuit. Writes are atomic: the JSON goes to save.json.tmp, then replaces
/// save.json (the old one is kept as save.json.bak and read if the main file is ever missing or broken).
/// </summary>
public static class SaveSystem
{
    public const int CurrentVersion = 1;
    public const string DefaultSkin = "classic";
    const string FileName = "save.json";

    static SaveData data;
    static bool dirty;
    static string fileName = FileName;

    public static string FilePath => Path.Combine(Application.persistentDataPath, fileName);
    public static SaveData Data { get { EnsureLoaded(); return data; } }
    public static bool IsDirty => dirty;

    /// <summary>Raised after any change (wallet, unlock, skins, settings).</summary>
    public static event Action Changed;
    /// <summary>Raised right before the game is paused/quit, so runs can bank their coins first.</summary>
    public static event Action BeforeAppSuspend;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { data = null; dirty = false; fileName = FileName; Changed = null; BeforeAppSuspend = null; }

    public static void EnsureLoaded()
    {
        if (data != null) return;
        data = Load();
        if (Application.isPlaying) SaveSystemHost.Ensure();
    }

    static SaveData Load()
    {
        SaveData d = TryRead(FilePath) ?? TryRead(FilePath + ".bak");
        if (d == null) return new SaveData();
        Migrate(d);
        return d;
    }

    static SaveData TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonUtility.FromJson<SaveData>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SaveSystem] could not read " + path + ": " + e.Message);
            return null;
        }
    }

    static void Migrate(SaveData d)
    {
        // v1 is the first version. Future versions convert old fields here, then bump d.version.
        if (d.snapshots == null) d.snapshots = new System.Collections.Generic.List<CheckpointSnapshot>();
        if (d.ownedSkins == null || d.ownedSkins.Count == 0) d.ownedSkins = new System.Collections.Generic.List<string> { DefaultSkin };
        if (!d.ownedSkins.Contains(DefaultSkin)) d.ownedSkins.Add(DefaultSkin);
        if (string.IsNullOrEmpty(d.equippedSkin) || !d.ownedSkins.Contains(d.equippedSkin)) d.equippedSkin = DefaultSkin;
        if (d.settings == null) d.settings = new SaveSettings();
        d.highestCheckpointUnlocked = Mathf.Max(1, d.highestCheckpointUnlocked);
        d.version = CurrentVersion;
    }

    /// <summary>Something changed: it is written at the end of this frame (or now, outside play mode).</summary>
    public static void MarkDirty()
    {
        dirty = true;
        Changed?.Invoke();
        if (!Application.isPlaying) Flush();
    }

    /// <summary>Writes the file if anything changed since the last write.</summary>
    public static void Flush()
    {
        if (!dirty || data == null) return;
        dirty = false;
        Write(data);
    }

    /// <summary>The app is being paused or quit (SaveSystemHost; tests may call it): runs bank, then the file is written.</summary>
    public static void NotifySuspend()
    {
        try { BeforeAppSuspend?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
        Flush();
    }

    static void Write(SaveData d)
    {
        string path = FilePath, tmp = path + ".tmp", bak = path + ".bak";
        try
        {
            string json = JsonUtility.ToJson(d, true);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, bak); }
                catch (Exception)
                {
                    // Some file systems have no atomic replace: keep a backup copy, then swap.
                    File.Copy(path, bak, true);
                    File.Delete(path);
                    File.Move(tmp, path);
                }
            }
            else File.Move(tmp, path);
        }
        catch (Exception e)
        {
            dirty = true;   // try again next time
            Debug.LogError("[SaveSystem] write failed: " + e.Message);
        }
    }

    // ------------------------------------------------------------------ wallet

    public static int Coins => Data.coins;

    public static void AddCoins(int amount)
    {
        if (amount <= 0) return;
        Data.coins += amount;
        data.lifetimeCoins += amount;
        MarkDirty();
    }

    public static bool TrySpend(int amount)
    {
        if (amount < 0 || Data.coins < amount) return false;
        data.coins -= amount;
        MarkDirty();
        return true;
    }

    // ------------------------------------------------------------------ checkpoints

    public static int HighestCheckpoint => Data.highestCheckpointUnlocked;
    public static bool IsUnlocked(int checkpoint) => checkpoint <= Data.highestCheckpointUnlocked;

    public static CheckpointSnapshot GetSnapshot(int checkpoint)
    {
        var list = Data.snapshots;
        for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].checkpoint == checkpoint) return list[i];
        return null;
    }

    /// <summary>
    /// The runner came out of the portal that reaches this checkpoint: unlock it (flagging it for the menu's celebration
    /// when it is new) and keep the snapshot. A normal run's snapshot is the reference: it replaces a missing one or one
    /// recorded by a checkpoint-start run; a checkpoint-start run only fills a missing one.
    /// </summary>
    public static void RecordCheckpoint(int checkpoint, CheckpointSnapshot snap, int maxCheckpoint)
    {
        if (checkpoint < 2 || checkpoint > maxCheckpoint) return;
        SaveData d = Data;
        if (checkpoint > d.highestCheckpointUnlocked)
        {
            d.highestCheckpointUnlocked = checkpoint;
            d.newlyUnlockedCheckpoint = Mathf.Max(d.newlyUnlockedCheckpoint, checkpoint);
        }
        if (snap != null)
        {
            CheckpointSnapshot old = GetSnapshot(checkpoint);
            if (old == null) d.snapshots.Add(snap);
            else if (old.fromCheckpointStart && !snap.fromCheckpointStart) d.snapshots[d.snapshots.IndexOf(old)] = snap;
        }
        MarkDirty();
    }

    /// <summary>The checkpoint the menu should celebrate (0 = none), cleared by the menu once played.</summary>
    public static int NewlyUnlocked => Data.newlyUnlockedCheckpoint;
    public static void ClearNewlyUnlocked()
    {
        if (Data.newlyUnlockedCheckpoint == 0) return;
        data.newlyUnlockedCheckpoint = 0;
        MarkDirty();
    }

    // ------------------------------------------------------------------ skins

    public static string EquippedSkin => Data.equippedSkin;
    public static bool Owns(string skinId) => Data.ownedSkins.Contains(skinId);

    public static void GrantSkin(string skinId)
    {
        if (string.IsNullOrEmpty(skinId) || Owns(skinId)) return;
        data.ownedSkins.Add(skinId);
        MarkDirty();
    }

    public static bool Equip(string skinId)
    {
        if (!Owns(skinId)) return false;
        if (data.equippedSkin == skinId) return true;
        data.equippedSkin = skinId;
        MarkDirty();
        return true;
    }

    // ------------------------------------------------------------------ dev

    /// <summary>Dev / tests: switch to another save file (the current one is written first). Null = the real save.</summary>
    public static void UseFile(string name)
    {
        Flush();
        fileName = string.IsNullOrEmpty(name) ? FileName : name;
        data = null;
        dirty = false;
    }

    /// <summary>Dev only: forget everything (the file is rewritten with defaults).</summary>
    public static void ResetAll()
    {
        data = new SaveData();
        MarkDirty();
        Flush();
    }
}
