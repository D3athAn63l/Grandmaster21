# Final artifact cleanup: owner check (0.12.0 Beta)

The owner has runtime-confirmed all seven phenomena, VFX, immediate healing, regeneration,
injury/battle provenance, artifact information and Dev controls. Only these final checks remain:

1. Boot the game and confirm the latest build stamp in Player.log.
2. Trigger Flame Wave several times. Confirm sound plays and no red error says
   `Tried to play sustainer SoundDef ... as a one-shot sound.`
3. Hover `DEV: Artifact state`. Its tooltip should clearly say that it prints state to the log;
   no popup is expected.
4. Click it and confirm the corresponding internal-state entry appears in the developer log / Player.log.
5. Save/reload once and confirm no new errors.

Flame Wave now uses vanilla `SoundDefOf.Interact_Ignite` through the existing positional one-shot
path. Audible suitability remains an owner check. No full Crafting21 torture test is required again.
Keep PR #9 unmerged.
