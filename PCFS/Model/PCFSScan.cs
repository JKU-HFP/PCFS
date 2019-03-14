using PIStage_Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TimeTagger_Library;

namespace PCFS.Model
{
    public class PCFSScan
    {
        //Private fields
        private Action<string> _loggerCallback;

        private List<PCFSPoint> _PCFSPoints;
        private List<(byte ch0, byte ch1)> _corrConfig;
        private List<(long low, long high)> _binningList;

        private PI_GCS2_Stage _linearStage;
        private ITimeTagger _timeTagger;

        //Properties
        public byte chan0 { get; set; } = 0;
        public byte chan1 { get; set; } = 1;
        public long TimeWindow { get; private set; } = 0;

        public PCFSScan()
        {
            _PCFSPoints = new List<PCFSPoint> { };

            _linearStage = new PI_GCS2_Stage(_loggerCallback);
            _linearStage.Connect("C - 863");

            _timeTagger = new HydraHarpTagger(_loggerCallback);
        }
        
        public void InitializePCFSPoints(double StageMin, double StageMax, int NumPoints, int Repetitions, string BinninglistFilename)
        {
            _binningList = GetBinningList(BinninglistFilename);
            
            double StepWidth = (StageMax - StageMin) / NumPoints;

            TimeWindow = Math.Abs(_binningList.Max(p => p.high) - _binningList.Min(p => p.low));

            for (int i=0; i<NumPoints; i++)
            {
                BinningListHistogram hist = new BinningListHistogram(_corrConfig, _binningList, TimeWindow);
                _PCFSPoints.Add(new PCFSPoint(hist, (ulong)TimeWindow)
                {
                    StagePosition = StageMin + i * StepWidth,
                    NumScans = Repetitions
                });               
            }

        }

        private List<(long low, long high)> GetBinningList(string filename)
        {
            List<(long low, long high)> bins = new List<(long low, long high)> { };
            string[] lines = File.ReadAllLines(filename);
            
            foreach(string line in lines)
            {
                string[] tmp_string = line.Split(',');
                          
                if (tmp_string.Length != 2)
                {
                    if(long.TryParse(tmp_string[0], out long lower) && long.TryParse(tmp_string[1], out long higher))
                    {
                        bins.Add((lower, higher));
                    }          
                    else
                    {
                        WriteLog("Invalid timebin in binning file.");
                        return new List<(long low, long high)> { };
                    }
                }
                else
                {
                    WriteLog("Wrong binning mask file format");
                    return new List<(long low, long high)> { };
                }
            }

            return bins;
        }

        private void WriteLog(string message)
        {
            _loggerCallback?.Invoke(message);
        }
    }
}
