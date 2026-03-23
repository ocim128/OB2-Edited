using RuriLib.Extensions;
using RuriLib.Models.Blocks;
using RuriLib.Models.Blocks.Custom;
using RuriLib.Models.Blocks.Parameters;
using RuriLib.Models.Blocks.Settings;
using RuriLib.Models.Bots;
using RuriLib.Models.Trees;
using RuriLib.Models.Variables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace RuriLib.Helpers.Blocks
{
    /// <summary>
    /// Repository of all block descriptors that are available to create new blocks.
    /// </summary>
    public class DescriptorsRepository
    {
        public ConcurrentDictionary<string, BlockDescriptor> Descriptors { get; set; } = new ConcurrentDictionary<string, BlockDescriptor>();

        /// <summary>
        /// Initializes a <see cref="DescriptorsRepository"/> and imports blocks from the executing assembly.
        /// </summary>
        public DescriptorsRepository()
        {
            Recreate();
        }

        /// <summary>
        /// Recreates the repository with only the descriptors in the executing assembly (no plugins).
        /// </summary>
        public void Recreate()
        {
            Descriptors.Clear();
            BuiltInBlockRegistry.RegisterBuiltIns(this);
        }

        /// <summary>
        /// Gets a <see cref="BlockDescriptor"/> by its unique <paramref name="id"/>
        /// and automatically casts it to the type <typeparamref name="T"/>.
        /// </summary>
        public T GetAs<T>(string id) where T : BlockDescriptor
        {
            if (!Descriptors.TryGetValue(id, out BlockDescriptor descriptor))
                throw new Exception($"No descriptor was found with the given id: {id}");

            return descriptor as T;
        }

        public void AddDescriptor(BlockDescriptor descriptor)
        {
            if (!Descriptors.TryAdd(descriptor.Id, descriptor))
                throw new Exception($"Duplicate descriptor id: {descriptor.Id}");
        }

        /// <summary>
        /// Adds descriptors to the repository by finding exposed methods in the given
        /// <paramref name="assembly"/>.
        /// </summary>
        public void AddFromExposedMethods(Assembly assembly)
            => AddFromExposedMethods(assembly.GetTypes());

        public void AddFromExposedMethods(IEnumerable<Type> types)
        {
            var exposedTypes = types.ToList();
            foreach (var type in exposedTypes)
            {
                AddDescriptorsFromType(type);
            }

            AddBlockActions(exposedTypes);
        }

        private void AddDescriptorsFromType(Type type)
        {
            var category = type.GetCustomAttribute<Attributes.BlockCategory>();
            if (category == null)
            {
                return;
            }

            foreach (var method in type.GetMethods())
            {
                var attribute = method.GetCustomAttribute<Attributes.Block>();
                if (attribute == null)
                {
                    continue;
                }

                AddDescriptor(BuildAutoBlockDescriptor(type, method, category, attribute));
            }
        }

        private void AddBlockActions(IEnumerable<Type> types)
        {
            foreach (var type in types)
            {
                foreach (var method in type.GetMethods())
                {
                    var attribute = method.GetCustomAttribute<Attributes.BlockAction>();
                    if (attribute == null)
                    {
                        continue;
                    }

                    var id = attribute.parentBlockId;

                    if (!Descriptors.TryGetValue(id, out var descriptor))
                        throw new Exception($"Invalid descriptor id: {id}");

                    descriptor.Actions.Add(new BlockActionInfo
                    {
                        Name = attribute.name ?? method.Name.ToReadableName(),
                        Description = attribute.description ?? string.Empty,
                        Delegate = method.CreateDelegate<BlockActionDelegate>()
                    });
                }
            }
        }

        private static AutoBlockDescriptor BuildAutoBlockDescriptor(
            Type type,
            MethodInfo method,
            Attributes.BlockCategory category,
            Attributes.Block attribute)
        {
            var parameters = method.GetParameters().Where(p => p.ParameterType != typeof(BotData)).ToArray();

            return new AutoBlockDescriptor
            {
                Id = method.Name,
                Async = method.CustomAttributes.Any(a => a.AttributeType == typeof(AsyncStateMachineAttribute)),
                Name = attribute.name ?? method.Name.ToReadableName(),
                Description = attribute.description ?? string.Empty,
                ExtraInfo = attribute.extraInfo ?? string.Empty,
                AssemblyFullName = type.Assembly.FullName,
                Parameters = parameters.Select(BuildBlockParameter).ToDictionary(p => p.Name, p => p),
                OriginalParameterTypes = parameters.ToDictionary(GetParameterName, p => p.ParameterType),
                ReturnType = ToVariableType(method.ReturnType),
                Category = new BlockCategory
                {
                    Name = category.name ?? GetDefaultCategoryName(type),
                    Path = type.Namespace ?? string.Empty,
                    Namespace = $"{type.Namespace}.{type.Name}",
                    Description = category.description,
                    ForegroundColor = category.foregroundColor,
                    BackgroundColor = category.backgroundColor
                },
                Images = method.GetCustomAttributes<Attributes.BlockImage>()
                    .ToDictionary(a => a.id, a => new BlockImageInfo
                    {
                        Name = a.id.ToReadableName(),
                        MaxWidth = a.maxWidth,
                        MaxHeight = a.maxHeight
                    }),
                InstanceFactory = CreateAutoBlockInstanceFactory(method.Name)
            };
        }

        private static string GetDefaultCategoryName(Type type)
        {
            var namespaceParts = type.Namespace?.Split('.');
            return namespaceParts?.Length > 2 ? namespaceParts[2] : type.Name;
        }

        private static string GetParameterName(ParameterInfo parameter)
            => parameter.Name ?? throw new ArgumentException($"Parameter without a name in method signature {parameter.Member.Name}");

        private static Func<BlockDescriptor, BlockInstance> CreateAutoBlockInstanceFactory(string id)
            => id == "ConstantString"
                ? static descriptor => new ConditionalConstantStringBlockInstance((AutoBlockDescriptor)descriptor)
                : static descriptor => new AutoBlockInstance((AutoBlockDescriptor)descriptor);

        private static BlockParameter BuildBlockParameter(ParameterInfo info)
        {
            var parameter = ToBlockParameter(info);
            var variableParam = info.GetCustomAttribute<Attributes.Variable>();
            var interpParam = info.GetCustomAttribute<Attributes.Interpolated>();

            if (variableParam != null)
            {
                parameter.InputMode = SettingInputMode.Variable;
                // Updated to use the new immutable property name
                parameter.DefaultVariableName = variableParam.DefaultVariableName;
            }
            else if (interpParam != null)
            {
                parameter.InputMode = SettingInputMode.Interpolated;
            }

            return parameter;
        }

        /// <summary>
        /// Converts the return <paramref name="type"/> of a method to a <see cref="VariableType"/>.
        /// Returns null if the method returns <see cref="void"/> or <see cref="Task"/>.
        /// </summary>
        public static VariableType? ToVariableType(Type type)
        {
            if (type == typeof(void))
                return null;

            var dict = new Dictionary<Type, VariableType>
            {
                { typeof(string), VariableType.String },
                { typeof(int), VariableType.Int },
                { typeof(float), VariableType.Float },
                { typeof(bool), VariableType.Bool },
                { typeof(List<string>), VariableType.ListOfStrings },
                { typeof(Dictionary<string, string>), VariableType.DictionaryOfStrings },
                { typeof(byte[]), VariableType.ByteArray }
            };

            if (dict.ContainsKey(type))
                return dict[type];

            if (type == typeof(Task))
                return null;

            var taskDict = new Dictionary<Type, VariableType>
            {
                { typeof(Task<string>), VariableType.String },
                { typeof(Task<int>), VariableType.Int },
                { typeof(Task<float>), VariableType.Float },
                { typeof(Task<bool>), VariableType.Bool },
                { typeof(Task<List<string>>), VariableType.ListOfStrings },
                { typeof(Task<Dictionary<string, string>>), VariableType.DictionaryOfStrings },
                { typeof(Task<byte[]>), VariableType.ByteArray }
            };

            if (taskDict.ContainsKey(type))
                return taskDict[type];

            throw new InvalidCastException($"The type {type} could not be casted to VariableType");
        }

        /// <summary>
        /// Casts a C# variable with a given <paramref name="name"/>, <paramref name="type"/>
        /// and <paramref name="value"/> to a custom <see cref="Variable"/> object.
        /// </summary>
        public static Variable ToVariable(string name, Type type, dynamic value)
        {
            var t = ToVariableType(type);

            if (!t.HasValue)
                throw new InvalidCastException($"Cannot cast type {type} to a variable");

            Variable variable = t switch
            {
                VariableType.String => new StringVariable(value),
                VariableType.Bool => new BoolVariable(value),
                VariableType.ByteArray => new ByteArrayVariable(value),
                VariableType.DictionaryOfStrings => new DictionaryOfStringsVariable(value),
                VariableType.Float => new FloatVariable(value),
                VariableType.Int => new IntVariable(value),
                VariableType.ListOfStrings => new ListOfStringsVariable(value),
                _ => throw new NotImplementedException(),
            };

            variable.Name = name;
            variable.Type = t.Value;
            return variable;
        }

        private static BlockParameter ToBlockParameter(ParameterInfo parameter)
        {
            var dict = new Dictionary<Type, Func<BlockParameter>>
            {
                { typeof(string), () => new StringParameter
                    {
                        DefaultValue = parameter.HasDefaultValue ? (string)parameter.DefaultValue : "",
                        MultiLine = parameter.GetCustomAttribute<Attributes.MultiLine>() != null
                    }
                },

                { typeof(int), () => new IntParameter
                    { DefaultValue = parameter.HasDefaultValue ? (int)parameter.DefaultValue : 0 } },

                { typeof(float), () => new FloatParameter
                    { DefaultValue = parameter.HasDefaultValue ? (float)parameter.DefaultValue : 0.0f } },

                { typeof(bool), () => new BoolParameter
                    { DefaultValue = parameter.HasDefaultValue ? (bool)parameter.DefaultValue : false } },

                { typeof(bool?), () => new BoolParameter
                    { DefaultValue = parameter.HasDefaultValue && parameter.DefaultValue != null ? (bool)parameter.DefaultValue : false } },

                // TODO: Add defaults for these through parameter attributes
                { typeof(List<string>), () => new ListOfStringsParameter() },
                { typeof(Dictionary<string, string>), () => new DictionaryOfStringsParameter() },
                { typeof(byte[]), () => new ByteArrayParameter() },
                { typeof(string[]), () => new ListOfStringsParameter() }
            };

            var blockParamAttribute = parameter.GetCustomAttribute<Attributes.BlockParam>();

            // If it's one of the standard types
            if (dict.ContainsKey(parameter.ParameterType))
            {
                var blockParam = dict[parameter.ParameterType].Invoke();

                if (blockParamAttribute != null)
                {
                    blockParam.AssignedName = blockParamAttribute.name;
                    blockParam.Description = blockParamAttribute.description;
                }

                blockParam.Name = parameter.Name;
                return blockParam;
            }

            // If it's an enum type
            if (parameter.ParameterType.IsEnum)
            {
                var blockParam = new EnumParameter
                {
                    Name = parameter.Name,
                    EnumType = parameter.ParameterType,
                    DefaultValue = parameter.HasDefaultValue
                        ? parameter.DefaultValue.ToString()
                        : Enum.GetNames(parameter.ParameterType).First()
                };

                if (blockParamAttribute != null)
                {
                    blockParam.AssignedName = blockParamAttribute.name;
                }

                return blockParam;
            }

            // If it's a nullable enum type
            var underlyingType = Nullable.GetUnderlyingType(parameter.ParameterType);
            if (underlyingType != null && underlyingType.IsEnum)
            {
                var blockParam = new EnumParameter
                {
                    Name = parameter.Name,
                    EnumType = underlyingType,
                    DefaultValue = parameter.HasDefaultValue && parameter.DefaultValue != null
                        ? parameter.DefaultValue.ToString()
                        : Enum.GetNames(underlyingType).First()
                };

                if (blockParamAttribute != null)
                {
                    blockParam.AssignedName = blockParamAttribute.name;
                }

                return blockParam;
            }

            throw new ArgumentException($"Parameter {parameter.Name} has an invalid type ({parameter.ParameterType})");
        }

        /// <summary>
        /// Retrieves the category tree of all categories and block descriptors.
        /// </summary>
        public CategoryTreeNode AsTree()
        {
            // This is the root node, all assemblies are direct children of this node
            var root = new CategoryTreeNode
            {
                Name = "Root",
                // Add all descriptors as children of the root node (we need the ToList() in order to have
                // a new pointer to list and not operate on the same one Descriptors uses, since we will be removing items)
                Descriptors = Descriptors.Values.ToList()
            };

            // Push leaves down
            PushLeaves(root, 0);

            return root;
        }

        private void PushLeaves(CategoryTreeNode node, int level)
        {
            // Check all descriptors of the node
            for (var i = 0; i < node.Descriptors.Count; i++)
            {
                var d = node.Descriptors[i];
                var split = d.Category.Path.Split('.'); // Example: RuriLib.Blocks.Http

                // If a descriptor's category has a namespace which (split) is longer than the current tree level
                if (split.Length > level)
                {
                    var subCat = split[level]; // Example: level 0 => RuriLib, level 1 => Http

                    // Try to get an existing subcategory node
                    var subCatNode = node.SubCategories.FirstOrDefault(s => s.Name == subCat);

                    // Create the subcategory node if it doesn't exist
                    if (subCatNode == null)
                    {
                        subCatNode = new CategoryTreeNode { Parent = node, Name = subCat };
                        node.SubCategories.Add(subCatNode);
                    }

                    subCatNode.Descriptors.Add(d);
                    node.Descriptors.RemoveAt(i);
                    i--;
                }
            }

            // Order them alphabetically
            node.SubCategories = node.SubCategories.OrderBy(s => s.Name).ToList();
            node.Descriptors = node.Descriptors.OrderBy(d => d.Name).ToList();

            // Push leaves of subcategories recursively
            for (var i = 0; i < node.SubCategories.Count; i++)
            {
                var s = node.SubCategories[i];
                PushLeaves(s, level + 1);
            }
        }
    }
}
