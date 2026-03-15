# Profile UX Redesign

**Date:** 2026-03-15
**Status:** Approved for implementation

## Problem

The current Profile page is a flat wall of textboxes on a pure-black background. It:
- Uses comma-separated strings for list data (losing SkillPreference.Reason)
- Hides most of WorkPreferences (no salary, target roles, industries, hybrid/onsite toggles)
- Missing identity fields: CurrentTitle, YearsOfExperience
- Missing AdditionalContext field
- No visual hierarchy, empty bottom half

## Design

### 1. Dynamic Schema Model (lucidRESUME.Core)

Mirror DocExtractor's FormDefinition pattern:

```
ProfileFormDefinition
  └── ProfileFieldGroup[]
        └── ProfileField[]
              ├── FieldId, DisplayName, Description
              ├── DataType: Text | LongText | Number | Currency | Boolean |
              │            TagList | TagListWithReason | WorkStyleToggles
              ├── Required, AllowedValues, DefaultValue
              └── CvMappingHint  ← extraction pipeline uses this to auto-populate
```

**Built-in groups (seeded by ProfileSchemaDefaults):**
- `identity` — Display Name, Current Title, Years Experience
- `work-prefs` — Work Style, Preferred Locations, Target Roles, Min Salary, Max Commute, Target Industries, Blocked Industries
- `skills` — Skills to Emphasise (TagListWithReason), Skills to Avoid (TagListWithReason)
- `ai-context` — Career Goals (LongText), Additional Context (LongText), Blocked Companies (TagList)

Fields can be added/removed at runtime. User-added fields are persisted in AppState.

### 2. Work Style

**Not** a single "open to remote" checkbox. Three independent checkboxes:

```
Work Style
[ ] Remote   [ ] Hybrid   [ ] Onsite
                         [Remote Only ↩]  ← quick-set: ticks Remote, unticks others
```

Maps to `WorkPreferences.OpenToRemote / OpenToHybrid / OpenToOnsite`.

### 3. Visual Design (Catppuccin Mocha)

**Page background:** `#1E1E2E` (not pure black)

**Cards:**
- Background: `#313244`
- Border: `#45475A` (1px)
- Corner radius: 8px
- Left accent bar: 3px colored strip per section
- Padding: 20px

**Accent colours per section:**
- Identity: `#89B4FA` (blue)
- Work Preferences: `#CBA6F7` (purple)
- Skills: `#A6E3A1` (green) / `#F38BA8` (red)
- AI Context: `#F9E2AF` (yellow)

**Tag chips:**
- Background: section accent at 20% opacity
- Border: section accent
- Text: `#CDD6F4`
- Delete [×]: `#6C7086`, hover `#F38BA8`
- Add chip: dashed border, `#6C7086` text → `#CDD6F4` on hover

**Input fields:**
- Background: `#1E1E2E`
- Border: `#45475A`, focus `#89B4FA`
- Radius: 6px

**Section headers:**
- Icon + label, `#CDD6F4`, FontSize 14, FontWeight SemiBold
- Small muted description line below

### 4. Tag Chip Control (reusable)

`TagChipEditor` UserControl accepting:
- `Items: ObservableCollection<TagItem>` where `TagItem { Value, Reason? }`
- `Placeholder` string
- `AccentColor` brush
- `AllowReasons` bool — when true, clicking a chip expands an inline reason TextBox

### 5. ViewModel

`ProfilePageViewModel` refactored to:
- Load `ProfileFormDefinition` from schema defaults + stored overrides
- Expose strongly-typed properties for built-in fields (keeps binding simple)
- Expose `ObservableCollection<TagItem>` for each tag-list field
- Autosave on any property change (debounced 800ms), replaces manual Save button
- Show floating toast "Saved ✓" on autosave

### 6. Files Changed

| File | Change |
|------|--------|
| `Core/Models/Profile/ProfileFormDefinition.cs` | New — schema model |
| `Core/Models/Profile/ProfileField.cs` | New — field + FieldDataType |
| `Core/Models/Profile/ProfileSchemaDefaults.cs` | New — built-in field seeder |
| `Core/Models/Profile/TagItem.cs` | New — tag value + optional reason |
| `Views/Controls/TagChipEditor.axaml` | New — reusable tag chip control |
| `Views/Controls/WorkStyleToggleGroup.axaml` | New — remote/hybrid/onsite checkboxes |
| `Views/Pages/ProfilePage.axaml` | Redesigned — card layout |
| `ViewModels/Pages/ProfilePageViewModel.cs` | Refactored — autosave, tag collections |
