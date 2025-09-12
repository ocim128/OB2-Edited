# UI Organization Structure

This document outlines the improved organization structure for the OpenBullet2.Native UI layer.

## Folder Structure

### `/Controls/`
Organized into logical categories for better maintainability:

#### `/Controls/Common/`
Shared UI components used across multiple parts of the application:
- `ColoredLog` - Enhanced log display component
- `HTMLViewer` - HTML content viewer
- `MarkdownViewer` - Markdown content renderer

#### `/Controls/Settings/`
Settings-related controls organized by functionality:

##### `/Settings/Viewers/`
Basic setting value viewers:
- `BoolSettingViewer` - Boolean value display
- `ByteArraySettingViewer` - Byte array display
- `EnumSettingViewer` - Enumeration value selector
- `FloatSettingViewer` - Floating-point number input
- `IntSettingViewer` - Integer number input
- `StringSettingViewer` - Text string input
- `ListOfStringsSettingViewer` - String list management
- `DictionaryOfStringsSettingViewer` - Key-value pair management

##### `/Settings/Blocks/`
Block-specific configuration controls:
- `AutoBlockSettingsViewer` - Auto block configuration
- `HttpRequestBlockSettingsViewer` - HTTP request block settings
- `KeycheckBlockSettingsViewer` - Keycheck block configuration
- `LoliCodeBlockSettingsViewer` - LoliCode block settings
- `ParseBlockSettingsViewer` - Parse block configuration
- `ScriptBlockSettingsViewer` - Script block settings

##### `/Settings/Inputs/`
Specialized input controls:
- `ImagePicker` - Image selection control
- `MultipleSelector` - Multi-selection component
- `TimeSpanPicker` - Time duration selector
- `KeychainViewer` - Security key management
- `KeyViewer` - Individual key display
- `CreateMultipleConstantViewer` - Constant creation utility

### `/Constants/`
Centralized constants and configuration:
- `UIConstants.cs` - UI-related constants (animations, layouts, resources)

## ViewModels Organization

### `/ViewModels/Infrastructure/`
Clean MVVM infrastructure:
- `ViewModelBase.cs` - Enhanced base class with better property change notification
- `RelayCommand.cs` - Command pattern implementation
- `IRelayCommand.cs` - Command interfaces

### Key Improvements

#### 1. Clean MVVM Infrastructure
- **Better Property Management**: Improved OnPropertyChanged with CallerMemberName
- **Command Pattern**: Proper RelayCommand implementation for better separation of concerns
- **Backward Compatibility**: Maintains compatibility with existing ViewModels

#### 2. Logical Organization
- **Category-Based Structure**: Controls organized by functionality
- **Clear Separation**: Different types of controls in appropriate folders
- **Better Discoverability**: Easier to find specific controls

#### 3. Simplified Constants
- **Essential Constants Only**: Removed unused/speculative constants
- **Based on Actual Usage**: Only includes constants that match existing resources

## Best Practices

### ViewModel Development
1. Inherit from `Infrastructure.ViewModelBase` for better property change notification
2. Use `RelayCommand` for command binding where needed
3. Keep ViewModels focused and lightweight

### Control Development
1. Place controls in appropriate category folders
2. Follow naming conventions (e.g., `*Viewer` for display, `*Picker` for input)
3. Use constants from `UIConstants` for consistent styling (when needed)

### Service Usage
1. Continue using the existing `ViewModelsService` which works well
2. Follow the established service location pattern with `ServiceLocator.GetService<T>()`

## Migration Guide

### For Existing Code
1. Update using statements to reference new namespace locations for moved controls
2. Existing ViewModels continue to work without changes
3. Optionally migrate to enhanced ViewModelBase for better property change handling

### For New Development
1. Use the new organizational structure for controls
2. Consider using the enhanced ViewModelBase for cleaner property management
3. Follow established patterns for consistency

This organization improves code maintainability, promotes better separation of concerns, and provides a solid foundation for future UI development.
