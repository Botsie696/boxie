using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Hooked to the MainUi buttons: loads the gameplay scene by name.</summary>
public class SceneSelector : MonoBehaviour
{
    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName);
    }
}
