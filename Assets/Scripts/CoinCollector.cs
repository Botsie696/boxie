using UnityEngine;

/// <summary>
/// The runner's coin count. DynamicPathGenerator finds the pickups (a distance test against the
/// runner, see DynamicPathGeneratorCoins.cs) and calls Collect; this keeps the score and tells the
/// counter in the top right.
///
/// Takes the place of the old game's three pickup paths (CoinCollector, the trigger handler in
/// CubeRailRunner and CoinPickup), of which only CoinCollector ever ran. Added to the runner by the
/// generator the first time coins are in play.
/// </summary>
public class CoinCollector : MonoBehaviour
{
    [Header("Settings")]
    public int scorePerCoin = 1;
    [Tooltip("Show the coin counter in the top right corner.")]
    public bool showCounter = true;

    [Header("Debug")]
    public int currentScore = 0;

    /// <summary>Raised on every pickup with the new total and where the coin was.</summary>
    public event System.Action<int, Vector3> CoinCollected;

    void Start()
    {
        if (showCounter && GetComponent<CoinCounterUI>() == null) gameObject.AddComponent<CoinCounterUI>();
    }

    public void Collect(Vector3 worldPosition)
    {
        currentScore += scorePerCoin;
        Haptics.LightImpact();
        CoinCollected?.Invoke(currentScore, worldPosition);
    }

    /// <summary>A coin worth several (the triangle gate's special coin): one pickup, one event, `coins` coins.</summary>
    public void Collect(Vector3 worldPosition, int coins)
    {
        currentScore += scorePerCoin * Mathf.Max(1, coins);
        Haptics.MediumImpact();
        CoinCollected?.Invoke(currentScore, worldPosition);
    }
}
