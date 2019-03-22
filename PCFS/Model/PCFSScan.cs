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
using System.Globalization;

namespace PCFS.Model
{
    public class PCFSScan
    {
        //Private fields
        private Action<string> _loggerCallback;

        private List<DataPoint> _DataPoints;
        private List<PCFSCurve> _PCFSCurves;

        private List<(byte ch0, byte ch1)> _corrConfig;
        private List<(long low, long high)> _binningList;

        private ILinearStage _linearStage;
        private ITimeTagger _timeTagger;

        private BackgroundWorker _scanBgWorker;
        private Stopwatch _stopwatch;

        DirectoryInfo _dataPointsDirectory;

        //#################################
        // P R O P E R T I E S
        //#################################
        //Data

        //Status
        public bool ScanPointsInitialized { get; private set; } = false;
        public bool DataAvailable { get; private set; } = false;
        public bool ScanInProgress { get; private set; } = false;
        public int RepetionsDone { get; private set; } = 0;
        public DateTime ScanStartTime;

        //TimeTagger
        public byte chan0 { get; set; } = 0;
        public byte chan1 { get; set; } = 1;
        public long Offset { get; set; } = 0;
        public long TimeWindow { get; private set; } = 0;
        public int CountrateChan0 { get => _timeTagger.GetCountrate()[chan0]; }
        public int CountrateChan1 { get => _timeTagger.GetCountrate()[chan1]; }

        //Linear Stage
        public double SlowVelocity { get; set; } = 0.005;
        public double FastVelocity { get; set; } = 25.0;

        //Aquisition
        public double MinPosition { get; set; } = 0;
        public double MaxPosition { get; set; } = 300.0;
        public int NumSteps { get; set; } = 240;
        public double StepWidth { get; private set; } = 0.0;
        public double IntegrationTime { get; set; } = 10.0;
        public int NumRepetitions { get; set; } = 4;
        public string BinningListFilename { get; set; } = "";

        public int CurrentStep { get; private set; } = 0;
        public int Totalsteps
        {
            get { return NumRepetitions * NumSteps; }
        }


        //Static Getters
        public static TimeSpan GetEstimatedTime(int donesteps, int totalNumsteps, double integrationtime)
        {
            int timeSeconds = (int)((totalNumsteps - donesteps) * integrationtime * 1.125);
            return new TimeSpan(0, 0, timeSeconds);
        }
        

        //#################################
        // E V E N T S
        //#################################
        public event EventHandler<ScanInitializedEventArgs> ScanInitialized;
        private void OnScanInitialized(ScanInitializedEventArgs e)
        {
            ScanInitialized?.Invoke(this, e);
        }


        public event EventHandler<DataChangedEventArgs> DataChanged;
        private void OnDataChanged(DataChangedEventArgs e)
        {
            DataChanged?.Invoke(this, e);
        }

        public event EventHandler<ScanProgressChangedEventArgs> ScanProgressChanged;
        private void OnScanProgressChanged(ScanProgressChangedEventArgs e)
        {
            ScanProgressChanged?.Invoke(this, e);
        }
        

        public PCFSScan(Action<string> loggercallback)
        {  
            _loggerCallback = loggercallback;

            _DataPoints = new List<DataPoint> { };
            _PCFSCurves = new List<PCFSCurve>();

            //================ Simulations ================

            //_linearStage = new PI_GCS2_Stage(_loggerCallback);
            //_linearStage.Connect("C - 863");

            //_timeTagger = new HydraHarpTagger(_loggerCallback);

            _linearStage = new SimulatedLinearStage();

            _timeTagger = new SimulatedTagger(_loggerCallback)
            {
                PacketSize = 1000,
                FileName = @"E:\Dropbox\Dropbox\Coding\EQKD\Testfiles\RL_correct.dat",
                PacketDelayTimeMilliSeonds = 500
            };
            
            //==============================================

            _scanBgWorker = new BackgroundWorker();
            _scanBgWorker.WorkerReportsProgress = true;
            _scanBgWorker.WorkerSupportsCancellation = true;
            _scanBgWorker.DoWork += DoScan;
            _scanBgWorker.ProgressChanged += BgwScanProgressChanged;
            _scanBgWorker.RunWorkerCompleted += ScanCompleted;

            _stopwatch = new Stopwatch();
        }

        public void InitializePCFSPoints()
        {
            if(!File.Exists(BinningListFilename))
            {
                WriteLog("Binning list file does not exist.");
                return;
            }
            _binningList = GetBinningList(BinningListFilename);
            
            StepWidth = (MaxPosition - MinPosition) / NumSteps;
            _corrConfig = new List<(byte ch0, byte ch1)> { ((byte)chan0, (byte)chan1) };
            TimeWindow = Math.Abs(_binningList.Max(p => p.high) - _binningList.Min(p => p.low));

            RepetionsDone = 0;
            DataAvailable = false;

            _DataPoints.Clear();
            for (int i=0; i<NumSteps; i++)
            {
                BinningListHistogram hist = new BinningListHistogram(_corrConfig, _binningList, (ulong)TimeWindow);
                _DataPoints.Add(new DataPoint(hist, (ulong)TimeWindow)
                {
                    Index = i,
                    StagePosition = MinPosition + i * StepWidth,
                    NumScans = NumRepetitions,
                    OffsetCh1 = Offset
                });               
            }

            //Initialize PCFS Curves

            _PCFSCurves.Clear();
            foreach(var bin in _binningList)
            {
                _PCFSCurves.Add(new PCFSCurve() { Binning = bin });
            }

            OnScanInitialized(new ScanInitializedEventArgs(_DataPoints,_PCFSCurves));

            ScanPointsInitialized = true;
        }

        private List<(long low, long high)> GetBinningList(string filename)
        {
            List<(long low, long high)> bins = new List<(long low, long high)> { };             
                        
            string[] lines = File.ReadAllLines(filename);
            
            foreach(string line in lines)
            {
                string[] tmp_string = line.Split(',');
                          
                if (tmp_string.Length == 2)
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
            ScanInProgress = true;
            CurrentStep = 0;

            ScanStartTime = DateTime.Now;
            _dataPointsDirectory = Directory.CreateDirectory("DataPoints_"+ScanStartTime.ToString("yyyy_MM_dd_HH_mm"));

            WriteLog("Start scanning.");

            _scanBgWorker.RunWorkerAsync();
        }

        private void DoScan(object sender, DoWorkEventArgs e)
        {
            while(RepetionsDone < NumRepetitions)
            {
                foreach(DataPoint pcfsPoint in _DataPoints)
                {
                    //ReportProgress
                    _scanBgWorker.ReportProgress(0,new ScanProgressChangedEventArgs()
                    {
                        CurrentStep = CurrentStep,
                        TotalSteps = Totalsteps,
                        StagePosition = pcfsPoint.StagePosition,
                        RemainingTime = GetEstimatedTime(CurrentStep, Totalsteps, IntegrationTime)
                    });

                    if (_scanBgWorker.CancellationPending) return;

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
                    _timeTagger.BackupFilename = "TTTRBackup_Step"+CurrentStep.ToString()+".ht2";
                    _timeTagger.StartCollectingTimeTagsAsync();

                    //Wait for stage to arrive at target position
                    _linearStage.WaitForPos();
                    _stopwatch.Stop();
                    _timeTagger.StopCollectingTimeTags();

                    //ASYNCHRONOUSLY PROCESS DATA
                    ProcessDataAsync(pcfsPoint, _timeTagger.GetAllTimeTags(), _stopwatch.ElapsedMilliseconds * 1000000000);

                    CurrentStep++;
                }
                RepetionsDone++;
            }
        }

        private async void ProcessDataAsync(DataPoint currPoint, List<TimeTags> tt, long totaltime)
        {
            await Task.Run(() =>
            {
                currPoint.AddMeasurement(tt, totaltime);
                WriteDataPoint(currPoint);
                CalculatePCFS();
            });

            OnDataChanged(new DataChangedEventArgs(_DataPoints, _PCFSCurves));
        }

        private void WriteDataPoint(DataPoint dp)
        {
            int numLines = dp.HistogramX.Length;
            string fF = "F3";
            CultureInfo cult = CultureInfo.InvariantCulture;

            string[] outputLines = new string[numLines + 1];


            outputLines[0] = "Tau \t Coinc \t CoincErr \t G2 \t G2Err";
            for(int i=0; i<numLines; i++)
            {
                outputLines[i + 1] = dp.HistogramX[i].ToString() + "\t" + dp.HistogramY[i].ToString() + "\t" + dp.HistogramYErr[i].ToString(fF, cult) + "\t" +
                                     dp.HistogramYNorm[i].ToString(fF, cult) + "\t" + dp.HistogramYNormErr[i].ToString(fF, cult);   
            }

            string filename = Path.Combine(_dataPointsDirectory.ToString(), "Point_" + dp.Index +".dat");
            File.WriteAllLines(filename, outputLines);
        }

        private void CalculatePCFS()
        {
            //Get G2 Data
            IEnumerable<DataPoint> relevantPoints = _DataPoints.Where(p => p.PerformedScans > 0);

            //Calculate energy scale
            double[] positions = relevantPoints.Select(p => p.StagePosition).ToArray();
            double eScaleFactor = 1239.84 / (positions.Length * 2* StepWidth); //10^6 * 2 pi c hbar / eCharge [ueV]
            int[] posIndices = Enumerable.Range(0, relevantPoints.Count()).ToArray();
            double[] energyScale = posIndices.Select(p => p * eScaleFactor).ToArray();        

            int numBins = _binningList.Count;
            for (int i=0; i<numBins; i++)
            {
                _PCFSCurves[i].positions = positions;
                _PCFSCurves[i].Energy = energyScale;
                _PCFSCurves[i].G2 = relevantPoints.Select(p => p.HistogramYNorm[i]).ToArray();
                _PCFSCurves[i].G2Err = relevantPoints.Select(p => p.HistogramYNormErr[i]).ToArray();  
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

        private void BgwScanProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            ScanProgressChangedEventArgs scanprogress = (ScanProgressChangedEventArgs) e.UserState;
            OnScanProgressChanged(scanprogress);
        }

        private void ScanCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            ScanInProgress = false;

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
           if(_scanBgWorker.IsBusy) _scanBgWorker.CancelAsync();
        }

        private void WriteLog(string message)
        {
            _loggerCallback?.Invoke(message);
        }
    }


    public class  ScanInitializedEventArgs
    {
        public List<PCFSCurve> PCFSCurves { get; }
        public List<DataPoint> DataPoints { get; }

        public ScanInitializedEventArgs(List<DataPoint> datapoints, List<PCFSCurve> pcfscurves)
        {
            DataPoints = datapoints;
            PCFSCurves = pcfscurves;
        }
    }

    public class ScanProgressChangedEventArgs
    {
        public int CurrentStep { get; set; } = 0;
        public int TotalSteps { get; set; } = 0;
        public double StagePosition { get; set; } = 0;
        public TimeSpan RemainingTime { get; set; } = new TimeSpan(0);

        public ScanProgressChangedEventArgs()
        {

        }
    }

    public class DataChangedEventArgs
    {
        public List<DataPoint> DataPoints { get; }
        public List<PCFSCurve> PCFSCurves { get; }

        public DataChangedEventArgs(List<DataPoint> pcfspoints, List<PCFSCurve> pcfscurves)
        {
            DataPoints = pcfspoints;
            PCFSCurves = pcfscurves;
        }

    }

}
