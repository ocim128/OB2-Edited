# Optimization Summary

## Completed Optimizations

### 1. Direct LoliCode to C# Transpilation
**Goal**: reduce overhead when converting LoliCode (the text format) to C# (the executable format) by skipping the intermediate "Block Stack" object creation.

**Changes Implemented**:
- **Refactoring**: Extracted statement-level logic from `LoliCodeBlockInstance` into a reusable static class `LoliCodeStatementTranspiler`.
- **New Transpiler**: Created `FastLoli2CSharpTranspiler` which reads LoliCode text and outputs C# text directly.
  - Efficiently handles variables and label detection in a first pass.
  - significantly reduces memory allocations (no `BlockInstance` objects created for every line).
- **Integration**: Updated `Loli2CSharpTranspiler` to use the `FastLoli2CSharpTranspiler` by default.
  - Retained the old "Stack" based transpilation for "Step-by-Step" mode (Debugger), ensuring the Block UI highlighting still works when needed.

**Expected Impact**:
- Faster switching between LoliCode and C# modes.
- Reduced memory pressure when running Bots that compile configs frequently.
- Smoother experience for users with very large configs.

## Pending/Potential Optimizations
- **Simpler Block Optimization**: Detecting "Simple" blocks in the Stack-to-C# path and skipping Roslyn normalization for them (discussed but not fully implemented).
- **VariableDetector Optimization**: Optimizing the Regex usage or using `Span<T>` in `VariableDetector` to reduce allocation during variable scanning.
