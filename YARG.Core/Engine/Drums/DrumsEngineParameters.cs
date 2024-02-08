﻿using System.IO;
using YARG.Core.Utility;

namespace YARG.Core.Engine.Drums
{
    public class DrumsEngineParameters : BaseEngineParameters
    {
        public enum DrumMode : byte
        {
            NonProFourLane,
            ProFourLane,
            FiveLane
        }

        /// <summary>
        /// What mode the inputs should be processed in.
        /// </summary>
        public DrumMode Mode { get; private set; }

        public DrumsEngineParameters()
        {
        }

        public DrumsEngineParameters(HitWindowSettings hitWindow, int maxMultiplier, float[] starMultiplierThresholds,
            DrumMode mode)
            : base(hitWindow, maxMultiplier, starMultiplierThresholds)
        {
            Mode = mode;
        }

        public override void Serialize(IBinaryDataWriter writer)
        {
            base.Serialize(writer);

            writer.Write((byte) Mode);
        }

        public override void Deserialize(IBinaryDataReader reader, int version = 0)
        {
            base.Deserialize(reader, version);

            Mode = (DrumMode) reader.ReadByte();
        }
    }
}