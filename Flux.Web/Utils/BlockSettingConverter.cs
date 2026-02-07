using AutoMapper;
using Flux.Web.Dtos.Config.Blocks;
using RuriLib.Models.Blocks.Settings;

namespace Flux.Web.Utils;

internal class BlockSettingConverter : ITypeConverter<BlockSettingDto, BlockSetting>
{
    public BlockSetting Convert(BlockSettingDto source, BlockSetting destination, ResolutionContext context) =>
        destination;
}
