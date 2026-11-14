using UnityEngine;
public class Shop : MonoBehaviour { public static Shop _instance; }
public class ShopLeaveButton : MonoBehaviour
{
    public int Calls;
    public bool Throw;
    public bool Close = true;
    public void OnPress()
    {
        Calls++;
        if (Throw) throw new System.InvalidOperationException("native failure");
        if (Close) Shop._instance.gameObject.activeInHierarchy = false;
    }
}
namespace Michsky.UI.Reach { public class ButtonManager : MonoBehaviour { public bool isInteractable = true; } }
