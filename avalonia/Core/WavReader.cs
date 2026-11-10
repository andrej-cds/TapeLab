using CDSMeasure.Core;

namespace CDSMeasure.Audio;

/// <summary>Prebere WAV (PCM 8/16/24/32 bit ali float) in ga analizira z jedrom PbCore (Mac/Linux, brez dodatnih knjižnic).</summary>
public static class PbFile
{
    public static PbCore Analyze(string path, bool stepsMode)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a WAV file");
        br.ReadInt32();
        if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAV file");
        int fmtTag = 1, ch = 2, rate = 44100, bits = 16;
        long dataPos = -1, dataLen = 0;
        while (fs.Position + 8 <= fs.Length)
        {
            string id = new string(br.ReadChars(4));
            int len = br.ReadInt32();
            long next = fs.Position + len + (len & 1);
            if (id == "fmt ")
            {
                fmtTag = br.ReadInt16(); ch = br.ReadInt16(); rate = br.ReadInt32(); br.ReadInt32(); br.ReadInt16(); bits = br.ReadInt16();
                if (fmtTag == 0xFFFE && len >= 40) { br.ReadInt16(); br.ReadInt16(); br.ReadInt32(); fmtTag = br.ReadInt16(); }
            }
            else if (id == "data") { dataPos = fs.Position; dataLen = len; break; }
            fs.Position = next;
        }
        if (dataPos < 0) throw new InvalidDataException("No audio data");
        if (ch < 1 || ch > 8) throw new InvalidDataException("Unsupported channel count");
        int bps = bits / 8;
        if (!(fmtTag == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32)) && !(fmtTag == 3 && bits == 32))
            throw new InvalidDataException($"Unsupported WAV format ({fmtTag}, {bits} bit)");
        int useCh = Math.Min(ch, 2);
        var core = new PbCore(rate, useCh) { StepsMode = stepsMode };
        fs.Position = dataPos;
        long frames = Math.Min(dataLen, fs.Length - dataPos) / (bps * ch);
        const int Block = 16384;
        var raw = new byte[Block * ch * bps];
        var outBuf = new float[Block * useCh];
        long done = 0;
        while (done < frames)
        {
            int nf = (int)Math.Min(Block, frames - done);
            int read = fs.Read(raw, 0, nf * ch * bps);
            nf = read / (ch * bps);
            if (nf <= 0) break;
            for (int i = 0; i < nf; i++)
                for (int c = 0; c < useCh; c++)
                {
                    int o = (i * ch + c) * bps;
                    float v = bits switch
                    {
                        8 => (raw[o] - 128) / 128f,
                        16 => BitConverter.ToInt16(raw, o) / 32768f,
                        24 => ((raw[o] | (raw[o + 1] << 8) | ((sbyte)raw[o + 2] << 16))) / 8388608f,
                        _ => fmtTag == 3 ? BitConverter.ToSingle(raw, o) : BitConverter.ToInt32(raw, o) / 2147483648f,
                    };
                    outBuf[i * useCh + c] = v;
                }
            core.Push(outBuf, nf, useCh);
            done += nf;
        }
        core.Flush();
        return core;
    }
}
