# Localization Guide

SynapseAdmin.NET supports multiple languages using standard .NET localization (.resx files).

## How to add a new language

To add support for a new language (e.g., French):

1.  **Create a new resource file**:
    *   Navigate to `src/SynapseAdmin/Resources/`.
    *   Copy `SharedResources.resx` and rename it to `SharedResources.fr.resx` (using the [ISO 639-1](https://en.wikipedia.org/wiki/List_of_ISO_639-1_codes) language code).

2.  **Translate the strings**:
    *   Open the new `.resx` file and translate the values in the `<value>` tags for each `<data>` key.
    *   Example:
        ```xml
        <data name="Dashboard" xml:space="preserve">
          <value>Tableau de bord</value>
        </data>
        ```

3.  **Register the new culture**:
    *   Open `src/SynapseAdmin/Program.cs`.
    *   Add the new culture code to the `supportedCultures` array:
        ```csharp
        var supportedCultures = new[] { "en-US", "de-DE", "fr-FR" };
        ```

4.  **Update the Language Selector**:
    *   Open `src/SynapseAdmin/Components/LanguageSelector.razor`.
    *   Add a new menu item for the language:
        ```razor
        <MudMenuItem OnClick="@(() => ChangeLanguage("fr-FR"))">Français</MudMenuItem>
        ```

5.  **Test**:
    *   Build and run the application.
    *   Select the new language from the dropdown to verify the translations.

## Contributing

If you've added a new translation, please submit a Pull Request! We welcome translations for any language.
