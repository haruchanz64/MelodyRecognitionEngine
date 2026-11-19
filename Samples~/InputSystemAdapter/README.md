# InputSystem Virtual Instrument Adapter Sample

Maps a configurable list of `com.unity.inputsystem` `InputActionReference`s or keyboard keys into `MelodyRecognitionController`'s `DiscreteNoteInputSource`.

By default, 8 keys (`Z`, `X`, `C`, `V`, `B`, `N`, `M`, `,`) map to `D4` through `D5`, with `Left Shift` (+1 semitone), `Left Ctrl` (-1 semitone), and `[` / `]` octave shift modifiers. You can add any number of custom chromatic bindings in the Inspector or let the adapter sync its note layout automatically from an equipped `InstrumentAsset`.
