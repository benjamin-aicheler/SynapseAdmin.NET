# Theme Guide

SynapseAdmin.NET supports multiple MudBlazor themes, allowing users to customize the visual appearance of the application. The theme system is designed to be easily extensible.

## How the Theme System Works

The application uses a reflection-based discovery system to find available themes:

1.  **Interface**: Themes must implement the `IAppTheme` interface (found in `src/SynapseAdmin/Infrastructure/Themes/`).
2.  **Discovery**: On startup, the `ThemeService` scans the assembly for all non-abstract classes that implement `IAppTheme`.
3.  **Persistence**: The selected theme ID and the "Dark Mode" preference are automatically persisted in the browser's local storage using `ProtectedLocalStorage`.
4.  **Application**: Themes are applied globally via the `MudThemeProvider` in `MainLayout.razor`.

## How to add a new theme

To add a new custom theme (e.g., "Deep Sea"):

1.  **Create a new theme class**:
    *   Navigate to `src/SynapseAdmin/Infrastructure/Themes/`.
    *   Create a new C# file (e.g., `DeepSeaTheme.cs`).
    *   Implement the `IAppTheme` interface.

2.  **Define your palette**:
    *   Customize the `PaletteLight` and `PaletteDark` properties using hex codes or `MudBlazor.Colors`.
    *   Example:
        ```csharp
        using MudBlazor;
        namespace SynapseAdmin.Infrastructure.Themes;

        public class DeepSeaTheme : IAppTheme
        {
            public string Id => "deep-sea";
            public string Name => "Deep Sea";

            public MudTheme Theme => new MudTheme
            {
                PaletteLight = new PaletteLight()
                {
                    Primary = "#006994",
                    AppbarBackground = "#006994",
                },
                PaletteDark = new PaletteDark()
                {
                    Primary = "#00BFFF",
                    Background = "#001B2E",
                    Surface = "#002B44",
                }
            };
        }
        ```

3.  **Build and Run**:
    *   Rebuild the application: `dotnet build`.
    *   Run the application. The new theme will automatically appear in the theme selector (palette icon in the top bar).

## Existing Themes

*   **Indigo (Default)**: The standard professional look.
*   **Matrix (Green)**: A thematic green theme inspired by the Matrix protocol.
*   **Nord (Arctic)**: A clean, muted blue/grey theme for reduced eye strain.
*   **Cyberpunk (Synthwave)**: High-contrast neon colors for a futuristic feel.
*   **Solarized (Professional)**: A precision-balanced palette optimized for readability.

## Contributing

If you've created a cool theme that others might enjoy, please consider submitting a Pull Request!
