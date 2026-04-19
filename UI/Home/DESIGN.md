# Design System Strategy: The Fluid Archive

This design system is built to transform a utility-focused file-sharing tool into a high-end digital experience. We are moving away from the "utility-grid" aesthetic and toward **"The Fluid Archive"**—a Creative North Star that treats shared content not as data entries, but as curated editorial objects. 

The goal is to provide a sense of weightless efficiency. We achieve this through sophisticated tonal layering, the elimination of structural lines, and a high-contrast typographic hierarchy that feels more like a premium magazine than a file manager.

---

## 1. Colors & Surface Philosophy

The palette is anchored by a professional, high-performance blue, but its luxury comes from the nuanced use of grays and whites.

### The "No-Line" Rule
To achieve a premium, seamless look, **1px solid borders are strictly prohibited for sectioning.** Structural separation must be achieved through:
*   **Background Shifts:** Use `surface-container-low` for secondary areas against a `surface` background.
*   **Negative Space:** Utilize the larger values of our spacing scale (`spacing-10` to `spacing-20`) to define boundaries.

### Surface Hierarchy & Nesting
Think of the UI as layers of physical material.
*   **Base:** `surface` (#f8f9fa) is your canvas.
*   **Containers:** Use `surface-container-lowest` (#ffffff) for primary content cards to make them "pop" with a clean, crisp edge.
*   **Recessed Areas:** Use `surface-container-high` (#e7e8e9) for sidebars or "drop zones" to give them a sense of functional depth.

### The "Glass & Gradient" Rule
To break the "flat" web look:
*   **Glassmorphism:** Use semi-transparent versions of `surface-container-lowest` with a `backdrop-blur` (12px–20px) for floating navigation or overlay modals.
*   **Signature Textures:** For primary actions, do not use a flat hex. Apply a subtle linear gradient from `primary` (#0050cb) to `primary_container` (#0066ff) at a 135-degree angle. This adds "soul" and a tactile, liquid quality to the "Drop" action.

---

## 2. Typography: The Editorial Scale

We pair the character-rich **Manrope** for headers with the functional precision of **Inter** for data.

*   **Display & Headlines (Manrope):** These should feel authoritative. Use `display-lg` for empty states and welcome screens. The generous tracking and organic curves of Manrope suggest a high-end, custom-built feel.
*   **Body & Labels (Inter):** Inter handles the heavy lifting of file names and timestamps. Keep `body-md` as the standard for sharing "bubbles" to ensure maximum readability across devices.
*   **Intentional Asymmetry:** Avoid centering all text. Align headlines to the left with wide `spacing-16` margins to create a modern, editorial rhythm.

---

## 3. Elevation & Depth

In this system, depth is felt, not seen.

*   **Tonal Layering:** Instead of shadows, place a `surface-container-lowest` card on top of a `surface-container-low` background. The subtle 2-bit color shift creates a sophisticated "lift."
*   **Ambient Shadows:** If an element must float (e.g., a mobile action button), use an ultra-diffused shadow: `box-shadow: 0 20px 40px rgba(25, 28, 29, 0.06)`. The shadow color is derived from `on_surface`, not pure black.
*   **The Ghost Border:** For accessibility in input fields or drag-and-drop zones, use a "Ghost Border." Apply `outline-variant` at 15% opacity. It provides a hint of structure without cluttering the visual field.

---

## 4. Components

### Shared Item Bubbles (The Archive "Cells")
*   **Structure:** Avoid the traditional "chat bubble" tail. Use `rounded-xl` (1.5rem) for the container.
*   **Background:** Use `surface-container-low` for received items and a soft `primary-fixed` for sent items.
*   **No Dividers:** Items in the list are separated by `spacing-4` (1.4rem) of vertical space. No horizontal lines.

### Primary "Drop" Action
*   **Style:** A large, `rounded-full` pill.
*   **Color:** The signature gradient (Primary to Primary Container).
*   **Interaction:** On hover, the elevation should not increase; instead, the gradient should subtly shift in intensity.

### Drag-and-Drop Zones
*   **Visuals:** Use `surface-container-highest` with a dashed "Ghost Border." 
*   **State:** When a file is hovered over the zone, transition the background to `primary-fixed` at 50% opacity to signal "Ready."

### Sidebar Navigation
*   **Surface:** `surface-container-low`.
*   **Active State:** Do not use a highlight box. Use a `primary` color vertical pill (4px width) on the far left and transition the text from `on-surface-variant` to `on-surface` (Bold).

---

## 5. Do’s and Don’ts

### Do
*   **DO** use whitespace as a structural tool. If the layout feels crowded, increase spacing to `spacing-12` before adding a line.
*   **DO** use `tertiary` (#a33200) sparingly for "Attention" or "Urgent" file transfers to provide a warm contrast to the professional blue.
*   **DO** leverage `surface-bright` for moments of "Delight" or success states.

### Don’t
*   **DON'T** use 1px solid borders for lists. It breaks the "Fluid Archive" aesthetic.
*   **DON'T** use standard Material shadows. They are too heavy for this minimalist system. Stick to Tonal Layering.
*   **DON'T** use high-contrast blacks. The darkest text should be `on-surface` (#191c1d), which is a soft, deep charcoal that feels more premium.
*   **DON'T** crowd the sidebar. Use `label-md` for secondary navigation items to maintain a clear visual hierarchy.

---

## 6. Implementation Reference

| Element | Token | Value |
| :--- | :--- | :--- |
| **Main Background** | `surface` | #f8f9fa |
| **Card / Bubble** | `surface-container-lowest` | #ffffff |
| **Primary Action** | `primary` | #0050cb |
| **Corner Radius (Large)** | `rounded-xl` | 1.5rem |
| **Content Padding** | `spacing-6` | 2rem |
| **Headline Font** | `headline-lg` | Manrope, 2rem |
| **Body Font** | `body-md` | Inter, 0.875rem |