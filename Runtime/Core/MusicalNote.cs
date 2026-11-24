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
        /// <summary>Default concert pitch tuning frequency for A4 in Hz.</summary>
        public const float DefaultReferenceA4Hz = 440f;
        /// <summary>Standard MIDI note number for A4.</summary>
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

        /// <summary>
        /// Converts a frequency in Hz to a continuous fractional MIDI note number.
        /// </summary>
        /// <param name="frequencyHz">Input fundamental frequency in Hz (must be positive).</param>
        /// <param name="referenceA4Hz">Concert A4 reference frequency in Hz.</param>
        /// <returns>Fractional MIDI note number, or -1 if either frequency is non-positive.</returns>
        public static float FrequencyToMidiFloat(float frequencyHz, float referenceA4Hz = DefaultReferenceA4Hz)
        {
            if (frequencyHz <= 0f || referenceA4Hz <= 0f)
            {
                return -1f;
            }

            return ReferenceA4Midi + 12f * Mathf.Log(frequencyHz / referenceA4Hz, 2f);
        }

        /// <summary>
        /// Converts a MIDI note number (integer or fractional) to its frequency in Hz.
        /// </summary>
        /// <param name="midiNote">MIDI note number (69 = A4).</param>
        /// <param name="referenceA4Hz">Concert A4 reference frequency in Hz.</param>
        /// <returns>Frequency in Hz.</returns>
        public static float MidiToFrequency(float midiNote, float referenceA4Hz = DefaultReferenceA4Hz)
        {
            return referenceA4Hz * Mathf.Pow(2f, (midiNote - ReferenceA4Midi) / 12f);
        }

        /// <summary>
        /// Quantizes a frequency in Hz to the nearest integer MIDI note and computes the signed deviation in cents.
        /// </summary>
        /// <param name="frequencyHz">Input fundamental frequency in Hz.</param>
        /// <param name="centsOffset">Receives the signed pitch offset from the nearest semitone in cents (-50 to +50).</param>
        /// <param name="referenceA4Hz">Concert A4 reference frequency in Hz.</param>
        /// <returns>Nearest integer MIDI note, or -1 if <paramref name="frequencyHz"/> is invalid.</returns>
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

        /// <summary>
        /// Returns the signed pitch interval in semitones from <paramref name="midiA"/> to <paramref name="midiB"/>.
        /// </summary>
        public static float SemitoneDifference(float midiA, float midiB)
        {
            return midiB - midiA;
        }

        /// <summary>
        /// Formats a MIDI note number [0, 127] into scientific pitch notation using sharps (e.g. "C4", "F#5").
        /// </summary>
        /// <param name="midiNote">MIDI note number to format.</param>
        /// <returns>Pitch name string, or "--" if outside [0, 127].</returns>
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

        /// <summary>
        /// Returns true when the given MIDI note falls on a sharp/flat black key in 12-TET.
        /// </summary>
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
        /// <param name="midiNote">MIDI note number to map onto the treble staff.</param>
        /// <returns>Signed diatonic step offset from E4.</returns>
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
        /// <param name="token">Pitch token string to parse.</param>
        /// <param name="midiNote">Receives the parsed MIDI note in [0, 127], or -1 on failure.</param>
        /// <returns>True if the token resolved to a valid MIDI note.</returns>
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
        /// <param name="notation">Whitespace-, comma-, or semicolon-separated notation string.</param>
        /// <param name="midiNotes">Receives the parsed list of MIDI note numbers.</param>
        /// <param name="relativeDurations">Receives the corresponding positive relative duration weights (default 1.0).</param>
        /// <returns>True if at least one valid note was parsed and no malformed tokens were encountered.</returns>
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
        /// Appends <c>:duration</c> suffixes only when <paramref name="relativeDurations"/> contains non-unit weights.
        /// </summary>
        /// <param name="midiNotes">Sequence of MIDI note numbers to format.</param>
        /// <param name="relativeDurations">Optional relative beat weights matching <paramref name="midiNotes"/> in length.</param>
        /// <returns>Space-separated notation string, or an empty string if <paramref name="midiNotes"/> is null or empty.</returns>
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
