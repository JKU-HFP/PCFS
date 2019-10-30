using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using TimeTagger_Library;
using System.Windows.Forms;
using PCFS.Model;
using System.Collections.ObjectModel;
using LiveCharts;
using LiveCharts.Wpf;
using LiveCharts.Defaults;
using System.Windows.Media;

namespace PCFS.ViewModel
{
    public class MainWindowViewModel : ViewModelBase
    {
        //Private fields
        PCFSScan _pcfsScan;
        Timer _CountrateTimer;

        //#########################
        // P R O P E R T I E S
        //#########################

        private string _messages;
        public string Messages
        {
            get { return _messages; }
            set
            {
                _messages = value;
                OnPropertyChanged("Messages");
            }
        }

        #region Settings

        //Settings
        private byte _chan0 = 0;
        public byte Chan0
        {
            get { return _chan0; }
            set
            {
                _chan0 = value;
                OnPropertyChanged("Chan0");
            }
        }

        private byte _chan1 = 1;
        public byte Chan1
        {
            get { return _chan1; }
            set
            {
                _chan1 = value;
                OnPropertyChanged("Chan1");
            }
        }

        private long _offset = 0;
        public long Offset
        {
            get { return _offset; }
            set
            {
                _offset = value;
                OnPropertyChanged("Offset");
            }
        }

        private int _PacketSize;
        public int PacketSize
        {
            get { return _PacketSize; }
            set
            {
                _PacketSize = value;
                OnPropertyChanged("PacketSize");
            }
        }

        private bool _autoCalcPacketSize = true;
        public bool AutoCalcPacketSize
        {
            get { return _autoCalcPacketSize; }
            set
            {
                _autoCalcPacketSize = value;
                OnPropertyChanged("AutoCalcPacketSize");
            }
        }

        private bool _backupTTTRData = true;
        public bool BackupTTTRData
        {
            get { return _backupTTTRData; }
            set
            {
                _backupTTTRData = value;
                OnPropertyChanged("BackupTTTRData");
            }
        }



        private double _fastVelocity = 25.0;
        public double FastVelocity
        {
            get { return _fastVelocity; }
            set
            {
                _fastVelocity = value;
                OnPropertyChanged("FastVelocity");
            }
        }

        private double _slowVelocity = 0.000125;
        public double SlowVelocity
        {
            get { return _slowVelocity; }
            set
            {
                _slowVelocity = value;
                OnPropertyChanged("SlowVelocity");
            }
        }

        private double _minPosition = 0.0; 
        public double MinPosition
        {
            get { return _minPosition; }
            set
            {
                _minPosition = value;
                OnPropertyChanged("MinPosition");
                OnStepWidthChanged();
            }
        }

        private double _maxPosition = 300.0; 
        public double MaxPosition
        {
            get { return _maxPosition; }
            set
            {
                _maxPosition = value;
                OnPropertyChanged("MaxPosition");
                OnStepWidthChanged();
            }
        }

        private int _numSteps = 240;
        public int NumSteps
        {
            get { return _numSteps; }
            set
            {
                _numSteps = value;
                OnPropertyChanged("NumSteps");
                OnStepWidthChanged();
                OnEstimatedTotalTimeChanged();
            }
        }

        public double StepWidth { get; private set; }
        private void OnStepWidthChanged()
        {
            StepWidth = (MaxPosition - MinPosition) / NumSteps;
            OnPropertyChanged("StepWidth");
        }

        private int _integrationTime = 10;
        public int IntegrationTime
        {
            get { return _integrationTime; }
            set
            {
                _integrationTime = value;
                OnPropertyChanged("IntegrationTime");
                OnEstimatedTotalTimeChanged();
            }
        }

        private int _repetitions = 4;
        public int Repetitions
        {
            get { return _repetitions; }
            set
            {
                _repetitions = value;
                OnPropertyChanged("Repetitions");
                OnEstimatedTotalTimeChanged();
            }
        }

        public TimeSpan EstimatedTotalTime { get; private set; }
        private void OnEstimatedTotalTimeChanged()
        {
            //EstimatedTotalTime = new TimeSpan(0, 0, (int)(NumSteps*IntegrationTime*Repetitions*1.125));
            EstimatedTotalTime = PCFSScan.GetEstimatedTime(0, NumSteps * Repetitions, IntegrationTime);
            OnPropertyChanged("EstimatedTotalTime");
        }

      
        private string _binningListFilename;
        public string BinningListFilename
        {
            get { return _binningListFilename; }
            set
            {
                _binningListFilename = value;
                OnPropertyChanged("BinningListFilename");
            }
        }

        private int _renormalizePercent = 20;
        public int RenormalizePercent
        {
            get { return _renormalizePercent; }
            set
            {
                _renormalizePercent = value;
                OnPropertyChanged("RenormalizePercent");
            }
        }


        #endregion

        #region Data
        //Data
        private ObservableCollection<DataPoint> dataPoints;
        public ObservableCollection<DataPoint> DataPoints
        {
            get { return dataPoints; }
            set
            {
                dataPoints = value;
                OnPropertyChanged("DataPoints");
            }
        }

        private ObservableCollection<PCFSCurve> _PCFSCurves;
        public ObservableCollection<PCFSCurve> PCFSCurves
        {
            get => _PCFSCurves;
            set
            {
                _PCFSCurves = value;
                OnPropertyChanged("PCFSCurves");
            }
        }

        private DataPoint _selectedDataPoint;
        public DataPoint SelectedDataPoint
        {
            get { return _selectedDataPoint; }
            set
            {
                _selectedDataPoint = value;
                OnPropertyChanged("SelectedDataPoint");
            }
        }

        private PCFSCurve _selectedPCFSCurve;
        public PCFSCurve SelectedPCFSCurve
        {
            get { return _selectedPCFSCurve; }
            set
            {
                _selectedPCFSCurve = value;
                OnPropertyChanged("SelectedPCFSCurve");
                UpdateCharts();
            }
        }


        public DataChartViewModel G2Chart { get; set; } = new DataChartViewModel(Colors.Blue, 5.0)
        {
            XAxisTitle = "Position (mm)",
            YAxisTitle ="g2"
        };
        public DataChartViewModel PreviewChart { get; set; } = new DataChartViewModel(Colors.Blue, 0.0)
        {
            XAxisTitle = "Time delay (ns)",
            YAxisTitle = "Coincidences"
        };
        public DataChartViewModel PEChart { get; set; } = new DataChartViewModel(Colors.Red, 5.0)
        {
            XAxisTitle = "ε (μeV)",
            YAxisTitle = "p(ε)"
        };
        #endregion

        #region Status
        //Status
        private string _CountrateCh0;
        public string CountrateCh0
        {
            get { return _CountrateCh0; }
            set
            {
                _CountrateCh0 = value;
                OnPropertyChanged("CountrateCh0");
            }
        }

        private string _CountrateCh1;
        public string CountrateCh1
        {
            get { return _CountrateCh1; }
            set
            {
                _CountrateCh1 = value;
                OnPropertyChanged("CountrateCh1");
            }
        }

        private string _step;
        public string Step
        {
            get { return _step; }
            set
            {
                _step = value;
                OnPropertyChanged("Step");
            }
        }

        private string _stagePosition;
        public string StagePosition
        {
            get { return _stagePosition; }
            set
            {
                _stagePosition = value;
                OnPropertyChanged("StagePosition");
            }
        }

        private string _remainingTime;
        public string RemainingTime
        {
            get { return _remainingTime; }
            set
            {
                _remainingTime = value;
                OnPropertyChanged("RemainingTime");
            }
        }

        private string _processedPoints;
        public string ProcessedPoints
        {
            get { return _processedPoints; }
            set
            {
                _processedPoints = value;
                OnPropertyChanged("ProcessedPoints");
            }

        }

        #endregion  
        
        //Commands
        public RelayCommand<object> OpenBinningListCommand { get; private set; }

        public RelayCommand<object> InitializeCommand { get; private set; }
        public RelayCommand<object> StartScanCommand { get; private set; }
        public RelayCommand<object> StopScanCommand { get; private set; }

        public RelayCommand<object> AddRepetitionCommand { get; private set; }
        public RelayCommand<object> RemoveRepetitionCommand { get; private set; }

        public MainWindowViewModel()
        {                     
            OnStepWidthChanged();
            OnEstimatedTotalTimeChanged();

            //Create Scan object
            _pcfsScan = new PCFSScan(WriteLog);
            _pcfsScan.ScanInitialized += OnScanInitialized;
            _pcfsScan.DataChanged += OnDataChanged;
            _pcfsScan.ScanProgressChanged += OnScanProgressChanged;

            //Subscribe to events
            G2Chart.DataPointClicked += OnDataPointSelected;               
           
            //Wire Relay Commands
            OpenBinningListCommand = new RelayCommand<object>(o =>
            {
                OpenFileDialog of = new OpenFileDialog();
                if (of.ShowDialog() == DialogResult.OK) BinningListFilename = of.FileName;
            });

            InitializeCommand = new RelayCommand<object>(
            o =>
            {
                //Reset Charts
                G2Chart.Clear();
                PEChart.Clear();

                G2Chart.XAxisMin = MinPosition;
                G2Chart.XAxisMax = MaxPosition;

                PreviewChart.XAxisMin = -20.0;
                PreviewChart.XAxisMax = 20.0;


                //Set scan parameters
                _pcfsScan.chan0 = Chan0;
                _pcfsScan.chan1 = Chan1;
                _pcfsScan.Offset = Offset;

                if(AutoCalcPacketSize)
                {
                    (int cr0, int cr1) countrate = _pcfsScan.GetCountrates();

                    //Target: 100 Packets total. Minimum packet size: 100
                    _pcfsScan.PacketSize = Math.Max( (countrate.cr0 + countrate.cr1) * IntegrationTime / (2 * 100), 100);
                    PacketSize = _pcfsScan.PacketSize;
                }
                else
                {
                    _pcfsScan.PacketSize = PacketSize;
                }

                _pcfsScan.BackupTTTRData = BackupTTTRData;

                _pcfsScan.SlowVelocity = SlowVelocity;
                _pcfsScan.FastVelocity = FastVelocity;

                _pcfsScan.MinPosition = MinPosition;
                _pcfsScan.MaxPosition = MaxPosition;
                _pcfsScan.NumSteps = NumSteps;

                _pcfsScan.IntegrationTime = IntegrationTime;
                _pcfsScan.NumRepetitions = Repetitions;

                _pcfsScan.BinningListFile = BinningListFilename;

                _pcfsScan.RenormalizePercent = RenormalizePercent;

                _pcfsScan.InitializePCFSPoints();
            },
            o => !_pcfsScan.ScanInProgress
            );

            StartScanCommand = new RelayCommand<object>(
            o=>
            {
                _pcfsScan.StartScanAsync();
            },
            o=> _pcfsScan.ScanPointsInitialized         
            );

            StopScanCommand = new RelayCommand<object>(o =>
            {
                //PROMPT USER IF SCAN IN PROGRESS
                _pcfsScan.StopScan();
            });

            AddRepetitionCommand = new RelayCommand<object>(o =>
            {
                _pcfsScan.AddRepetition();
            },
            o => _pcfsScan.ScanInProgress
            ) ;

            RemoveRepetitionCommand = new RelayCommand<object>(o =>
            {
                _pcfsScan.RemoveRepetition();
            },
            o => _pcfsScan.ScanInProgress && ((_pcfsScan.NumRepetitions - 1) > _pcfsScan.RepetitionsDone)
            );
            

            //Setup timer
            _CountrateTimer = new Timer();
            _CountrateTimer.Interval = 1000;
            _CountrateTimer.Tick += (sender, e) =>
            {
                (int c0, int c1) counts = _pcfsScan.GetCountrates();
                CountrateCh0 = counts.c0.ToString("0.###E+00");
                CountrateCh1 = counts.c1.ToString("0.###E+00");
            };
            _CountrateTimer.Enabled = true;

        }
         

        //##################################
        //  E V E N T   H A N D L E R
        //##################################

        private void OnScanProgressChanged(object sender, ScanProgressChangedEventArgs e)
        {
            Step = (e.CurrentStep+1).ToString() + " / " + e.TotalSteps.ToString();
            StagePosition = e.StagePosition.ToString()+" mm";
            RemainingTime = e.RemainingTime.ToString("hh\\:mm\\:ss");
        }

        private void OnDataPointSelected(object sender, ChartPoint point)
        {
            if (point == null) return;

            SelectedDataPoint = DataPoints.Where(p => IsInRange(p.StagePosition,point.X)).FirstOrDefault();

            UpdatePreviewChart();
        }

        private void UpdatePreviewChart()
        {
            if (SelectedDataPoint == null) return;
            if (SelectedDataPoint.HistogramXPreview == null) return;

            PreviewChart.Clear();
            PreviewChart.AddPoints(SelectedDataPoint.HistogramXPreview.Zip(SelectedDataPoint.HistogramYPreview, (X, Y) => new ObservablePoint(X / 1000.0, Y)));
        }
        
        private void OnScanInitialized(object sender, ScanInitializedEventArgs e)
        {
            DataPoints = new ObservableCollection<DataPoint>(e.DataPoints);
            PCFSCurves = new ObservableCollection<PCFSCurve>(e.PCFSCurves);

            SelectedDataPoint = null;
            PreviewChart.Clear();
            SelectedPCFSCurve = PCFSCurves[0];

            Step = "";
            StagePosition = "";
            RemainingTime = "";
            ProcessedPoints = "";
        }

        private void OnDataChanged(object sender, DataChangedEventArgs e)
        {
            ProcessedPoints = _pcfsScan.ProcessedSteps.ToString() + " / " + _pcfsScan.Totalsteps;
            UpdateCharts();
        }

        //##################################
        //   A U X   F U N C T I O N S
        //##################################
        
        private void WriteLog(string message)
        {
            Messages = DateTime.Now.ToString("HH:mm:ss")+" "+message + "\n" + Messages;
        }

        private void UpdateCharts()
        {
            if (SelectedPCFSCurve == null) return;
            if (SelectedPCFSCurve.positions == null) return;

            G2Chart.Clear();
            G2Chart.AddPoints(SelectedPCFSCurve.positions.Zip(SelectedPCFSCurve.G2, (pos, g2) => new ObservablePoint(pos, g2)));

            PEChart.Clear();
            PEChart.AddPoints(SelectedPCFSCurve.Energy.Zip(SelectedPCFSCurve.pE, (e, pe) => new ObservablePoint(e, pe)));
            
        }

        private bool IsInRange(double x1, double x2)
        {
            double ratio = 0.001;
            double upper = x2 * (1 + ratio);
            double lower = x2 * (1 - ratio);

            return x1 >= lower && x2 <= upper;
        }

    }
}
