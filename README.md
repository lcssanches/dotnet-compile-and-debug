# Dynamic Compile and Debug

A .NET 8 demo that compiles and executes C# code at runtime with full debugging support.

## Features

- **Runtime Compilation**: Compiles C# functions from JSON definitions using Roslyn
- **Debug Support**: Enables breakpoints in dynamically compiled code via `Debugger.Break()`
- **Version Management**: Automatically recompiles when function versions change
- **Hot Reload**: Reload function definitions without restarting the app

## How It Works

1. Functions are defined in `functions.json` with name, version, and source code
2. On execution, the app compiles the source to a DLL in the `generated/` folder
3. The assembly is loaded and the `Run` method is invoked
4. If the version changes, the function is recompiled automatically

## Usage

Start the debugger in VSCode

Or run directly, but without the debugger attached.

```bash
dotnet build
dotnet run
```

Select a function from the list to execute it.