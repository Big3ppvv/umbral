---
version: alpha
name: UMBRA
description: A small keeper's journal surrounding a living, torchlit pixel dungeon.
colors:
  background: "#10191c"
  surface: "#172225"
  primary: "#d5b476"
  text: "#e9dcc0"
  secondary: "#7dbcb5"
  muted: "#a0aea7"
  danger: "#d77b5e"
typography:
  display:
    fontFamily: "Georgia, 'Times New Roman', serif"
  sans:
    fontFamily: "'Trebuchet MS', Arial, sans-serif"
  mono:
    fontFamily: "'Courier New', monospace"
rounded:
  DEFAULT: "6px"
  sm: "3px"
  lg: "12px"
spacing:
  panel: "22px"
  gap: "20px"
components:
  button:
    rounded: "6px"
  panel:
    rounded: "6px"
  dialog:
    rounded: "12px"
---

# UMBRA design system

## Overview

A keeper's field journal beside a rain-darkened ruined abbey. The audience is Spanish-speaking players looking for a short browser action game. Product register: the live dungeon is the central task, with a quiet character sheet and route ledger surrounding it. The signature is a warm, finely textured pixel crypt containing a tiny teal-cloaked light keeper. Avoid neon arcade dashboards and oversized marketing sections.

New project: the user's request is the product source; no prior brand, workflows or business documents exist. Spanish UI; no geographic-market assumption. Desktop keyboard and pointer, with touch movement and dash controls on small screens. No billing, identity, or competitive ranking.

DESIGN.md owns palette/type/radius values. `src/styles.css` directly maps colors to identically named `--color-*` variables, type to `--font-*`, and radii to `--radius-*`. `scripts/build.py` verifies palette mapping during builds. Canvas scene colors intentionally use the same art palette in `engine.ts`.

## Colors

Slate background, slightly lighter panel surfaces, parchment copy, brass primary actions, sea-glass guardian light. Secondary denotes vitality and completed rooms; danger denotes incoming damage or failed saving, with accompanying text. Muted text remains readable. Gold outlines indicate keyboard focus. Single dark theme, with a system-operable forced-color mode for controls.

## Typography

Georgia display titles evoke a weathered fantasy book, used sparingly. Trebuchet provides compact, readable Spanish body copy; Courier New supplies timer and room counters. Body line-height 1.6; labels 1.4. No remote font dependency or font-swap layout shifts.

## Layout

At 1200px the main game uses a 205px character sheet, flexible 960:600 canvas, and 205px route ledger. Max page width 1480px. Under 1100px the route ledger moves below; under 760px the character sheet becomes a compact horizontal status and the canvas spans the page. Document owns scrolling. Gaps of 20px and panel padding of 22px keep a measured journal rhythm. Canvas aspect ratio reserves geometry.

## Elevation & Depth

Hairline brass-tinted borders, subtle stone-like surfaces, and one deep shadow around the dungeon. Native dialog is the shared modal primitive with a dark backdrop. Toasts sit at the lower right and do not move page content. No floating decorative panels.

## Shapes

6px controls/panels, 12px dialogs, 3px keycaps. Circular route nodes communicate travel; square corners belong to pixel art. SVG icons share thin strokes; game sprites are deliberately blocky.

## Components

Primary actions use solid brass with dark text; hover lightens, pressed shifts 1px, focus uses an offset 2px gold outline. Ghost actions have subtle borders. Disabled controls have reduced emphasis and a text reason. Pending actions keep their dimensions and disable repeat input. Native buttons own interaction semantics.

Shared modal: native `dialog.showModal()` owns focus trapping, Escape, inertness, and focus return. Reward choices require a choice, so Escape stays in the modal. Help and codex remain distinct from combat. Pause is a persistent in-canvas panel with a resume action, not a modal.

Shared status: one polite live region for useful notices. Per-frame stats are not live regions. Save failure stays inline with a retry action. Profile store owns saving; engine owns the run state. Scrollbar variables in the global stylesheet apply to every scroll container, including dialogs, with standards-based properties and WebKit fallbacks.

Game motion conveys movement, attack and damage. Reduced motion removes camera shake and decorative pulses. Each run is a six-room sequence; room clear opens three upgrades; choosing advances; death/win records one result. Meta upgrades cost fragments and take effect next run. Seed is generated per run.

Unity edition: `unity/UMBRA` preserves this palette and Spanish vocabulary using runtime UGUI and procedural SpriteRenderer art. `UmbraHud` owns modal/menu behavior and `UmbraRenderer` owns scene art; no external font or texture dependency. Unity uses its platform-local profile, independent of web/Python saves. The website footer offers the Unity project and combined source as actual ZIP downloads. The Unity default viewport is 1280×800, with a scalable Canvas, pause on focus loss, optional sound and reduced motion. Unity controls and graphics require Editor verification before claiming platform-ready builds.

## Do's and Don'ts

- Do keep enemy silhouettes and projectiles distinct from the floor.
- Do preserve readable game space and accessible focus at narrow widths.
- Don't autoplay sound; the sound button explicitly enables it.
- Don't obscure saving errors with transient notifications.
