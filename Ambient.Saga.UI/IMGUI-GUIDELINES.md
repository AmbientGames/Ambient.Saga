# ImGui Sizing & Pixel-Perfect Layout Guidelines

This document provides guidelines for creating consistent, pixel-perfect ImGui layouts in the Ambient.Saga.UI project.

## Core Truth

ImGui is immediate-mode and layout is driven by style metrics, current cursor, and rounding. Most "one pixel bigger" problems are caused by:

- Hidden padding/spacing (`FramePadding`, `ItemSpacing`, `WindowPadding`)
- Scrollbars appearing/disappearing
- DPI scaling + rounding differences between logical units and framebuffer pixels
- Mixing baseline text with framed widgets
- Using "outer" sizes when you meant "content" sizes
- Table/column sizing rules overriding you

**If you stop hand-computing sizes and instead use `GetContentRegionAvail()` / `SetNextItemWidth(-FLT_MIN)` everywhere, 80% of pain disappears.**

---

## 1. Canonical Measurements (Use These, Stop Guessing)

### Heights

| Method | Purpose |
|--------|---------|
| `ImGui.GetFontSize()` | Raw font height |
| `ImGui.GetTextLineHeight()` | Font line height (no spacing) |
| `ImGui.GetTextLineHeightWithSpacing()` | Line height + `ItemSpacing.Y` |
| `ImGui.GetFrameHeight()` | Standard framed widget height: `FontSize + 2*FramePadding.Y` |
| `ImGui.GetFrameHeightWithSpacing()` | Frame height + `ItemSpacing.Y` |

**Rule:** If you want rows that match buttons/inputs, base everything on `GetFrameHeight()`.

### Widths / Remaining Space

| Method | Purpose |
|--------|---------|
| `ImGui.GetContentRegionAvail()` | Remaining usable content area from current cursor. **Most reliable "how much space do I have"** |
| `ImGui.SetNextItemWidth(ImGuiSizes.Fill)` | Make next item fill remaining width (idiomatic "stretch") |
| `ImGui.CalcTextSize("...")` | For exact label widths (widgets add padding on top) |

### Window/Client Areas

- `ImGui.GetWindowSize()` - outer window (includes padding and scrollbars)
- `ImGui.GetWindowPos()` - top-left outer position
- `ImGui.GetCursorPos()` - current cursor in window-local coordinates
- `ImGui.GetCursorScreenPos()` - cursor in screen coordinates
- Prefer `GetContentRegionAvail()` over `GetWindowContentRegionMin/Max()` for layout

### Style Metrics That Secretly Change Everything

```csharp
var style = ImGui.GetStyle();
// These affect layout:
style.WindowPadding      // Content inset
style.FramePadding       // Padding inside framed widgets
style.ItemSpacing        // Space between items
style.ItemInnerSpacing   // Space between label/value
style.ScrollbarSize      // Space stolen by scrollbars
style.CellPadding        // Tables
style.IndentSpacing      // Indentation
```

**Rule:** When something is off by a few pixels, inspect the relevant style fields.

---

## 2. "Off by One Pixel" Root Causes and Fixes

### A) DPI Scaling & Rounding
On HiDPI, fractional coordinates round differently at different stages.

**Fixes:**
- Prefer "let ImGui decide" widths/heights
- Use `ImGuiHelpers.FullWidth()` and `ImGuiHelpers.RemainingHeight()`
- Don't mix `GetCursorPos()` and `GetCursorScreenPos()` without converting

### B) Hidden Padding/Spacing in Your Math
You compute a "row height" but forget `FramePadding.Y` or `ItemSpacing.Y`.

**Fix:** Use `GetFrameHeight()` / `GetFrameHeightWithSpacing()` instead of custom formulas.

### C) Scrollbars Appear/Disappear
A child/window gets a scrollbar, you lose `ScrollbarSize` width.

**Fixes:**
- Force scrollbar: `ImGuiWindowFlags.AlwaysVerticalScrollbar`
- Use `GetContentRegionAvail().X` at point of placement (not earlier)
- Use `ImGuiHelpers.FullWidth()` which handles this

### D) Text Baseline vs Framed Widgets
`Text()` aligns to baseline; `Button()`/`InputText()` align to frame height.

**Fix:** Call `ImGui.AlignTextToFramePadding()` before `Text()` when on same line as framed widgets.

### E) Tables/Columns Override Your Widths
Table column sizing policy may clamp or distribute width.

**Fixes:**
- Use `ImGuiTableColumnFlags.WidthStretch` for fill columns
- Use `ImGuiHelpers.FullWidth()` inside table cells

---

## 3. Recommended Patterns

### Pattern 1: Full-Width Widgets (Most Common)
```csharp
ImGuiHelpers.FullWidth();
ImGui.InputText("##path", ref path, 256);
```

### Pattern 2: Two-Column Form (Label + Field)
```csharp
if (ImGui.BeginTable("form", 2))
{
    ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed);
    ImGui.TableSetupColumn("field", ImGuiTableColumnFlags.WidthStretch);

    ImGui.TableNextRow();
    ImGui.TableSetColumnIndex(0);
    ImGui.AlignTextToFramePadding();
    ImGui.Text("Name");
    ImGui.TableSetColumnIndex(1);
    ImGuiHelpers.FullWidth();
    ImGui.InputText("##name", ref name, 256);

    ImGui.EndTable();
}
```

### Pattern 3: Child Fills Rest of Window
```csharp
var avail = ImGui.GetContentRegionAvail();
ImGui.BeginChild("content", avail, ImGuiChildFlags.Border);
// ...
ImGui.EndChild();
```

### Pattern 4: Fixed-Height Scrolling List
```csharp
float height = ImGui.GetFrameHeightWithSpacing() * 10; // 10 rows
ImGui.BeginChild("list", new Vector2(0, height), ImGuiChildFlags.Border,
    ImGuiWindowFlags.AlwaysVerticalScrollbar);
foreach (var item in items)
{
    ImGuiHelpers.FullWidth();
    ImGui.Selectable(item.Label, item.Selected);
}
ImGui.EndChild();
```

### Pattern 5: Bottom-Aligned Button Row
```csharp
float footerHeight = ImGui.GetFrameHeightWithSpacing();
var avail = ImGui.GetContentRegionAvail();

ImGui.BeginChild("main", new Vector2(0, avail.Y - footerHeight));
// ... main content ...
ImGui.EndChild();

ImGui.Separator();
ImGuiHelpers.FullWidth();
if (ImGui.Button("OK")) { /* ... */ }
```

---

## 4. Helper Methods (ImGuiHelpers.cs)

Use these helpers instead of manual calculations:

```csharp
// Make next widget fill remaining width
ImGuiHelpers.FullWidth();

// Get remaining height accounting for footer
float mainHeight = ImGuiHelpers.RemainingHeight(footerRows: 1);

// Render label + value in aligned columns
ImGuiHelpers.LabeledValue("Health:", "100/100");

// Render section with proper frame-aligned text
ImGuiHelpers.SectionHeader("Equipment");
```

---

## 5. Golden Rules

1. **Default to stretching:** `ImGuiHelpers.FullWidth()` or `SetNextItemWidth(ImGuiSizes.Fill)`
2. **Default to remaining space:** `GetContentRegionAvail()`
3. **Default to frame metrics:** `GetFrameHeight()` / `GetFrameHeightWithSpacing()`
4. **Use tables for forms:** Avoid manual label-width math
5. **Assume scrollbars will happen:** Either force them or design around them
6. **Use `AlignTextToFramePadding()`** when mixing Text with framed widgets
7. **Don't mix coordinate spaces** (window-local vs screen)

---

## 6. Debugging Tips

When something is off by pixels:

1. Check `ImGui.GetContentRegionAvail()` at the point of issue
2. Verify you're using `GetFrameHeight()` not `GetFontSize()` for row heights
3. Check if a scrollbar is appearing/disappearing
4. Ensure you called `AlignTextToFramePadding()` for mixed text/widget lines
5. In tables, verify column flags (`WidthStretch` vs `WidthFixed`)

---

## 7. Font Usage

Use `UIConstants` for consistent fonts:

```csharp
// Title text (30pt bold)
ImGui.PushFont(UIConstants.FontTitle);
ImGui.Text("MODAL TITLE");
ImGui.PopFont();

// Body text (20pt, default)
ImGui.Text("Regular body text");

// Small text (16pt)
ImGui.PushFont(UIConstants.FontSmall);
ImGui.Text("Fine print");
ImGui.PopFont();
```

Fonts are automatically DPI-scaled via `UIConstants.LoadFonts(dpiScale)`.
