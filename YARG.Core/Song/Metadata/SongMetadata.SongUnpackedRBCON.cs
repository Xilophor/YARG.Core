﻿using System;
using System.IO;
using YARG.Core.Song.Deserialization;
using System.Buffers.Binary;
using System.Collections.Generic;
using YARG.Core.Song.Cache;

#nullable enable
namespace YARG.Core.Song
{
    public sealed partial class SongMetadata
    {
        [Serializable]
        public sealed class RBUnpackedCONMetadata : IRBCONMetadata
        {
            private readonly AbridgedFileInfo? DTA;
            private readonly RBCONSubMetadata _metadata;
            private readonly AbridgedFileInfo Midi;

            public RBCONSubMetadata SharedMetadata => _metadata;

            public DateTime MidiLastWrite => Midi.LastWriteTime;

            public RBUnpackedCONMetadata(string folder, AbridgedFileInfo dta, RBCONSubMetadata metadata, string nodeName)
            {
                _metadata = metadata;
                DTA = dta;
                folder = Path.Combine(folder, nodeName);
                string file = Path.Combine(folder, nodeName);
                string midiPath = file + ".mid";

                FileInfo midiInfo = new(midiPath);
                if (!midiInfo.Exists)
                    throw new Exception($"Required midi file '{midiPath}' was not located");
                Midi = midiInfo;

                FileInfo mogg = new(file + ".yarg_mogg");
                metadata.Mogg = mogg.Exists ? mogg : new AbridgedFileInfo(file + ".mogg");

                file = Path.Combine(folder, "gen", nodeName);
                metadata.Milo = new(file + ".milo_xbox");
                metadata.Image = new(file + "_keep.png_xbox");
                metadata.Directory = Path.GetDirectoryName(midiPath)!;
            }

            public RBUnpackedCONMetadata(AbridgedFileInfo dta, AbridgedFileInfo midi, AbridgedFileInfo? moggInfo, AbridgedFileInfo? updateInfo, YARGBinaryReader reader)
            {
                DTA = dta;
                Midi = midi;

                string str = reader.ReadLEBString();
                AbridgedFileInfo? miloInfo = str.Length > 0 ? new(str) : null;

                str = reader.ReadLEBString();
                AbridgedFileInfo? imageInfo = str.Length > 0 ? new(str) : null;

                _metadata = new(reader)
                {
                    Mogg = moggInfo,
                    UpdateMidi = updateInfo,
                    Milo = miloInfo,
                    Image = imageInfo,
                };
            }

            public void Serialize(BinaryWriter writer)
            {
                writer.Write(Midi.FullName);
                writer.Write(Midi.LastWriteTime.ToBinary());

                writer.Write(_metadata.Mogg!.FullName);
                writer.Write(_metadata.Mogg.LastWriteTime.ToBinary());

                if (_metadata.UpdateMidi != null)
                {
                    writer.Write(true);
                    writer.Write(_metadata.UpdateMidi.FullName);
                    writer.Write(_metadata.UpdateMidi.LastWriteTime.ToBinary());
                }
                else
                    writer.Write(false);

                if (_metadata.Milo != null)
                    writer.Write(_metadata.Milo.FullName);
                else
                    writer.Write(string.Empty);

                if (_metadata.Image != null)
                    writer.Write(_metadata.Image.FullName);
                else
                    writer.Write(string.Empty);

                _metadata.Serialize(writer);
            }

            public byte[]? LoadMidiFile()
            {
                if (!Midi.IsStillValid())
                    return null;
                return File.ReadAllBytes(Midi.FullName);
            }

            public byte[]? LoadMoggFile()
            {
                using var stream = _metadata.GetMoggStream();
                if (stream == null)
                    return null;
                return stream.ReadBytes((int) stream.Length);
            }

            public byte[]? LoadMiloFile()
            {
                if (_metadata.Milo == null || !File.Exists(_metadata.Milo.FullName))
                    return null;
                return File.ReadAllBytes(_metadata.Milo.FullName);
            }

            public byte[]? LoadImgFile()
            {
                if (_metadata.Image == null || !File.Exists(_metadata.Image.FullName))
                    return null;
                return File.ReadAllBytes(_metadata.Image.FullName);
            }

            public bool IsMoggValid()
            {
                using var stream = _metadata.GetMoggStream();
                if (stream == null)
                    return false;

                int version = stream.ReadInt32LE();
                return version == 0x0A || version == 0xf0;
            }
        }

        private SongMetadata(string folder, AbridgedFileInfo dta, string nodeName, YARGDTAReader reader)
        {
            RBCONSubMetadata rbMetadata = new();

            ParseDTA(nodeName, rbMetadata, reader);
            _rbData = new RBUnpackedCONMetadata(folder, dta, rbMetadata, nodeName);
            _directory = rbMetadata.Directory;
        }

        public static (ScanResult, SongMetadata?) FromUnpackedRBCON(string folder, AbridgedFileInfo dta, string nodeName, YARGDTAReader reader, Dictionary<string, List<(string, YARGDTAReader)>> updates, Dictionary<string, (YARGDTAReader?, IRBProUpgrade)> upgrades)
        {
            try
            {
                SongMetadata song = new(folder, dta, nodeName, reader);
                song.ApplyRBCONUpdates(nodeName, updates);
                song.ApplyRBProUpgrade(nodeName, upgrades);

                var result = song.ParseRBCONMidi();
                if (result != ScanResult.Success)
                    return (result, null);
                return (result, song);
            }
            catch (Exception ex)
            {
                YargTrace.LogError(ex.Message);
                return (ScanResult.DTAError, null);
            }
        }

        public static SongMetadata? UnpackedRBCONFromCache(AbridgedFileInfo dta, string nodeName, Dictionary<string, (YARGDTAReader?, IRBProUpgrade)> upgrades, YARGBinaryReader reader, CategoryCacheStrings strings)
        {
            FileInfo midiInfo = new(reader.ReadLEBString());
            if (!midiInfo.Exists || midiInfo.LastWriteTime != DateTime.FromBinary(reader.ReadInt64()))
                return null;

            FileInfo moggInfo = new(reader.ReadLEBString());
            if (!moggInfo.Exists || moggInfo.LastWriteTime != DateTime.FromBinary(reader.ReadInt64()))
                return null;

            FileInfo? updateInfo = null;
            if (reader.ReadBoolean())
            {
                updateInfo = new FileInfo(reader.ReadLEBString());
                if (!updateInfo.Exists || updateInfo.LastWriteTime != DateTime.FromBinary(reader.ReadInt64()))
                    return null;
            }

            RBUnpackedCONMetadata packedMeta = new(dta, midiInfo, moggInfo, updateInfo, reader);
            if (upgrades.TryGetValue(nodeName, out var upgrade))
                packedMeta.SharedMetadata.Upgrade = upgrade.Item2;
            return new SongMetadata(packedMeta, reader, strings);
        }

        public static SongMetadata UnpackedRBCONFromCache_Quick(AbridgedFileInfo dta, string nodeName, Dictionary<string, (YARGDTAReader?, IRBProUpgrade)> upgrades, YARGBinaryReader reader, CategoryCacheStrings strings)
        {
            string filename = reader.ReadLEBString();
            var lastWrite = DateTime.FromBinary(reader.ReadInt64());

            AbridgedFileInfo midiInfo = new(filename, lastWrite);

            filename = reader.ReadLEBString();
            lastWrite = DateTime.FromBinary(reader.ReadInt64());

            AbridgedFileInfo moggInfo = new(filename, lastWrite);

            AbridgedFileInfo? updateInfo = null;
            if (reader.ReadBoolean())
            {
                filename = reader.ReadLEBString();
                lastWrite = DateTime.FromBinary(reader.ReadInt64());
                updateInfo = new(filename, lastWrite);
            }

            RBUnpackedCONMetadata packedMeta = new(dta, midiInfo, moggInfo, updateInfo, reader);
            if (upgrades.TryGetValue(nodeName, out var upgrade))
                packedMeta.SharedMetadata.Upgrade = upgrade.Item2;
            return new SongMetadata(packedMeta, reader, strings);
        }
    }
}