using System.Collections.Generic;

/// <summary>Everything the game keeps between sessions (one JSON file, see SaveSystem). JsonUtility: no dictionaries.</summary>
[System.Serializable]
public class SaveData
{
    public int version = SaveSystem.CurrentVersion;
    public int coins;
    public long lifetimeCoins;
    [UnityEngine.Tooltip("1 = only the run start. Checkpoint N is unlocked by going through portal N - 1.")]
    public int highestCheckpointUnlocked = 1;
    [UnityEngine.Tooltip("Set when a new checkpoint is unlocked; the menu plays its celebration and clears it (0 = none).")]
    public int newlyUnlockedCheckpoint;
    public List<CheckpointSnapshot> snapshots = new List<CheckpointSnapshot>();
    public List<string> ownedSkins = new List<string> { SaveSystem.DefaultSkin };
    public string equippedSkin = SaveSystem.DefaultSkin;
    public SaveSettings settings = new SaveSettings();
}

[System.Serializable]
public class SaveSettings
{
    public float musicVolume = 0.8f;
    public float sfxVolume = 1f;
    public bool haptics = true;
}

/// <summary>
/// The run at the exact moment the runner came out of portal (checkpoint - 1), in MainScene prop camera 3.6: what a
/// checkpoint start must reproduce. Written by CheckpointRunController on DoorCrossed. The first snapshot a NORMAL run
/// records is kept as the reference; a checkpoint-start run only fills a missing one.
/// </summary>
[System.Serializable]
public class CheckpointSnapshot
{
    public int checkpoint;
    public int portal;
    public string scene;
    public string savedUtc;
    [UnityEngine.Tooltip("True when this run itself began at a checkpoint.")]
    public bool fromCheckpointStart;
    public int startedFromCheckpoint;
    public float runSeconds;

    [UnityEngine.Header("Progress")]
    public int legIndex;          // DynamicPathGenerator.Score as the door is crossed (= the entry runway; the runway out scores +1 on arrival)
    public int displayScore;      // Score + close-call bonus points
    public int segmentSerial;     // = exitSegmentSerial (the legs past it are laid by the generator as it goes)
    public int exitSegmentSerial; // the runway out of the door
    public int exitRunwayCubes;
    public int runnerSide;

    [UnityEngine.Header("Speed and difficulty")]
    public float baseSpeed;       // runner.forwardSpeed (before boost / cinematic factors)
    public float speedForLeg;     // runner.SpeedForLeg(legIndex): the formula a checkpoint start uses
    public float moveSpeed;       // what the runner actually moved at
    public float maxSpeed;
    public int difficultyTier;
    public string difficultyProfile;
    public int rngSeed;           // 0 = unseeded (every run is random, 3.6 adds no seeding)
    public GeneratorState generator = new GeneratorState();

    [UnityEngine.Header("Zone and camera")]
    public int portalsTaken;
    public string lookName;       // the look this portal wipes into
    public int cameraMode;
    public float cameraYaw;
}

/// <summary>DynamicPathGenerator's per-run counters (DynamicPathGenerator.Checkpoint.cs fills and applies it).</summary>
[System.Serializable]
public class GeneratorState
{
    public int legsBetweenPortals, nextPortalSerial;
    public int highestScoredSerial, turnBiasBlock, leftWeight, rightWeight, upWeight, downWeight;
    public int bonusPoints, closeCallStreak;
    // waves + hazard budget
    public int cycleIndex, cycleStartSerial, breatherFrom, breatherUntil, lastHeavyEndSerial;
    public bool taughtDecoy, taughtBridge, taughtMulti, peakBoostDone, buildBoostDone;
    public int decoysThisCycle, lastDecoySerial, family, peelSide;
    public float budgetCredit, budgetSpent;
    public int budgetPhaseKey;
    // boosts, power diamond, shield
    public int nextBoostSerial, boostPlanIndex, boostPlanCount, boostSerialCounter;
    public int powerNextSerial, powerQuietUntil, powerQuietFrom, powerHandoverSerial;
    public int lastPowerUpSerial, shieldNextSerial, shieldWaitLegs;
    public bool shieldRolled, shieldWaitHazard;
}
