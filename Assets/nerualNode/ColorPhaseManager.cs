using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class ColorPhaseManager : MonoBehaviour
{
    public static ColorPhaseManager Instance;

    private int currentPaletteIndex = 0;
    private NeuralGlobeGenerator generator;

    void Awake() => Instance = this;

    public void Initialize(NeuralGlobeGenerator gen)
    {
        generator = gen;
        currentPaletteIndex = generator.coloredPairCount - 1; // Start with initially active colors
    }

    public void CheckColorCompletion(Color color)
    {
        if (generator == null || !generator.level2) return;

        var nodes = generator.GetNodes();
        int count = nodes.Count(n => !n.isNeutral && n.nodeColor == color);

        if (count == 1)
        {
            NodeView lastNode = nodes.FirstOrDefault(n => !n.isNeutral && n.nodeColor == color);
            if (lastNode != null)
            {
                currentPaletteIndex++;
                if (currentPaletteIndex < generator.nodePalette.Length)
                {
                    Color nextColor = generator.nodePalette[currentPaletteIndex];
                    ActivateNextPhase(lastNode, nextColor);
                }
                else
                {
                    // No more colors in palette, destroy the final node of this color
                    lastNode.DestroyWithBurst();
                    generator.GetNodes().Remove(lastNode);
                }
            }
        }
        
        // Final Win Condition for Level 2: No non-neutral nodes left
        if (nodes.Count(n => !n.isNeutral) == 0)
        {
            Debug.Log("[NeuralScene] Level 2 Cleared - Every node destroyed!");
            NeuralUIManager.Instance?.OnWin();
        }
    }

    void ActivateNextPhase(NodeView origin, Color nextColor)
    {
        origin.SetColor(nextColor);
        StartCoroutine(RippleEffect(origin.transform.position));

        var whiteNodes = generator.GetNodes()
            .Where(n => n.isNeutral)
            .OrderBy(n => Vector3.Distance(n.transform.position, origin.transform.position))
            .Take(Random.Range(2, 6))
            .ToList();

        foreach (var wn in whiteNodes)
        {
            wn.ActivateFromWhite(nextColor);
        }
        Debug.Log($"[ColorPhaseManager] Phase Complete! New Color: {nextColor}");
    }

    System.Collections.IEnumerator RippleEffect(Vector3 center)
    {
        float dur = 1.2f;
        float elapsed = 0;
        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            float radius = (elapsed / dur) * generator.radius * 2f;
            foreach (var node in generator.GetNodes())
            {
                float dist = Vector3.Distance(node.transform.position, center);
                if (Mathf.Abs(dist - radius) < 0.5f)
                {
                    node.Shake();
                }
            }
            yield return null;
        }
    }
}
