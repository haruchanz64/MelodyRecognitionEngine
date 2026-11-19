using System;
using System.Collections.Generic;
using MelodyRecognitionEngine.Components;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MelodyRecognitionEngine.Samples.InputSystemAdapter
{
    /// <summary>
    /// Data-driven Unity Input System adapter mapping any number of InputActionReferences
    /// or keyboard keys (plus optional sharp/flat and octave modifiers) into
    /// MelodyRecognitionController's DiscreteNoteInputSource.
    /// Can also populate note slots automatically from an InstrumentAsset.
    /// </summary>
    public sealed class InputSystemVirtualInstrument : MonoBehaviour
    {
        [Serializable]
        public struct NoteBinding
        {
            public string Label;
            [Range(24, 108)] public int MidiNote;
#if ENABLE_INPUT_SYSTEM
            public InputActionReference Action;
            public Key FallbackKey;
#endif
        }

        [SerializeField] private MelodyRecognitionController controller;
        [SerializeField] private bool switchToDiscreteModeOnPress = true;
        [SerializeField] private bool syncWithEquippedInstrument = true;

        [Header("Data-Driven Note Bindings (Add any number of notes/chromatics)")]
        [SerializeField] private List<NoteBinding> noteBindings = new List<NoteBinding>
        {
            new NoteBinding { Label = "D4", MidiNote = 62
#if ENABLE_INPUT_SYSTEM
                , FallbackKey = Key.Z
#endif
            },
            new NoteBinding { Label = "E4", MidiNote = 64
#if ENABLE_INPUT_SYSTEM
                , FallbackKey = Key.X
#endif
            },
            new NoteBinding { Label = "F4", MidiNote = 65
#if ENABLE_INPUT_SYSTEM
                , FallbackKey = Key.C
#endif
            },
            new NoteBinding { Label = "G4", MidiNote = 67
#if ENABLE_INPUT_SYSTEM
                , FallbackKey = Key.V
#endif
            },
            new NoteBinding { Label = "A4", MidiNote = 69
#if ENABLE_INPUT_SYSTEM
                , FallbackKey = Key.B
#endif
            },
            new NoteBinding { Label = "B4", MidiNote = 71
#if ENABLE_INPUT_SYSTEM
                , FallbackKey = Key.N
#endif
            },
            new NoteBinding { Label = "C5", MidiNote = 72
#if ENABLE_INPUT_SYSTEM
                , FallbackKey = Key.M
#endif
            },
            new NoteBinding { Label = "D5", MidiNote = 74
#if ENABLE_INPUT_SYSTEM
                , FallbackKey = Key.Comma
#endif
            }
        };

#if ENABLE_INPUT_SYSTEM
        [Header("Optional Pitch Modifiers")]
        [SerializeField] private Key sharpModifierKey = Key.LeftShift;
        [SerializeField] private Key flatModifierKey = Key.LeftCtrl;
        [SerializeField] private Key octaveDownKey = Key.LeftBracket;
        [SerializeField] private Key octaveUpKey = Key.RightBracket;

        private readonly List<(InputAction action, Action<InputAction.CallbackContext> onPerformed, Action<InputAction.CallbackContext> onCanceled)> _boundCallbacks
            = new List<(InputAction, Action<InputAction.CallbackContext>, Action<InputAction.CallbackContext>)>();

        private static readonly Key[] DefaultKeyOrder =
        {
            Key.Z, Key.X, Key.C, Key.V, Key.B, Key.N, Key.M, Key.Comma, Key.Period, Key.Slash,
            Key.A, Key.S, Key.D, Key.F, Key.G, Key.H, Key.J, Key.K, Key.L
        };
#endif

        private int _octaveShift;
        private int _heldMidi = -1;

        public int OctaveShift => _octaveShift;
        public IReadOnlyList<NoteBinding> NoteBindings => noteBindings;

        private void Awake()
        {
            if (controller == null)
            {
                controller = FindAnyObjectByType<MelodyRecognitionController>();
            }
        }

        private void OnEnable()
        {
            if (controller != null && syncWithEquippedInstrument)
            {
                controller.OnInstrumentEquippedEvent += HandleInstrumentEquipped;
                if (controller.EquippedInstrument != null)
                {
                    LoadFromInstrumentLayout(controller.EquippedInstrument);
                }
            }

            BindAllActions();
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.OnInstrumentEquippedEvent -= HandleInstrumentEquipped;
            }

            UnbindAllActions();
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (octaveDownKey != Key.None && kb[octaveDownKey].wasPressedThisFrame)
            {
                _octaveShift = Mathf.Clamp(_octaveShift - 1, -2, 2);
            }

            if (octaveUpKey != Key.None && kb[octaveUpKey].wasPressedThisFrame)
            {
                _octaveShift = Mathf.Clamp(_octaveShift + 1, -2, 2);
            }

            for (int i = 0; i < noteBindings.Count; i++)
            {
                NoteBinding binding = noteBindings[i];
                if (binding.Action != null || binding.FallbackKey == Key.None) continue;

                var keyControl = kb[binding.FallbackKey];
                if (keyControl.wasPressedThisFrame)
                {
                    Press(ResolveModifiedMidi(binding.MidiNote, kb));
                }
                else if (keyControl.wasReleasedThisFrame && _heldMidi >= 0)
                {
                    Release(_heldMidi);
                }
            }
#endif
        }

        /// <summary>
        /// Replaces the active bindings using an InstrumentAsset's NoteLayout.
        /// </summary>
        public void LoadFromInstrumentLayout(InstrumentAsset instrument)
        {
            if (instrument == null || instrument.NoteLayout == null || instrument.NoteLayout.Count == 0)
            {
                return;
            }

            UnbindAllActions();
            noteBindings.Clear();

            for (int i = 0; i < instrument.NoteLayout.Count; i++)
            {
                InstrumentNoteSlot slot = instrument.NoteLayout[i];
                var binding = new NoteBinding
                {
                    Label = slot.Label,
                    MidiNote = slot.MidiNote
#if ENABLE_INPUT_SYSTEM
                    , FallbackKey = i < DefaultKeyOrder.Length ? DefaultKeyOrder[i] : Key.None
#endif
                };
                noteBindings.Add(binding);
            }

            BindAllActions();
        }

        private void HandleInstrumentEquipped(InstrumentAsset instrument)
        {
            if (instrument != null)
            {
                LoadFromInstrumentLayout(instrument);
            }
        }

        private void BindAllActions()
        {
#if ENABLE_INPUT_SYSTEM
            UnbindAllActions();
            for (int i = 0; i < noteBindings.Count; i++)
            {
                NoteBinding binding = noteBindings[i];
                if (binding.Action == null || binding.Action.action == null) continue;

                int baseMidi = binding.MidiNote;
                InputAction action = binding.Action.action;
                Action<InputAction.CallbackContext> onPerformed = _ => Press(ResolveModifiedMidi(baseMidi, Keyboard.current));
                Action<InputAction.CallbackContext> onCanceled = _ => Release(_heldMidi);

                action.Enable();
                action.performed += onPerformed;
                action.canceled += onCanceled;
                _boundCallbacks.Add((action, onPerformed, onCanceled));
            }
#endif
        }

        private void UnbindAllActions()
        {
#if ENABLE_INPUT_SYSTEM
            for (int i = 0; i < _boundCallbacks.Count; i++)
            {
                var entry = _boundCallbacks[i];
                entry.action.performed -= entry.onPerformed;
                entry.action.canceled -= entry.onCanceled;
            }
            _boundCallbacks.Clear();
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private int ResolveModifiedMidi(int baseMidi, Keyboard kb)
        {
            int midi = baseMidi + (_octaveShift * 12);
            if (kb != null)
            {
                if (sharpModifierKey != Key.None && kb[sharpModifierKey].isPressed) midi += 1;
                if (flatModifierKey != Key.None && kb[flatModifierKey].isPressed) midi -= 1;
            }

            return Mathf.Clamp(midi, 24, 108);
        }
#endif

        private void Press(int midi)
        {
            if (controller == null) return;
            if (switchToDiscreteModeOnPress && controller.ActiveModality != InputModality.DiscreteButtons)
            {
                controller.SetModality(InputModality.DiscreteButtons);
            }

            _heldMidi = midi;
            controller.PressVirtualNote(midi);
        }

        private void Release(int midi)
        {
            if (controller == null || _heldMidi != midi) return;
            _heldMidi = -1;
            controller.ReleaseVirtualNote();
        }
    }
}
