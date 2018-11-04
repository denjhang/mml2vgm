﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core
{
    public class YM2612 : ClsOPN
    {
        protected int[][] _FNumTbl = new int[1][] {
            new int[13]
            //{
            ////   c    c+     d    d+     e     f    f+     g    g+     a    a+     b    >c
            // 0x289,0x2af,0x2d8,0x303,0x331,0x362,0x395,0x3cc,0x405,0x443,0x484,0x4c8,0x289*2
            //}
        };
        public const string FMF_NUM = "FMF-NUM";


        public YM2612(ClsVgm parent, int chipID, string initialPartName, string stPath, bool isSecondary) : base(parent, chipID, initialPartName, stPath, isSecondary)
        {
            _chipType = enmChipType.YM2612;
            _Name = "YM2612";
            _ShortName = "OPN2";
            _ChMax = 9;
            _canUsePcm = true;
            _canUsePI = false;
            FNumTbl = _FNumTbl;
            IsSecondary = isSecondary;

            Frequency = 7670454;

            Dictionary<string, List<double>> dic = MakeFNumTbl();
            if (dic != null)
            {
                int c = 0;
                foreach (double v in dic["FNUM_00"])
                {
                    FNumTbl[0][c++] = (int)v;
                    if (c == FNumTbl[0].Length) break;
                }
                FNumTbl[0][FNumTbl[0].Length - 1] = FNumTbl[0][0] * 2;
            }

            Ch = new ClsChannel[ChMax];
            SetPartToCh(Ch, initialPartName);
            foreach (ClsChannel ch in Ch)
            {
                ch.Type = enmChannelType.FMOPN;
                ch.isSecondary = chipID == 1;
            }

            Ch[2].Type = enmChannelType.FMOPNex;
            Ch[5].Type = enmChannelType.FMPCM;
            Ch[6].Type = enmChannelType.FMOPNex;
            Ch[7].Type = enmChannelType.FMOPNex;
            Ch[8].Type = enmChannelType.FMOPNex;

            pcmDataInfo = new clsPcmDataInfo[] { new clsPcmDataInfo() };
            pcmDataInfo[0].totalBuf = new byte[7] { 0x67, 0x66, 0x00, 0x00, 0x00, 0x00, 0x00 };
            pcmDataInfo[0].totalBufPtr = 0L;
            pcmDataInfo[0].use = false;

        }

        public override void InitPart(ref partWork pw)
        {
            pw.slots = (byte)(((pw.Type == enmChannelType.FMOPN || pw.Type == enmChannelType.FMPCM) || pw.ch == 2) ? 0xf : 0x0);
            pw.volume = 127;
            pw.MaxVolume = 127;
            pw.port0 = (byte)(0x2 | (pw.isSecondary ? 0xa0 : 0x50));
            pw.port1 = (byte)(0x3 | (pw.isSecondary ? 0xa0 : 0x50));
        }

        public override void InitChip()
        {
            if (!use) return;

            OutOPNSetHardLfo(lstPartWork[0], false, 0);
            OutOPNSetCh3SpecialMode(lstPartWork[0], false);
            OutSetCh6PCMMode(lstPartWork[0], false);

            OutFmAllKeyOff();

            foreach (partWork pw in lstPartWork)
            {
                if (pw.ch == 0)
                {
                    pw.hardLfoSw = false;
                    pw.hardLfoNum = 0;
                }

                if (pw.ch < 6)
                {
                    pw.pan.val = 3;
                    pw.ams = 0;
                    pw.fms = 0;
                    if (!pw.dataEnd) OutOPNSetPanAMSPMS(pw, 3, 0, 0);
                }
            }

            if (ChipID != 0) parent.dat[0x2f] |= 0x40;

        }


        public void OutSetCh6PCMMode(partWork pw, bool sw)
        {
            parent.OutData(
                pw.port0
                , 0x2b
                , (byte)((sw ? 0x80 : 0))
                );
        }

        public void GetPcmNote(partWork pw)
        {
            int shift = pw.shift + pw.keyShift;
            int o = pw.octaveNow;//
            int n = Const.NOTE.IndexOf(pw.noteCmd) + shift;
            if (n >= 0)
            {
                o += n / 12;
                o = Common.CheckRange(o, 1, 8);
                n %= 12;
            }
            else
            {
                o += n / 12 - 1;
                o = Common.CheckRange(o, 1, 8);
                n %= 12;
                if (n < 0) { n += 12; }
            }

            pw.pcmOctave = o;
            pw.pcmNote = n;
        }


        public override void SetFNum(partWork pw)
        {
            SetFmFNum(pw);
        }

        public override void SetKeyOn(partWork pw)
        {
            OutFmKeyOn(pw);
        }

        public override void SetKeyOff(partWork pw)
        {
            OutFmKeyOff(pw);
        }

        public override void StorePcm(Dictionary<int, clsPcm> newDic, KeyValuePair<int, clsPcm> v, byte[] buf, bool is16bit, int samplerate, params object[] option)
        {
            clsPcmDataInfo pi = pcmDataInfo[0];

            try
            {
                long size = buf.Length;
                if (newDic.ContainsKey(v.Key))
                {
                    newDic.Remove(v.Key);
                }
                newDic.Add(
                    v.Key
                    , new clsPcm(
                        v.Value.num
                        , v.Value.seqNum
                        , v.Value.chip
                        , false
                        , v.Value.fileName
                        , v.Value.freq != -1 ? v.Value.freq : samplerate
                        , v.Value.vol
                        , pi.totalBufPtr
                        , pi.totalBufPtr + size - 1
                        , size
                        , -1
                        , is16bit
                        , samplerate));
                pi.totalBufPtr += size;

                byte[] newBuf = new byte[pi.totalBuf.Length + buf.Length];
                Array.Copy(pi.totalBuf, newBuf, pi.totalBuf.Length);
                Array.Copy(buf, 0, newBuf, pi.totalBuf.Length, buf.Length);

                pi.totalBuf = newBuf;
                Common.SetUInt32bit31(pi.totalBuf, 3, (UInt32)(pi.totalBuf.Length - 7), IsSecondary);
                pi.use = true;
                pcmDataEasy = pi.use ? pi.totalBuf : null;
            }
            catch
            {
                pi.use = false;
            }

        }

        public override void StorePcmRawData(clsPcmDatSeq pds, byte[] buf, bool isRaw, bool is16bit, int samplerate, params object[] option)
        {
            msgBox.setWrnMsg(msg.get("E20004"));
        }


        public override void CmdY(partWork pw, MML mml)
        {
            base.CmdY(pw, mml);

            if (mml.args[0] is string) return;

            byte adr = (byte)mml.args[0];
            byte dat = (byte)mml.args[1];
            parent.OutData((pw.ch > 2 && pw.ch < 6) ? pw.port1 : pw.port0, adr, dat);
        }

        public override void CmdMPMS(partWork pw, MML mml)
        {

            int n = (int)mml.args[1];
            n = Common.CheckRange(n, 0, 3);
            pw.pms = n;
            ((ClsOPN)pw.chip).OutOPNSetPanAMSPMS(
                pw
                , (int)pw.pan.val
                , pw.ams
                , pw.pms);
        }

        public override void CmdMAMS(partWork pw, MML mml)
        {

            int n = (int)mml.args[1];
            n = Common.CheckRange(n, 0, 7);
            pw.ams = n;
            ((ClsOPN)pw.chip).OutOPNSetPanAMSPMS(
                pw
                , (int)pw.pan.val
                , pw.ams
                , pw.pms);
        }

        public override void CmdLfo(partWork pw, MML mml)
        {
            base.CmdLfo(pw, mml);

            int c = (char)mml.args[0] - 'P';
            if (pw.lfo[c].type == eLfoType.Hardware)
            {
                if (pw.lfo[c].param.Count < 4)
                {
                    msgBox.setErrMsg(msg.get("E20000"), pw.getSrcFn(), pw.getLineNumber());
                    return;
                }
                if (pw.lfo[c].param.Count > 5)
                {
                    msgBox.setErrMsg(msg.get("E20001"), pw.getSrcFn(), pw.getLineNumber());
                    return;
                }

                pw.lfo[c].param[0] = Common.CheckRange(pw.lfo[c].param[0], 0, (int)parent.info.clockCount);//Delay(無視)
                pw.lfo[c].param[1] = Common.CheckRange(pw.lfo[c].param[1], 0, 7);//Freq
                pw.lfo[c].param[2] = Common.CheckRange(pw.lfo[c].param[2], 0, 7);//PMS
                pw.lfo[c].param[3] = Common.CheckRange(pw.lfo[c].param[3], 0, 3);//AMS
                if (pw.lfo[c].param.Count == 5)
                {
                    pw.lfo[c].param[4] = Common.CheckRange(pw.lfo[c].param[4], 0, 1); //Switch
                }
                else
                {
                    pw.lfo[c].param.Add(1);
                }
            }
        }

        public override void CmdLfoSwitch(partWork pw, MML mml)
        {
            base.CmdLfoSwitch(pw, mml);

            int c = (char)mml.args[0] - 'P';
            int n = (int)mml.args[1];
            if (pw.lfo[c].type == eLfoType.Hardware)
            {
                if (pw.lfo[c].param[4] == 0)
                {
                    ((ClsOPN)pw.chip).OutOPNSetHardLfo(pw, (n == 0) ? false : true, pw.lfo[c].param[1]);
                }
                else
                {
                    ((ClsOPN)pw.chip).OutOPNSetHardLfo(pw, false, pw.lfo[c].param[1]);
                }
            }
        }

        public override void CmdPan(partWork pw, MML mml)
        {
            int n = (int)mml.args[0];

            //強制的にモノラルにする
            if (parent.info.monoPart != null 
                && parent.info.monoPart.Contains(Ch[5].Name))
            {
                n = 3;
            }

            n = Common.CheckRange(n, 1, 3);
            pw.pan.val = n;
            ((ClsOPN)pw.chip).OutOPNSetPanAMSPMS(pw, n, pw.ams, pw.fms);
        }

        public override void CmdMode(partWork pw, MML mml)
        {
            int n = (int)mml.args[0];
            if (pw.Type == enmChannelType.FMPCM)
            {
                n = Common.CheckRange(n, 0, 1);
                pw.pcm = (n == 1);
                pw.freq = -1;//freqをリセット
                pw.instrument = -1;
                ((YM2612)(pw.chip)).OutSetCh6PCMMode(pw, pw.pcm);

                return;
            }

            base.CmdMode(pw, mml);

        }

        public override void CmdInstrument(partWork pw, MML mml)
        {
            char type = (char)mml.args[0];
            int n = (int)mml.args[1];

            if (type == 'N')
            {
                if (pw.Type == enmChannelType.FMOPNex)
                {
                    pw.instrument = n;
                    lstPartWork[2].instrument = n;
                    lstPartWork[6].instrument = n;
                    lstPartWork[7].instrument = n;
                    lstPartWork[8].instrument = n;
                    OutFmSetInstrument(pw, n, pw.volume);
                    return;
                }

                if (pw.pcm)
                {
                    pw.instrument = n;
                    if (!parent.instPCM.ContainsKey(n))
                    {
                        msgBox.setErrMsg(string.Format(msg.get("E20002"), n), pw.getSrcFn(), pw.getLineNumber());
                    }
                    else
                    {
                        if (parent.instPCM[n].chip != enmChipType.YM2612)
                        {
                            msgBox.setErrMsg(string.Format(msg.get("E20003"), n), pw.getSrcFn(), pw.getLineNumber());
                        }
                    }
                    return;
                }

            }

            base.CmdInstrument(pw, mml);
        }

        public override void SetLfoAtKeyOn(partWork pw)
        {
            for (int lfo = 0; lfo < 4; lfo++)
            {
                clsLfo pl = pw.lfo[lfo];
                if (!pl.sw)
                    continue;

                if (pl.param[5] != 1)
                    continue;

                pl.isEnd = false;
                pl.value = (pl.param[0] == 0) ? pl.param[6] : 0;//ディレイ中は振幅補正は適用されない
                pl.waitCounter = pl.param[0];
                pl.direction = pl.param[2] < 0 ? -1 : 1;

                if (pl.type == eLfoType.Vibrato)
                {
                    SetFmFNum(pw);

                }

                if (pl.type == eLfoType.Tremolo)
                {
                    pw.beforeVolume = -1;
                    SetFmVolume(pw);

                }

            }
        }


        public override string DispRegion(clsPcm pcm)
        {
            return string.Format("{0,-10} {1,-7} {2,-5:D3} N/A  ${3,-7:X6} ${4,-7:X6} N/A      ${5,-7:X6}  NONE {6}\r\n"
                , Name
                , pcm.isSecondary ? "SEC" : "PRI"
                , pcm.num
                , pcm.stAdr & 0xffffff
                , pcm.edAdr & 0xffffff
                , pcm.size
                , pcm.status.ToString()
                );
        }

    }
}