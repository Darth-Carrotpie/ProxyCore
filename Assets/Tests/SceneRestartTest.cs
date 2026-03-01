using UnityEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneRestartTest : MonoBehaviour
{

    [EditorCools.Button]
    void RestartScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
