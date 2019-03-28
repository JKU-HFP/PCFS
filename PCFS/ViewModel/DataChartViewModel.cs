using LiveCharts;
using LiveCharts.Defaults;
using LiveCharts.Wpf;
using PCFS.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace PCFS.ViewModel
{
    public class DataChartViewModel : ViewModelBase
    {
        //######################
        // Private fields
        //######################

        private LineSeries _LineSeries;
        private ChartValues<ObservablePoint> _ChartPoints;

        //######################
        // Properties
        //######################

        public SeriesCollection Collection { get; private set; }

        private string _xAxisTitle = "";
        public string XAxisTitle
        {
            get { return _xAxisTitle; }
            set
            {
                _xAxisTitle = value;
                OnPropertyChanged("XAxisTitle");
            }
        }

        private string _yAxisTitle = "";
        public string YAxisTitle
        {
            get { return _yAxisTitle; }
            set
            {
                _yAxisTitle = value;
                OnPropertyChanged("YAxisTitle");
            }
        }

        private double _xAxisMin = double.NaN;
        public double XAxisMin
        {
            get { return _xAxisMin; }
            set
            {
                _xAxisMin = value;
                OnPropertyChanged("XAxisMin");
            }
        }

        private double _xAxisMax = double.NaN;
        public double XAxisMax
        {
            get { return _xAxisMax; }
            set
            {
                _xAxisMax = value;
                OnPropertyChanged("XAxisMax");
            }
        }

        private double _yAxisMin = double.NaN;
        public double YAxisMin
        {
            get { return _yAxisMin; }
            set
            {
                _yAxisMin = value;
                OnPropertyChanged("YAxisMin");
            }
        }

        private double _yAxisMax = double.NaN;
        public double YAxisMax
        {
            get { return _yAxisMax; }
            set
            {
                _yAxisMax = value;
                OnPropertyChanged("YAxisMax");
            }
        }


        //######################
        // Events
        //######################
        public event EventHandler<ChartPoint> DataPointClicked;
        private void OnDataPointClicked(ChartPoint p)
        {
            DataPointClicked?.Invoke(this, p);
        }

        //######################
        // Commands
        //######################

        public RelayCommand<ChartPoint> DataPointClickCommand { get; private set; }
        public RelayCommand<object> XAutoScaleCommand { get; private set; }
        public RelayCommand<object> YAutoScaleCommand { get; private set; }

        //######################
        // Constructor
        //######################
        public DataChartViewModel(Color color, double pointgeometrysize)
        {
            //Bind commands
            DataPointClickCommand = new RelayCommand<ChartPoint>((p) => OnDataPointClicked(p));
            XAutoScaleCommand = new RelayCommand<object>(o =>
            {
                XAxisMin = double.NaN;
                XAxisMax = double.NaN;
            });
            YAutoScaleCommand = new RelayCommand<object>(o =>
            {
                YAxisMin = double.NaN;
                YAxisMax = double.NaN;
            });

            //Create chart elements
            _ChartPoints = new ChartValues<ObservablePoint> { };
            _LineSeries = new LineSeries()
            {
                Fill = new SolidColorBrush() { Opacity = 0.2, Color = color },
                //PointForeground = new SolidColorBrush() { Color = color },
                Stroke = new SolidColorBrush() { Color = color },
                LineSmoothness = 0.0, //Spline Interpolation 0 off, 1 strong
                PointGeometrySize = pointgeometrysize,
                Values = _ChartPoints
            };
            Collection = new SeriesCollection();
            Collection.Add(_LineSeries);
        }
        
        public void Clear()
        {
            _ChartPoints.Clear();
        }

        public void AddPoints (IEnumerable<ObservablePoint> XYPoints)
        {
            _ChartPoints.AddRange(XYPoints);
        }
                
    }
}
