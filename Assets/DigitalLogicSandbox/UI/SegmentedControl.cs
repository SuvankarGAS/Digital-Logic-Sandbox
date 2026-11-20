using System;
using System.Collections.Generic;
using DigitalLogicSandbox.UI.Panels;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// A row of mutually exclusive segments on a recessed track (the same look as the library's All · Favorites ·
    /// Recent tabs). Build one with <see cref="UiFactory.Segmented"/>.
    /// </summary>
    public sealed class SegmentedControl : MonoBehaviour
    {
        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<LibraryTabView> _views = new List<LibraryTabView>();
        private int _selected = -1;

        /// <summary>Raised when the user picks a different segment (not for <see cref="SetSelectedWithoutNotify"/>).</summary>
        public event Action<int> Changed;

        public int Selected => _selected;

        public int Count => _buttons.Count;

        public IReadOnlyList<Button> Buttons => _buttons;

        internal void Add(Button button, LibraryTabView view)
        {
            int index = _buttons.Count;
            _buttons.Add(button);
            _views.Add(view);
            button.onClick.AddListener(() => Select(index));
        }

        /// <summary>Selects a segment as if clicked (raises <see cref="Changed"/> when it changes).</summary>
        public void Select(int index)
        {
            if (index == _selected || index < 0 || index >= _buttons.Count)
            {
                return;
            }

            SetSelectedWithoutNotify(index);
            Changed?.Invoke(index);
        }

        public void SetSelectedWithoutNotify(int index)
        {
            _selected = index;
            for (int i = 0; i < _views.Count; i++)
            {
                _views[i].SetActive(i == index, instant: !isActiveAndEnabled);
            }
        }

        /// <summary>Enables or disables every segment.</summary>
        public void SetInteractable(bool interactable)
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].interactable = interactable;
            }

            CanvasGroup group = GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = gameObject.AddComponent<CanvasGroup>();
            }

            group.alpha = interactable ? 1f : 0.45f;
        }
    }
}
