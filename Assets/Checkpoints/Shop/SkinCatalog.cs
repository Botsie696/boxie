using System.Collections.Generic;
using UnityEngine;

/// <summary>Every cube skin, in shop order. Lives at Resources/SkinCatalog so the game and the menu can both load it.</summary>
[CreateAssetMenu(menuName = "Checkpoints/Skin Catalog", fileName = "SkinCatalog")]
public class SkinCatalog : ScriptableObject
{
    public const string ResourcePath = "SkinCatalog";
    public List<CubeSkin> skins = new List<CubeSkin>();

    static SkinCatalog cached;
    public static SkinCatalog Load() => cached != null ? cached : cached = Resources.Load<SkinCatalog>(ResourcePath);

    public CubeSkin Find(string id)
    {
        for (int i = 0; i < skins.Count; i++) if (skins[i] != null && skins[i].id == id) return skins[i];
        return skins.Count > 0 ? skins[0] : null;
    }

    public int IndexOf(string id)
    {
        for (int i = 0; i < skins.Count; i++) if (skins[i] != null && skins[i].id == id) return i;
        return 0;
    }

    /// <summary>The skin the save says is equipped (Classic when unknown).</summary>
    public CubeSkin Equipped => Find(SaveSystem.EquippedSkin);
}
