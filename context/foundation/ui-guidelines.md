# UI/UX Guidelines for ChoNaBojo App

## 1. Context and Golden Rules for AI Agent
The ChoNaBojo project is a mobile sports matchmaking app (MVP) built exclusively for the Android platform. The visual style is highly functional, sporty, based on clear event lists (Card UI) and a readable map. The app strictly follows Material Design 3 (MD3) principles using the **Uranium UI** framework.

When generating XAML code, **you are strictly forbidden to**:
- Use hardcoded color values (e.g., `Color="#FF0000"`, `TextColor="Black"`).
- Use hardcoded margins, paddings, or spacing values that are not multiples of 8pt (e.g., `Margin="10"`, `Spacing="5"`).
- Manually build custom input fields by wrapping native controls. Always use Uranium UI components.

**Always use ResourceDictionary references: `{StaticResource [ResourceName]}`.**

## 2. Color Palette ("Pitch" Sports Theme)
The interface must be high-contrast and legible outdoors. We use a deep "pitch" green as the primary accent on a clean, light background.
- `PrimaryColor`: `#2E7D32` (Sports green - for main actions like "Join" or "Login" buttons)
- `OnPrimaryColor`: `#FFFFFF` (White text on main primary buttons)
- `BackgroundColor`: `#F5F5F5` (Very light gray - main app background)
- `SurfaceColor`: `#FFFFFF` (White - backgrounds for cards, bottom sheets, and dialogs)
- `OnSurfaceColor`: `#1C1B1F` (Almost black - primary text color)
- `SecondaryTextColor`: `#49454F` (Dark gray - dates, subtitles, icons)
- `ErrorColor`: `#B3261E` (Red - error states, validation messages, destructive actions)
- `DividerColor`: `#CAC4D0` (Subtle dividing lines and input borders)

## 3. Spacing System (8pt Grid)
Every `Margin`, `Padding`, `Spacing`, `HeightRequest`, `WidthRequest`, and `CornerRadius` must strictly be one of the following values:
- `4` (Extra Small - minor visual tweaks, e.g., shadow offset)
- `8` (Small - spacing between closely related elements)
- `16` (Medium - default screen edge margin, inner card padding, standard spacing between groups)
- `24` (Large - spacing between major sections)
- `32` (Extra Large - spacing above sticky primary buttons)
- `48` (Minimum height constraint for all clickable elements to meet MD3 Touch Target guidelines)

## 4. Typography (Clean Sans-Serif)
Typography resources are defined as `Label` styles within the app:
- `HeadlineStyle`: Large screen headers (FontAttributes="Bold", FontSize="24").
- `TitleStyle`: Titles of event cards, sports venue names (FontAttributes="Bold", FontSize="18").
- `BodyStyle`: Main body text, descriptions (FontSize="16").
- `LabelStyle`: Small utility text, dates, times, participant counters e.g., "8/10" (FontSize="14", TextColor="{StaticResource SecondaryTextColor}").

## 5. UI Framework (Uranium UI) & Key Components
This project heavily relies on the **Uranium UI** framework for Material Design. Ensure the XML namespace `xmlns:material="clr-namespace:UraniumUI.Material.Controls;assembly=UraniumUI.Material"` is included in the root of XAML files when required.

### A. Input Fields (Forms)
- **DO NOT** use the native `<Entry>`.
- Always use `<material:TextField>` for text inputs. It natively supports MD3 floating labels, icons, and validation borders.
- Set the `HeightRequest` to at least `48` if not handled natively by the control.

### B. Action Buttons
- Primary Actions (Login, Create, Join): Use `<material:MaterialButton>` with `BackgroundColor="{StaticResource PrimaryColor}"`, `TextColor="{StaticResource OnPrimaryColor}"`, and `CornerRadius="24"` (MD3 pill shape).
- Secondary / Text Button (Cancel, Go Back): Use `<material:MaterialButton>` with `StyleClass="TextButton"`.

### C. Event Cards (Match/Event Card)
The core entity is an event[cite: 1]. Each event is displayed within a Card container.
- MAUI Component: `Border` or Uranium's `StatefulContentView` (if ripple effect is needed on tap).
- Style: Background `{StaticResource SurfaceColor}`, `CornerRadius="16"`, apply a subtle drop shadow (`Shadow`). Inner padding must always be `16`.

---
**Instruction for the AI Agent when implementing new screens (Slices):**
Always start your layout structure with a `VerticalStackLayout` or `Grid`. Apply appropriate default screen edge margins (`Margin="16"`). Build the UI exclusively with Uranium UI Material controls wherever applicable.