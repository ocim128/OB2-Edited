using RuriLib.Models.Blocks;
using System;

namespace RuriLib.Helpers.Blocks
{
    /// <summary>
    /// In charge of creating new blocks.
    /// </summary>
    public class BlockFactory
    {
        /// <summary>
        /// The label of the block, useful to specify the purpose of the block.
        /// </summary>
        public string Label { get; set; } = null;

        /// <summary>
        /// Whether the block is disabled and will not be executed.
        /// </summary>
        public bool Disabled { get; set; } = false;

        /// <summary>
        /// Gets a block by <paramref name="id"/> and casts it to the requested type.
        /// </summary>
        public static T GetBlock<T>(string id) where T : BlockInstance
        {
            if (!Globals.DescriptorsRepository.Descriptors.TryGetValue(id, out BlockDescriptor descriptor))
                throw new Exception($"Invalid block id: {id}");

            return descriptor.CreateBlockInstance() as T
                ?? throw new InvalidCastException($"Block {id} could not be cast to {typeof(T).Name}");
        }
    }
}
