# Config Menu

This document describes the main config-related pages in the desktop app and how they connect to each other.

## Overview

The Config menu is split into two main stages:

1. `Configs`
   This is the config list page where users search, create, select, save, delete, export, and rescan configs.
2. `ConfigEditor`
   This is the editor shell for the currently selected config. It hosts the active editor page on the left and the debugger on the right.

The first page opened after selecting a config depends on:

- `GeneralSettings.ConfigSectionOnLoad`
- the selected config mode (`Stack`, `LoliCode`, or `CSharp`)

Mode-aware routing:

- `Stacker` opens the visual stacker for `Stack` and `LoliCode` configs
- `LoliCode` opens the text editor for `Stack` and `LoliCode` configs
- `C#` opens the generated/read-only C# page for every mode
- if the config is already `CSharp`, both stacker- and LoliCode-related navigation fall back to the C# page

## Config List

The config list page is the entry point for selecting and managing configs.

Main behaviors:

- shows the current selected config and total config count
- supports search/filtering
- supports sorting from the list headers
- supports double-click to open a config
- blocks editing for remote configs
- warns before leaving another config with unsaved changes

Main actions:

- `New`
- `Edit`
- `Save`
- `Delete`
- `Export Selected`
- `Open Folder`
- `Rescan`

List source and navigation are handled from:

- `Flux.Native/Views/Pages/ConfigPages/Configs.xaml`
- `Flux.Native/Views/Pages/ConfigPages/Configs.xaml.cs`

## Editor Shell

`ConfigEditor` is the shared shell after a config is opened.

Layout:

- left side: active editor page
- right side: debugger
- middle: resizable splitter

Behavior:

- lazy-loads `ConfigStacker`, `ConfigLoliCode`, and `ConfigCSharpCode`
- keeps a persistent splitter ratio in settings
- auto-saves on a timer when the selected config has unsaved changes
- shows the bottom editor buttons only for `Stack` and `LoliCode` configs

Bottom editor buttons:

- `Stacker`
- `LoliCode`
- `C#`
- `Save`

Shell implementation:

- `Flux.Native/Views/Pages/ConfigPages/ConfigEditor.xaml`
- `Flux.Native/Views/Pages/ConfigPages/ConfigEditor.xaml.cs`

## Stacker

The stacker is the visual block editor.

Layout:

- left: block list and stacker control center
- right: selected block inspector

Block list / control center features:

- add block
- remove selected block(s)
- clone selected block(s)
- move selected block(s) up/down
- enable or disable selected block(s)
- undo
- search blocks

Inspector behavior:

- shows a placeholder until a block is selected
- loads the block-specific settings editor for the selected block

Key files:

- `Flux.Native/Views/Pages/ConfigPages/ConfigStacker.xaml`
- `Flux.Native/Views/Controls/Config/ConfigStackerBlockListControl.xaml`
- `Flux.Native/Views/Controls/Config/ConfigStackerInspectorControl.xaml`
- `Flux.Native/ViewModels/Config/ConfigStackerViewModel*.cs`

## LoliCode

The LoliCode page is the editable text-based script view.

Features:

- editable main LoliCode editor
- optional startup LoliCode editor
- optional custom `using` statements section
- syntax highlighting
- search panel support
- autocomplete/snippet completion
- line numbers
- `Ctrl+S` save

Important behavior:

- opening this page forces the selected config into `ConfigMode.LoliCode`
- editor content is pushed back into the selected config on lost focus and on page change

Key files:

- `Flux.Native/Views/Pages/ConfigPages/ConfigLoliCode.xaml`
- `Flux.Native/Views/Pages/ConfigPages/ConfigLoliCode.xaml.cs`

## C#

The C# page is the generated code view.

Features:

- read-only generated C# editor
- optional read-only startup C# editor
- optional `using` statements section
- syntax highlighting
- search panel support
- line numbers

Important behavior:

- if the config is not already `CSharp`, the page transpiles:
  - `Stack` to C# with `Stack2CSharpTranspiler`
  - `LoliCode` to C# with `Loli2CSharpTranspiler`
- startup LoliCode is also transpiled to startup C#

This page is the fallback view for C# configs when stacker/LoliCode pages are not applicable.

Key files:

- `Flux.Native/Views/Pages/ConfigPages/ConfigCSharpCode.xaml`
- `Flux.Native/Views/Pages/ConfigPages/ConfigCSharpCode.xaml.cs`

## Debugger

The debugger lives on the right side of `ConfigEditor`.

Primary controls:

- input data field
- `Start`
- `Stop`
- `Step`

Layout controls:

- show/hide main UI
- show/hide debugger options
- show/hide stacker
- focus mode

Runtime options:

- `Step-by-Step`
- wordlist type selector
- `Persist Log`
- `Use Proxy`
- proxy value
- proxy type

Tabs:

- `Log`
- `Variables`
- `HTML`

Search/navigation tools:

- search within log output
- previous/next match
- previous/next block
- clear search
- clear log
- auto-scroll toggle

Keyboard shortcuts implemented in code include:

- `Alt+A` focus input data
- `Alt+S` start
- `Ctrl+F` focus search
- `F3` / `Shift+F3` next/previous match
- `Ctrl+Up` / `Ctrl+Down` previous/next block
- `Ctrl+L` clear log

Key files:

- `Flux.Native/Views/Pages/Shared/Debugger.xaml`
- `Flux.Native/Views/Pages/Shared/Debugger.xaml.cs`
- `Flux.Native/Views/Pages/Shared/DebuggerUIManager.cs`

## Related Pages

The Config menu also routes into adjacent config pages that are not part of the left editor/debugger split described above:

- `ConfigMetadata`
- `ConfigReadme`
- `ConfigSettings`

These are chosen by `ConfigSectionOnLoad` in the same routing path used by the config list page.
