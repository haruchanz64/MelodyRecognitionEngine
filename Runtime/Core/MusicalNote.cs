using System;
using System.Collections.Generic;
using UnityEngine;

namespace MelodyRecognitionEngine.Core
{
    /// <summary>
    /// Pitch conversion, musical notation parsing, and treble-staff coordinate math.
    /// </summary>
    public static class MusicalNote
    {
        public const float DefaultReferenceA4Hz = 440f;
        public const int ReferenceA4Midi = 69;

        private static readonly string[] SharpNames =
        {
            "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
        };

        // Diatonic step inside an octave (0..6 for C..B) for each chromatic semitone (0..11).
        private static readonly int[] ChromaticToDiatonicStep =
        {
            0, 0, 1, 1, 2, 3, 3, 4, 4, 5, 5, 6
        };

        private static readonly bool[] ChromaticIsAccidental =
        {
            false, true, false, true, false, false, true, false, true, false, true, false
        };

        public static float FrequencyToMidiFloat(float frequencyHz, float referenceA4Hz = DefaultReferenceA4Hz)
        {
            if (frequencyHz <= 0f || referenceA4Hz <= 0f)
            {
                return -1f;
            }

            return ReferenceA4Midi + 12f * Mathf.Log(frequencyHz / referenceA4Hz, 2f);
        }

        public static float MidiToFrequency(float midiNote, float referenceA4Hz = DefaultReferenceA4Hz)
        {
            return referenceA4Hz * Mathf.Pow(2f, (midiNote - ReferenceA4Midi) / 12f);
        }

        public static int FrequencyToNearestMidi(float frequencyHz, out float centsOffset, float referenceA4Hz = DefaultReferenceA4Hz)
        {
            float midiFloat = FrequencyToMidiFloat(frequencyHz, referenceA4Hz);
            if (midiFloat < 0f)
            {
                centsOffset = 0f;
                return -1;
            }

            int nearest = Mathf.RoundToInt(midiFloat);
            centsOffset = (midiFloat - nearest) * 100f;
            return nearest;
        }

        public static float SemitoneDifference(float midiA, float midiB)
        {
            return midiB - midiA;
        }

        public static string MidiToName(int midiNote)
        {
            if (midiNote < 0 || midiNote > 127)
            {
                return "--";
            }

            int pitchClass = ((midiNote % 12) + 12) % 12;
            int octave = (midiNote / 12) - 1;
            return $"{SharpNames[pitchClass]}{octave}";
        }

        public static bool IsAccidental(int midiNote)
        {
            if (midiNote < 0) return false;
            int pitchClass = ((midiNote % 12) + 12) % 12;
            return ChromaticIsAccidental[pitchClass];
        }

        /// <summary>
        /// Returns diatonic staff steps relative to E4 (bottom line of treble clef = 0).
        /// Each whole integer is one line-or-space step (E4=0, F4=1, G4=2, A4=3, B4=4, C5=5, D5=6, E5=7, F5=8).
        /// </summary>
        public static int MidiToTrebleStaffStep(int midiNote)
        {
            int octave = (midiNote / 12) - 1;
            int pitchClass = ((midiNote % 12) + 12) % 12;
            int diatonicFromC0 = octave * 7 + ChromaticToDiatonicStep[pitchClass];
            const int e4DiatonicFromC0 = 4 * 7 + 2; // E4
            return diatonicFromC0 - e4DiatonicFromC0;
        }

        /// <summary>
        /// Parses a single note token such as "D4", "F#5", "Bb3", or an integer MIDI note "69".
        /// </summary>
        public static bool TryParseNoteToken(string token, out int midiNote)
        {
            midiNote = -1;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string trimmed = token.Trim();
            if (int.TryParse(trimmed, out int numericMidi) && numericMidi >= 0 && numericMidi <= 127)
            {
                midiNote = numericMidi;
                return true;
            }

            int baseSemitone;
            switch (char.ToUpperInvariant(trimmed[0]))
            {
                case 'C': baseSemitone = 0; break;
                case 'D': baseSemitone = 2; break;
                case 'E': baseSemitone = 4; break;
                case 'F': baseSemitone = 5; break;
                case 'G': baseSemitone = 7; break;
                case 'A': baseSemitone = 9; break;
                case 'B': baseSemitone = 11; break;
                default: return false;
            }

            int idx = 1;
            int accidental = 0;
            while (idx < trimmed.Length)
            {
                char c = trimmed[idx];
                if (c == '#')
                {
                    accidental++;
                    idx++;
                }
                else if (c == 'b' || c == 'B')
                {
                    accidental--;
                    idx++;
                }
                else
                {
                    break;
                }
            }

            if (idx >= trimmed.Length)
            {
                return false;
            }

            if (!int.TryParse(trimmed.Substring(idx), out int octave))
            {
                return false;
            }

            int computed = (octave + 1) * 12 + baseSemitone + accidental;
            if (computed < 0 || computed > 127)
            {
                return false;
            }

            midiNote = computed;
            return true;
        }

        /// <summary>
        /// Parses a space- or comma-separated sequence of notes (e.g. "B4 D5 A4 B4 D5 A4").
        /// Optional duration weights can be attached with ':' (e.g. "B4:2 D5:1 A4:3").
        /// </summary>
        public static bool TryParseNotationSequence(string notation, out List<int> midiNotes, out List<float> relativeDurations)
        {
            midiNotes = new List<int>();
            relativeDurations = new List<float>();

            if (string.IsNullOrWhiteSpace(notation))
            {
                return false;
            }

            string[] tokens = notation.Split(new[] { ' ', ',', '\t', '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string raw = tokens[i].Trim();
                float duration = 1f;
                int colonIndex = raw.IndexOf(':');
                string notePart = raw;
                if (colonIndex > 0)
                {
                    notePart = raw.Substring(0, colonIndex);
                    string durPart = raw.Substring(colonIndex + 1);
                    if (!float.TryParse(durPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out duration) || duration <= 0f)
                    {
                        return false;
                    }
                }

                if (!TryParseNoteToken(notePart, out int midi))
                {
                    return false;
                }

                midiNotes.Add(midi);
                relativeDurations.Add(duration);
            }

            return midiNotes.Count > 0;
        }

        /// <summary>
        /// Formats a sequence of MIDI notes into a readable string like "B4 D5 A4".
        /// </summary>
        public static string FormatSequence(IReadOnlyList<int> midiNotes, IReadOnlyList<float> relativeDurations = null)
        {
            if (midiNotes == null || midiNotes.Count == 0)
            {
                return string.Empty;
            }

            var parts = new string[midiNotes.Count];
            bool includeDurations = relativeDurations != null && relativeDurations.Count == midiNotes.Count;
            if (includeDurations)
            {
                bool allDefault = true;
                for (int i = 0; i < relativeDurations.Count; i++)
                {
                    if (Mathf.Abs(relativeDurations[i] - 1f) > 0.01f)
                    {
                        allDefault = false;
                        break;
                    }
                }

                if (allDefault)
                {
                    includeDurations = false;
                }
            }

            for (int i = 0; i < midiNotes.Count; i++)
            {
                string name = MidiToName(midiNotes[i]);
                parts[i] = includeDurations
                    ? $"{name}:{relativeDurations[i].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"
                    : name;
            }

            return string.Join(" ", parts);
        }
    }
}
