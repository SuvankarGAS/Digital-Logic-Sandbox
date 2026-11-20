using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalLogicSandbox.UI
{
    /// <summary>Key/value storage for <see cref="UiPreferences"/>.</summary>
    public interface IPreferenceStore
    {
        /// <summary>Stored text for <paramref name="key"/>, or null.</summary>
        string Load(string key);

        void Save(string key, string value);
    }

    /// <summary>Unity PlayerPrefs storage (per user and machine, survives restarts).</summary>
    public sealed class PlayerPrefsStore : IPreferenceStore
    {
        public string Load(string key)
        {
            return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
        }

        public void Save(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>In-memory storage: nothing outlives the session (editor play mode by default, tests).</summary>
    public sealed class MemoryPreferenceStore : IPreferenceStore
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        public string Load(string key)
        {
            return _values.TryGetValue(key, out string value) ? value : null;
        }

        public void Save(string key, string value)
        {
            _values[key] = value;
        }
    }

    /// <summary>
    /// Per-user interface state: library width and collapse, which library sections are open, pinned entries and
    /// recently placed entries. This is deliberately not project data: it never marks a project as changed and is
    /// not shared with project files. Entries are identified by stable library keys (see
    /// <see cref="Panels.LibraryPanel.KeyOf"/>), never by display names.
    /// </summary>
    public sealed class UiPreferences
    {
        public const string StorageKey = "DigitalLogicSandbox.UI.Preferences";

        public const float DefaultLibraryWidth = UiFactory.LibraryWidth;

        public const float MinLibraryWidth = 220f;

        public const float MaxLibraryWidth = 420f;

        public const int MaxRecent = 6;

        public const int MaxPinned = 24;

        private readonly IPreferenceStore _store;
        private readonly Data _data;
        private readonly Dictionary<string, bool> _sections = new Dictionary<string, bool>(StringComparer.Ordinal);

        public UiPreferences(IPreferenceStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _data = Read(store.Load(StorageKey));
            for (int i = 0; i < _data.Sections.Count; i++)
            {
                string entry = _data.Sections[i];
                int colon = entry.LastIndexOf(':');
                if (colon > 0 && colon == entry.Length - 2)
                {
                    _sections[entry.Substring(0, colon)] = entry[colon + 1] == '1';
                }
            }
        }

        /// <summary>Raised after pinned or recent entries change (not for width or section changes).</summary>
        public event Action EntriesChanged;

        public float LibraryWidth
        {
            get => _data.LibraryWidth;
            set
            {
                float clamped = Mathf.Clamp(value, MinLibraryWidth, MaxLibraryWidth);
                if (!Mathf.Approximately(clamped, _data.LibraryWidth))
                {
                    _data.LibraryWidth = clamped;
                    Persist();
                }
            }
        }

        public bool LibraryCollapsed
        {
            get => _data.LibraryCollapsed;
            set
            {
                if (value != _data.LibraryCollapsed)
                {
                    _data.LibraryCollapsed = value;
                    Persist();
                }
            }
        }

        /// <summary>Library tab shown last (0 = all parts, 1 = favorites, 2 = recent).</summary>
        public int LibraryTab
        {
            get => _data.LibraryTab;
            set
            {
                if (value != _data.LibraryTab)
                {
                    _data.LibraryTab = value;
                    Persist();
                }
            }
        }

        public IReadOnlyList<string> Pinned => _data.Pinned;

        /// <summary>Most recent first.</summary>
        public IReadOnlyList<string> Recent => _data.Recent;

        public bool IsSectionExpanded(string key, bool defaultExpanded)
        {
            return _sections.TryGetValue(key, out bool expanded) ? expanded : defaultExpanded;
        }

        public void SetSectionExpanded(string key, bool expanded)
        {
            if (string.IsNullOrEmpty(key) || (_sections.TryGetValue(key, out bool current) && current == expanded))
            {
                return;
            }

            _sections[key] = expanded;
            Persist();
        }

        public bool IsPinned(string key)
        {
            return key != null && _data.Pinned.Contains(key);
        }

        public void SetPinned(string key, bool pinned)
        {
            if (string.IsNullOrEmpty(key) || IsPinned(key) == pinned)
            {
                return;
            }

            if (pinned)
            {
                if (_data.Pinned.Count >= MaxPinned)
                {
                    _data.Pinned.RemoveAt(0);
                }

                _data.Pinned.Add(key);
            }
            else
            {
                _data.Pinned.Remove(key);
            }

            Persist();
            EntriesChanged?.Invoke();
        }

        /// <summary>Moves <paramref name="key"/> to the front of the recent list (keeping at most <see cref="MaxRecent"/>).</summary>
        public void AddRecent(string key)
        {
            if (string.IsNullOrEmpty(key) || (_data.Recent.Count > 0 && _data.Recent[0] == key))
            {
                return;
            }

            _data.Recent.Remove(key);
            _data.Recent.Insert(0, key);
            while (_data.Recent.Count > MaxRecent)
            {
                _data.Recent.RemoveAt(_data.Recent.Count - 1);
            }

            Persist();
            EntriesChanged?.Invoke();
        }

        public void ClearRecent()
        {
            if (_data.Recent.Count == 0)
            {
                return;
            }

            _data.Recent.Clear();
            Persist();
            EntriesChanged?.Invoke();
        }

        /// <summary>Forgets an entry everywhere (a deleted custom gate).</summary>
        public void Forget(string key)
        {
            bool changed = _data.Pinned.Remove(key) | _data.Recent.Remove(key);
            if (changed)
            {
                Persist();
                EntriesChanged?.Invoke();
            }
        }

        /// <summary>Renames a section key (a renamed custom category keeps its open/closed state).</summary>
        public void RenameSection(string oldKey, string newKey)
        {
            if (oldKey == newKey || !_sections.TryGetValue(oldKey, out bool expanded))
            {
                return;
            }

            _sections.Remove(oldKey);
            _sections[newKey] = expanded;
            Persist();
        }

        private void Persist()
        {
            _data.Sections.Clear();
            foreach (KeyValuePair<string, bool> pair in _sections)
            {
                _data.Sections.Add(pair.Key + (pair.Value ? ":1" : ":0"));
            }

            _store.Save(StorageKey, JsonUtility.ToJson(_data));
        }

        private static Data Read(string json)
        {
            Data data = null;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    data = JsonUtility.FromJson<Data>(json);
                }
                catch (ArgumentException)
                {
                    data = null;
                }
            }

            data ??= new Data();
            data.Sections ??= new List<string>();
            data.Pinned ??= new List<string>();
            data.Recent ??= new List<string>();
            data.Pinned.RemoveAll(string.IsNullOrEmpty);
            data.Recent.RemoveAll(string.IsNullOrEmpty);
            if (data.LibraryWidth <= 0f)
            {
                data.LibraryWidth = DefaultLibraryWidth;
            }

            data.LibraryWidth = Mathf.Clamp(data.LibraryWidth, MinLibraryWidth, MaxLibraryWidth);
            data.LibraryTab = Mathf.Clamp(data.LibraryTab, 0, 2);
            return data;
        }

        [Serializable]
        private sealed class Data
        {
            public float LibraryWidth = DefaultLibraryWidth;
            public bool LibraryCollapsed;
            public int LibraryTab;
            public List<string> Sections = new List<string>();
            public List<string> Pinned = new List<string>();
            public List<string> Recent = new List<string>();
        }
    }
}
