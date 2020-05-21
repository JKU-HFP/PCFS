using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using TimeTagger_Library;
using TimeTagger_Library.TimeTagger;
using TimeTagger_Library.Correlation;
using System.ComponentModel;
using System.Diagnostics;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using System.Globalization;
using System.Text.RegularExpressions;
using Stage_Library;
using Stage_Library.PI;

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
   
        DirectoryInfo _PCFSDataDirectoryInfo;
        string _PCFSDataDirectory = "";

        double _currentPosition = 0.0;

        private object _processesLock = new object();

        //#################################
        // P R O P E R T I E S
        //#################################
        //Data

        //Status
        public bool ScanPointsInitialized { get; private set; } = false;
        public bool DataAvailable { get; private set; } = false;
        public bool ScanInProgress { get; private set; } = false;
        public int RepetitionsDone { get; private set; } = 0;
        public DateTime StartScanTime;
        public TimeSpan TotalScanTime;

        //TimeTagger
        public byte chan0 { get; set; } = 0;
        public byte chan1 { get; set; } = 1;
        public long Offset { get; set; } = 0;
        public int PacketSize { get; set; } = 5000;
        public long TimeWindow { get; private set; } = 0;

        public string SimulationFilesFolder { get; private set; } = "";

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
        public bool RenormalizeG2 { get; set; } = true;
        public string BinningListFile { get; set; } = "";
        public string BackupDirectory { get; set; } = "";
        public bool BackupTTTRData { get; set; } = false;
        public int RenormalizePercent { get; set; } = 20;

        public int CurrentStep { get; private set; } = 0;
        public int ProcessedSteps { get; private set; } = 0;
        public int Totalsteps
        {
            get { return NumRepetitions * NumSteps; }
        }


        //Getters
        public static TimeSpan GetEstimatedTime(int donesteps, int totalNumsteps, double integrationtime)
        {
            int timeSeconds = (int)((totalNumsteps - donesteps) * integrationtime * 1.125);
            return new TimeSpan(0, 0, timeSeconds);
        }
       
        public (int counts0, int counts1) GetCountrates()
        {
            List<int> tmp_counts = _timeTagger.GetCountrate();
            (int counts0, int counts1) counts = (0, 0);

            counts.counts0 = chan0 < tmp_counts.Count ? tmp_counts[chan0] : 0;
            counts.counts1 = chan1 < tmp_counts.Count ? tmp_counts[chan1] : 0;

            return counts;
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
        

        //#############################################
        //  C O N S T R U C T O R 
        //#############################################
        public PCFSScan(Action<string> loggercallback, string simfilefolder="")
        {  
            _loggerCallback = loggercallback;

            _DataPoints = new List<DataPoint> { };
            _PCFSCurves = new List<PCFSCurve>();

            SimulationFilesFolder = simfilefolder;

            //Simulation with SimulatedTimeTagger
            if(!string.IsNullOrEmpty(simfilefolder))
            {              
                var simStage = new SimulatedLinearStage(_loggerCallback);
                simStage.Connect("");
                _linearStage = simStage;

                _timeTagger = new SimulatedTagger(_loggerCallback)
                {
                    PacketSize = PacketSize                 
                };
                _timeTagger.Connect();
            }
            //Real measurement with TimeTagger
            else
            {
                var linStage = new PI_GCS2_Stage(_loggerCallback);             
                linStage.Connect("C-863");
                _linearStage = linStage;

                _timeTagger = new HydraHarp(_loggerCallback)
                {
                    DiscriminatorLevel = 200,
                    SyncDivider = 8,
                    SyncDiscriminatorLevel = 200,
                    MeasurementMode = HydraHarp.Mode.MODE_T2,
                    ClockMode = HydraHarp.Clock.Internal,
                    PackageMode = TimeTaggerBase.PMode.ByPackageSize,
                    BufferSize = 100000
                };
                _timeTagger.Connect();
            }

            _scanBgWorker = new BackgroundWorker();
            _scanBgWorker.WorkerReportsProgress = true;
            _scanBgWorker.WorkerSupportsCancellation = true;
            _scanBgWorker.DoWork += DoScan;
            _scanBgWorker.ProgressChanged += BgwScanProgressChanged;
            _scanBgWorker.RunWorkerCompleted += ScanCompleted;
        }

        public void AddRepetition()
        {
            if (!ScanInProgress) return;
            NumRepetitions++;

            OnScanProgressChanged(new ScanProgressChangedEventArgs()
            {
                CurrentStep = CurrentStep,
                TotalSteps = Totalsteps,
                StagePosition = _currentPosition,
                RemainingTime = GetEstimatedTime(CurrentStep, Totalsteps, IntegrationTime)
            });
        }

        public void RemoveRepetition()
        {
            if (!ScanInProgress) return;
            if ((NumRepetitions - 1) <= RepetitionsDone) return;

            NumRepetitions--;

            OnScanProgressChanged(new ScanProgressChangedEventArgs()
            {
                CurrentStep = CurrentStep,
                TotalSteps = Totalsteps,
                StagePosition = _currentPosition,
                RemainingTime = GetEstimatedTime(CurrentStep, Totalsteps, IntegrationTime)
            });
        }
        

        public void InitializePCFSPoints()
        {

            //Get Binning List
            if(!File.Exists(BinningListFile))
            {
                WriteLog("Binning list file does not exist.");
                return;
            }
            _binningList = GetBinningList(BinningListFile);

            //Set TimeTagger
            _timeTagger.PacketSize = PacketSize;

            //Setup Correlators
            StepWidth = (MaxPosition - MinPosition) / NumSteps;
            _corrConfig = new List<(byte ch0, byte ch1)> { ((byte)chan0, (byte)chan1) };
            TimeWindow = Math.Abs(_binningList.Max(p => p.high) - _binningList.Min(p => p.low));

            //Reset Status variables
            RepetitionsDone = 0;
            DataAvailable = false;
            ProcessedSteps = 0;
            CurrentStep = 0;

            //Create Data Points
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

            //Create PCFS Curves
            _PCFSCurves.Clear();
            foreach(var bin in _binningList)
            {
                _PCFSCurves.Add(new PCFSCurve() { Binning = bin });
            }

            //Set Status 
            OnScanInitialized(new ScanInitializedEventArgs(_DataPoints, _PCFSCurves));
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
            if(!_timeTagger.CanCollect)
            {
                WriteLog("Timetagger not ready. Aborting...");
                return;
            }

            if (!_linearStage.StageReady)
            {
                WriteLog("Linear Stage not ready. Aborting...");
                return;
            }

            if (!ScanPointsInitialized)
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
            ProcessedSteps = 0;

            StartScanTime = DateTime.Now;

            string backupDir = string.IsNullOrEmpty(BackupDirectory) ? "" : BackupDirectory + "\\";
            _PCFSDataDirectoryInfo = Directory.CreateDirectory(backupDir+"PCFSData_" + StartScanTime.ToString("yyyy_MM_dd_HH_mm_ss"));
            _PCFSDataDirectory = _PCFSDataDirectoryInfo.ToString();
            //Copy binning list
            File.Copy(BinningListFile, Path.Combine(_PCFSDataDirectory, "BinningMask.bm"),true);

            WriteLog("Start scanning. Files saved to "+_PCFSDataDirectoryInfo.FullName);

            _scanBgWorker.RunWorkerAsync();
        }

        public static IEnumerable<T> OrderByNatural<T>(IEnumerable<T> items, Func<T, string> selector, StringComparer stringComparer = null)
        {
            var regex = new Regex(@"\d+", RegexOptions.Compiled);

            int maxDigits = items
                          .SelectMany(i => regex.Matches(selector(i)).Cast<Match>().Select(digitChunk => (int?)digitChunk.Value.Length))
                          .Max() ?? 0;

            return items.OrderBy(i => regex.Replace(selector(i), match => match.Value.PadLeft(maxDigits, '0')), stringComparer ?? StringComparer.CurrentCulture);
        }

        private void DoScan(object sender, DoWorkEventArgs e)
        {
            //##################
            // Simulated Tagger
            //##################
            if (_timeTagger.GetType() == typeof(SimulatedTagger))
            {               
                if (!Directory.Exists(SimulationFilesFolder))
                {
                    WriteLog("Folder '" + SimulationFilesFolder + " does not exist.");
                    e.Cancel = true;
                    return;
                }

                string[] simulatedFiles = Directory.GetFiles(SimulationFilesFolder, "*.dat", SearchOption.TopDirectoryOnly);
                
                if(simulatedFiles.Length<= 0)
                {
                    WriteLog("Folder '" + SimulationFilesFolder + " contains no valid files.");
                    e.Cancel = true;
                    return;
                }

                if(simulatedFiles.Length != NumRepetitions*NumSteps)
                {
                    WriteLog("Warning: Number of files ("+simulatedFiles.Length+") in folder " + SimulationFilesFolder + " does not match the total number of anticipated steps ("+ NumRepetitions * NumSteps+").");
                }

                string[] sortedSimulatedFiles = OrderByNatural(simulatedFiles, p => p).ToArray();

                SimulatedTagger simTagger = (SimulatedTagger)_timeTagger;
                simTagger.PacketSize = 10000;

                Stopwatch stopWatch = new Stopwatch();
                List<long> stoppedMilliseconds = new List<long>();
                int estimatedRemainingSeconds = 0;

                while(RepetitionsDone < NumRepetitions)
                {
                    foreach (DataPoint pcfsPoint in _DataPoints)
                    {
                        if (_scanBgWorker.CancellationPending || !_timeTagger.CanCollect || CurrentStep>= sortedSimulatedFiles.Length)
                        {
                            e.Cancel = true;
                            break;
                        }

                        //ReportProgress
                        _currentPosition = pcfsPoint.StagePosition;

                        _scanBgWorker.ReportProgress(0, new ScanProgressChangedEventArgs()
                        {
                            CurrentStep = CurrentStep,
                            TotalSteps = Totalsteps,
                            StagePosition = _currentPosition,
                            RemainingTime = new TimeSpan(0, 0, estimatedRemainingSeconds)
                        });

                        stopWatch.Restart();

                        //Stop tagger and clear buffer
                        simTagger.StopCollectingTimeTags();
                        simTagger.ClearTimeTagBuffer();

                        simTagger.FileName = sortedSimulatedFiles[CurrentStep];
                        simTagger.StartCollectingTimeTagsAsync().GetAwaiter().GetResult(); //Collect all timetags in file;

                        //Process data
                        ProcessData(pcfsPoint, _timeTagger.GetAllTimeTags());

                        CurrentStep++;

                        //Remaining time estimation
                        stopWatch.Stop();
                        stoppedMilliseconds.Add(stopWatch.ElapsedMilliseconds);
                        estimatedRemainingSeconds = (int)((stoppedMilliseconds.Average() / 1000.0) * (Totalsteps - CurrentStep));                    
                    }

                    RepetitionsDone++;
                }

            }
            //##############
            // Real Tagger 
            //##############
            else
            {
                while(RepetitionsDone < NumRepetitions)
                {
                    //Initialize new task chain
                    Task processTask = Task.Factory.StartNew(() => {});

                    foreach(DataPoint pcfsPoint in _DataPoints)
                    {                                              
                        if (_scanBgWorker.CancellationPending || !_linearStage.StageReady || !_timeTagger.CanCollect)
                        {
                            e.Cancel = true;
                            break;
                        }

                        //ReportProgress
                        _currentPosition = pcfsPoint.StagePosition;

                        _scanBgWorker.ReportProgress(0,new ScanProgressChangedEventArgs()
                        {
                            CurrentStep = CurrentStep,
                            TotalSteps = Totalsteps,
                            StagePosition = _currentPosition,
                            RemainingTime = GetEstimatedTime(CurrentStep, Totalsteps, IntegrationTime)
                        });

               
                        //Stop tagger and clear buffer
                        _timeTagger.StopCollectingTimeTags();
                        _timeTagger.ClearTimeTagBuffer();                    

                        //Move stage in fast velocity to position
                        _linearStage.SetVelocity(FastVelocity);
                        _linearStage.Move_Absolute(pcfsPoint.StagePosition);

                        //Start moving stage in slow velocity & Start collecting timetags
                        _linearStage.SetVelocity(SlowVelocity);
                        Task slowMoveTask = Task.Run( ()=>_linearStage.Move_Relative(SlowVelocity * IntegrationTime) );

                        if(!String.IsNullOrEmpty(_PCFSDataDirectory) && BackupTTTRData)
                        {
                            string taggerbackupdirectory = Directory.CreateDirectory(Path.Combine(_PCFSDataDirectory, "TTTR_Backup")).ToString();
                            _timeTagger.BackupFilename = Path.Combine(_PCFSDataDirectory + "\\" + taggerbackupdirectory, "TTTRBackup_Step" + CurrentStep.ToString());
                        } 
                        else
                        {
                            _timeTagger.BackupFilename = "";
                        }

                        _timeTagger.StartCollectingTimeTagsAsync();

                        //Wait for stage to arrive at target position
                        slowMoveTask.GetAwaiter().GetResult();
                        _timeTagger.StopCollectingTimeTags();

                        //ASYNCHRONOUSLY PROCESS DATA
                        List<TimeTags> tt = _timeTagger.GetAllTimeTags();
                        processTask = processTask.ContinueWith((ant) => ProcessData(pcfsPoint,tt));

                        CurrentStep++;
                    }

                    //Wait for remaining datapoints to be processed
                    WriteLog("Waiting for remaining data points to be processed.");
                    processTask.GetAwaiter().GetResult();

                    if (e.Cancel) break;

                    RepetitionsDone++;
                }

            }


        }

        private void ProcessData(DataPoint currPoint, List<TimeTags> tt)
        {
            if (tt.Count > 0 )
            {
                long totaltime = tt.Last().time.Last() - tt.First().time.First();            
           
                currPoint.AddMeasurement(tt, totaltime);
                WriteDataPoint(currPoint);
                CalculatePCFS();    
            }

            lock (_processesLock)
            {
                ProcessedSteps++;
            }
        
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

            string datapointsDirectory = Directory.CreateDirectory(Path.Combine(_PCFSDataDirectory,"DataPoints")).ToString();
            string filename = Path.Combine(_PCFSDataDirectory+"\\"+datapointsDirectory, "Point_" + dp.Index +".dat");
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

            //int FFTshift = posIndices.Length % 2 == 0 ? posIndices.Length / 2 + 1: posIndices.Length / 2;        
            int FFTshift = posIndices.Length / 2;
            int[] energyIndices = posIndices.Select(p => p - FFTshift).ToArray();

            double[] energyScale = energyIndices.Select(p => p * eScaleFactor).ToArray();

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
                if(RenormalizeG2)
                {
                    int renormMinIndex = (int)(NumSteps * (100 - RenormalizePercent) / 100.0);
                    if (renormMinIndex < 0 || renormMinIndex >= NumSteps || curve.G2.Length <= renormMinIndex) curve.RenormFactor = 1.0;
                    else curve.RenormFactor = 1 / curve.G2.Skip(renormMinIndex).Average();

                    curve.G2Norm = curve.G2.Select(p => p * curve.RenormFactor).ToArray();
                    curve.G2NormErr = curve.G2Err.Select(p => p * curve.RenormFactor).ToArray();
                }
                else
                {
                    curve.RenormFactor = 1.0;
                    curve.G2Norm = curve.G2;
                    curve.G2NormErr = curve.G2Err;
                }

                curve.AverageRelErrorG2 = curve.G2Norm.Zip(curve.G2NormErr, (g2, g2err) => g2err / g2).Average();

                Complex32[] samples = curve.G2Norm.Select(g => new Complex32((float)1.0 - (float)g, 0)).ToArray();
                Fourier.Inverse(samples,FourierOptions.NoScaling);

                //FFT Shift
                Rotate<Complex32>(samples, FFTshift);

                curve.pE = samples.Select(p => 2.0 * (double)p.MagnitudeSquared).ToArray();

                curve.ErrorPE = curve.G2Err.Select(p => p * p).Sum();
                curve.PEErr = Enumerable.Repeat(curve.ErrorPE, curve.pE.Length).ToArray();

            }

        }

        public static void Rotate<T>(T[] array, int count)
        {
            if (array == null || array.Length < 2) return;
            count %= array.Length;
            if (count == 0) return;
            int left = count < 0 ? -count : array.Length + count;
            int right = count > 0 ? count : array.Length - count;
            if (left <= right)
            {
                for (int i = 0; i < left; i++)
                {
                    var temp = array[0];
                    Array.Copy(array, 1, array, 0, array.Length - 1);
                    array[array.Length - 1] = temp;
                }
            }
            else
            {
                for (int i = 0; i < right; i++)
                {
                    var temp = array[array.Length - 1];
                    Array.Copy(array, 0, array, 1, array.Length - 1);
                    array[0] = temp;
                }
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

            //Capture error
            if(e.Error!=null) throw e.Error;

            if(e.Cancelled)
            {
                WriteLog("Scan cancelled.");
            }
            else
            {
                WriteLog("Scan completed.");               
            }

            TotalScanTime = DateTime.Now - StartScanTime;

            WriteLog("Total scantime: "+TotalScanTime.ToString("hh\\:mm\\:ss"));
            WriteParameters();

            if(_PCFSCurves[0].G2!=null) WritePCFSCurves();
        }

        private void WriteParameters()
        {
            string fF = "F3";
            CultureInfo cult = CultureInfo.InvariantCulture;

            string filename = Path.Combine(_PCFSDataDirectory, "Parameters.dat");

            string[] filestring = new string[]{
                                 "Fast velocity:\t" + FastVelocity.ToString(fF, cult),
                                 "Slow velocity:\t" + SlowVelocity.ToString("0.###E+00", cult),
                                 "Min. Position:\t" + MinPosition.ToString(fF, cult),
                                 "Max. Position:\t" + MaxPosition.ToString(fF, cult),
                                 "Number of Steps:\t" + NumSteps.ToString(),
                                 "Stepwidth:\t" + StepWidth.ToString(fF, cult),
                                 "Integration Time:\t" + IntegrationTime.ToString(),
                                 "Repetitions:\t" + NumRepetitions.ToString(),
                                 "Total Scan Time:\t" + TotalScanTime.ToString("hh\\:mm\\:ss")
                                  };

            File.WriteAllLines(filename, filestring);
        }
    
        private void WritePCFSCurves()
        {
            string PCFSCurvesDirectory = Directory.CreateDirectory(Path.Combine(_PCFSDataDirectory, "PCFSCurves")).ToString();

            CultureInfo cult = CultureInfo.InvariantCulture;

            string filename = "";
            int numLines = 0;
            string[] outstrings;

            for (int i = 0; i < _PCFSCurves.Count; i++)
            {
                if(_PCFSCurves[i].G2.Length != _PCFSCurves[i].Energy.Length)
                {
                    WriteLog("PCFS Curves output dimension mismatch. Aborting file writing.");
                    continue;
                }

                filename = Path.Combine(_PCFSDataDirectory + "\\" + PCFSCurvesDirectory, "PCFS_" + i + ".dat");

                PCFSCurve curve = _PCFSCurves[i];
                numLines = curve.G2.Length;
                outstrings = new string[numLines + 2];

                outstrings[0] = "Timebin: "+_PCFSCurves[i].BinningStringUnicode+"\t Normalization Factor: "+_PCFSCurves[i].RenormFactor.ToString("0.###E+00", cult);
                outstrings[1] = "Pos \t G2 \t G2Err \t G2Norm \t G2NormErr \t E \t pE \t pEErr";

                for(int j=0; j<numLines; j++)
                {
                    outstrings[j + 2] = curve.positions[j].ToString("F3", cult) + "\t" + curve.G2[j].ToString("F3", cult) + "\t" + curve.G2Err[j].ToString("F3", cult) + "\t"
                                      + curve.G2Norm[j].ToString("F3", cult) + "\t" + curve.G2NormErr[j].ToString("F3", cult) + "\t"
                                      + curve.Energy[j].ToString("F3", cult) + "\t" + curve.pE[j].ToString("F5", cult) + "\t" + curve.PEErr[j].ToString("F5", cult);
                }

                File.WriteAllLines(filename, outstrings);
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
