# Design System Specification: The Fluid Atmosphere

## 1. Overview & Creative North Star

### The Creative North Star: "Atmospheric Precision"
This design system rejects the "boxed-in" nature of traditional desktop software. Instead, it treats the interface as a series of light-infused layers. We move away from the rigid, grid-heavy "template" look toward an **Editorial High-Tech** aesthetic. 

The experience is defined by **Atmospheric Precision**: the sidebar feels like a frosted window into the operating system, while the content areas utilize subtle tonal shifts rather than hard lines to define structure. By embracing intentional asymmetry—such as oversized typography headers paired with hyper-minimal action icons—we create a signature look that feels custom-tailored and premium.

---

## 2. Colors & Surface Logic

The palette is rooted in a sophisticated range of "Cool Grays" (`surface` tiers) contrasted against a "Vibrant Digital Cobalt" (`primary`).

### The "No-Line" Rule
**Explicit Instruction:** Designers are prohibited from using 1px solid borders to section off the primary layout. 
*   Boundaries must be defined by background color shifts. For example, the sessions list sits on `surface_container_low`, while the main chat area rests on the base `surface`.
*   This creates a seamless, "liquid" transition between functional areas.

### Surface Hierarchy & Nesting
Depth is achieved through the physical stacking of surface tokens:
*   **Level 0 (Base):** `surface` (#f5f7fa) - The foundation of the main workspace.
*   **Level 1 (Nesting):** `surface_container_low` (#eef1f4) - Used for secondary panels like the sessions list.
*   **Level 2 (Interaction):** `surface_container_lowest` (#ffffff) - Used for interactive elements like the message input field or active cards to make them "pop" against the gray.

### The "Glass & Gradient" Rule
To elevate the UI beyond a standard flat look:
*   **Sidebar:** Utilize Glassmorphism. Apply a 20-40px `backdrop-blur` behind a semi-transparent `surface` or `surface_variant`. 
*   **Primary Actions:** Main CTAs (like the "Send" button) should use a subtle linear gradient from `primary` (#0053cd) to `primary_container` (#789dff) at a 135-degree angle. This adds a "soul" and depth that flat hex codes cannot replicate.

---

## 3. Typography

The system utilizes a dual-typeface strategy to balance editorial character with high-tech readability.

*   **Display & Headlines (Manrope):** This is our "Editorial" voice. Manrope’s geometric yet warm curves should be used for section headers (e.g., "Photos," "Sessions"). Use `display-md` or `headline-lg` with generous tracking to assert authority.
*   **Interface & Data (Inter):** For everything functional—chat bubbles, device names, timestamps—use Inter. It provides maximum legibility at small scales (`body-sm` and `label-md`).
*   **Visual Hierarchy:** Establish a high contrast between the `title-lg` headers and `label-sm` metadata. This "Big-and-Small" approach mimics high-end magazine layouts.

---

## 4. Elevation & Depth

We convey hierarchy through **Tonal Layering** rather than structural scaffolding.

### The Layering Principle
Do not use drop shadows for static layout elements. If a session item is "Active," do not give it a shadow; instead, change its background to `surface_container_highest` or a soft `primary_fixed_dim` with low opacity.

### Ambient Shadows
Shadows are reserved for "Floating" elements (e.g., Tooltips, Modals, or the Message Input bar). 
*   **Spec:** Blur: 32px-64px | Opacity: 4-6% | Color: A tinted version of `on_surface`.
*   **Effect:** The element should appear to be levitating on a cushion of air, not "stuck" to the page.

### The "Ghost Border" Fallback
If an element requires a border for accessibility (e.g., a text input on a white background), use a **Ghost Border**:
*   Token: `outline_variant` (#abadb0)
*   Opacity: Set to 15-20%.
*   **Prohibition:** Never use 100% opaque borders for containment.

---

## 5. Components

### Buttons
*   **Primary:** High-pill shape (Rounded `full`). Uses the Primary-to-Container gradient. 
*   **Secondary/Action Icons:** Minimalist stroke-based icons. When hovered, they should reveal a soft `surface_container_high` circular background.

### Input Fields
*   **The "Pill" Input:** The main message bar should be a floating `surface_container_lowest` pill with an `xl` (3rem) corner radius. It should sit "above" the chat content with an ambient shadow to signify it is the primary interaction point.

### Lists & Sessions
*   **No Dividers:** Absolutely forbid horizontal divider lines. 
*   **Separation:** Use a vertical 8px or 12px gap from the Spacing Scale. 
*   **Active State:** The "Active" session item should use a distinct `surface_container_highest` background with a `sm` (0.5rem) or `md` (1.5rem) corner radius to "hug" the content.

### File Transfer Chips
*   Use `secondary_container` (#caceff) for incoming file indicators. These should be rounded `md` (1.5rem) to feel friendly and tactile.

---

## 6. Do’s and Don’ts

### Do
*   **Do** use `surface_tint` at 5% opacity over the sidebar to give the glass effect a subtle brand-aligned hue.
*   **Do** prioritize white space. If in doubt, add more padding between the three panes.
*   **Do** use stroke-based icons with a consistent 1.5px or 2px weight to match the "crisp" typography.

### Don't
*   **Don't** use pure black (#000000) for text. Always use `on_surface` (#2c2f32) to maintain the soft, premium feel.
*   **Don't** use "Default" 1rem padding. Use the specific `md` (1.5rem) or `lg` (2rem) roundedness and spacing to create a more spacious, desktop-optimized feel.
*   **Don't** use sharp 90-degree corners. Even the "none" setting in our scale is only for edge-to-edge bleed; every container should feel "softened" by at least `sm` (0.5rem) roundedness.