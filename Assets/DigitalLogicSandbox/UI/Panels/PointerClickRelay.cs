using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DigitalLogicSandbox.UI.Panels
{
    /// <summary>Forwards pointer clicks on a UI element (e.g. right-click on a section header) to a callback.</summary>
    public sealed class PointerClickRelay : MonoBehaviour, IPointerClickHandler
    {
        public Action<PointerEventData> Clicked { get; set; }

        public void OnPointerClick(PointerEventData eventData)
        {
            Clicked?.Invoke(eventData);
        }
    }
}
