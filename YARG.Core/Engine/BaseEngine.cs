﻿using System;
using System.Collections.Generic;
using YARG.Core.Chart;
using YARG.Core.Input;

namespace YARG.Core.Engine
{
    // This is a hack lol
    public abstract class BaseEngine
    {
        public bool IsInputQueued => InputQueue.Count > 0;

        public int BaseScore { get; protected set; }

        protected bool IsInputUpdate { get; private set; }
        protected bool IsBotUpdate   { get; private set; }

        protected abstract float[] StarMultiplierThresholds { get; }
        protected abstract float[] StarScoreThresholds      { get; }

        private readonly SyncTrack _syncTrack;

        protected readonly Queue<GameInput> InputQueue;

        protected readonly uint Resolution;
        protected readonly int  TicksPerSustainPoint;

        protected GameInput CurrentInput;

        protected double CurrentTime;
        protected double LastUpdateTime;

        protected uint LastTick;

        protected BaseEngine(SyncTrack syncTrack)
        {
            _syncTrack = syncTrack;
            Resolution = syncTrack.Resolution;

            TicksPerSustainPoint = (int) Resolution / 25;

            InputQueue = new Queue<GameInput>();
            CurrentInput = new GameInput(-9999, -9999, -9999);
        }

        /// <summary>
        /// Queue an input to be processed by the engine.
        /// </summary>
        /// <param name="input">The input to queue into the engine.</param>
        public void QueueInput(GameInput input)
        {
            InputQueue.Enqueue(input);
        }

        /// <summary>
        /// Updates the engine and processes all inputs currently queued.
        /// </summary>
        public void UpdateEngine()
        {
            if (!IsInputQueued)
            {
                return;
            }

            IsInputUpdate = true;
            ProcessInputs();
        }

        /// <summary>
        /// Updates the engine with no input processing.
        /// </summary>
        /// <param name="time">The time to simulate hit logic at.</param>
        public void UpdateEngine(double time)
        {
            IsInputUpdate = false;
            bool noteUpdated;
            do
            {
                noteUpdated = UpdateHitLogic(time);
            } while (noteUpdated);
        }

        /// <summary>
        /// Loops through the input queue and processes each input. Invokes engine logic for each input.
        /// </summary>
        protected void ProcessInputs()
        {
            while (InputQueue.TryDequeue(out var input))
            {
                CurrentInput = input;
                IsInputUpdate = true;
                bool noteUpdated;
                do
                {
                    noteUpdated = UpdateHitLogic(input.Time);
                    IsInputUpdate = false;
                } while (noteUpdated);
            }
        }

        protected uint GetCurrentTick(double time)
        {
            return _syncTrack.TimeToTick(time);
        }

        public virtual void UpdateBot(double songTime)
        {
            IsInputUpdate = false;
            IsBotUpdate = true;
        }

        /// <summary>
        /// Executes engine logic with respect to the given time.
        /// </summary>
        /// <param name="time">The time in which to simulate hit logic at.</param>
        /// <returns>True if a note was updated (hit or missed). False if no changes.</returns>
        protected abstract bool UpdateHitLogic(double time);
    }

    public abstract class BaseEngine<TNoteType, TActionType, TEngineParams, TEngineStats, TEngineState> : BaseEngine
        where TNoteType : Note<TNoteType>
        where TActionType : unmanaged, Enum
        where TEngineParams : BaseEngineParameters
        where TEngineStats : BaseStats, new()
        where TEngineState : BaseEngineState, new()
    {
        protected const int POINTS_PER_NOTE = 50;
        protected const int POINTS_PER_BEAT = 25;

        protected const double STAR_POWER_PHRASE_AMOUNT = 0.25;

        public delegate void NoteHitEvent(int noteIndex, TNoteType note);

        public delegate void NoteMissedEvent(int noteIndex, TNoteType note);

        public delegate void StarPowerPhraseHitEvent(TNoteType note);

        public delegate void StarPowerPhraseMissEvent(TNoteType note);

        public delegate void StarPowerStatusEvent(bool active);

        public NoteHitEvent    OnNoteHit;
        public NoteMissedEvent OnNoteMissed;

        public StarPowerPhraseHitEvent  OnStarPowerPhraseHit;
        public StarPowerPhraseMissEvent OnStarPowerPhraseMissed;
        public StarPowerStatusEvent     OnStarPowerStatus;

        public readonly TEngineStats EngineStats;

        protected readonly InstrumentDifficulty<TNoteType> Chart;

        protected readonly List<TNoteType> Notes;
        protected readonly TEngineParams   EngineParameters;

        protected TEngineState State;

        protected BaseEngine(InstrumentDifficulty<TNoteType> chart, SyncTrack syncTrack,
            TEngineParams engineParameters) : base(syncTrack)
        {
            Chart = chart;
            Notes = Chart.Notes;

            EngineParameters = engineParameters;

            EngineStats = new TEngineStats();
            State = new TEngineState();

            EngineStats.ScoreMultiplier = 1;
        }

        /// <summary>
        /// Checks if the given note can be hit with the current input state.
        /// </summary>
        /// <param name="note">The Note to attempt to hit.</param>
        /// <returns>True if note can be hit. False otherwise.</returns>
        protected abstract bool CanNoteBeHit(TNoteType note);

        protected abstract bool HitNote(TNoteType note);

        protected abstract void MissNote(TNoteType note);

        protected abstract void AddScore(TNoteType note);

        protected abstract void UpdateMultiplier();

        protected virtual void StripStarPower(TNoteType note)
        {
            OnStarPowerPhraseMissed?.Invoke(note);
        }

        protected void AwardStarPower(TNoteType note)
        {
            EngineStats.StarPowerAmount += STAR_POWER_PHRASE_AMOUNT;
            if (EngineStats.StarPowerAmount >= 1)
            {
                EngineStats.StarPowerAmount = 1;
            }

            OnStarPowerPhraseHit?.Invoke(note);
        }

        protected void DepleteStarPower(double amount)
        {
            EngineStats.StarPowerAmount -= amount;
            if (EngineStats.StarPowerAmount <= 0)
            {
                EngineStats.StarPowerAmount = 0;
                EngineStats.IsStarPowerActive = false;
                OnStarPowerStatus?.Invoke(false);
            }
        }

        /// <summary>
        /// Resets the engine's state back to default and then processes the list of inputs up to the given time.
        /// </summary>
        /// <param name="time">Time to process up to.</param>
        /// <param name="inputs">List of inputs to execute against.</param>
        /// <returns>The input index that was processed up to.</returns>
        public virtual int ProcessUpToTime(double time, IList<GameInput> inputs)
        {
            State.Reset();

            foreach (var note in Notes)
            {
                note.SetHitState(false, true);
                note.SetMissState(false, true);
            }

            var inputIndex = 0;
            while (inputIndex < inputs.Count && inputs[inputIndex].Time <= time)
            {
                InputQueue.Enqueue(inputs[inputIndex]);
                inputIndex++;
            }

            ProcessInputs();

            return inputIndex;
        }

        /// <summary>
        /// Processes the list of inputs from the given start time to the given end time. Does not reset the engine's state.
        /// </summary>
        /// <param name="startTime">Time to begin processing from.</param>
        /// <param name="endTime">Time to process up to.</param>
        /// <param name="inputs">List of inputs to execute against.</param>
        public virtual void ProcessFromTimeToTime(double startTime, double endTime, IList<GameInput> inputs)
        {
            throw new NotImplementedException();
        }

        protected abstract int CalculateBaseScore();
    }
}