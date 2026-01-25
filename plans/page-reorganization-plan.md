# Page Organization Plan

## Goal
Group pages by feature domain to improve code organization, maintainability, and scalability.

## Current Structure Analysis

### Current Views/Pages Structure (Flat)
```
Views/Pages/
├── About.xaml + .xaml.cs
├── ConfigCSharpCode.xaml + .xaml.cs
├── ConfigEditor.xaml + .xaml.cs
├── ConfigLoliCode.xaml + .xaml.cs
├── ConfigMetadata.xaml + .xaml.cs
├── ConfigReadme.xaml + .xaml.cs
├── Configs.xaml + .xaml.cs
├── ConfigSettings.xaml + .xaml.cs
├── ConfigStacker.xaml + .xaml.cs
├── Hits.xaml + .xaml.cs
├── Home.xaml + .xaml.cs
├── Jobs.xaml + .xaml.cs
├── Monitor.xaml + .xaml.cs
├── MultiRunJobViewer.xaml + .xaml.cs
├── OBSettings.xaml + .xaml.cs
├── Plugins.xaml + .xaml.cs
├── Proxies.xaml + .xaml.cs
├── ProxyCheckJobViewer.xaml + .xaml.cs
├── RLSettings.xaml + .xaml.cs
├── Wordlists.xaml + .xaml.cs
└── Shared/
    ├── Debugger.xaml + .xaml.cs
    └── (other debugger files)
```

### Current ViewModels Structure (Flat)
```
ViewModels/
├── ConfigMetadataViewModel.cs
├── ConfigReadmeViewModel.cs
├── ConfigSettingsViewModel.cs
├── ConfigStackerViewModel.cs
├── ConfigsViewModel.cs
├── DebuggerViewModel.cs
├── HitsViewModel.cs
├── JobsViewModel.cs
├── MainWindowViewModel.cs
├── MultiRunJobViewerViewModel.cs
├── OBSettingsViewModel.cs
├── PluginsViewModel.cs
├── ProxiesViewModel.cs
├── ProxyCheckJobViewerViewModel.cs
├── RLSettingsViewModel.cs
├── WordlistsViewModel.cs
├── Base/
├── Pages/
└── Tools/
```

## Proposed Structure

### Views/Pages Structure (Feature-Domain Organized)
```
Views/Pages/
├── Home/
│   └── Home.xaml + .xaml.cs
│
├── Config/              # Config management pages
│   ├── Configs.xaml + .xaml.cs
│   ├── ConfigEditor.xaml + .xaml.cs
│   ├── ConfigMetadata.xaml + .xaml.cs
│   ├── ConfigReadme.xaml + .xaml.cs
│   ├── ConfigSettings.xaml + .xaml.cs
│   ├── ConfigStacker.xaml + .xaml.cs
│   ├── ConfigLoliCode.xaml + .xaml.cs
│   └── ConfigCSharpCode.xaml + .xaml.cs
│
├── Job/                 # Job pages
│   ├── Jobs.xaml + .xaml.cs
│   ├── MultiRunJobViewer.xaml + .xaml.cs
│   └── ProxyCheckJobViewer.xaml + .xaml.cs
│
├── Data/                # Data management
│   ├── Proxies.xaml + .xaml.cs
│   ├── Wordlists.xaml + .xaml.cs
│   └── Hits.xaml + .xaml.cs
│
├── Tools/               # Tools & monitoring
│   ├── Monitor.xaml + .xaml.cs
│   └── Plugins.xaml + .xaml.cs
│
├── Settings/            # Settings pages
│   ├── OBSettings.xaml + .xaml.cs
│   └── RLSettings.xaml + .xaml.cs
│
├── About/               # About page
│   └── About.xaml + .xaml.cs
│
└── Shared/              # Shared page components
    ├── Debugger.xaml + .xaml.cs
    └── (existing debugger files)
```

### ViewModels Structure (Feature-Domain Organized)
```
ViewModels/
├── MainWindowViewModel.cs
├── Base/
│
├── Config/
│   ├── ConfigsViewModel.cs
│   ├── ConfigMetadataViewModel.cs
│   ├── ConfigReadmeViewModel.cs
│   ├── ConfigSettingsViewModel.cs
│   └── ConfigStackerViewModel.cs
│
├── Job/
│   ├── JobsViewModel.cs
│   ├── MultiRunJobViewerViewModel.cs
│   └── ProxyCheckJobViewerViewModel.cs
│
├── Data/
│   ├── ProxiesViewModel.cs
│   ├── WordlistsViewModel.cs
│   └── HitsViewModel.cs
│
├── Tools/
│   ├── PluginsViewModel.cs
│   └── (other tool viewmodels)
│
├── Settings/
│   ├── OBSettingsViewModel.cs
│   └── RLSettingsViewModel.cs
│
├── About/
│   └── (if AboutViewModel exists)
│
└── Shared/
    └── DebuggerViewModel.cs
```

## Implementation Steps

### Phase 1: Preparation
1. **Backup current structure** - Create a backup of the entire Views/Pages and ViewModels directories
2. **Identify all references** - Search for all usings and type references to pages and viewmodels across the codebase
3. **Document dependencies** - Create a dependency graph showing which files reference which pages/viewmodels

### Phase 2: Create New Directory Structure
1. Create new feature-domain directories under Views/Pages:
   - `Views/Pages/Home/`
   - `Views/Pages/Config/`
   - `Views/Pages/Job/`
   - `Views/Pages/Data/`
   - `Views/Pages/Tools/`
   - `Views/Pages/Settings/`
   - `Views/Pages/About/`

2. Create new feature-domain directories under ViewModels:
   - `ViewModels/Config/`
   - `ViewModels/Job/`
   - `ViewModels/Data/`
   - `ViewModels/Tools/`
   - `ViewModels/Settings/`
   - `ViewModels/About/`
   - `ViewModels/Shared/`

### Phase 3: Move Files
1. **Move View files** - Move XAML and code-behind files to their respective feature directories
2. **Move ViewModel files** - Move ViewModel files to their respective feature directories
3. **Update namespaces** - Update namespace declarations in all moved files to reflect new structure

### Phase 4: Update References
1. **Update NavigationService.cs** - Update using statements and type references
2. **Update MainWindow.xaml.cs** - Update using statements and type references
3. **Update all other files** - Update using statements and type references throughout the codebase
4. **Update XAML files** - Update x:Class attributes and any type references in XAML

### Phase 5: Update Build Configuration
1. **Update .csproj file** - Ensure all files are included in the project with correct paths
2. **Verify build** - Build the project and fix any compilation errors

### Phase 6: Testing
1. **Verify navigation** - Test all navigation paths work correctly
2. **Verify page functionality** - Test each page to ensure functionality is preserved
3. **Verify ViewModels** - Ensure all ViewModels are properly connected to their Views

## Detailed File Mapping

### Home Domain
| Current Path | New Path |
|-------------|----------|
| `Views/Pages/Home.xaml` | `Views/Pages/Home/Home.xaml` |
| `Views/Pages/Home.xaml.cs` | `Views/Pages/Home/Home.xaml.cs` |

### Config Domain
| Current Path | New Path |
|-------------|----------|
| `Views/Pages/Configs.xaml` | `Views/Pages/Config/Configs.xaml` |
| `Views/Pages/Configs.xaml.cs` | `Views/Pages/Config/Configs.xaml.cs` |
| `Views/Pages/ConfigEditor.xaml` | `Views/Pages/Config/ConfigEditor.xaml` |
| `Views/Pages/ConfigEditor.xaml.cs` | `Views/Pages/Config/ConfigEditor.xaml.cs` |
| `Views/Pages/ConfigMetadata.xaml` | `Views/Pages/Config/ConfigMetadata.xaml` |
| `Views/Pages/ConfigMetadata.xaml.cs` | `Views/Pages/Config/ConfigMetadata.xaml.cs` |
| `Views/Pages/ConfigReadme.xaml` | `Views/Pages/Config/ConfigReadme.xaml` |
| `Views/Pages/ConfigReadme.xaml.cs` | `Views/Pages/Config/ConfigReadme.xaml.cs` |
| `Views/Pages/ConfigSettings.xaml` | `Views/Pages/Config/ConfigSettings.xaml` |
| `Views/Pages/ConfigSettings.xaml.cs` | `Views/Pages/Config/ConfigSettings.xaml.cs` |
| `Views/Pages/ConfigStacker.xaml` | `Views/Pages/Config/ConfigStacker.xaml` |
| `Views/Pages/ConfigStacker.xaml.cs` | `Views/Pages/Config/ConfigStacker.xaml.cs` |
| `Views/Pages/ConfigLoliCode.xaml` | `Views/Pages/Config/ConfigLoliCode.xaml` |
| `Views/Pages/ConfigLoliCode.xaml.cs` | `Views/Pages/Config/ConfigLoliCode.xaml.cs` |
| `Views/Pages/ConfigCSharpCode.xaml` | `Views/Pages/Config/ConfigCSharpCode.xaml` |
| `Views/Pages/ConfigCSharpCode.xaml.cs` | `Views/Pages/Config/ConfigCSharpCode.xaml.cs` |

### Job Domain
| Current Path | New Path |
|-------------|----------|
| `Views/Pages/Jobs.xaml` | `Views/Pages/Job/Jobs.xaml` |
| `Views/Pages/Jobs.xaml.cs` | `Views/Pages/Job/Jobs.xaml.cs` |
| `Views/Pages/MultiRunJobViewer.xaml` | `Views/Pages/Job/MultiRunJobViewer.xaml` |
| `Views/Pages/MultiRunJobViewer.xaml.cs` | `Views/Pages/Job/MultiRunJobViewer.xaml.cs` |
| `Views/Pages/ProxyCheckJobViewer.xaml` | `Views/Pages/Job/ProxyCheckJobViewer.xaml` |
| `Views/Pages/ProxyCheckJobViewer.xaml.cs` | `Views/Pages/Job/ProxyCheckJobViewer.xaml.cs` |

### Data Domain
| Current Path | New Path |
|-------------|----------|
| `Views/Pages/Proxies.xaml` | `Views/Pages/Data/Proxies.xaml` |
| `Views/Pages/Proxies.xaml.cs` | `Views/Pages/Data/Proxies.xaml.cs` |
| `Views/Pages/Wordlists.xaml` | `Views/Pages/Data/Wordlists.xaml` |
| `Views/Pages/Wordlists.xaml.cs` | `Views/Pages/Data/Wordlists.xaml.cs` |
| `Views/Pages/Hits.xaml` | `Views/Pages/Data/Hits.xaml` |
| `Views/Pages/Hits.xaml.cs` | `Views/Pages/Data/Hits.xaml.cs` |

### Tools Domain
| Current Path | New Path |
|-------------|----------|
| `Views/Pages/Monitor.xaml` | `Views/Pages/Tools/Monitor.xaml` |
| `Views/Pages/Monitor.xaml.cs` | `Views/Pages/Tools/Monitor.xaml.cs` |
| `Views/Pages/Plugins.xaml` | `Views/Pages/Tools/Plugins.xaml` |
| `Views/Pages/Plugins.xaml.cs` | `Views/Pages/Tools/Plugins.xaml.cs` |

### Settings Domain
| Current Path | New Path |
|-------------|----------|
| `Views/Pages/OBSettings.xaml` | `Views/Pages/Settings/OBSettings.xaml` |
| `Views/Pages/OBSettings.xaml.cs` | `Views/Pages/Settings/OBSettings.xaml.cs` |
| `Views/Pages/RLSettings.xaml` | `Views/Pages/Settings/RLSettings.xaml` |
| `Views/Pages/RLSettings.xaml.cs` | `Views/Pages/Settings/RLSettings.xaml.cs` |

### About Domain
| Current Path | New Path |
|-------------|----------|
| `Views/Pages/About.xaml` | `Views/Pages/About/About.xaml` |
| `Views/Pages/About.xaml.cs` | `Views/Pages/About/About.xaml.cs` |

### Shared (No Change)
| Current Path | New Path |
|-------------|----------|
| `Views/Pages/Shared/` | `Views/Pages/Shared/` (unchanged) |

## ViewModel File Mapping

### Config Domain ViewModels
| Current Path | New Path |
|-------------|----------|
| `ViewModels/ConfigsViewModel.cs` | `ViewModels/Config/ConfigsViewModel.cs` |
| `ViewModels/ConfigMetadataViewModel.cs` | `ViewModels/Config/ConfigMetadataViewModel.cs` |
| `ViewModels/ConfigReadmeViewModel.cs` | `ViewModels/Config/ConfigReadmeViewModel.cs` |
| `ViewModels/ConfigSettingsViewModel.cs` | `ViewModels/Config/ConfigSettingsViewModel.cs` |
| `ViewModels/ConfigStackerViewModel.cs` | `ViewModels/Config/ConfigStackerViewModel.cs` |

### Job Domain ViewModels
| Current Path | New Path |
|-------------|----------|
| `ViewModels/JobsViewModel.cs` | `ViewModels/Job/JobsViewModel.cs` |
| `ViewModels/MultiRunJobViewerViewModel.cs` | `ViewModels/Job/MultiRunJobViewerViewModel.cs` |
| `ViewModels/ProxyCheckJobViewerViewModel.cs` | `ViewModels/Job/ProxyCheckJobViewerViewModel.cs` |

### Data Domain ViewModels
| Current Path | New Path |
|-------------|----------|
| `ViewModels/ProxiesViewModel.cs` | `ViewModels/Data/ProxiesViewModel.cs` |
| `ViewModels/WordlistsViewModel.cs` | `ViewModels/Data/WordlistsViewModel.cs` |
| `ViewModels/HitsViewModel.cs` | `ViewModels/Data/HitsViewModel.cs` |

### Tools Domain ViewModels
| Current Path | New Path |
|-------------|----------|
| `ViewModels/PluginsViewModel.cs` | `ViewModels/Tools/PluginsViewModel.cs` |
| `ViewModels/Tools/` | `ViewModels/Tools/` (unchanged) |

### Settings Domain ViewModels
| Current Path | New Path |
|-------------|----------|
| `ViewModels/OBSettingsViewModel.cs` | `ViewModels/Settings/OBSettingsViewModel.cs` |
| `ViewModels/RLSettingsViewModel.cs` | `ViewModels/Settings/RLSettingsViewModel.cs` |

### Shared ViewModels
| Current Path | New Path |
|-------------|----------|
| `ViewModels/DebuggerViewModel.cs` | `ViewModels/Shared/DebuggerViewModel.cs` |

### Base (No Change)
| Current Path | New Path |
|-------------|----------|
| `ViewModels/Base/` | `ViewModels/Base/` (unchanged) |

## Namespace Updates

### View Namespaces
- `OpenBullet2.Native.Views.Pages.Home` (for Home.xaml.cs)
- `OpenBullet2.Native.Views.Pages.Config` (for Config pages)
- `OpenBullet2.Native.Views.Pages.Job` (for Job pages)
- `OpenBullet2.Native.Views.Pages.Data` (for Data pages)
- `OpenBullet2.Native.Views.Pages.Tools` (for Tools pages)
- `OpenBullet2.Native.Views.Pages.Settings` (for Settings pages)
- `OpenBullet2.Native.Views.Pages.About` (for About pages)
- `OpenBullet2.Native.Views.Pages.Shared` (unchanged)

### ViewModel Namespaces
- `OpenBullet2.Native.ViewModels.Config` (for Config viewmodels)
- `OpenBullet2.Native.ViewModels.Job` (for Job viewmodels)
- `OpenBullet2.Native.ViewModels.Data` (for Data viewmodels)
- `OpenBullet2.Native.ViewModels.Tools` (for Tools viewmodels)
- `OpenBullet2.Native.ViewModels.Settings` (for Settings viewmodels)
- `OpenBullet2.Native.ViewModels.Shared` (for Shared viewmodels)
- `OpenBullet2.Native.ViewModels.Base` (unchanged)

## Files Requiring Updates

### High Priority Files
1. `OpenBullet2.Native/Services/NavigationService.cs` - Page instantiation and type references
2. `OpenBullet2.Native/MainWindow.xaml.cs` - Page references and type casts
3. `OpenBullet2.Native/MainWindow.xaml` - XAML type references (if any)
4. `OpenBullet2.Native/ViewModels/MainWindowViewModel.cs` - ViewModel references

### Medium Priority Files
1. `OpenBullet2.Native/Views/Dialogs/Job/ChangeBotsDialog.xaml.cs` - Job viewer references
2. `OpenBullet2.Native/ViewModels/Pages/ToolsPageViewModel.cs` - Tool references
3. Any other files with direct page/viewmodel references

### Low Priority Files
1. Test files (if any)
2. Documentation files

## Risk Assessment

### Low Risk
- Moving files to new directories
- Updating namespaces in moved files
- Creating new directory structure

### Medium Risk
- Updating using statements across multiple files
- Updating NavigationService page instantiation
- Updating MainWindow.xaml.cs type references

### High Risk
- Potential breaking changes if any external references exist
- Build configuration issues if paths are not correctly updated
- Runtime errors if all references are not updated

## Mitigation Strategies

1. **Incremental Approach** - Move one domain at a time and test thoroughly
2. **Comprehensive Search** - Use regex search to find all references before moving files
3. **Build Verification** - Build after each domain move to catch issues early
4. **Backup** - Keep backup of original structure until all changes are verified
5. **Testing** - Comprehensive testing of all navigation paths and page functionality

## Success Criteria

1. All files successfully moved to new directory structure
2. All namespaces updated correctly
3. Project builds without errors
4. All navigation paths work correctly
5. All page functionality preserved
6. No runtime errors or exceptions
7. Code is more organized and maintainable

## Estimated Complexity

- **Number of files to move**: ~30+ XAML/code-behind files
- **Number of ViewModels to move**: ~15+ ViewModel files
- **Number of files requiring updates**: ~10-15 files
- **Total estimated changes**: ~50-60 files

## Notes

- The `ConfigEditor` page is a special case that manages multiple sections (Stacker, LoliCode, CSharpCode). These are all part of the Config domain.
- The `Shared` directory contains the `Debugger` which is used by multiple pages. It should remain in the Shared folder.
- The `Base` directory in ViewModels contains base classes and should remain unchanged.
- Some ViewModels may not exist (e.g., AboutViewModel, MonitorViewModel) - verify before creating new directories.
- The `Tools` folder already exists in ViewModels - verify its contents before reorganizing.
