using UnityEngine;
using UnityEngine.EventSystems;

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// Passes mouse-wheel events on to the enclosing scroll view. Controls that handle the wheel themselves (a
    /// <see cref="TMPro.TMP_InputField"/> implements <see cref="IScrollHandler"/>) would otherwise stop the list from
    /// scrolling while the pointer is over them.
    /// </summary>
    public sealed class ScrollForwarder : MonoBehaviour, IScrollHandler
    {
        public void OnScroll(PointerEventData eventData)
        {
            Transform parent = transform.parent;
            if (parent != null)
            {
                ExecuteEvents.ExecuteHierarchy(parent.gameObject, eventData, ExecuteEvents.scrollHandler);
            }
        }
    }
}
