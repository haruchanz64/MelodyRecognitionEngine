using System;
using System.Collections.Generic;
using MelodyRecognitionEngine.Core;
using MelodyRecognitionEngine.Data;
using UnityEngine;

namespace MelodyRecognitionEngine.Matching
{
    /// <summary>
    /// Core melody recognition state machine supporting:
    /// - Sliding-window Free Play matching against a SongRepertoire
    /// - Relative semitone interval matching (mean-centered, transposition- and octave-invariant)
    /// - Strict absolute concert pitch matching (per-song or profile-enforced)
    /// - Optional inter-onset rhythm ratio validation
    /// - Stray-note forgiveness and prefix disambiguation window
    /// - Guided / Learn Mode for step-by-step teaching and unlocking
    /// </summary>
    public sealed class MelodyMatcher
    {
        private readonly SongRepertoire _repertoire;
        private readonly List<NoteEvent> _history = new List<NoteEvent>(32);

        private MatcherPlayMode _playMode = MatcherPlayMode.FreePlay;
        private float _defaultPitchToleranceCents = 55f;
        private bool _forceAbsolutePitch;
        private int _minHistoryLength = 24;
        private float _prefixDisambiguationSeconds = 0.65f;
        private float _matchCooldownSeconds = 0.35f;
        private bool _clearHistoryOnMatch = true;
        private double _lastMatchTime = -100.0;

        // Pending prefix disambiguation state.
        private bool _hasPendingMatch;
        private SongMatchResult _pendingMatch;
        private double _pendingMatchDeadline;
        private readonly List<string> _disambiguationSupersetIds = new List<string>(4);

        // Guided / Learn Mode state.
        private SongDefinition _guidedSong;
        private int _guidedStepIndex;
        private float _guidedAnchorMidi;
        private bool _guidedUnlockOnComplete;
        private bool _guidedResetOnMistake;

        /// <summary>Invoked when a song is fully recognized in Free Play or completed in Guided/Learn Mode.</summary>
        public event Action<SongMatchResult> OnSongMatched;
        /// <summary>Invoked as notes arrive in Free Play whenever at least one recognizable song matches a leading prefix.</summary>
        public event Action<SongMatchProgress> OnMatchProgress;
        /// <summary>Invoked on every evaluated note during Guided/Learn Mode.</summary>
        public event Action<GuidedStepResult> OnGuidedStep;
        /// <summary>Invoked whenever the sliding note history is cleared.</summary>
        public event Action OnHistoryCleared;

        /// <summary>Backing song repertoire queried for recognizable candidates.</summary>
        public SongRepertoire Repertoire => _repertoire;
        /// <summary>Active matcher mode (<see cref="MatcherPlayMode.FreePlay"/> or <see cref="MatcherPlayMode.GuidedLearn"/>).</summary>
        public MatcherPlayMode PlayMode => _playMode;
        /// <summary>Recent note history retained in the sliding recognition window.</summary>
        public IReadOnlyList<NoteEvent> NoteHistory => _history;
        /// <summary>Active target song in Guided/Learn Mode, or null in Free Play.</summary>
        public SongDefinition GuidedTargetSong => _guidedSong;
        /// <summary>Zero-based index of the next expected note in Guided/Learn Mode.</summary>
        public int GuidedStepIndex => _guidedStepIndex;
        /// <summary>True while holding a completed shorter song match to give a longer prefix-sharing song time to finish.</summary>
        public bool HasPendingPrefixDisambiguation => _hasPendingMatch;

        /// <summary>
        /// Minimum sliding-window history capacity. Automatically expands when the repertoire
        /// contains songs longer than this value (including their stray-note margin).
        /// </summary>
        public int MinHistoryLength
        {
            get => _minHistoryLength;
            set => _minHistoryLength = Mathf.Clamp(value, 8, 512);
        }

        /// <summary>
        /// Current sliding-window history limit, expanded dynamically to cover the longest registered song plus its stray-note margin.
        /// </summary>
        public int EffectiveMaxHistoryLength
        {
            get
            {
                int required = _minHistoryLength;
                IReadOnlyList<SongDefinition> songs = _repertoire.AllSongs;
                for (int i = 0; i < songs.Count; i++)
                {
                    SongDefinition s = songs[i];
                    if (s == null) continue;
                    int neededForSong = s.NoteCount + Mathf.Clamp(s.MaxStrayNotes, 0, 2) + 8;
                    if (neededForSong > required)
                    {
                        required = neededForSong;
                    }
                }

                return required;
            }
        }

        /// <summary>Fallback pitch tolerance in cents (clamped to [10, 150]) used when a song's own tolerance is &lt;= 0.</summary>
        public float DefaultPitchToleranceCents
        {
            get => _defaultPitchToleranceCents;
            set => _defaultPitchToleranceCents = Mathf.Clamp(value, 10f, 150f);
        }

        /// <summary>When true, overrides per-song settings and enforces strict concert pitch for all songs.</summary>
        public bool ForceAbsolutePitch
        {
            get => _forceAbsolutePitch;
            set => _forceAbsolutePitch = value;
        }

        /// <summary>Delay window in seconds (clamped to [0, 3]) before committing a shorter song that is a prefix of a longer recognizable song.</summary>
        public float PrefixDisambiguationSeconds
        {
            get => _prefixDisambiguationSeconds;
            set => _prefixDisambiguationSeconds = Mathf.Clamp(value, 0f, 3f);
        }

        /// <summary>Minimum elapsed time in seconds (clamped to [0, 5]) after a match before starting a new history window.</summary>
        public float MatchCooldownSeconds
        {
            get => _matchCooldownSeconds;
            set => _matchCooldownSeconds = Mathf.Clamp(value, 0f, 5f);
        }

        /// <summary>When true, clears the note history buffer immediately after firing a match.</summary>
        public bool ClearHistoryOnMatch
        {
            get => _clearHistoryOnMatch;
            set => _clearHistoryOnMatch = value;
        }

        /// <summary>
        /// Creates a melody matcher bound to <paramref name="repertoire"/>.
        /// </summary>
        public MelodyMatcher(SongRepertoire repertoire)
        {
            _repertoire = repertoire ?? throw new ArgumentNullException(nameof(repertoire));
        }

        /// <summary>
        /// Updates <see cref="DefaultPitchToleranceCents"/> and <see cref="ForceAbsolutePitch"/> from a <see cref="DetectionProfileConfig"/>.
        /// </summary>
        public void ApplyProfile(DetectionProfileConfig profile)
        {
            _defaultPitchToleranceCents = profile.DefaultPitchToleranceCents;
            _forceAbsolutePitch = profile.ForceStrictAbsolutePitch;
        }

        /// <summary>
        /// Switches the matcher to <see cref="MatcherPlayMode.FreePlay"/>, clears guided state, and resets the note history.
        /// </summary>
        public void EnterFreePlayMode()
        {
            _playMode = MatcherPlayMode.FreePlay;
            _guidedSong = null;
            _guidedStepIndex = 0;
            ClearHistory();
        }

        /// <summary>
        /// Switches the matcher to <see cref="MatcherPlayMode.GuidedLearn"/> for step-by-step validation of <paramref name="targetSong"/>.
        /// </summary>
        /// <param name="targetSong">Valid song definition (cloned internally).</param>
        /// <param name="unlockOnComplete">When true, unlocks the song in <see cref="Repertoire"/> upon completing the final note.</param>
        /// <param name="resetOnMistake">When true, a wrong note resets progress to step 0; when false, stays on the current step.</param>
        public void EnterGuidedLearnMode(SongDefinition targetSong, bool unlockOnComplete = true, bool resetOnMistake = false)
        {
            if (targetSong == null || !targetSong.IsValid)
            {
                throw new ArgumentException("Guided Learn Mode requires a valid SongDefinition.", nameof(targetSong));
            }

            _playMode = MatcherPlayMode.GuidedLearn;
            _guidedSong = targetSong.Clone();
            _guidedStepIndex = 0;
            _guidedAnchorMidi = 0f;
            _guidedUnlockOnComplete = unlockOnComplete;
            _guidedResetOnMistake = resetOnMistake;
            ClearHistory();
        }

        /// <summary>
        /// Clears buffered note history, cancels any pending prefix disambiguation match, and invokes <see cref="OnHistoryCleared"/>.
        /// </summary>
        public void ClearHistory()
        {
            _history.Clear();
            CancelPendingMatch();
            OnHistoryCleared?.Invoke();
        }

        /// <summary>
        /// Handles a phrase silence timeout by committing any pending shorter prefix match or clearing Free Play history.
        /// </summary>
        public void NotifySilenceTimeout()
        {
            if (_hasPendingMatch)
            {
                CommitPendingMatch();
                return;
            }

            if (_playMode == MatcherPlayMode.FreePlay && _history.Count > 0)
            {
                ClearHistory();
            }
        }

        /// <summary>
        /// Advances disambiguation timers (call once per frame or with simulated clock timestamps).
        /// </summary>
        public void Tick(double currentTimeSeconds)
        {
            if (_hasPendingMatch && currentTimeSeconds >= _pendingMatchDeadline)
            {
                CommitPendingMatch();
            }
        }

        /// <summary>
        /// Ingests a newly detected <see cref="NoteEvent"/> into either Guided/Learn validation or the Free Play sliding window.
        /// </summary>
        public void FeedNote(NoteEvent note)
        {
            if (_playMode == MatcherPlayMode.GuidedLearn)
            {
                ProcessGuidedNote(note);
                return;
            }

            if (note.StartTimeSeconds - _lastMatchTime < _matchCooldownSeconds && _history.Count == 0)
            {
                return;
            }

            _history.Add(note);
            int effectiveMaxHistory = EffectiveMaxHistoryLength;
            while (_history.Count > effectiveMaxHistory)
            {
                _history.RemoveAt(0);
            }

            EvaluateFreePlay(note.StartTimeSeconds);
        }

        /// <summary>
        /// Evaluates all recognizable repertoire songs against the current sliding note history, resolving full matches, prefix disambiguation, and partial progress.
        /// </summary>
        private void EvaluateFreePlay(double now)
        {
            IReadOnlyList<SongDefinition> candidates = _repertoire.GetRecognizableSongs();
            if (candidates.Count == 0)
            {
                return;
            }

            // If we were waiting for prefix disambiguation, check whether any superset song is still alive.
            bool hasBestFullMatch = false;
            SongMatchResult bestFullMatch = default;
            int bestFullMatchNoteCount = -1;

            SongMatchProgress bestProgress = default;
            int bestProgressMatched = 0;
            float bestProgressScore = -1f;

            for (int i = 0; i < candidates.Count; i++)
            {
                SongDefinition song = candidates[i];
                if (!song.IsValid) continue;

                // 1. Check full suffix match (with optional stray-note forgiveness).
                if (TryMatchFullSong(song, now, out SongMatchResult fullResult))
                {
                    // Prefer longer matches, then higher confidence.
                    if (!hasBestFullMatch ||
                        song.NoteCount > bestFullMatchNoteCount ||
                        (song.NoteCount == bestFullMatchNoteCount && fullResult.Confidence > bestFullMatch.Confidence))
                    {
                        hasBestFullMatch = true;
                        bestFullMatch = fullResult;
                        bestFullMatchNoteCount = song.NoteCount;
                    }
                }

                // 2. Track best partial prefix match ending at the newest note.
                int matchedPrefixLen = ComputeBestPartialPrefixMatch(song, out float partialConf);
                if (matchedPrefixLen > 0)
                {
                    float weighted = matchedPrefixLen + partialConf * 0.5f;
                    if (matchedPrefixLen > bestProgressMatched ||
                        (matchedPrefixLen == bestProgressMatched && weighted > bestProgressScore))
                    {
                        bestProgressMatched = matchedPrefixLen;
                        bestProgressScore = weighted;
                        bestProgress = new SongMatchProgress
                        {
                            BestCandidateSongId = song.SongId,
                            BestCandidateDisplayName = song.DisplayName,
                            MatchedNoteCount = matchedPrefixLen,
                            TargetNoteCount = song.NoteCount,
                            Confidence = partialConf,
                            IsAwaitingPrefixDisambiguation = false
                        };
                    }
                }
            }

            // Check if a longer recognizable song currently has a matching prefix of length >= bestFullMatchNoteCount.
            if (hasBestFullMatch)
            {
                _disambiguationSupersetIds.Clear();
                if (_prefixDisambiguationSeconds > 0f)
                {
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        SongDefinition other = candidates[i];
                        if (string.Equals(other.SongId, bestFullMatch.SongId, StringComparison.Ordinal)) continue;
                        if (other.NoteCount <= bestFullMatchNoteCount) continue;

                        if (MatchesPrefixOfLength(other, bestFullMatchNoteCount, out _))
                        {
                            _disambiguationSupersetIds.Add(other.SongId);
                        }
                    }
                }

                if (_disambiguationSupersetIds.Count > 0)
                {
                    _hasPendingMatch = true;
                    _pendingMatch = bestFullMatch;
                    _pendingMatchDeadline = now + _prefixDisambiguationSeconds;

                    bestProgress = new SongMatchProgress
                    {
                        BestCandidateSongId = bestFullMatch.SongId,
                        BestCandidateDisplayName = bestFullMatch.DisplayName,
                        MatchedNoteCount = bestFullMatchNoteCount,
                        TargetNoteCount = bestFullMatchNoteCount,
                        Confidence = bestFullMatch.Confidence,
                        IsAwaitingPrefixDisambiguation = true
                    };
                    OnMatchProgress?.Invoke(bestProgress);
                    return;
                }

                // Immediate match (no longer competing prefix song).
                CancelPendingMatch();
                FireMatch(bestFullMatch, now);
                return;
            }

            // If we had a pending shorter match, verify whether any superset song is still progressing.
            if (_hasPendingMatch)
            {
                bool anySupersetStillAlive = false;
                for (int s = 0; s < _disambiguationSupersetIds.Count; s++)
                {
                    if (_repertoire.TryGetSong(_disambiguationSupersetIds[s], out SongDefinition superset))
                    {
                        int prefixLen = ComputeBestPartialPrefixMatch(superset, out _);
                        if (prefixLen >= 2)
                        {
                            anySupersetStillAlive = true;
                            break;
                        }
                    }
                }

                if (!anySupersetStillAlive)
                {
                    // Player diverged from the longer song; promote the already-completed shorter song immediately.
                    CommitPendingMatch();
                    return;
                }

                // Keep extending deadline slightly while player actively continues the longer song.
                _pendingMatchDeadline = now + _prefixDisambiguationSeconds;
                bestProgress.IsAwaitingPrefixDisambiguation = true;
            }

            if (bestProgressMatched > 0)
            {
                OnMatchProgress?.Invoke(bestProgress);
            }
        }

        /// <summary>Promotes a buffered prefix-disambiguation match and fires <see cref="OnSongMatched"/>.</summary>
        private void CommitPendingMatch()
        {
            if (!_hasPendingMatch) return;
            SongMatchResult result = _pendingMatch;
            CancelPendingMatch();
            FireMatch(result, result.MatchedAtSeconds);
        }

        /// <summary>Clears any pending prefix-disambiguation candidate without emitting a match.</summary>
        private void CancelPendingMatch()
        {
            _hasPendingMatch = false;
            _pendingMatch = default;
            _disambiguationSupersetIds.Clear();
        }

        /// <summary>Records match cooldown timestamp, optionally clears history, and invokes <see cref="OnSongMatched"/>.</summary>
        private void FireMatch(SongMatchResult result, double now)
        {
            _lastMatchTime = now;
            if (_clearHistoryOnMatch)
            {
                _history.Clear();
            }

            OnSongMatched?.Invoke(result);
        }

        /// <summary>
        /// Tests whether the trailing notes in <see cref="_history"/> match <paramref name="song"/> exactly or with up to <see cref="SongDefinition.MaxStrayNotes"/> interior skips.
        /// </summary>
        private bool TryMatchFullSong(SongDefinition song, double now, out SongMatchResult result)
        {
            result = default;
            int targetLen = song.NoteCount;
            if (_history.Count < targetLen)
            {
                return false;
            }

            bool useAbsolute = _forceAbsolutePitch || song.PitchMode == PitchMatchingMode.AbsolutePitch;
            float toleranceCents = song.PitchToleranceCents > 0f ? song.PitchToleranceCents : _defaultPitchToleranceCents;

            // 1. Try exact contiguous suffix first (0 stray notes).
            int startIdx = _history.Count - targetLen;
            if (EvaluateSubsequence(song, startIdx, -1, -1, useAbsolute, toleranceCents, out float avgErr, out float offsetSemi, out bool rhythmOk))
            {
                float confidence = ComputeConfidence(avgErr, toleranceCents, strayCount: 0);
                result = BuildMatchResult(song, confidence, avgErr, Mathf.RoundToInt(offsetSemi), useAbsolute, rhythmOk, now);
                return true;
            }

            // 2. Optional stray-note forgiveness (skip up to MaxStrayNotes interior notes while keeping the final note anchored).
            int maxStray = targetLen >= 4 ? Mathf.Clamp(song.MaxStrayNotes, 0, 2) : 0;
            if (maxStray >= 1 && _history.Count >= targetLen + 1)
            {
                int windowStart = _history.Count - (targetLen + 1);
                float bestErr = float.MaxValue;
                float bestOffset = 0f;
                bool bestRhythm = false;
                bool found = false;

                // Skip 1 note strictly inside the window (neither the first nor last note of the phrase).
                for (int skip = windowStart + 1; skip < _history.Count - 1; skip++)
                {
                    if (EvaluateSubsequence(song, windowStart, skip, -1, useAbsolute, toleranceCents, out float err, out float off, out bool rOk))
                    {
                        if (err < bestErr)
                        {
                            bestErr = err;
                            bestOffset = off;
                            bestRhythm = rOk;
                            found = true;
                        }
                    }
                }

                if (found)
                {
                    float confidence = ComputeConfidence(bestErr, toleranceCents, strayCount: 1);
                    result = BuildMatchResult(song, confidence, bestErr, Mathf.RoundToInt(bestOffset), useAbsolute, bestRhythm, now);
                    return true;
                }
            }

            if (maxStray >= 2 && _history.Count >= targetLen + 2)
            {
                int windowStart = _history.Count - (targetLen + 2);
                float bestErr = float.MaxValue;
                float bestOffset = 0f;
                bool bestRhythm = false;
                bool found = false;

                for (int skipA = windowStart + 1; skipA < _history.Count - 2; skipA++)
                {
                    for (int skipB = skipA + 1; skipB < _history.Count - 1; skipB++)
                    {
                        if (EvaluateSubsequence(song, windowStart, skipA, skipB, useAbsolute, toleranceCents, out float err, out float off, out bool rOk))
                        {
                            if (err < bestErr)
                            {
                                bestErr = err;
                                bestOffset = off;
                                bestRhythm = rOk;
                                found = true;
                            }
                        }
                    }
                }

                if (found)
                {
                    float confidence = ComputeConfidence(bestErr, toleranceCents, strayCount: 2);
                    result = BuildMatchResult(song, confidence, bestErr, Mathf.RoundToInt(bestOffset), useAbsolute, bestRhythm, now);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Evaluates pitch residuals (using mean-centered transposition in relative mode) and optional inter-onset rhythm for a candidate history window.
        /// </summary>
        private bool EvaluateSubsequence(
            SongDefinition song,
            int windowStart,
            int skipIndexA,
            int skipIndexB,
            bool useAbsolute,
            float toleranceCents,
            out float avgErrorCents,
            out float meanOffsetSemitones,
            out bool rhythmMatched)
        {
            avgErrorCents = 0f;
            meanOffsetSemitones = 0f;
            rhythmMatched = !song.ValidateRhythm;

            int targetLen = song.NoteCount;

            // Compute optimal mean transposition offset for relative interval matching (eliminates anchor bias).
            if (!useAbsolute)
            {
                float offsetSum = 0f;
                int matchedIdx = 0;
                for (int h = windowStart; h < _history.Count && matchedIdx < targetLen; h++)
                {
                    if (h == skipIndexA || h == skipIndexB) continue;
                    float playedMidi = GetEffectiveMidi(_history[h]);
                    offsetSum += playedMidi - song.Notes[matchedIdx].MidiNote;
                    matchedIdx++;
                }

                if (matchedIdx != targetLen) return false;
                meanOffsetSemitones = offsetSum / targetLen;
            }

            // Validate every note's pitch residual against tolerance.
            float totalAbsErrCents = 0f;
            int idx = 0;
            for (int h = windowStart; h < _history.Count && idx < targetLen; h++)
            {
                if (h == skipIndexA || h == skipIndexB) continue;
                float playedMidi = GetEffectiveMidi(_history[h]);
                float expectedMidi = song.Notes[idx].MidiNote + meanOffsetSemitones;
                float errCents = Mathf.Abs(playedMidi - expectedMidi) * 100f;
                if (errCents > toleranceCents)
                {
                    return false;
                }

                totalAbsErrCents += errCents;
                idx++;
            }

            if (idx != targetLen) return false;
            avgErrorCents = totalAbsErrCents / targetLen;

            // Optional tempo-invariant inter-onset rhythm ratio validation.
            if (song.ValidateRhythm && targetLen >= 3)
            {
                rhythmMatched = ValidateInterOnsetRhythm(song, windowStart, skipIndexA, skipIndexB);
                if (!rhythmMatched)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Verifies that normalized inter-onset time intervals match the song's <see cref="SongNoteEntry.RelativeDuration"/> ratios within <see cref="SongDefinition.RhythmTolerance"/>.
        /// </summary>
        private bool ValidateInterOnsetRhythm(SongDefinition song, int windowStart, int skipIndexA, int skipIndexB)
        {
            int intervalCount = song.NoteCount - 1;
            if (intervalCount < 2) return true;

            // Gather actual inter-onset intervals and target relative durations for the first N-1 notes.
            float totalActualIoi = 0f;
            float totalTargetDur = 0f;

            // Small stack-like temporary arrays via fixed max note check (songs rarely exceed 32 notes).
            var actualIois = new float[intervalCount];
            var targetDurs = new float[intervalCount];

            int prevHistoryIdx = -1;
            int step = 0;
            for (int h = windowStart; h < _history.Count && step < song.NoteCount; h++)
            {
                if (h == skipIndexA || h == skipIndexB) continue;
                if (prevHistoryIdx >= 0)
                {
                    float ioi = (float)(_history[h].StartTimeSeconds - _history[prevHistoryIdx].StartTimeSeconds);
                    if (ioi <= 0.01f) return false;
                    actualIois[step - 1] = ioi;
                    targetDurs[step - 1] = Mathf.Max(0.1f, song.Notes[step - 1].RelativeDuration);
                    totalActualIoi += ioi;
                    totalTargetDur += targetDurs[step - 1];
                }

                prevHistoryIdx = h;
                step++;
            }

            if (totalActualIoi <= 0.01f || totalTargetDur <= 0.01f) return false;

            for (int i = 0; i < intervalCount; i++)
            {
                float actualNorm = actualIois[i] / totalActualIoi;
                float targetNorm = targetDurs[i] / totalTargetDur;
                float relError = Mathf.Abs(actualNorm - targetNorm) / Mathf.Max(0.05f, targetNorm);
                if (relError > song.RhythmTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Finds the longest proper prefix of <paramref name="song"/> that matches the trailing notes of <see cref="_history"/>.
        /// </summary>
        private int ComputeBestPartialPrefixMatch(SongDefinition song, out float confidence)
        {
            confidence = 0f;
            int maxLen = Mathf.Min(_history.Count, song.NoteCount - 1);
            for (int len = maxLen; len >= 1; len--)
            {
                if (MatchesPrefixOfLength(song, len, out float conf))
                {
                    confidence = conf;
                    return len;
                }
            }

            return 0;
        }

        /// <summary>
        /// Tests whether the last <paramref name="prefixLen"/> history notes match the first <paramref name="prefixLen"/> notes of <paramref name="song"/>.
        /// </summary>
        private bool MatchesPrefixOfLength(SongDefinition song, int prefixLen, out float confidence)
        {
            confidence = 0f;
            if (prefixLen <= 0 || prefixLen > _history.Count || prefixLen > song.NoteCount)
            {
                return false;
            }

            bool useAbsolute = _forceAbsolutePitch || song.PitchMode == PitchMatchingMode.AbsolutePitch;
            float toleranceCents = song.PitchToleranceCents > 0f ? song.PitchToleranceCents : _defaultPitchToleranceCents;
            int startIdx = _history.Count - prefixLen;

            float offset = 0f;
            if (!useAbsolute)
            {
                if (prefixLen == 1)
                {
                    // Single note always trivially begins any relative melody; report modest partial confidence.
                    confidence = 0.5f;
                    return true;
                }

                float sum = 0f;
                for (int i = 0; i < prefixLen; i++)
                {
                    sum += GetEffectiveMidi(_history[startIdx + i]) - song.Notes[i].MidiNote;
                }
                offset = sum / prefixLen;
            }

            float totalErr = 0f;
            for (int i = 0; i < prefixLen; i++)
            {
                float played = GetEffectiveMidi(_history[startIdx + i]);
                float expected = song.Notes[i].MidiNote + offset;
                float err = Mathf.Abs(played - expected) * 100f;
                if (err > toleranceCents)
                {
                    return false;
                }

                totalErr += err;
            }

            float avgErr = totalErr / prefixLen;
            confidence = ComputeConfidence(avgErr, toleranceCents, strayCount: 0);
            return true;
        }

        /// <summary>
        /// Validates a single note against the active step of <see cref="_guidedSong"/>, anchoring relative pitch on step 0 and unlocking on completion.
        /// </summary>
        private void ProcessGuidedNote(NoteEvent note)
        {
            if (_guidedSong == null || !_guidedSong.IsValid) return;

            bool useAbsolute = _forceAbsolutePitch || _guidedSong.PitchMode == PitchMatchingMode.AbsolutePitch;
            float toleranceCents = _guidedSong.PitchToleranceCents > 0f ? _guidedSong.PitchToleranceCents : _defaultPitchToleranceCents;

            float playedMidi = GetEffectiveMidi(note);
            int expectedNominalMidi = _guidedSong.Notes[_guidedStepIndex].MidiNote;
            float errCents;
            bool isCorrect;

            if (!useAbsolute && _guidedStepIndex == 0)
            {
                // First note establishes the player's vocal/instrument anchor in relative mode.
                _guidedAnchorMidi = playedMidi;
                errCents = 0f;
                isCorrect = true;
            }
            else
            {
                float targetMidi = useAbsolute
                    ? expectedNominalMidi
                    : _guidedAnchorMidi + (expectedNominalMidi - _guidedSong.Notes[0].MidiNote);

                errCents = Mathf.Abs(playedMidi - targetMidi) * 100f;
                isCorrect = errCents <= toleranceCents;
            }

            int completedStep = _guidedStepIndex;
            bool songCompleted = false;

            if (isCorrect)
            {
                _history.Add(note);
                _guidedStepIndex++;

                if (_guidedStepIndex >= _guidedSong.NoteCount)
                {
                    songCompleted = true;
                    if (_guidedUnlockOnComplete)
                    {
                        _repertoire.UnlockSong(_guidedSong.SongId);
                    }
                }
            }
            else if (_guidedResetOnMistake)
            {
                _guidedStepIndex = 0;
                _history.Clear();
            }

            OnGuidedStep?.Invoke(new GuidedStepResult
            {
                SongId = _guidedSong.SongId,
                StepIndex = completedStep,
                TotalSteps = _guidedSong.NoteCount,
                ExpectedMidiNote = expectedNominalMidi,
                PlayedMidiNote = note.MidiNote,
                IsCorrect = isCorrect,
                IsSongCompleted = songCompleted,
                PitchErrorCents = errCents
            });

            if (songCompleted)
            {
                int transposition = useAbsolute ? 0 : Mathf.RoundToInt(_guidedAnchorMidi - _guidedSong.Notes[0].MidiNote);
                var match = BuildMatchResult(_guidedSong, 1f, errCents, transposition, useAbsolute, true, note.StartTimeSeconds);
                _guidedStepIndex = 0;
                _history.Clear();
                OnSongMatched?.Invoke(match);
            }
        }

        /// <summary>Returns <see cref="NoteEvent.ExactMidiFloat"/> when populated, otherwise falls back to integer <see cref="NoteEvent.MidiNote"/>.</summary>
        private static float GetEffectiveMidi(NoteEvent note)
        {
            return note.ExactMidiFloat > 0f ? note.ExactMidiFloat : note.MidiNote;
        }

        /// <summary>Computes a normalized match confidence score in [0, 1] from average pitch error and stray-note count.</summary>
        private static float ComputeConfidence(float avgErrorCents, float toleranceCents, int strayCount)
        {
            float pitchScore = 1f - Mathf.Clamp01(avgErrorCents / Mathf.Max(1f, toleranceCents * 1.4f));
            float strayPenalty = strayCount * 0.12f;
            return Mathf.Clamp01(pitchScore - strayPenalty);
        }

        /// <summary>Assembles a populated <see cref="SongMatchResult"/> payload from match metrics.</summary>
        private static SongMatchResult BuildMatchResult(
            SongDefinition song,
            float confidence,
            float avgErrCents,
            int transpositionSemi,
            bool absolute,
            bool rhythmOk,
            double matchedAt)
        {
            return new SongMatchResult
            {
                SongId = song.SongId,
                DisplayName = song.DisplayName,
                Confidence = confidence,
                AveragePitchErrorCents = avgErrCents,
                TranspositionSemitones = transpositionSemi,
                MatchedViaAbsolutePitch = absolute,
                MatchedRhythm = rhythmOk,
                IsCustomSong = song.IsCustomSong,
                MatchedAtSeconds = matchedAt
            };
        }
    }
}
