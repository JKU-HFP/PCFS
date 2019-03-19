using PIStage_Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using TimeTagger_Library;
using System.ComponentModel;
using System.Diagnostics;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace PCFS.Model
{
    public class PCFSScan
    {
        //Private fields
        private Action<string> _loggerCallback;

        private List<PCFSPoint> _PCFSPoints;
        private List<PCFSCurve> _PCFSCurves;

        private List<(byte ch0, byte ch1)> _corrConfig;
        private List<(long low, long high)> _binningList;

        private PI_GCS2_Stage _linearStage;
        private ITimeTagger _timeTagger;

        private BackgroundWorker _scanBgWorker;
        private Stopwatch _stopwatch;

        //#################################
        // P R O P E R T I E S
        //#################################
        //Data

        //Status
        public bool ScanPointsInitialized { get; private set; } = false;
        public bool DataAvailable { get; private set; } = false;
        public int RepetionsDone { get; private set; } = 0;

        //TimeTagger
        public byte chan0 { get; set; } = 0;
        public byte chan1 { get; set; } = 1;
        public long Offset { get; set; } = 0;
        public long TimeWindow { get; private set; } = 0;

        //Linear Stage
        public double SlowVelocity { get; set; } = 0.005;
        public double FastVelocity { get; set; } = 25.0;

        //Aquisition
        public double MinPosition { get; set; } = 0;
        public double MaxPosition { get; set; } = 300.0;
        public double NumSteps { get; set; } = 240;
        public double StepWidth { get; private set; } = 0.0;
        public double IntegrationTime { get; set; } = 10.0;
        public int NumRepetitions { get; set; } = 4;
        public string BinningListFilename { get; set; } = "";

        //#################################
        // E V E N T S
        //#################################

        public event EventHandler<PCFSCalculatedEventArgs> PCFSCalculated;
        private void OnPCFSCalculated(PCFSCalculatedEventArgs e)
        {
            PCFSCalculated?.Invoke(this, e);
        }


        public PCFSScan(Action<string> loggercallback)
        {  
            _loggerCallback = loggercallback;

            _PCFSPoints = new List<PCFSPoint> { };

            _linearStage = new PI_GCS2_Stage(_loggerCallback);
            _linearStage.Connect("C - 863");

            _timeTagger = new HydraHarpTagger(_loggerCallback);

            _scanBgWorker = new BackgroundWorker();
            _scanBgWorker.WorkerReportsProgress = true;
            _scanBgWorker.WorkerSupportsCancellation = true;
            _scanBgWorker.DoWork += DoScan;
            _scanBgWorker.ProgressChanged += ScanProgressChanged;
            _scanBgWorker.RunWorkerCompleted += ScanCompleted;

            _stopwatch = new Stopwatch();
        }

        public void InitializePCFSPoints()
        {
            if(!File.Exists(BinningListFilename))
            {
                WriteLog("Binning list file does not exist.");
            }
            _binningList = GetBinningList(BinningListFilename);
            
            StepWidth = (MaxPosition - MinPosition) / NumSteps;
            _corrConfig = new List<(byte ch0, byte ch1)> { ((byte)chan0, (byte)chan1) };
            TimeWindow = Math.Abs(_binningList.Max(p => p.high) - _binningList.Min(p => p.low));

            RepetionsDone = 0;
            DataAvailable = false;
     
            for (int i=0; i<NumSteps; i++)
            {
                BinningListHistogram hist = new BinningListHistogram(_corrConfig, _binningList, (ulong)TimeWindow);
                _PCFSPoints.Add(new PCFSPoint(hist, (ulong)TimeWindow)
                {
                    StagePosition = MinPosition + i * StepWidth,
                    NumScans = NumRepetitions,
                    OffsetCh1 = Offset
                });               
            }

            ScanPointsInitialized = true;
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

        public void StartScanAsync()
        {
            if(!ScanPointsInitialized)
            {
                WriteLog("Scanning not initialized. Aborting...");
                return;
            }
            
            if(_scanBgWorker.IsBusy)
            {
                WriteLog("Scanning in progress. Cannot start a new scan.");
                return;
            }

            DataAvailable = true;
            ScanPointsInitialized = false;
            _scanBgWorker.RunWorkerAsync();
        }

        private void DoScan(object sender, DoWorkEventArgs e)
        {
            while(RepetionsDone < NumRepetitions)
            {
                foreach(PCFSPoint pcfsPoint in _PCFSPoints)
                {
                    if (e.Cancel) return;

                    //Stop tagger and clear buffer
                    _timeTagger.StopCollectingTimeTags();
                    _timeTagger.ClearTimeTagBuffer();                    

                    //Move stage in fast velocity to position
                    _linearStage.SetVelocity(FastVelocity);
                    _linearStage.Move_Absolute(pcfsPoint.StagePosition);
                    _linearStage.WaitForPos();

                    //Start moving stage in slow velocity & Start collecting timetags
                    _linearStage.SetVelocity(SlowVelocity);
                    _linearStage.Move_Relative(SlowVelocity * IntegrationTime);
                    _stopwatch.Restart();
                    _timeTagger.StartCollectingTimeTagsAsync();

                    //Wait for stage to arrive at target position
                    _linearStage.WaitForPos();
                    _stopwatch.Stop();
                    _timeTagger.StopCollectingTimeTags();

                    //ASYNCHRONOUSLY PROCESS DATA
                    ProcessDataAsync(pcfsPoint, _timeTagger.GetAllTimeTags(), _stopwatch.ElapsedMilliseconds * 1000000000);

                    //Report progress
                    ScanProgressChangedEventArgs scanprogress = new ScanProgressChangedEventArgs();
                    _scanBgWorker.ReportProgress(0, scanprogress);
                }
            }
        }

        private async void ProcessDataAsync(PCFSPoint currPoint, List<TimeTags> tt, long totaltime)
        {
            await Task.Run(() =>
            {
                currPoint.AddMeasurement(tt, totaltime);
                CalculatePCFS();
            });

            OnPCFSCalculated(new PCFSCalculatedEventArgs(_PCFSPoints, _PCFSCurves));
        }
        
        private void CalculatePCFS()
        {
            //Get G2 Data
            int numBins = _binningList.Count;

            _PCFSCurves = new List<PCFSCurve> { };
            IEnumerable<PCFSPoint> relevantPoints = _PCFSPoints.Where(p => p.PerformedScans > 0);

            //Calculate energy scale
            double[] positions = relevantPoints.Select(p => p.StagePosition).ToArray();
            double eScaleFactor = 299792458.0 * 2 * Math.PI / (positions.Length * StepWidth);
            double[] energyScale = positions.Select(p => p * eScaleFactor).ToArray();

            for (int i=0; i<numBins; i++)
            {
                PCFSCurve curve = new PCFSCurve()
                {
                    Energy = energyScale,
                    G2 = relevantPoints.Select(p => p.HistogramYNorm[i]).ToArray(),
                    G2Err = relevantPoints.Select(p => p.HistogramYNormErr[i]).ToArray()
                };
                _PCFSCurves.Add(curve);         
            }

            //Calculate Fourier Transforms
            foreach(PCFSCurve curve in _PCFSCurves)
            {
                Complex32[] samples = curve.G2.Select(p => new Complex32((float)p, 0)).ToArray();
                Fourier.Inverse(samples,FourierOptions.NoScaling);
                curve.pE = samples.Select(p => (double)p.MagnitudeSquared).ToArray();

                double error_PE = curve.G2Err.Select(p => p * p).Sum();
                curve.PEErr = Enumerable.Repeat(error_PE, curve.pE.Length).ToArray();
            }

        }

        private void ScanProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            ScanProgressChangedEventArgs scanprogress = (ScanProgressChangedEventArgs) e.UserState;
        }

        private void ScanCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if(e.Cancelled)
            {
                WriteLog("Scan cancelled.");
            }
            else
            {
                WriteLog("Scan completed.");
            }
        }

        public void StopScan()
        {
            _scanBgWorker.CancelAsync();
        }

        private void WriteLog(string message)
        {
            _loggerCallback?.Invoke(message);
        }
    }

    public class ScanProgressChangedEventArgs
    {
        public ScanProgressChangedEventArgs()
        {

        }
    }

    public class PCFSCalculatedEventArgs
    {
        public List<PCFSPoint> PCFSPoints { get; }
        public List<PCFSCurve> PCFSCurves { get; }

        public PCFSCalculatedEventArgs(List<PCFSPoint> pcfspoints, List<PCFSCurve> pcfscurves)
        {
            PCFSPoints = pcfspoints;
            PCFSCurves = pcfscurves;
        }

    }

}
