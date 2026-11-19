using UnityEngine;

namespace AlienSkies.Movement
{
    /// <summary>Global feel-settings multipliers (guardrail 3: FOV/shake are
    /// settings, never forced). Each scale is a multiplier on top of the
    /// game's own tuned feel, not a replacement for it — 1 always means
    /// "as designed". No blur setting: the game has no blur pass to scale.
    /// Backing fields load from PlayerPrefs on first touch (static field
    /// init) and every setter re-saves immediately.</summary>
    public static class FeelSettings
    {
        public const float FovScaleMin = 0.5f, FovScaleMax = 1.5f, FovScaleDefault = 1f;
        public const float ShakeScaleMin = 0f, ShakeScaleMax = 1.5f, ShakeScaleDefault = 1f;
        public const float WindScaleMin = 0f, WindScaleMax = 1.5f, WindScaleDefault = 1f;

        const string FovKey = "feel_fov_scale";
        const string ShakeKey = "feel_shake_scale";
        const string WindKey = "feel_wind_scale";

        static float _fovScale = PlayerPrefs.GetFloat(FovKey, FovScaleDefault);
        static float _shakeScale = PlayerPrefs.GetFloat(ShakeKey, ShakeScaleDefault);
        static float _windScale = PlayerPrefs.GetFloat(WindKey, WindScaleDefault);

        public static float FovScale
        {
            get => _fovScale;
            set
            {
                _fovScale = Mathf.Clamp(value, FovScaleMin, FovScaleMax);
                PlayerPrefs.SetFloat(FovKey, _fovScale);
                PlayerPrefs.Save();
            }
        }

        public static float ShakeScale
        {
            get => _shakeScale;
            set
            {
                _shakeScale = Mathf.Clamp(value, ShakeScaleMin, ShakeScaleMax);
                PlayerPrefs.SetFloat(ShakeKey, _shakeScale);
                PlayerPrefs.Save();
            }
        }

        public static float WindScale
        {
            get => _windScale;
            set
            {
                _windScale = Mathf.Clamp(value, WindScaleMin, WindScaleMax);
                PlayerPrefs.SetFloat(WindKey, _windScale);
                PlayerPrefs.Save();
            }
        }
    }
}
