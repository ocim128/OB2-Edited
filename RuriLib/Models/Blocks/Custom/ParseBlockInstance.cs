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
        private static readonly string[] caseSettingNames =
        [
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

        private BlockSetting GetCaseSetting(Dictionary<string, BlockSetting> overrides, string name)
        {
            if (overrides != null && overrides.TryGetValue(name, out var setting))
            {
                return setting;
            }

            return Settings[name];
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
            public Dictionary<string, BlockSetting> Settings { get; } = new Dictionary<string, BlockSetting>();
        }
    }
}
