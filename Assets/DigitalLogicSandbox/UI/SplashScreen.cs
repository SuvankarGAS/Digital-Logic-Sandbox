using System;
using DigitalLogicSandbox.Core.Signals;
using DigitalLogicSandbox.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DigitalLogicSandbox.UI
{
    /// <summary>
    /// Animated start-up intro shown above the editor while the first project loads behind it.
    /// <para>
    /// The visuals come from one full-screen quad running the <c>DigitalLogicSandbox/Splash</c> shader (circuit
    /// board, signal pulses, the self-drawing AND-gate logo and the closing radial dissolve). This component only
    /// advances one clock, feeds it to the material and animates the title text in sync. Any click or key skips
    /// straight to the dissolve. Presentation only: it never touches the simulation or the project.
    /// </para>
    /// </summary>
    public sealed class SplashScreen : MonoBehaviour
    {
        /// <summary>Seconds after which the intro starts its exit on its own.</summary>
        public const float AutoExitTime = 3.1f;

        /// <summary>Length of the closing dissolve, in seconds.</summary>
        public const float ExitDuration = 0.8f;

        // Layout unit: the shader draws the screen as 18 cells tall; the logo sits at (0, 2.3) cells.
        private const float CellsTall = 18f;
        private const float LogoY = 2.3f;

        // Title timeline (seconds), matched to the shader's logo timeline (the gate fires at 1.74 s).
        private const float TitleStart = 1.35f;
        private const float TitleLetterDelay = 0.035f;
        private const float TitleLetterDuration = 0.4f;
        private const float TitleSpacingDuration = 1.1f;
        private const float SubtitleStart = 1.95f;
        private const float RuleStart = 1.85f;
        private const float RuleWidth = 0.18f; // fraction of the canvas height

        private static readonly int TimeId = Shader.PropertyToID("_T");
        private static readonly int ExitId = Shader.PropertyToID("_Exit");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int BackgroundId = Shader.PropertyToID("_Background");
        private static readonly int GridId = Shader.PropertyToID("_Grid");
        private static readonly int LowId = Shader.PropertyToID("_Low");
        private static readonly int HighId = Shader.PropertyToID("_High");
        private static readonly int AccentId = Shader.PropertyToID("_Accent");
        private static readonly int GateFillId = Shader.PropertyToID("_GateFill");

        private static readonly float[] StatusTimes = { 0.15f, 0.95f, 1.75f, 2.45f };
        private static readonly string[] StatusTexts =
        {
            "INITIALIZING PRIMITIVES  ·  AND  ·  NOT",
            "LINKING GATE LIBRARY",
            "SIGNAL PROPAGATION ONLINE",
            "READY",
        };

        private Action _completed;
        private RectTransform _root;
        private CanvasGroup _rootGroup;
        private CanvasGroup _textGroup;
        private Material _material;
        private Material _titleMaterial;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _gateLabel;
        private TextMeshProUGUI _subtitle;
        private TextMeshProUGUI _status;
        private RectTransform _rule;
        private Image _ruleImage;
        private Color _subtitleColor;
        private Color _statusColor;
        private Color _gateLabelColor;
        private Color _ruleColor;
        private string[] _statusWithCursor;
        private int _statusIndex = -1;
        private bool _statusCursor;
        private bool _titleSettled;
        private float _layoutHeight = -1f;
        private float _time;
        private float _exitStart = -1f;
        private bool _finished;

        /// <summary>The splash currently on screen, or null.</summary>
        public static SplashScreen Current { get; private set; }

        /// <summary>True while a splash is on screen (including its exit dissolve).</summary>
        public static bool IsShowing => Current != null;

        /// <summary>Seconds of intro played so far.</summary>
        public float ElapsedTime => _time;

        /// <summary>True once the closing dissolve has started.</summary>
        public bool IsExiting => _exitStart >= 0f;

        /// <summary>
        /// Builds and starts the splash on its own overlay canvas above the application UI.
        /// <paramref name="completed"/> runs once, after the dissolve has finished and the splash is removed.
        /// </summary>
        public static SplashScreen Show(VisualTheme theme, UiFactory factory, Action completed)
        {
            if (theme == null)
            {
                throw new ArgumentNullException(nameof(theme));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (Current != null)
            {
                Current.Finish();
            }

            theme.EnsureMaterials();
            var go = new GameObject("Splash", typeof(RectTransform));
            go.layer = 5;
            var splash = go.AddComponent<SplashScreen>();
            splash._completed = completed;
            splash.Build(theme, factory);
            Current = splash;
            return splash;
        }

        /// <summary>Jumps to the closing dissolve (no-op if it has already started).</summary>
        public void Skip()
        {
            if (!IsExiting && !_finished)
            {
                BeginExit();
            }
        }

        private void Build(VisualTheme theme, UiFactory factory)
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)transform;
            _rootGroup = gameObject.AddComponent<CanvasGroup>();
            _rootGroup.blocksRaycasts = true;
            _rootGroup.interactable = true;

            Color high = theme.SignalColor(Signal.High);
            _material = new Material(theme.SplashMaterial) { name = "DLS Splash (instance)", hideFlags = HideFlags.DontSave };
            _material.SetFloat(SeedId, UnityEngine.Random.Range(1f, 97f));
            _material.SetColor(BackgroundId, theme.WorkspaceBackground);
            _material.SetColor(GridId, theme.GridMinor);
            _material.SetColor(LowId, theme.SignalColor(Signal.Low));
            _material.SetColor(HighId, high);
            _material.SetColor(AccentId, theme.Accent);
            _material.SetColor(GateFillId, theme.GateFill);
            _material.SetFloat(TimeId, 0f);
            _material.SetFloat(ExitId, 0f);

            RectTransform backdrop = UiFactory.CreateRect("Backdrop", transform);
            Stretch(backdrop);
            var image = backdrop.gameObject.AddComponent<Image>();
            image.material = _material;
            image.color = Color.white;
            image.raycastTarget = true; // swallow clicks meant for the editor until the dissolve starts

            RectTransform text = UiFactory.CreateRect("Text", transform);
            Stretch(text);
            _textGroup = text.gameObject.AddComponent<CanvasGroup>();
            _textGroup.blocksRaycasts = false;

            _gateLabelColor = high;
            _gateLabel = factory.Text("GateLabel", text, "AND", 20f, Clear(_gateLabelColor), TextAlignmentOptions.Center);
            _gateLabel.fontWeight = FontWeight.Bold;
            _gateLabel.characterSpacing = 6f;
            _gateLabel.overflowMode = TextOverflowModes.Overflow;

            _title = factory.Text("Title", text, "Digital Logic Sandbox", 52f, theme.TextPrimary, TextAlignmentOptions.Center);
            _title.fontWeight = FontWeight.Bold;
            _title.overflowMode = TextOverflowModes.Overflow;
            _title.enableVertexGradient = true;
            _title.colorGradient = new VertexGradient(Color.white, Color.white,
                Color.Lerp(Color.white, high, 0.45f), Color.Lerp(Color.white, high, 0.45f));
            _titleMaterial = _title.fontMaterial; // per-label instance, so the glow doesn't leak to other text
            _titleMaterial.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            _titleMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(high.r, high.g, high.b, 0.32f));
            _titleMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.85f);
            _titleMaterial.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.15f);
            _titleMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            _titleMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
            _title.alpha = 0f;

            _ruleColor = new Color(high.r, high.g, high.b, 0.55f);
            _rule = UiFactory.CreateRect("Rule", text);
            _ruleImage = _rule.gameObject.AddComponent<Image>();
            _ruleImage.sprite = RenderAssets.Square;
            _ruleImage.color = Clear(_ruleColor);
            _ruleImage.raycastTarget = false;

            _subtitleColor = theme.TextSecondary;
            _subtitle = factory.Text("Subtitle", text, "Build a computer from nothing but AND and NOT", 17f,
                Clear(_subtitleColor), TextAlignmentOptions.Center);
            _subtitle.characterSpacing = 2f;
            _subtitle.overflowMode = TextOverflowModes.Overflow;

            _statusColor = theme.TextMuted;
            _status = factory.Text("Status", text, string.Empty, 12.5f, Clear(_statusColor), TextAlignmentOptions.Center);
            _status.characterSpacing = 12f;
            _status.overflowMode = TextOverflowModes.Overflow;
            _status.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            _status.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            _status.rectTransform.sizeDelta = new Vector2(900f, 24f);
            _status.rectTransform.anchoredPosition = new Vector2(0f, 46f);
            _statusWithCursor = new string[StatusTexts.Length];
            for (int i = 0; i < StatusTexts.Length; i++)
            {
                _statusWithCursor[i] = StatusTexts[i] + " _";
            }

            string version = string.IsNullOrEmpty(Application.version) ? string.Empty : "v" + Application.version;
            TextMeshProUGUI versionLabel = factory.Text("Version", text, version, 11.5f,
                new Color(theme.TextMuted.r, theme.TextMuted.g, theme.TextMuted.b, 0.6f), TextAlignmentOptions.BottomRight);
            versionLabel.rectTransform.anchorMin = new Vector2(1f, 0f);
            versionLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
            versionLabel.rectTransform.pivot = new Vector2(1f, 0f);
            versionLabel.rectTransform.sizeDelta = new Vector2(200f, 20f);
            versionLabel.rectTransform.anchoredPosition = new Vector2(-20f, 16f);
        }

        private void Update()
        {
            if (_finished)
            {
                return;
            }

            // Clamp the step so a start-up hitch (scene load, shader warm-up) doesn't skip half the animation.
            _time += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            if (!IsExiting && (_time >= AutoExitTime || SkipPressed()))
            {
                BeginExit();
            }

            float exit = IsExiting ? Mathf.Clamp01((_time - _exitStart) / ExitDuration) : 0f;
            _material.SetFloat(TimeId, _time);
            _material.SetFloat(ExitId, exit);

            UpdateLayout();
            AnimateText(exit);

            if (exit >= 1f)
            {
                Finish();
            }
        }

        private void BeginExit()
        {
            _exitStart = _time;
            _rootGroup.blocksRaycasts = false; // the editor is usable as soon as it starts to show through
        }

        private static bool SkipPressed()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
            {
                return true;
            }

            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.anyKey.wasPressedThisFrame;
        }

        private void UpdateLayout()
        {
            float height = _root.rect.height;
            if (Mathf.Approximately(height, _layoutHeight) || height <= 0f)
            {
                return;
            }

            _layoutHeight = height;
            float unit = height / CellsTall;
            Place(_gateLabel.rectTransform, LogoY * unit, new Vector2(4f * unit, unit));
            _gateLabel.fontSize = 0.46f * unit;
            Place(_title.rectTransform, -0.55f * unit, new Vector2(30f * unit, 1.6f * unit));
            _title.fontSize = 1.05f * unit;
            Place(_rule, -1.12f * unit, new Vector2(0f, 1.5f));
            Place(_subtitle.rectTransform, -1.6f * unit, new Vector2(30f * unit, 0.8f * unit));
            _subtitle.fontSize = 0.36f * unit;
        }

        private void AnimateText(float exit)
        {
            float t = _time;

            // Gate label lights up with the gate body.
            _gateLabel.color = WithAlpha(_gateLabelColor, Smooth((t - 1.3f) / 0.3f) * 0.9f);

            AnimateTitle(t);

            _rule.sizeDelta = new Vector2(EaseOut((t - RuleStart) / 0.55f) * RuleWidth * _layoutHeight, 1.5f);
            _ruleImage.color = WithAlpha(_ruleColor, Smooth((t - RuleStart) / 0.3f) * _ruleColor.a);

            float subtitle = Smooth((t - SubtitleStart) / 0.5f);
            _subtitle.color = WithAlpha(_subtitleColor, subtitle);
            Vector2 subtitlePosition = _subtitle.rectTransform.anchoredPosition;
            subtitlePosition.y = -1.6f * (_layoutHeight / CellsTall) - (1f - EaseOut((t - SubtitleStart) / 0.6f)) * 10f;
            _subtitle.rectTransform.anchoredPosition = subtitlePosition;

            UpdateStatus(t);

            _textGroup.alpha = 1f - Smooth(exit / 0.45f);
        }

        // Letters fade and rise in one after another while the tracking tightens.
        private void AnimateTitle(float t)
        {
            if (_titleSettled)
            {
                return;
            }

            float local = t - TitleStart;
            if (local < 0f)
            {
                return;
            }

            _title.alpha = 1f;
            _title.characterSpacing = Mathf.Lerp(26f, 2f, EaseOut(local / TitleSpacingDuration));
            _title.ForceMeshUpdate();
            TMP_TextInfo info = _title.textInfo;
            float rise = _layoutHeight / CellsTall * 0.35f;
            bool allSettled = local >= TitleSpacingDuration;
            for (int i = 0; i < info.characterCount; i++)
            {
                TMP_CharacterInfo character = info.characterInfo[i];
                if (!character.isVisible)
                {
                    continue;
                }

                float k = Mathf.Clamp01((local - i * TitleLetterDelay) / TitleLetterDuration);
                if (k < 1f)
                {
                    allSettled = false;
                }

                float eased = EaseOut(k);
                byte alpha = (byte)(Smooth(k) * 255f);
                float offset = (1f - eased) * -rise;
                TMP_MeshInfo mesh = info.meshInfo[character.materialReferenceIndex];
                int v = character.vertexIndex;
                for (int j = 0; j < 4; j++)
                {
                    Color32 c = mesh.colors32[v + j];
                    c.a = (byte)(c.a * alpha / 255);
                    mesh.colors32[v + j] = c;
                    mesh.vertices[v + j].y += offset;
                }
            }

            _title.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32 | TMP_VertexDataUpdateFlags.Vertices);
            if (allSettled)
            {
                _titleSettled = true;
                _title.ForceMeshUpdate();
            }
        }

        private void UpdateStatus(float t)
        {
            int index = -1;
            for (int i = 0; i < StatusTimes.Length; i++)
            {
                if (t >= StatusTimes[i])
                {
                    index = i;
                }
            }

            if (index < 0)
            {
                return;
            }

            bool cursor = index < StatusTexts.Length - 1 && Mathf.Repeat(t, 0.5f) < 0.3f;
            if (index != _statusIndex || cursor != _statusCursor)
            {
                _statusIndex = index;
                _statusCursor = cursor;
                _status.text = cursor ? _statusWithCursor[index] : StatusTexts[index];
            }

            float fade = Smooth((t - StatusTimes[0]) / 0.4f);
            Color color = index == StatusTexts.Length - 1 ? Color.Lerp(_statusColor, _gateLabelColor, 0.7f) : _statusColor;
            _status.color = WithAlpha(color, fade * 0.9f);
        }

        private void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            if (Current == this)
            {
                Current = null;
            }

            Action completed = _completed;
            _completed = null;
            Destroy(gameObject);
            completed?.Invoke();
        }

        private void OnDestroy()
        {
            if (Current == this)
            {
                Current = null;
            }

            if (_material != null)
            {
                Object.Destroy(_material);
            }

            if (_titleMaterial != null)
            {
                Object.Destroy(_titleMaterial);
            }
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, float y, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, y);
        }

        private static Color Clear(Color color)
        {
            return WithAlpha(color, 0f);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static float EaseOut(float x)
        {
            x = Mathf.Clamp01(x);
            float inv = 1f - x;
            return 1f - inv * inv * inv;
        }
    }
}
