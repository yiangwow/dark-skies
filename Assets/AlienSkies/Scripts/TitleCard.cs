using UnityEngine;

namespace AlienSkies.Run
{
    /// <summary>Boot title card: full-screen overlay shown from scene load
    /// until the player's first KEYBOARD press (never mouse — a left-click
    /// dismissal would fire the equipped rocket the same frame, and the
    /// first thing the game shows you must not be a point-blank explosion).
    /// "ALIEN SKIES" materializes through the same glitch-decode trick
    /// MachineVoice uses on its spoken lines. Disables itself on dismissal;
    /// no RunManager interaction — the clock starts on first movement, which
    /// is usually the dismissing key itself (acceptable and honest).</summary>
    public sealed class TitleCard : MonoBehaviour
    {
        const float CorruptDuration = 1.0f;
        const string Title = "ALIEN SKIES";

        float _age;
        GUIStyle _titleStyle, _subStyle, _cueStyle;

        static readonly char[] GlitchGlyphs = "▓▒░#/\\|01<>¥".ToCharArray();

        void Update()
        {
            _age += Time.deltaTime;

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame)
                enabled = false;
        }

        void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle
            {
                fontSize = 72,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _titleStyle.normal.textColor = Color.white;
            _subStyle = new GUIStyle(_titleStyle) { fontSize = 22, fontStyle = FontStyle.Normal };
            _subStyle.normal.textColor = AlienSkies.Palette.Magenta;
            _cueStyle = new GUIStyle(_titleStyle) { fontSize = 16, fontStyle = FontStyle.Normal };
            _cueStyle.normal.textColor = AlienSkies.Palette.Cyan;
        }

        void OnGUI()
        {
            EnsureStyles();

            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Materialize: characters render as glitch glyphs with decreasing
            // probability as the card ages — the same block MachineVoice uses
            // on its spoken lines (OnGUI, below).
            float corrupt = Mathf.Clamp01(1f - _age / CorruptDuration);
            var chars = Title.ToCharArray();
            var rng = new System.Random(Title.GetHashCode() ^ (int)(_age * 30f));
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == ' ') continue;
                if (rng.NextDouble() < corrupt * 0.8f)
                    chars[i] = GlitchGlyphs[rng.Next(GlitchGlyphs.Length)];
            }
            string shown = new string(chars);

            var titleRect = new Rect(Screen.width / 2f - 500f, Screen.height / 2f - 100f, 1000f, 100f);
            GUI.Label(titleRect, shown, _titleStyle);

            var subRect = new Rect(Screen.width / 2f - 500f, titleRect.y + titleRect.height + 6f, 1000f, 30f);
            GUI.Label(subRect, "THE GYROSCOPE IS WATCHING.", _subStyle);

            var cueRect = new Rect(Screen.width / 2f - 500f, Screen.height - 60f, 1000f, 30f);
            GUI.Label(cueRect, "press any key to begin — TAB at the start line changes spheres", _cueStyle);
        }
    }
}
