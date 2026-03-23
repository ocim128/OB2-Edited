using RuriLib.Exceptions;
using RuriLib.Extensions;
using RuriLib.Helpers.LoliCode;
using RuriLib.Models.Blocks.Custom.HttpRequest;
using RuriLib.Models.Blocks.Custom.HttpRequest.Multipart;
using RuriLib.Models.Blocks.Parameters;
using System;
using System.IO;

namespace RuriLib.Models.Blocks.Custom;

public partial class HttpRequestBlockInstance
{
    public override string ToLC(bool printDefaultParams = false)
    {
        NormalizeLegacySettings();

        using var writer = new LoliCodeWriter(base.ToLC(printDefaultParams));

        if (Safe)
        {
            writer.AppendLine("SAFE", 2);
        }

        switch (RequestParams)
        {
            case StandardRequestParams x:
                writer
                    .AppendLine("TYPE:STANDARD", 2)
                    .AppendLine(LoliCodeWriter.GetSettingValue(x.Content), 2)
                    .AppendLine(LoliCodeWriter.GetSettingValue(x.ContentType), 2);
                break;

            case RawRequestParams x:
                writer
                    .AppendLine("TYPE:RAW", 2)
                    .AppendLine(LoliCodeWriter.GetSettingValue(x.Content), 2)
                    .AppendLine(LoliCodeWriter.GetSettingValue(x.ContentType), 2);
                break;

            case BasicAuthRequestParams x:
                writer
                    .AppendLine("TYPE:BASICAUTH", 2)
                    .AppendLine(LoliCodeWriter.GetSettingValue(x.Username), 2)
                    .AppendLine(LoliCodeWriter.GetSettingValue(x.Password), 2);
                break;

            case MultipartRequestParams x:
                writer
                    .AppendLine("TYPE:MULTIPART", 2)
                    .AppendLine(LoliCodeWriter.GetSettingValue(x.Boundary), 2);

                foreach (var content in x.Contents)
                {
                    switch (content)
                    {
                        case StringHttpContentSettingsGroup y:
                            writer
                                .AppendToken("CONTENT:STRING", 2)
                                .AppendToken(LoliCodeWriter.GetSettingValue(y.Name))
                                .AppendToken(LoliCodeWriter.GetSettingValue(y.Data))
                                .AppendLine(LoliCodeWriter.GetSettingValue(y.ContentType));
                            break;

                        case RawHttpContentSettingsGroup y:
                            writer
                                .AppendToken("CONTENT:RAW", 2)
                                .AppendToken(LoliCodeWriter.GetSettingValue(y.Name))
                                .AppendToken(LoliCodeWriter.GetSettingValue(y.Data))
                                .AppendLine(LoliCodeWriter.GetSettingValue(y.ContentType));
                            break;

                        case FileHttpContentSettingsGroup y:
                            writer
                                .AppendToken("CONTENT:FILE", 2)
                                .AppendToken(LoliCodeWriter.GetSettingValue(y.Name))
                                .AppendToken(LoliCodeWriter.GetSettingValue(y.FileName))
                                .AppendLine(LoliCodeWriter.GetSettingValue(y.ContentType));
                            break;
                    }
                }

                break;
        }

        return writer.ToString();
    }

    public override void FromLC(ref string script, ref int lineNumber)
    {
        base.FromLC(ref script, ref lineNumber);

        using var reader = new StringReader(script);

        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            var lineCopy = line;
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("SAFE"))
            {
                Safe = true;
                continue;
            }

            if (line.StartsWith("TYPE:"))
            {
                try
                {
                    var reqParams = MyRegex().Match(line).Groups[1].Value;

                    switch (reqParams)
                    {
                        case "STANDARD":
                            var standardReqParams = new StandardRequestParams();

                            line = reader.ReadLine().Trim();
                            lineCopy = line;
                            lineNumber++;
                            LoliCodeParser.ParseSettingValue(ref line, standardReqParams.Content, new StringParameter());

                            line = reader.ReadLine().Trim();
                            lineCopy = line;
                            lineNumber++;
                            LoliCodeParser.ParseSettingValue(ref line, standardReqParams.ContentType, new StringParameter());

                            RequestParams = standardReqParams;
                            break;

                        case "RAW":
                            var rawReqParams = new RawRequestParams();

                            line = reader.ReadLine().Trim();
                            lineCopy = line;
                            lineNumber++;
                            LoliCodeParser.ParseSettingValue(ref line, rawReqParams.Content, new ByteArrayParameter());

                            line = reader.ReadLine().Trim();
                            lineCopy = line;
                            lineNumber++;
                            LoliCodeParser.ParseSettingValue(ref line, rawReqParams.ContentType, new StringParameter());

                            RequestParams = rawReqParams;
                            break;

                        case "BASICAUTH":
                            var basicAuthReqParams = new BasicAuthRequestParams();

                            line = reader.ReadLine().Trim();
                            lineCopy = line;
                            lineNumber++;
                            LoliCodeParser.ParseSettingValue(ref line, basicAuthReqParams.Username, new StringParameter());

                            line = reader.ReadLine().Trim();
                            lineCopy = line;
                            lineNumber++;
                            LoliCodeParser.ParseSettingValue(ref line, basicAuthReqParams.Password, new StringParameter());

                            RequestParams = basicAuthReqParams;
                            break;

                        case "MULTIPART":
                            var multipartReqParams = new MultipartRequestParams();

                            line = reader.ReadLine().Trim();
                            lineCopy = line;
                            lineNumber++;
                            LoliCodeParser.ParseSettingValue(ref line, multipartReqParams.Boundary, new StringParameter());

                            RequestParams = multipartReqParams;
                            break;

                        default:
                            throw new LoliCodeParsingException(lineNumber, $"Invalid type: {reqParams}");
                    }
                }
                catch (NullReferenceException)
                {
                    throw new LoliCodeParsingException(lineNumber, "Missing options for the selected content");
                }
                catch
                {
                    throw new LoliCodeParsingException(lineNumber, $"Could not parse the setting: {lineCopy.TruncatePretty(50)}");
                }
            }
            else if (line.StartsWith("CONTENT:"))
            {
                try
                {
                    var multipart = (MultipartRequestParams)RequestParams;
                    var token = LineParser.ParseToken(ref line);
                    switch (MyRegex1().Match(token).Groups[1].Value)
                    {
                        case "STRING":
                            var stringContent = new StringHttpContentSettingsGroup();
                            LoliCodeParser.ParseSettingValue(ref line, stringContent.Name, new StringParameter());
                            LoliCodeParser.ParseSettingValue(ref line, stringContent.Data, new StringParameter());
                            LoliCodeParser.ParseSettingValue(ref line, stringContent.ContentType, new StringParameter());
                            multipart.Contents.Add(stringContent);
                            break;

                        case "RAW":
                            var rawContent = new RawHttpContentSettingsGroup();
                            LoliCodeParser.ParseSettingValue(ref line, rawContent.Name, new StringParameter());

                            var lineCopyCache = line;

                            try
                            {
                                LoliCodeParser.ParseSettingValue(ref line, rawContent.Data, new ByteArrayParameter());
                            }
                            catch
                            {
                                line = lineCopyCache;
                            }

                            LoliCodeParser.ParseSettingValue(ref line, rawContent.ContentType, new StringParameter());
                            multipart.Contents.Add(rawContent);
                            break;

                        case "FILE":
                            var fileContent = new FileHttpContentSettingsGroup();
                            LoliCodeParser.ParseSettingValue(ref line, fileContent.Name, new StringParameter());
                            LoliCodeParser.ParseSettingValue(ref line, fileContent.FileName, new StringParameter());
                            LoliCodeParser.ParseSettingValue(ref line, fileContent.ContentType, new StringParameter());
                            multipart.Contents.Add(fileContent);
                            break;
                    }
                }
                catch
                {
                    throw new LoliCodeParsingException(lineNumber, $"Could not parse the multipart content: {lineCopy.TruncatePretty(50)}");
                }
            }
            else
            {
                try
                {
                    if (IsLegacyHttpCloakPresetSetting(line))
                    {
                        continue;
                    }

                    line = RewriteLegacyHttpLibrarySetting(line);
                    LoliCodeParser.ParseSetting(ref line, Settings, Descriptor);
                }
                catch
                {
                    throw new LoliCodeParsingException(lineNumber, $"Could not parse the setting: {lineCopy.TruncatePretty(50)}");
                }
            }
        }
    }
}
