# Native VR UI Architecture for Mouse-First Seated VR

## Summary

This proposes adding a native VR UI layer alongside the current patched screen-space UI path. The goal is to reduce long-term maintenance caused by converting Nuclear Option's existing UI hierarchy into world space, especially around extreme local Z offsets, clipping masks, and TextMeshPro rendering issues.

## Motivation

Current UI issues mostly come from translating existing screen-space canvases into world-space VR canvases. Nuclear Option uses local Z values heavily in menus, which requires per-screen flattening and layout fixes. TextMeshPro text also appears to break when ancestor clipping masks are active after conversion to world space.

A native VR UI avoids those inherited layout assumptions by building VR-first panels and controls that are fed by game state and call existing game actions.

## Proposed Direction

- Add a separate native VR UI system under `NOVR/VrUi/Native`.
- Keep mouse as the primary input path for seated VR users.
- Keep existing UI alive where needed for state ownership, but hide or bypass its presentation.
- Replace screens incrementally rather than rewriting all UI at once.
- Start with a small bounded screen before attempting complex menus or the tactical map.

## Input Model

Mouse should remain first-class:

- Headset controls view.
- Mouse controls a VR cursor or panel cursor.
- Mouse buttons click.
- Scroll wheel scrolls lists/maps.
- Keyboard remains available for text and shortcuts.

## Implementation Plan

1. Create a `VrPointerState` abstraction for mouse-driven VR UI input.
2. Add a native VR UI root managed by `NOUIManager`.
3. Build one experimental native panel.
4. Add adapters for game state and game actions.
5. Add a config switch between patched-original UI and native-experimental UI.
6. Gradually migrate screens based on complexity.

## Out of Scope

- Full replacement of every menu in one PR.
- VR controller support as a required input path.
- Rebuilding the tactical map first.

## Risks

- Some game actions may need reflection or Harmony access.
- Original UI may still need to exist for state side effects.
- Dense UI may need panel-cursor mode instead of ray-only pointing.

## Testing

- Verify mouse click, scroll, and hover behavior in headset.
- Verify original UI still maintains required game state.
- Test with main menu, pause/menu flow, and in-flight UI independently.
