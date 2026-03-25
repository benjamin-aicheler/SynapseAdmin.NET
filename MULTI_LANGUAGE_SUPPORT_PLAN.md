# Multi-Language Support Plan (SynapseAdmin.NET)

This document outlines the strategy for implementing multi-language support in SynapseAdmin.NET, as requested in Issue #18.

## Phase 1: Preparation (Infrastructure)

The goal is to make the application "translation-ready" without necessarily translating everything at once.

1.  **Project Dependencies:**
    *   Install the `MudBlazor.Translations` NuGet package (Version 3.1.0 for MudBlazor 9).
    *   Ensure `Microsoft.Extensions.Localization` is available (standard in .NET SDK).
2.  **Resource Management Setup:**
    *   Create a `src/SynapseAdmin/Resources` directory.
    *   Create a marker class `SharedResources.cs` in that directory.
    *   Create `SharedResources.resx` for default English strings.
    *   Create `SharedResources.de.resx` for German (as a first non-English example).
3.  **Program.cs Configuration:**
    *   Register localization services: `builder.Services.AddLocalization()`.
    *   Register MudBlazor translations: `builder.Services.AddMudTranslations()`.
    *   Configure `RequestLocalizationOptions` with supported cultures (e.g., `en-US`, `de-DE`).
    *   Add `app.UseRequestLocalization()` middleware.
4.  **Culture Preference Persistence:**
    *   Implement a mechanism to store and retrieve the user's culture preference using a cookie (`.AspNetCore.Culture`). This is the most reliable method for Blazor Server to ensure correct pre-rendering.
    *   Create a small endpoint or controller to handle culture changes and redirect back to the previous page.

## Phase 2: UI Foundation

1.  **Global Language Selector:**
    *   Create a `LanguageSelector.razor` component.
    *   This component will display available languages (e.g., in a `MudMenu` or `MudSelect`).
    *   When a language is selected, it will call the culture change endpoint.
2.  **Layout Integration:**
    *   Embed the `LanguageSelector` into the `MudAppBar` in `MainLayout.razor`.
    *   Wrap the content in `MudRTLProvider` (integrated into `MainLayout`) to support future RTL languages.
3.  **Imports Update:**
    *   Update `src/SynapseAdmin/Components/_Imports.razor` to include:
        ```razor
        @using Microsoft.Extensions.Localization
        @using SynapseAdmin.Resources
        @inject IStringLocalizer<SharedResources> L
        ```
    *   This makes the `L` localizer available in all components.

## Phase 3: Incremental Page Translation

We will migrate pages one by one to use the localizer.

1.  **Common Layout Elements:**
    *   Translate `NavMenu.razor` (Dashboard, User Management, Room Management, etc.).
    *   Translate `MainLayout.razor` (Logout, Theme toggle, etc.).
2.  **Initial Pages:**
    *   `Login.razor`: Ensure the login experience is localized.
    *   `Home.razor` (Dashboard): Localize status messages and headers.
3.  **Core Admin Pages:**
    *   `Users.razor` and `UserDetails.razor`.
    *   `Rooms.razor` and `RoomDetails.razor`.
    *   `RegistrationTokens.razor`.
    *   `FederationDestinations.razor`.
    *   `EventReports.razor`.
    *   `ServerNotices.razor`.
4.  **About Page:**
    *   `About.razor`: Localize project information and legal text.

## Phase 4: Validation & Refinement

1.  **Automated/Manual Testing:**
    *   Verify browser language detection works.
    *   Verify manual language switching persists across sessions (via cookie).
    *   Check for any hardcoded strings that were missed (using grep/automated tools).
2.  **Community Contributions:**
    *   Document how new languages can be added by creating new `.resx` files.

---
**Status:** ✅ Phase 1-4 Complete. (2026-03-25)
