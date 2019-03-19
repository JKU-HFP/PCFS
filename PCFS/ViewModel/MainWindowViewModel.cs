using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PIStage_Library;
using System.Diagnostics;
using TimeTagger_Library;
using System.Windows.Forms;
using PCFS.Model;

namespace PCFS.ViewModel
{
    public class MainWindowViewModel : ViewModelBase
    {
        //Private fields
        PCFSScan _pcfsScan;

        //Properties
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
            EstimatedTotalTime = new TimeSpan(0, 0, (int)(NumSteps*IntegrationTime*Repetitions*1.125));
            OnPropertyChanged("EstimatedTotalTime");
        }
               
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


        //Commands
        public RelayCommand<object> OpenBinningListCommand { get; private set; }

        public RelayCommand<object> InitializeCommand { get; private set; }
        public RelayCommand<object> StartScanCommand { get; private set; }
        public RelayCommand<object> StopScanCommand { get; private set; }

        public MainWindowViewModel()
        {
            OnStepWidthChanged();
            OnEstimatedTotalTimeChanged();

            //Create Scan object
            _pcfsScan = new PCFSScan(WriteLog);

            //Wire Relay Commands
           OpenBinningListCommand = new RelayCommand<object>(o =>
           {
               OpenFileDialog of = new OpenFileDialog();
               if (of.ShowDialog() == DialogResult.OK) BinningListFilename = of.FileName;
           });

            InitializeCommand = new RelayCommand<object>(o =>
            {
                _pcfsScan.chan0 = Chan0;
                _pcfsScan.chan1 = Chan1;
                _pcfsScan.Offset = Offset;

                _pcfsScan.SlowVelocity = SlowVelocity;
                _pcfsScan.FastVelocity = FastVelocity;

                _pcfsScan.MinPosition = MinPosition;
                _pcfsScan.MaxPosition = MaxPosition;
                _pcfsScan.NumSteps = NumSteps;

                _pcfsScan.IntegrationTime = IntegrationTime;
                _pcfsScan.NumRepetitions = Repetitions;

                _pcfsScan.BinningListFilename = BinningListFilename;

                _pcfsScan.InitializePCFSPoints();
            });

            StartScanCommand = new RelayCommand<object>(
            o=>
            {
                _pcfsScan.StartScanAsync();
            },
            o=>
            {
               return _pcfsScan.ScanPointsInitialized;
            }
            );

            StopScanCommand = new RelayCommand<object>(o =>
            {
                //PROMPT USER IF SCAN IN PROGRESS
                _pcfsScan.StopScan();
            });

        }

        private void WriteLog(string message)
        {
            _messages = message + "\n" + _messages;
        }
    }
}
