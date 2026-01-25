# MainWindow Refactoring Design Document

## 1. Overview
This document outlines the design for refactoring `MainWindow.xaml.cs` (799 lines) into smaller, more focused services. The goal is to reduce cognitive load, improve testability, and enhance maintainability.

## 2. Component Identification

### 2.1 Navigation & Page Management
**Current Implementation:** `NavigateTo`, `OnNavigated`, `DisplayJob`, `EditJob`, `HandleNavigationClick`, `ChangePage`, `InitializePageButtonMap`, `MapButton`, `GetButtonForPage`.
**New Component:** `INavigationHandler` / `NavigationHandler`
**Responsibilities:**
- Manage page transitions.
- Handle job display logic.
- Maintain the mapping between `MainWindowPage` enums and UI `Button` elements.
- Update menu highlight when navigation occurs.

### 2.2 Menu & Submenu Logic
**Current Implementation:** `UpdateMenuHighlight`, `ConfigSubmenuMouseEnter`, `ConfigSubmenuMouseLeave`, `ConfigsMenuOptionMouseEnter`, `ConfigsMenuOptionMouseLeave`, `CheckCloseSubmenuAsync`, `CloseSubmenu`.
**New Component:** `IMenuHandler` / `MenuHandler`
**Responsibilities:**
- Handle sidebar menu button styling (active/inactive).
- Manage the Config submenu visibility and hover logic.

### 2.3 Sidebar Management
**Current Implementation:** `ToggleSidebar`, `AnimateSidebarWidth`, `SetSidebarTextVisibility`, `InitializeSidebarState`.
**New Component:** `ISidebarHandler` / `SidebarHandler`
**Responsibilities:**
- Manage sidebar expansion/collapse state.
- Handle sidebar width animations.
- Control visibility of sidebar text elements and headers.

### 2.4 Command Handlers
**Current Implementation:** `OnCanExecuteConfigCommand`, `OnNewConfigExecuted`, `OnOpenConfigExecuted`, `OnSaveConfigExecuted`, `OnRefreshExecuted`, `OnCanExecuteRefreshCommand`, `OnQuitExecuted`, `BindNavigationCommand`.
**New Component:** `ICommandHandler` / `CommandHandler`
**Responsibilities:**
- Centralize logic for application commands (New Config, Save, Refresh, etc.).
- Handle command execution and "can execute" checks.

### 2.5 Window Control & Lifecycle
**Current Implementation:** `OnWindowLoaded`, `OnWindowStateChanged`, `NotifyDebuggerWindowStateChanged`, `MinimizeWindow`, `MaximizeRestoreWindow`, `CloseWindow`.
**New Component:** `IWindowControlHandler` / `WindowControlHandler`
**Responsibilities:**
- Handle window state changes (Minimized/Maximized).
- Communicate window state to child components (e.g., Debugger).
- Manage window layout restoration.

### 2.6 Accessibility
**Current Implementation:** `ApplyAccessibilitySettings`, `ApplyButtonSpacing`, `ConfigureTooltips`.
**New Component:** `AccessibilityHandler`
**Responsibilities:**
- Apply accessibility-related styles and settings (focus visuals, spacing, tooltips).

## 3. Dependency Injection Strategy

We will register all new handlers as Singletons (or Scoped if appropriate for the DI container being used, though `MainWindow` is a singleton in this context) in `OpenBullet2.Native/Services/ServiceCollectionExtensions.cs`.

### New Services to Register:
- `INavigationHandler` -> `NavigationHandler`
- `IMenuHandler` -> `MenuHandler`
- `ISidebarHandler` -> `SidebarHandler`
- `ICommandHandler` -> `CommandHandler`
- `IWindowControlHandler` -> `WindowControlHandler`
- `PageButtonMapper` (Helper)
- `SidebarAnimator` (Helper)
- `SubmenuController` (Helper)
- `AccessibilityHandler` (Logic class)

## 4. File Structure

```
OpenBullet2.Native/
├── Services/
│   ├── Navigation/
│   │   ├── INavigationHandler.cs
│   │   ├── NavigationHandler.cs
│   │   └── PageButtonMapper.cs
│   ├── Menu/
│   │   ├── IMenuHandler.cs
│   │   ├── MenuHandler.cs
│   │   └── SubmenuController.cs
│   ├── Sidebar/
│   │   ├── ISidebarHandler.cs
│   │   ├── SidebarHandler.cs
│   │   └── SidebarAnimator.cs
│   ├── Commands/
│   │   ├── ICommandHandler.cs
│   │   └── CommandHandler.cs
│   └── Window/
│       ├── IWindowControlHandler.cs
│       ├── WindowControlHandler.cs
│       └── AccessibilityHandler.cs
└── MainWindow.xaml.cs (Refactored)
```

## 5. Implementation Steps (Phase 1 Deliverables)
1. Define all interfaces (`INavigationHandler`, `IMenuHandler`, etc.).
2. Update `MainWindow.xaml.cs` to accept these interfaces in its constructor. (Wait for Phase 2+ for actual extraction).
3. Ensure no circular dependencies are created.
