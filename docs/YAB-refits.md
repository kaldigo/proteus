# Proteus for YAB+

This Aetherworks fork of Solona’s Proteus refits equipped gear to the chest and leg presets chosen for your Penumbra collection. Use the [Kaldigo plugin feed](https://raw.githubusercontent.com/kaldigo/DalamudPlugins/main/pluginmaster.json). The fork uses the upstream plugin ID and settings. Disable the upstream installation before switching builds.

1. In **/proteus → Settings → Auto refit**, enable automatic refitting and the checkbox for game gear. Choose your YAB body, chest size and leg shape for the collection.
2. Equip a piece or use **Refit what I’m wearing now**. Vanilla models come straight from the game. Tre and Bibo clothing refits are optional. Disable any clothing donors you are retiring so Penumbra serves vanilla. Skin textures still come from your body setup.
3. Under **YAB refit options**, choose the custom solver or the upstream solver. Vanilla chest and leg displacement strengths default to **0.75** and **1.00**. The vanilla solver measures the torso and each thigh. The modded-body solver interpolates nearby corresponding body points. Hands and feet retain their upstream fitting method.
4. The two generated folders in Penumbra are **Proteus Upscales / Base refits** and **Proteus Upscales / YAB size refits**. Their default priorities are **0** and **7**. Put handmade YAB fits between them. Studio gear brush edits are saved under **Proteus Upscales / Manual corrections**, with a default priority of **8**. A resize or correction can rise above its source. Adjust all three priorities to suit your collection.
5. Start with one top and one pair of bottoms in your usual poses. The plugin saves refits as you equip gear and reuses them on subsequent wears.

