using UnityEngine;

public enum SkinRarity { Common, Rare, Epic, Legendary }

/// <summary>
/// A cosmetic cube (no stats): how the runner looks, trails, sounds and breaks. Used by the runner in MainScene prop camera
/// 3.6 (SkinApplier), the menu's ghost runners (MenuDioramaRig.ApplyGhostSkin) and the shop stage. Ids are what the save
/// stores (SaveData.ownedSkins / equippedSkin), so never rename one that has shipped.
/// </summary>
[CreateAssetMenu(menuName = "Checkpoints/Cube Skin", fileName = "Skin_")]
public class CubeSkin : ScriptableObject
{
    public string id = "classic";
    public string displayName = "Classic";
    [Tooltip("One line of personality, shown in the shop.")]
    public string tagline = "";
    public SkinRarity rarity = SkinRarity.Common;
    [Min(0)] public int price;

    [Header("Look")]
    [Tooltip("Null = the built-in cube.")]
    public Mesh mesh;
    [Tooltip("The cube's material (shop, icons; and in game unless keepSceneLook).")]
    public Material material;
    [Tooltip("In game and on the menu ghosts, keep the scene's own player material (the checkpoint look drives it). Classic.")]
    public bool keepSceneLook;
    [Tooltip("Main colour, for cards, icons and the trail when it overrides the look's.")]
    public Color uiColor = Color.white;

    [Header("Soul (character)")]
    [Tooltip("A character cube with a face, clothes and cloth (SoulCharacter at its root). Set = drawn in place of the cube " +
             "in game, in the shop and on the card icon; mesh / material are then only a fallback.")]
    public GameObject characterPrefab;
    [Tooltip("characterPrefab is only the shop / card picture: in a run the scene's own player (3.10 / 3.11's totem) stays as it is " +
             "and nothing is spawned or hidden. The default skin uses this to show the base character.")]
    public bool sceneCharacter;

    [Header("Trail")]
    [Tooltip("Off = the checkpoint look colours the trail (as before skins).")]
    public bool overrideTrail;
    public Color trailHead = Color.white, trailMid = Color.white, trailTail = new Color(1f, 1f, 1f, 0f);
    [Tooltip("Particles that follow the cube (embers, frost, stars). Null = none.")]
    public GameObject attachedParticles;

    [Header("Feel")]
    public AudioClip rollSfx;
    [Range(0f, 0.4f)] public float landingSquash = 0.08f;
    [Tooltip("Played where the cube breaks on a death. Null = the game's own shatter only.")]
    public GameObject deathBurstPrefab;

    public Color RarityColor => RarityColorOf(rarity);

    public static Color RarityColorOf(SkinRarity r) => r switch
    {
        SkinRarity.Rare => new Color(0.36f, 0.82f, 1f, 1f),
        SkinRarity.Epic => new Color(0.78f, 0.5f, 1f, 1f),
        SkinRarity.Legendary => new Color(1f, 0.6f, 0.22f, 1f),
        _ => new Color(0.86f, 0.9f, 0.96f, 1f),
    };
}
