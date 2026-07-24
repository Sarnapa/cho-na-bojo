# UI/UX Guidelines for ChoNaBojo App

## 1. Context and Golden Rules for AI Agent
The ChoNaBojo project is a mobile sports matchmaking app (MVP) built exclusively for the Android platform. The visual style is highly functional, sporty, based on clear event lists (Card UI) and a readable map. The app strictly follows Material Design 3 (MD3) principles using the **Uranium UI** framework.

The app is **light-theme only for the MVP**. Do not author `Dark` theme values or `AppThemeBinding` Dark branches; every surface is light. Dark theme is explicitly out of scope.

When generating XAML code, **you are strictly forbidden to**:
- Use hardcoded color values (e.g., `Color="#FF0000"`, `TextColor="Black"`).
- Use hardcoded margins, paddings, or spacing values that are not multiples of 8pt (e.g., `Margin="10"`, `Spacing="5"`).
- Manually build custom input fields by wrapping native controls. Always use Uranium UI components.
- Bind a user's raw contact fields (phone, email, messenger) directly in XAML. Contact values must bind through a view-model flag that is **false unless the participant status is `Accepted`** (see Section 8).

**Always use ResourceDictionary references: `{StaticResource [ResourceName]}`.**

## 2. Color Palette ("Pitch" Sports Theme)
The interface must be high-contrast and legible outdoors. We use a deep "pitch" green as the primary accent on a clean, light background. Every color is exposed both as a `Color` (key `[Name]`) and as a matching `SolidColorBrush` (key `[Name]Brush`) for controls that require a brush.

- `PrimaryColor`: `#2E7D32` (Sports green - for main actions like "Join" or "Login" buttons)
- `OnPrimaryColor`: `#FFFFFF` (White text on main primary buttons)
- `BackgroundColor`: `#F5F5F5` (Very light gray - main app background)
- `SurfaceColor`: `#FFFFFF` (White - backgrounds for cards, bottom sheets, and dialogs)
- `OnSurfaceColor`: `#1C1B1F` (Almost black - primary text color)
- `SecondaryTextColor`: `#49454F` (Dark gray - dates, subtitles, icons)
- `ErrorColor`: `#B3261E` (Red - error states, validation messages, destructive actions)
- `DividerColor`: `#CAC4D0` (Subtle dividing lines and input borders)

A neutral `Gray100`–`Gray950` ramp is retained for utility use (disabled states, placeholders). Do **not** use the legacy template accents (`Tertiary`, `Magenta`, `MidnightBlue`) — they have been removed.

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

## 5. Elevation & Shadow
Cards, bottom sheets, and the in-app notification banner use a single shared shadow token — never hardcode `Shadow` values.
- `CardShadow`: `Shadow` with `Brush="{StaticResource OnSurfaceColor}"`, `Offset="0,4"`, `Radius="8"`, `Opacity="0.15"`.

## 6. UI Framework (Uranium UI) & Key Components
This project heavily relies on the **Uranium UI** framework for Material Design. Ensure the XML namespace `xmlns:material="clr-namespace:UraniumUI.Material.Controls;assembly=UraniumUI.Material"` is included in the root of XAML files when required.

### A. Input Fields (Forms)
- **DO NOT** use the native `<Entry>`.
- Always use `<material:TextField>` for text inputs. It natively supports MD3 floating labels, icons, and validation borders.
- Set the `HeightRequest` to at least `48` if not handled natively by the control.

### B. Action Buttons
- Primary Actions (Login, Create, Join): Use `<material:MaterialButton>` with `BackgroundColor="{StaticResource PrimaryColor}"`, `TextColor="{StaticResource OnPrimaryColor}"`, and `CornerRadius="24"` (MD3 pill shape).
- Secondary / Text Button (Cancel, Go Back): Use `<material:MaterialButton>` with `StyleClass="TextButton"`.

### C. Event Cards (Match/Event Card)
The core entity is an event. Each event is displayed within a Card container.
- MAUI Component: `Border` or Uranium's `StatefulContentView` (if ripple effect is needed on tap).
- Style: Background `{StaticResource SurfaceColor}`, `CornerRadius="16"`, `Shadow="{StaticResource CardShadow}"`. Inner padding must always be `16`.
- **States** (a card must communicate joinability at a glance — see PRD FR-006/FR-007):
  - **Joinable:** full color, `CardShadow`, participant counter in `SecondaryTextColor`, enabled "Join" `MaterialButton`.
  - **Full** (participants = limit): the participant counter turns `ErrorColor`, and the "Join" button is replaced by a disabled "Full" label. The card is otherwise normal.
  - **Past** (current time is after the event's estimated end time): card is dimmed (`Opacity="0.6"`), has no Join action, and shows a small "Ended" chip. Past events are excluded from joinable lists but may appear in the user's own-events list.

### D. Iconography
All iconography uses the **Uranium UI MaterialSymbols** icon pack (`UraniumUI.Icons.MaterialSymbols`), consumed via `FontImageSource`. Icons that carry meaning must tint via `TextColor`/`Color` tokens (`OnSurfaceColor`, `SecondaryTextColor`, or `PrimaryColor`) — never a hardcoded color. Brand logos (messenger apps) are **not** part of Material Symbols; see Section 8 for the brand-icon asset set.

### E. Sport Filter Chips
Horizontal, scrollable row of chips used to filter venues/events by sport (PRD FR-003).
- Each chip renders the **sport's Material Symbol icon on the left and the discipline name on the right**.
- Height `48` (touch target), `CornerRadius="24"`, `8` spacing between chips.
- **Unselected:** `SurfaceColor` background, `DividerColor` border, `OnSurfaceColor` text/icon.
- **Selected:** `PrimaryColor` background, `OnPrimaryColor` text/icon.
- An **"All" chip is selected by default** (matches FR-003's "all events shown by default").

### F. Participant Counter
A defined inline component for the "8/10" indicator.
- A people Material Symbol icon followed by `LabelStyle` text (`filled/limit`).
- Color is `SecondaryTextColor` normally and flips to `ErrorColor` when the event is full (ties to the card **Full** state).

## 7. Map & Venue Discovery
The map is the app's core discovery surface (PRD FR-003/FR-004).
- **Map:** `Microsoft.Maui.Controls.Maps.Map`, centered on the user's current location. Venue markers use the **sport icon on a `PrimaryColor` pin**. Tapping a marker opens the venue bottom sheet.
- **Venue bottom sheet:** `SurfaceColor` background, top `CornerRadius="16"`, `Shadow="{StaticResource CardShadow}"`, `16` padding. Lists joinable event cards followed by a sticky "Create event" `MaterialButton` with `32` top spacing.
- **Location-denied fallback (FR-004):** when location permission is denied, show a `material:TextField` address input at the top of the screen; on submit, recenter the map to that address. Display a non-blocking info banner (Section 9) explaining why the map isn't centered on the user.
- **Sport filter:** the horizontal chip row (Section 6E) is pinned above the map.

## 8. Contact Reveal
Revealing contact info is the product's top privacy guardrail (PRD FR-011). Contact info is rendered through a dedicated component with two states:
- **Locked (default):** never render the raw value. Show a disabled row with a lock Material Symbol and the text "Contact shared after acceptance" in `SecondaryTextColor`.
- **Revealed (only after the participant status is `Accepted`):** show each provided method — phone, email, messenger — as a row on a `SurfaceColor` card in `BodyStyle`, with a tap action (`tel:` / `mailto:` / open handle, plus tap-to-copy).
  - **Messenger rows show the communicator app's brand icon** (e.g., WhatsApp, Messenger, Instagram) to the left of the handle. Brand logos live in `Resources/Images` as monochrome SVGs (`msg_whatsapp`, `msg_messenger`, `msg_instagram`) tinted to `SecondaryTextColor`. Unknown apps fall back to the generic chat icon (`msg_generic`).

**Hard rule (also in Section 1):** contact values must bind through a view-model flag that is false unless the participant status is `Accepted`. Never bind raw contact fields directly in XAML.

## 9. Feedback & Notifications
Push notifications are core to the workflow (PRD FR-008/010/012/013/014); the UI must also confirm actions in-app.
- **Transient success/info:** a Snackbar (Uranium UI / `CommunityToolkit.Maui`) on `OnSurfaceColor` background, `16` inset, auto-dismiss ~3s. Used for "Join request sent", "You left the event", etc.
- **Destructive confirmations** (cancel event, remove participant, leave event): an MD3 dialog on `SurfaceColor` with an `ErrorColor` confirm button.
- **In-app foreground push banner:** a top banner on `SurfaceColor` with `Shadow="{StaticResource CardShadow}"`, showing the sport icon and message, tappable to open the related event.

## 10. Screen States
Every data-backed screen handles three recurring states.
- **Loading:** a centered `ActivityIndicator` tinted `PrimaryColor`. For lists, a lightweight placeholder is acceptable — no heavy skeletons for the MVP.
- **Empty:** a centered icon/illustration + a `TitleStyle` message + an optional primary action. Example: a venue with no events shows "No events here yet" + a "Create event" button.
- **Error / offline:** an `ErrorColor` icon + a `BodyStyle` message + a "Retry" `MaterialButton`. Used for map-load failure (see the 2-second map NFR) and network errors.

## 11. Accessibility
- **Contrast:** all text meets WCAG AA (4.5:1 for body, 3:1 for large text). White on `PrimaryColor #2E7D32` measures ~5.1:1 and therefore passes AA at all text sizes — white-on-green button labels are compliant.
- **Semantics:** every interactive control sets `SemanticProperties.Description`. Icons that convey meaning get a description; purely decorative icons are hidden from the accessibility tree.
- **Touch targets:** all clickable elements are at least `48` in height/width (reaffirms Section 3).
- **Dynamic type:** do not cap font scaling. Layouts must tolerate larger fonts — avoid fixed heights on text containers.

---
**Instruction for the AI Agent when implementing new screens (Slices):**
Always start your layout structure with a `VerticalStackLayout` or `Grid`. Apply appropriate default screen edge margins (`Margin="16"`). Build the UI exclusively with Uranium UI Material controls wherever applicable.
