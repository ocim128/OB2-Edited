using RuriLib.Helpers;
using RuriLib.Models.Blocks.Custom.Parse;
using RuriLib.Models.Blocks.Parameters;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Blocks.Settings.Interpolated;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RuriLib.Models.Blocks.Custom
{
    public partial class ParseBlockInstance : BlockInstance
    {
        private static readonly Regex keyRegex = new("^[A-Z]+KEY ", RegexOptions.Compiled);
        private const string CaseModeToken = "CASEMODE";
        private const string InputSettingName = "input";
        private const string InputOverrideToken = "INPUTOVERRIDE";
        private static readonly string[] caseSettingNames =
        [
            InputSettingName,
            "prefix",
            "suffix",
            "leftDelim",
            "rightDelim",
            "caseSensitive",
            "cssSelector",
            "attributeName",
            "xPath",
            "jToken",
            "pattern",
            "outputFormat",
            "multiLine"
        ];

        private string outputVariable = "parseOutput";
        public string OutputVariable
        {
            get => outputVariable;
            set => outputVariable = VariableNames.MakeValid(value);
        }

        public bool Recursive { get; set; }
        public bool IsCapture { get; set; }
        public bool Safe { get; set; }
        public ParseMode Mode { get; set; } = ParseMode.LR;
        public List<ParseConditionalCase> ConditionalCases { get; } = new();

        public ParseConditionalCase CreateConditionalCase()
        {
            var conditionalCase = new ParseConditionalCase();
            InitializeCaseSettings(conditionalCase);
            return conditionalCase;
        }

        private void InitializeCaseSettings(ParseConditionalCase conditionalCase)
        {
            conditionalCase.OverrideMode = Mode;

            foreach (var name in caseSettingNames)
            {
                if (!Descriptor.Parameters.ContainsKey(name) || !Settings.ContainsKey(name))
                {
                    continue;
                }

                conditionalCase.Settings[name] = CloneSetting(Settings[name], Descriptor.Parameters[name]);
            }

            if (Settings.TryGetValue(InputSettingName, out var input))
            {
                TrackInheritedConditionalInput(conditionalCase, input);
            }
        }

        public void SyncInheritedConditionalInputs()
        {
            foreach (var conditionalCase in ConditionalCases)
            {
                SyncInheritedConditionalInput(conditionalCase);
            }
        }

        public void SyncInheritedConditionalInput(ParseConditionalCase conditionalCase)
        {
            NormalizeLegacyConditionalInputOverride(conditionalCase);

            if (IsInputOverridden(conditionalCase) ||
                !Settings.TryGetValue(InputSettingName, out var source) ||
                !Descriptor.Parameters.ContainsKey(InputSettingName) ||
                !conditionalCase.Settings.TryGetValue(InputSettingName, out var destination))
            {
                return;
            }

            if (conditionalCase.InheritedInputSetting != null &&
                !HasSameInputValue(destination, conditionalCase.InheritedInputSetting))
            {
                conditionalCase.InputOverridden = true;
                conditionalCase.InputOverrideExplicitlySet = true;
                return;
            }

            CopySettingValues(source, destination);
            TrackInheritedConditionalInput(conditionalCase, source);
        }

        public ParseBlockInstance(ParseBlockDescriptor descriptor)
            : base(descriptor)
        {
        }

        private BlockSetting CloneSetting(BlockSetting source, BlockParameter parameter)
        {
            var clone = parameter.ToBlockSetting();
            CopySettingValues(source, clone);
            return clone;
        }

        private BlockSetting GetCaseSetting(ParseConditionalCase conditionalCase, string name)
        {
            if (conditionalCase != null &&
                (name != InputSettingName || IsInputOverridden(conditionalCase)) &&
                conditionalCase.Settings.TryGetValue(name, out var setting))
            {
                return setting;
            }

            return Settings[name];
        }

        private bool IsInputOverridden(ParseConditionalCase conditionalCase)
            => conditionalCase?.InputOverridden == true &&
               conditionalCase.Settings.TryGetValue(InputSettingName, out var setting) &&
               (conditionalCase.InputOverrideExplicitlySet || !IsDescriptorDefaultInput(setting));

        private void NormalizeLegacyConditionalInputOverride(ParseConditionalCase conditionalCase)
        {
            if (conditionalCase.InputOverridden &&
                !conditionalCase.InputOverrideExplicitlySet &&
                conditionalCase.Settings.TryGetValue(InputSettingName, out var setting) &&
                IsDescriptorDefaultInput(setting))
            {
                conditionalCase.InputOverridden = false;
            }
        }

        private void TrackInheritedConditionalInput(ParseConditionalCase conditionalCase, BlockSetting source)
        {
            if (Descriptor.Parameters.TryGetValue(InputSettingName, out var parameter))
            {
                conditionalCase.InheritedInputSetting = CloneSetting(source, parameter);
            }
        }

        private static bool HasSameInputValue(BlockSetting first, BlockSetting second)
        {
            if (first.InputMode != second.InputMode)
            {
                return false;
            }

            return first.InputMode switch
            {
                SettingInputMode.Variable => first.InputVariableName == second.InputVariableName,
                SettingInputMode.Fixed when first.FixedSetting is StringSetting firstString &&
                    second.FixedSetting is StringSetting secondString =>
                    firstString.Value == secondString.Value,
                SettingInputMode.Interpolated when first.InterpolatedSetting is InterpolatedStringSetting firstString &&
                    second.InterpolatedSetting is InterpolatedStringSetting secondString =>
                    firstString.Value == secondString.Value,
                _ => false
            };
        }

        private bool IsDescriptorDefaultInput(BlockSetting setting)
        {
            if (!Descriptor.Parameters.TryGetValue(InputSettingName, out var parameter))
            {
                return false;
            }

            if (setting.InputMode != parameter.InputMode)
            {
                return false;
            }

            return parameter switch
            {
                StringParameter stringParameter when setting.InputMode == SettingInputMode.Variable =>
                    setting.InputVariableName == stringParameter.DefaultVariableName,
                StringParameter stringParameter when setting.InputMode == SettingInputMode.Fixed &&
                    setting.FixedSetting is StringSetting fixedSetting =>
                    fixedSetting.Value == stringParameter.DefaultValue,
                StringParameter stringParameter when setting.InputMode == SettingInputMode.Interpolated &&
                    setting.InterpolatedSetting is InterpolatedStringSetting interpolatedSetting =>
                    interpolatedSetting.Value == stringParameter.DefaultValue,
                _ => false
            };
        }

        private static void CopySettingValues(BlockSetting source, BlockSetting destination)
        {
            destination.InputMode = source.InputMode;
            destination.InputVariableName = source.InputVariableName;

            switch (source.FixedSetting)
            {
                case StringSetting src when destination.FixedSetting is StringSetting dest:
                    dest.Value = src.Value;
                    dest.MultiLine = src.MultiLine;
                    break;
                case BoolSetting srcBool when destination.FixedSetting is BoolSetting destBool:
                    destBool.Value = srcBool.Value;
                    break;
                case IntSetting srcInt when destination.FixedSetting is IntSetting destInt:
                    destInt.Value = srcInt.Value;
                    break;
                case FloatSetting srcFloat when destination.FixedSetting is FloatSetting destFloat:
                    destFloat.Value = srcFloat.Value;
                    break;
                case ListOfStringsSetting srcList when destination.FixedSetting is ListOfStringsSetting destList:
                    destList.Value = srcList.Value.ToList();
                    break;
                case DictionaryOfStringsSetting srcDict when destination.FixedSetting is DictionaryOfStringsSetting destDict:
                    destDict.Value = srcDict.Value.ToDictionary(k => k.Key, v => v.Value);
                    break;
            }

            switch (source.InterpolatedSetting)
            {
                case InterpolatedStringSetting src when destination.InterpolatedSetting is InterpolatedStringSetting dest:
                    dest.Value = src.Value;
                    dest.MultiLine = src.MultiLine;
                    break;
                case InterpolatedListOfStringsSetting srcList when destination.InterpolatedSetting is InterpolatedListOfStringsSetting destList:
                    destList.Value = srcList.Value.ToList();
                    break;
                case InterpolatedDictionaryOfStringsSetting srcDict when destination.InterpolatedSetting is InterpolatedDictionaryOfStringsSetting destDict:
                    destDict.Value = srcDict.Value.ToDictionary(k => k.Key, v => v.Value);
                    break;
            }
        }

        public class ParseConditionalCase : ConditionalConstantStringCase
        {
            public ParseMode OverrideMode { get; set; } = ParseMode.LR;
            public bool InputOverridden { get; set; }
            public bool InputOverrideExplicitlySet { get; set; }
            internal BlockSetting InheritedInputSetting { get; set; }
            public Dictionary<string, BlockSetting> Settings { get; } = new Dictionary<string, BlockSetting>();
        }
    }
}
