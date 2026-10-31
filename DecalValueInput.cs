using System;

using System.Globalization;

using Colossal.Json;

using Game.Settings;

namespace MedianGrassReplacement

{

    internal static class DecalValueInput

    {

        internal static bool TryFloat(string text, out float value)

        {

            bool valid = float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)

                || float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

            return valid && !float.IsNaN(value) && !float.IsInfinity(value);

        }

        internal static string Format(float value) => value.ToString("0.#####", CultureInfo.CurrentCulture);

    }

    // UI text stays independent of the live number until the user applies it.

    internal sealed class DecalNumberDraft

    {

        private string _text;

        private float _source;

        private bool _dirty;

        internal string Read(float value)

        {

            if (!_dirty || value != _source) { _text=DecalValueInput.Format(value); _source=value; _dirty=false; }

            return _text;

        }

        internal void Write(string text,float source) { _text=text??""; _source=source; _dirty=true; }

        internal void Clear() { _dirty=false; _text=null; }

    }

    public sealed partial class Setting

    {

        private string _decalValueError;

        private readonly DecalNumberDraft _widthDraft=new DecalNumberDraft(), _lengthDraft=new DecalNumberDraft(), _offsetDraft=new DecalNumberDraft(), _setbackDraft=new DecalNumberDraft(), _rotationDraft=new DecalNumberDraft(), _overlapDraft=new DecalNumberDraft(), _limitDraft=new DecalNumberDraft();

        [SettingsUISection(kTexturesTab,kDecalGroup)] public bool ShowExactDecalValues { get; set; }

        public bool HideExactDecalValues() => !ShowExactDecalValues;

        private void ClearDecalDrafts() { _widthDraft.Clear(); _lengthDraft.Clear(); _offsetDraft.Clear(); _setbackDraft.Clear(); _rotationDraft.Clear(); _overlapDraft.Clear(); _limitDraft.Clear(); }

        [Exclude][SettingsUITextInput][SettingsUIHideByCondition(typeof(Setting),nameof(HideExactDecalValues))][SettingsUISection(kTexturesTab,kDecalGroup)]

        public string ExactDecalWidth { get => _widthDraft.Read(CurbDecalWidth); set => _widthDraft.Write(value,CurbDecalWidth); }

        [Exclude][SettingsUITextInput][SettingsUIHideByCondition(typeof(Setting),nameof(HideExactDecalValues))][SettingsUISection(kTexturesTab,kDecalGroup)]

        public string ExactDecalLength { get => _lengthDraft.Read(CurbDecalLength); set => _lengthDraft.Write(value,CurbDecalLength); }

        [Exclude][SettingsUITextInput][SettingsUIHideByCondition(typeof(Setting),nameof(HideExactDecalValues))][SettingsUISection(kTexturesTab,kDecalGroup)]

        public string ExactDecalOffset { get => _offsetDraft.Read(CurbDecalOffset); set => _offsetDraft.Write(value,CurbDecalOffset); }

        [Exclude][SettingsUITextInput][SettingsUIHideByCondition(typeof(Setting),nameof(HideExactDecalValues))][SettingsUISection(kTexturesTab,kDecalGroup)]

        public string ExactDecalSetback { get => _setbackDraft.Read(CurbDecalSetback); set => _setbackDraft.Write(value,CurbDecalSetback); }

        [Exclude][SettingsUITextInput][SettingsUIHideByCondition(typeof(Setting),nameof(HideExactDecalValues))][SettingsUISection(kTexturesTab,kDecalGroup)]

        public string ExactDecalRotation { get => _rotationDraft.Read(CurbDecalRotation); set => _rotationDraft.Write(value,CurbDecalRotation); }

        [Exclude][SettingsUITextInput][SettingsUIHideByCondition(typeof(Setting),nameof(HideExactDecalValues))][SettingsUISection(kTexturesTab,kDecalGroup)]

        public string ExactDecalOverlap { get => _overlapDraft.Read(CurbDecalOverlap); set => _overlapDraft.Write(value,CurbDecalOverlap); }

        [Exclude][SettingsUIHidden]

        public string ExactDecalLimit { get => _limitDraft.Read(CurbDecalLimit); set => _limitDraft.Write(value,CurbDecalLimit); }

        [SettingsUIButton][SettingsUIHideByCondition(typeof(Setting),nameof(HideExactDecalValues))][SettingsUISection(kTexturesTab,kDecalGroup)]

        public bool ApplyExactDecalValues

        {

            set

            {

                if(!value) return;

                // Validate the entire edit before changing any live setting.

                string[] text={ExactDecalWidth,ExactDecalLength,ExactDecalOffset,ExactDecalSetback,ExactDecalRotation,ExactDecalOverlap};

                string[] names={"Width","Height / length","Curb gap","Intersection setback","Rotation","Overlap"};

                var parsed=new float[text.Length];

                for(int i=0;i<text.Length;i++) if(!DecalValueInput.TryFloat(text[i],out parsed[i])) { _decalValueError=names[i]+": enter a finite number, for example -0.125 or 22.5. Typed values have not been applied."; return; }

                CurbDecalWidth=parsed[0]; CurbDecalLength=parsed[1]; CurbDecalOffset=parsed[2]; CurbDecalSetback=parsed[3]; CurbDecalRotation=parsed[4]; CurbDecalOverlap=parsed[5];

                _decalValueError=null; ClearDecalDrafts();

            }

        }

    }

}
