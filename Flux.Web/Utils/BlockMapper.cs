using AutoMapper;
using System.Collections.Generic;
using Flux.Web.Dtos.Config.Blocks;
using Flux.Web.Dtos.Config.Blocks.HttpRequest;
using Flux.Web.Dtos.Config.Blocks.Keycheck;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Custom;
using RuriLib.Models.Blocks.Custom.HttpRequest;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Blocks.Custom.Keycheck;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Blocks.Settings.Interpolated;
using System.Text.Json;

namespace Flux.Web.Utils;

static internal class BlockMapper
{
    // Here I did the dto -> block mappings manually while I find
    // a way to make automapper work properly on BlockSetting...
    static internal List<BlockInstance> MapStack(
        this List<JsonElement> jsonElements, IMapper mapper)
    {
        var stack = new List<BlockInstance>();

        foreach (var jsonElement in jsonElements)
        {
            // Get the id of the block
            var id = jsonElement.GetProperty("id").GetString();
            BlockInstance block;

            switch (id)
            {
                case "HttpRequest":
                    var httpRequestDescriptor = RuriLib.Globals
                        .DescriptorsRepository.GetAs<HttpRequestBlockDescriptor>(id);
                    var httpRequestBlock = new HttpRequestBlockInstance(httpRequestDescriptor);
                    var httpRequestBlockDto = jsonElement
                        .Deserialize<HttpRequestBlockInstanceDto>(Globals.JsonOptions);
                    MapBlock(httpRequestBlockDto!, httpRequestBlock);
                    block = httpRequestBlock;
                    break;

                case "Parse":
                    var parseDescriptor = RuriLib.Globals
                        .DescriptorsRepository.GetAs<ParseBlockDescriptor>(id);
                    var parseBlock = new ParseBlockInstance(parseDescriptor);
                    var parseBlockDto = jsonElement
                        .Deserialize<ParseBlockInstanceDto>(Globals.JsonOptions);
                    MapBlock(parseBlockDto!, parseBlock);
                    block = parseBlock;
                    break;

                case "Script":
                    var scriptDescriptor = RuriLib.Globals
                        .DescriptorsRepository.GetAs<ScriptBlockDescriptor>(id);
                    var scriptBlock = new ScriptBlockInstance(scriptDescriptor);
                    var scriptBlockDto = jsonElement
                        .Deserialize<ScriptBlockInstanceDto>(Globals.JsonOptions);
                    MapBlock(scriptBlockDto!, scriptBlock);
                    block = scriptBlock;
                    break;

                case "Keycheck":
                    var keycheckDescriptor = RuriLib.Globals
                        .DescriptorsRepository.GetAs<KeycheckBlockDescriptor>(id);
                    var keycheckBlock = new KeycheckBlockInstance(keycheckDescriptor);
                    var keycheckBlockDto = jsonElement
                        .Deserialize<KeycheckBlockInstanceDto>(Globals.JsonOptions);
                    MapBlock(keycheckBlockDto!, keycheckBlock);
                    block = keycheckBlock;
                    break;

                case "loliCode":
                    var lolicodeBlock = new LoliCodeBlockInstance(
                        new LoliCodeBlockDescriptor());
                    var lolicodeBlockDto = jsonElement
                        .Deserialize<LoliCodeBlockInstanceDto>(Globals.JsonOptions);
                    MapBlock(lolicodeBlockDto!, lolicodeBlock);
                    block = lolicodeBlock;
                    break;

                default:
                    var autoDescriptor = RuriLib.Globals
                        .DescriptorsRepository.GetAs<AutoBlockDescriptor>(id);
                    AutoBlockInstance autoBlock = id == "ConstantString"
                        ? new ConditionalConstantStringBlockInstance(autoDescriptor)
                        : new AutoBlockInstance(autoDescriptor);
                    var autoBlockDto = jsonElement
                        .Deserialize<AutoBlockInstanceDto>(Globals.JsonOptions);
                    MapBlock(autoBlockDto!, autoBlock);
                    block = autoBlock;
                    break;
            }

            stack.Add(block);
        }

        return stack;
    }

    private static void MapBlock(AutoBlockInstanceDto dto, AutoBlockInstance block)
    {
        MapBaseBlock(dto, block);
        block.OutputVariable = dto.OutputVariable;
        block.IsCapture = dto.IsCapture;
        block.Safe = dto.Safe;

        if (block is ConditionalConstantStringBlockInstance conditionalBlock)
        {
            MapConditionalCases(dto.ConditionalCases, conditionalBlock.ConditionalCases);
        }
    }

    private static void MapBlock(ScriptBlockInstanceDto dto, ScriptBlockInstance block)
    {
        MapBaseBlock(dto, block);
        block.Script = dto.Script;
        block.OutputVariables = dto.OutputVariables;
        block.InputVariables = dto.InputVariables;
        block.Interpreter = dto.Interpreter;
    }

    private static void MapBlock(LoliCodeBlockInstanceDto dto, LoliCodeBlockInstance block)
    {
        MapBaseBlock(dto, block);
        block.Script = dto.Script;
    }

    private static void MapBlock(ParseBlockInstanceDto dto, ParseBlockInstance block)
    {
        MapBaseBlock(dto, block);
        block.OutputVariable = dto.OutputVariable;
        block.Recursive = dto.Recursive;
        block.IsCapture = dto.IsCapture;
        block.Safe = dto.Safe;
        block.Mode = dto.Mode;
        MapConditionalCases(dto.ConditionalCases, block);
    }

    private static void MapBlock(KeycheckBlockInstanceDto dto, KeycheckBlockInstance block)
    {
        MapBaseBlock(dto, block);
        foreach (var keychainDto in dto.Keychains)
        {
            var keychain = new Keychain { ResultStatus = keychainDto.ResultStatus, Mode = keychainDto.Mode };

            foreach (var k in keychainDto.Keys)
            {
                var keyDto = PolyMapper.ConvertPolyDto<KeyDto>((JsonElement)k);
                if (keyDto is null)
                {
                    continue;
                }

                var key = CreateKeyFromDto(keyDto);

                if (keyDto.Left is not null)
                {
                    MapSetting(keyDto.Left, key.Left);
                }

                if (keyDto.Right is not null)
                {
                    MapSetting(keyDto.Right, key.Right);
                }

                keychain.Keys.Add(key);
            }

            block.Keychains.Add(keychain);
        }
    }

    private static void MapBlock(HttpRequestBlockInstanceDto dto, HttpRequestBlockInstance block)
    {
        MapBaseBlock(dto, block);
        block.Safe = dto.Safe;

        var requestParamsDto = PolyMapper.ConvertPolyDto<RequestParamsDto>(
            (JsonElement)dto.RequestParams!);

        RequestParams requestParams;

        switch (requestParamsDto)
        {
            case StandardRequestParamsDto x:
                var standardRequestParams = new StandardRequestParams();
                MapSetting(x.Content, standardRequestParams.Content);
                MapSetting(x.ContentType, standardRequestParams.ContentType);
                requestParams = standardRequestParams;
                break;

            case RawRequestParamsDto x:
                var rawRequestParams = new RawRequestParams();
                MapSetting(x.Content, rawRequestParams.Content);
                MapSetting(x.ContentType, rawRequestParams.ContentType);
                requestParams = rawRequestParams;
                break;

            case BasicAuthRequestParamsDto x:
                var basicAuthRequestParams = new BasicAuthRequestParams();
                MapSetting(x.Username, basicAuthRequestParams.Username);
                MapSetting(x.Password, basicAuthRequestParams.Password);
                requestParams = basicAuthRequestParams;
                break;

            case MultipartRequestParamsDto x:
                var multipartRequestParams = new MultipartRequestParams();
                MapSetting(x.Boundary, multipartRequestParams.Boundary);
                MapMultipartSettings(x, multipartRequestParams);
                requestParams = multipartRequestParams;
                break;

            default:
                throw new NotImplementedException();
        }

        block.RequestParams = requestParams;
    }

    private static void MapMultipartSettings(MultipartRequestParamsDto dto,
        MultipartRequestParams multipart)
    {
        foreach (var c in dto.Contents)
        {
            var contentDto = PolyMapper.ConvertPolyDto<HttpContentSettingsGroupDto>(
                (JsonElement)c!);

            HttpContentSettingsGroup content;

            switch (contentDto)
            {
                case StringHttpContentSettingsGroupDto x:
                    var str = new StringHttpContentSettingsGroup();
                    MapSetting(x.Data, str.Data);
                    MapSetting(x.Name, str.Name);
                    MapSetting(x.ContentType, str.ContentType);
                    content = str;
                    break;

                case RawHttpContentSettingsGroupDto x:
                    var raw = new RawHttpContentSettingsGroup();
                    MapSetting(x.Data, raw.Data);
                    MapSetting(x.Name, raw.Name);
                    MapSetting(x.ContentType, raw.ContentType);
                    content = raw;
                    break;

                case FileHttpContentSettingsGroupDto x:
                    var file = new FileHttpContentSettingsGroup();
                    MapSetting(x.FileName, file.FileName);
                    MapSetting(x.Name, file.Name);
                    MapSetting(x.ContentType, file.ContentType);
                    content = file;
                    break;

                default:
                    throw new NotImplementedException();
            }

            multipart.Contents.Add(content);
        }
    }

    private static void MapConditionalCases(List<ConditionalConstantCaseDto>? cases, List<ConditionalConstantStringCase> target)
    {
        target.Clear();

        if (cases == null || cases.Count == 0)
        {
            return;
        }

        foreach (var caseDto in cases)
        {
            var conditionalCase = new ConditionalConstantStringCase
            {
                Name = caseDto.Name,
                Mode = caseDto.Mode
            };

            if (caseDto.Value is not null)
            {
                MapSetting(caseDto.Value, conditionalCase.Value);
            }

            foreach (var entry in caseDto.Keys)
            {
                if (entry is not JsonElement element)
                {
                    continue;
                }

                var keyDto = PolyMapper.ConvertPolyDto<KeyDto>(element);
                if (keyDto is null)
                {
                    continue;
                }

                var key = CreateKeyFromDto(keyDto);

                if (keyDto.Left is not null)
                {
                    MapSetting(keyDto.Left, key.Left);
                }

                if (keyDto.Right is not null)
                {
                    MapSetting(keyDto.Right, key.Right);
                }

                conditionalCase.Keys.Add(key);
            }

            target.Add(conditionalCase);
        }
    }

    private static void MapConditionalCases(List<ConditionalConstantCaseDto>? cases, ParseBlockInstance block)
    {
        block.ConditionalCases.Clear();

        if (cases == null || cases.Count == 0)
        {
            return;
        }

        foreach (var caseDto in cases)
        {
            var conditionalCase = block.CreateConditionalCase();
            conditionalCase.Name = caseDto.Name;
            conditionalCase.Mode = caseDto.Mode;
            conditionalCase.OverrideMode = caseDto.ParseModeOverride ?? block.Mode;

            if (caseDto.Value is not null)
            {
                MapSetting(caseDto.Value, conditionalCase.Value);
            }

            foreach (var entry in caseDto.Keys)
            {
                if (entry is not JsonElement element)
                {
                    continue;
                }

                var keyDto = PolyMapper.ConvertPolyDto<KeyDto>(element);
                if (keyDto is null)
                {
                    continue;
                }

                var key = CreateKeyFromDto(keyDto);

                if (keyDto.Left is not null)
                {
                    MapSetting(keyDto.Left, key.Left);
                }

                if (keyDto.Right is not null)
                {
                    MapSetting(keyDto.Right, key.Right);
                }

                conditionalCase.Keys.Add(key);
            }

            if (caseDto.ParseSettings != null)
            {
                foreach (var kvp in caseDto.ParseSettings)
                {
                    if (conditionalCase.Settings.TryGetValue(kvp.Key, out var setting))
                    {
                        MapSetting(kvp.Value, setting);
                    }
                }
            }

            block.ConditionalCases.Add(conditionalCase);
        }
    }

    private static void MapBaseBlock(BlockInstanceDto dto, BlockInstance block)
    {
        block.Disabled = dto.Disabled;
        block.Label = dto.Label;

        foreach (var kvp in dto.Settings)
        {
            MapSetting(kvp.Value, block.Settings[kvp.Key]);
        }
    }

    private static void MapSetting(BlockSettingDto? dto, BlockSetting setting)
    {
        if (dto is null)
        {
            return;
        }

        setting.InputMode = dto.InputMode;
        setting.InputVariableName = dto.InputVariableName;
        var value = (JsonElement)dto.Value!;

        switch (dto.Type)
        {
            case BlockSettingType.String:
                ((StringSetting)setting.FixedSetting).Value = value.GetString();
                ((InterpolatedStringSetting)setting.InterpolatedSetting).Value = value.GetString();
                break;

            case BlockSettingType.Int:
                ((IntSetting)setting.FixedSetting).Value = value.GetInt32();
                break;

            case BlockSettingType.Float:
                ((FloatSetting)setting.FixedSetting).Value = value.GetSingle();
                break;

            case BlockSettingType.Bool:
                ((BoolSetting)setting.FixedSetting).Value = value.GetBoolean();
                break;

            case BlockSettingType.ByteArray:
                ((ByteArraySetting)setting.FixedSetting).Value = value.GetBytesFromBase64();
                break;

            case BlockSettingType.ListOfStrings:
                ((ListOfStringsSetting)setting.FixedSetting).Value = value
                    .Deserialize<List<string>>(Globals.JsonOptions);
                ((InterpolatedListOfStringsSetting)setting.InterpolatedSetting).Value = value
                    .Deserialize<List<string>>(Globals.JsonOptions);
                break;

            case BlockSettingType.DictionaryOfStrings:
                ((DictionaryOfStringsSetting)setting.FixedSetting).Value = value
                    .Deserialize<Dictionary<string, string>>(Globals.JsonOptions);
                ((InterpolatedDictionaryOfStringsSetting)setting.InterpolatedSetting).Value = value
                    .Deserialize<Dictionary<string, string>>(Globals.JsonOptions);
                break;

            case BlockSettingType.Enum:
                ((EnumSetting)setting.FixedSetting).Value = value.GetString();
                break;
        }
    }

    private static Key CreateKeyFromDto(KeyDto keyDto)
    {
        return keyDto switch
        {
            StringKeyDto x => new StringKey { Comparison = x.Comparison },
            IntKeyDto x => new IntKey { Comparison = x.Comparison },
            FloatKeyDto x => new FloatKey { Comparison = x.Comparison },
            ListKeyDto x => new ListKey { Comparison = x.Comparison },
            BoolKeyDto x => new BoolKey { Comparison = x.Comparison },
            DictionaryKeyDto x => new DictionaryKey { Comparison = x.Comparison },
            _ => throw new NotImplementedException()
        };
    }
}



